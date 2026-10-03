using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// A token role the host mapped to a database role of its own, analysts who look across tenants say, holds no seat:
/// tenant selection answers nobody for it, Tenancy's tables and every table kept to a tenant are closed to its
/// role, none of Tenancy's functions is its to ask, and the scoped system role's privileges never reach it. What
/// such a role may read is for later rules to open on purpose; until then a module's rule for it lets nothing of
/// a tenant through. A host that gave the role more than that, the privileges of another caller's role or a
/// function of Tenancy's, is refused at start-up.
/// </summary>
/// <remarks>
/// Roles are the server's, and the other tests of the run share the server: the roles made here have names no
/// other test uses, and only the services of these tests map a token role to them. For the same reason the
/// class runs under the default names alone: a second naming would make the same roles on the same server.
/// </remarks>
public sealed class TokenRoleTests(TenancyPostgres postgres)
{
    private const string Analyst = "analyst";

    private const string AnalystRole = "tenancy_analyst";

    /// <summary>A module's rule for the analysts, which names no tenant: read, add, change and remove every widget.</summary>
    private static readonly RowAccessRule AnalystsDoAnythingWithWidgets = RowAccessRule.For<Widget>(
        "Analysts do anything with widgets", RowOperations.All, "TRUE", RowAccessRoles.Token(Analyst));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_mapped_token_role_holds_no_seat_and_reads_nothing_of_tenancys()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await using var services = new TenancyServices(database, roles: Map);

        // Ada administers Harbor. Before the host maps the role, her seat reads what it reads.
        var asASeat = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Widgets().Widgets.CountAsync(Cancellation));
        asASeat.Should().Be(3, "Harbor's widgets");

        await MapAnalystsAsync(database);

        List<string> tables;
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            tables = await owner.ListAsync<string>(
                "SELECT pg_catalog.quote_ident(schemaname) || '.' || pg_catalog.quote_ident(tablename) FROM pg_catalog.pg_tables WHERE schemaname IN ('tenancy', 'widgets') ORDER BY 1",
                Cancellation);
            tables.Should().Contain(["tenancy.\"Seats\"", "tenancy.\"SeatRights\"", "tenancy.\"Tenants\"", "widgets.\"Widgets\"", "widgets.\"WidgetPart\""]);
            (await owner.ScalarAsync<long>("SELECT count(*) FROM widgets.\"Widgets\"", Cancellation)).Should().Be(4, "there is something to read");

            // The module's rule is there as a policy, and so is what closes the table to the role.
            (await owner.ListAsync<string>(
                "SELECT policyname || ' ' || permissive FROM pg_catalog.pg_policies WHERE schemaname = 'widgets' AND tablename = 'Widgets' AND roles = '{tenancy_analyst}' AND cmd IN ('SELECT', 'ALL') ORDER BY 1",
                Cancellation)).Should().Equal(
                    "Analysts do anything with widgets (select) for tenancy_analyst PERMISSIVE",
                    "Closed to token roles (all) for tenancy_analyst RESTRICTIVE");

            // None of Tenancy's functions is the role's to call, those that run as their owner and read across
            // seats and tenants included: nothing was granted to it, and nothing is left with every role.
            (await owner.ScalarAsync<long>("SELECT count(*) FROM pg_catalog.pg_proc WHERE pronamespace = 'tenancy'::pg_catalog.regnamespace", Cancellation))
                .Should().BeGreaterThan(10, "there are functions to ask about");
            (await owner.ListAsync<string>(
                $"""
                SELECT p.oid::pg_catalog.regprocedure::text FROM pg_catalog.pg_proc p
                WHERE p.pronamespace = 'tenancy'::pg_catalog.regnamespace AND pg_catalog.has_function_privilege('{AnalystRole}', p.oid, 'EXECUTE')
                ORDER BY 1
                """,
                Cancellation)).Should().BeEmpty("a mapped token role asks none of Tenancy's questions");
        }

        // The start-up check finds the role as the access files leave it: every privilege on the tables, which the
        // policies close, and no function of Tenancy's.
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(services.Provider, Cancellation);

        // Her token with the analyst role, as a query of the application's own that forgot every condition would run:
        // the mapped role, with every privilege on the tables, and the tenant of her seat named on the connection.
        await using (var analyst = await AsCaller.TokenRoleAsync(database, AnalystRole, Analyst, Ada.Identity, Harbor, Cancellation))
        {
            (await analyst.ScalarAsync<string>("SELECT current_user::text", Cancellation)).Should().Be(AnalystRole);

            foreach (var table in tables)
            {
                (await analyst.ScalarAsync<long>($"SELECT count(*) FROM {table}", Cancellation)).Should().Be(0, $"{table} is closed to a role that holds no seat");
                (await analyst.AttemptAsync($"DELETE FROM {table}", Cancellation)).Should().Be(0, $"and nothing of {table} is its to remove");
            }

            (await analyst.AttemptAsync("UPDATE widgets.\"Widgets\" SET \"Name\" = 'Renamed'", Cancellation)).Should().Be(0, "the module's rule lets it change every widget, and the table stays closed");
            var adding = () => analyst.AttemptAsync(
                $"INSERT INTO widgets.\"Widgets\" (\"Id\", \"TenantId\", \"UnitId\", \"Name\", \"Version\") VALUES (gen_random_uuid(), 1, '{North.Value}', 'Smuggled', 0)",
                Cancellation);
            (await adding.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, "nor is a row its to add");

            // The questions a seat asks are not its to ask.
            foreach (var function in (string[])["caller_seat()", "caller_tenant()", "identity_tenants()", "holds_key('tenancy.seats.manage')", "readable_units()"])
            {
                var asking = () => analyst.AttemptAsync($"SELECT tenancy.{function}", Cancellation);
                (await asking.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, $"{function} is granted to seats and to system work alone");
            }
        }

        // In the application: no seat is chosen for the role, whatever seats the identity has. Nothing is looked up
        // for it either: no caller is begun here, and a query without one would fail.
        var selected = await services.InScopeAsync(scoped => scoped.GetRequiredService<TenantSelection<TenantId, SeatId>>()
            .ResolveAsync(Callers.FromClaims(AnalystClaims(Ada.Identity)), "harbor", Cancellation));
        selected.Should().Be(HostCaller.Nobody(TenancyRefusals.NotSeated));

        // And the contexts run its queries as the mapped role, which reads nothing, with the filters skipped as well.
        using (Callers.Begin(Callers.FromClaims(AnalystClaims(Ada.Identity))))
        {
            var (role, widgets, seats) = await services.InScopeAsync(async scoped => (
                await scoped.Widgets().Database.SqlQueryRaw<string>("SELECT current_user::text AS \"Value\"").SingleAsync(Cancellation),
                await scoped.Widgets().Widgets.IgnoreQueryFilters().CountAsync(Cancellation),
                await scoped.Tenancy().Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM tenancy.\"Seats\"").SingleAsync(Cancellation)));

            role.Should().Be(AnalystRole);
            widgets.Should().Be(0);
            seats.Should().Be(0);
        }

        // A seat is what it was: the files written with the map change nothing for a signed-in user.
        (await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Widgets().Widgets.CountAsync(Cancellation))).Should().Be(asASeat);
        using (Callers.Begin(Caller.User(Ada.Identity)))
        {
            (await services.InScopeAsync(scoped => scoped.GetRequiredService<TenantSelection<TenantId, SeatId>>().ResolveAsync(Caller.User(Ada.Identity), "harbor", Cancellation)))
                .Should().Be(HostCaller.InSeat(Harbor, Ada.Seat), "the directory is read as the user, under the policies written with the map");
        }
    }

    [Fact]
    public void The_contribution_closes_every_table_it_closes_to_anonymous_callers_to_a_mapped_role()
    {
        var mapped = RowAccessRoleNames.Default with
        {
            TokenRoles = new Dictionary<string, string> { [Analyst] = AnalystRole, ["examiner"] = AnalystRole, ["member"] = "authenticated" },
        };

        var script = string.Concat(TenancyPostgres.AccessScripts(roles: mapped));

        script.Should().Contain(
            "CREATE POLICY \"Closed to token roles (all) for tenancy_analyst\" ON tenancy.\"Seats\" AS RESTRICTIVE FOR ALL TO tenancy_analyst\n" +
            "    USING (false)\n" +
            "    WITH CHECK (false);\n");
        script.Should().Contain("CREATE POLICY \"Closed to token roles (all) for tenancy_analyst\" ON widgets.\"Widgets\" AS RESTRICTIVE FOR ALL TO tenancy_analyst\n")
            .And.Contain("CREATE POLICY \"Closed to token roles (all) for tenancy_analyst\" ON widgets.\"WidgetPart\" AS RESTRICTIVE FOR ALL TO tenancy_analyst\n");
        Count(script, "CREATE POLICY \"Closed to token roles (all) for tenancy_analyst\"").Should().Be(
            Count(script, "CREATE POLICY \"Closed to anonymous callers (all) for anon\""),
            "wherever anonymous callers are kept out, so is the role, once, however many token roles are mapped to it");
        script.Should().NotContain("Closed to token roles (all) for authenticated", "a token role mapped to the role of a signed-in user is a signed-in user: closing the tables to that role would close them to every seat");
        script.Should().Contain("            CREATE ROLE tenancy_analyst NOLOGIN NOINHERIT;\n", "the access file makes the role its policies are for");

        // A host that maps no token role of its own gets the files it had.
        var plain = string.Concat(TenancyPostgres.AccessScripts());
        plain.Should().NotContain("token role").And.NotContain(AnalystRole);
        string.Concat(TenancyPostgres.AccessScripts(roles: RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { ["member"] = "authenticated" } }))
            .Should().Be(plain, "a token role that is the user's role changes nothing");

        static int Count(string text, string part) => (text.Length - text.Replace(part, "", StringComparison.Ordinal).Length) / part.Length;
    }

    [Fact]
    public async Task The_confinement_check_fails_for_a_mapped_role_that_has_the_scoped_system_roles_privileges()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await TenancyPostgres.ExecuteAsync(database.SuperuserConnectionString, """
            DO $do$ BEGIN CREATE ROLE examiners_of_their_own NOLOGIN NOINHERIT; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            DO $do$ BEGIN CREATE ROLE analysts_inside_the_scope NOLOGIN NOINHERIT; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            GRANT ddd_system_in TO analysts_inside_the_scope WITH INHERIT TRUE;
            """, Cancellation);

        await using (var apart = new TenancyServices(database, roles: roles => roles.TokenRoles["examiner"] = "examiners_of_their_own"))
        {
            await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(apart.Provider, Cancellation);
        }

        // System work in a tenant is kept there by the policies written for its role. A token role that has that
        // role's privileges gets those policies too: the holder of a token would do the application's own work.
        await using var reaching = new TenancyServices(database, roles: roles =>
        {
            roles.TokenRoles["examiner"] = "examiners_of_their_own";
            roles.TokenRoles[Analyst] = "analysts_inside_the_scope";
        });
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(reaching.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*analysts_inside_the_scope has its privileges, so its callers would get every policy written for it: REVOKE ddd_system_in FROM analysts_inside_the_scope;*");
    }

    [Fact]
    public async Task The_confinement_check_fails_for_a_mapped_role_that_has_the_signed_in_users_privileges()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await TenancyPostgres.ExecuteAsync(database.SuperuserConnectionString, """
            DO $do$ BEGIN CREATE ROLE analysts_who_are_signed_in NOLOGIN NOINHERIT; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            GRANT authenticated TO analysts_who_are_signed_in WITH INHERIT TRUE;
            """, Cancellation);

        // The other way round from the scoped system role: the mapped role was given the role of a signed-in user. Its
        // holders have no seat, and Tenancy's own tables stay closed to them, but every function a signed-in user may
        // ask would answer them for the seats their identity has, and so would every policy on a table kept to no tenant.
        await using (var reaching = new TenancyServices(database, roles: roles => roles.TokenRoles[Analyst] = "analysts_who_are_signed_in"))
        {
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(reaching.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*analysts_who_are_signed_in, the role of a mapped token role, has the privileges of authenticated,*REVOKE authenticated FROM analysts_who_are_signed_in;*");
        }

        // A token role mapped to the user's own role is a signed-in user, and is not refused for being one.
        await using var same = new TenancyServices(database, roles: roles => roles.TokenRoles["member"] = roles.UserRole);
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(same.Provider, Cancellation);
    }

    [Fact]
    public async Task The_confinement_check_fails_for_a_mapped_role_that_may_execute_a_function_of_tenancys()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await TenancyPostgres.ExecuteAsync(database.SuperuserConnectionString, """
            DO $do$ BEGIN CREATE ROLE analysts_given_functions NOLOGIN NOINHERIT; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            DO $do$ BEGIN CREATE ROLE askers_of_tenancy NOLOGIN NOINHERIT; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            DO $do$ BEGIN CREATE ROLE analysts_among_the_askers NOLOGIN NOINHERIT; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            GRANT askers_of_tenancy TO analysts_among_the_askers WITH INHERIT TRUE;
            """, Cancellation);

        // Two hosts over one database, each with the analysts mapped to a role of its own. As the access files leave
        // the database, neither role may execute anything of Tenancy's.
        await using var granted = new TenancyServices(database, roles: roles => roles.TokenRoles[Analyst] = "analysts_given_functions");
        await using var among = new TenancyServices(database, roles: roles => roles.TokenRoles[Analyst] = "analysts_among_the_askers");
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(granted.Provider, Cancellation);
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(among.Provider, Cancellation);

        // Granted by hand to the role itself: a question that runs as its owner, and a function a module reads
        // through, which runs as its caller. The holder of such a token has no seat, and would ask what a seat asks.
        const string Two = "tenancy.caller_rights(), tenancy.holds_key(text)";
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, $"GRANT EXECUTE ON FUNCTION {Two} TO analysts_given_functions", Cancellation);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(granted.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(
                $"*- analysts_given_functions, the role of a mapped token role, may execute {Two} by a grant of its own, and Tenancy's functions are for signed-in users and the scoped system role alone, " +
                $"so the holder of such a token, who has no seat, could ask what they answer a seat: REVOKE EXECUTE ON FUNCTION {Two} FROM analysts_given_functions;*");
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(among.Provider, Cancellation);

        // The statement the check names puts it right.
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, $"REVOKE EXECUTE ON FUNCTION {Two} FROM analysts_given_functions;", Cancellation);
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(granted.Provider, Cancellation);

        // Every function of the schema at once, as a host grants them that gives a role its privileges schema by
        // schema: the questions, the reads across tenants and the functions that answer about other seats' rights.
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA tenancy TO analysts_given_functions", Cancellation);
        var all = (await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(granted.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()).Which.Message;
        all.Should().ContainAll("tenancy.caller_seat()", "tenancy.role_keys_in_use()", "tenancy.tenant_administrators()", "tenancy.rights_follow_grants()");

        // The access files applied again take back every grant they did not write, on the functions they write. The
        // trigger functions keep theirs: Postgres runs those as triggers alone, and the check goes on naming them.
        foreach (var script in TenancyPostgres.AccessScripts())
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        var left = (await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(granted.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()).Which.Message;
        left.Should().Contain("tenancy.rights_follow_grants()").And.NotContainAny("tenancy.caller_seat()", "tenancy.role_keys_in_use()", "tenancy.tenant_administrators()");
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA tenancy FROM analysts_given_functions", Cancellation);
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(granted.Provider, Cancellation);

        // Granted to another role, whose privileges the mapped role has: the role may execute it all the same, and
        // what puts it right is taking the role out of the other.
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "GRANT EXECUTE ON FUNCTION tenancy.caller_seat() TO askers_of_tenancy", Cancellation);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(among.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "*- analysts_among_the_askers, the role of a mapped token role, may execute tenancy.caller_seat() with the privileges of askers_of_tenancy, which it has, " +
                "and Tenancy's functions are for signed-in users*: REVOKE askers_of_tenancy FROM analysts_among_the_askers;*");
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "GRANT EXECUTE ON FUNCTION tenancy.holds_key(text), tenancy.readable_units() TO askers_of_tenancy", Cancellation);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(among.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*- analysts_among_the_askers, the role of a mapped token role, may execute 3 functions of tenancy, tenancy.caller_seat() among them with the privileges of askers_of_tenancy, which it has,*");

        // The first host's role is no member of that one, and is refused nothing for it.
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(granted.Provider, Cancellation);

        // The check reads the schema, so a function of the host's own that it keeps there counts as Tenancy's do:
        // what such a role is to run belongs in another schema.
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, """
            CREATE FUNCTION tenancy.widgets_counted_for_analysts() RETURNS bigint LANGUAGE sql STABLE AS 'SELECT 0::bigint';
            REVOKE ALL ON FUNCTION tenancy.widgets_counted_for_analysts() FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION tenancy.widgets_counted_for_analysts() TO analysts_given_functions;
            """, Cancellation);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(granted.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*- analysts_given_functions, the role of a mapped token role, may execute tenancy.widgets_counted_for_analysts() by a grant of its own,*");
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "ALTER FUNCTION tenancy.widgets_counted_for_analysts() SET SCHEMA widgets", Cancellation);
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(granted.Provider, Cancellation);
    }

    /// <summary>The roles of a host that maps the analysts' token role.</summary>
    private static void Map(PostgresRowLevelSecurityOptions roles) => roles.TokenRoles[Analyst] = AnalystRole;

    /// <summary>
    /// What such a host does to its database: the setup script with its roles, which makes the mapped role and lets
    /// the login role switch to it; the access files written with the same roles, with a rule of a module for the
    /// analyst; and every privilege on the tables for the role, so only the policies stand in its way.
    /// </summary>
    private static async Task MapAnalystsAsync(TestDatabase database)
    {
        var options = new PostgresRowLevelSecurityOptions();
        Map(options);

        await TenancyPostgres.ExecuteAsync(database.SuperuserConnectionString, PostgresRowAccess.SetupScript(options, TenancyPostgres.LoginRole), Cancellation);
        foreach (var script in TenancyPostgres.AccessScripts(rules: [.. WidgetRules.All, AnalystsDoAnythingWithWidgets], roles: RowAccessRoleNames.Of(options)))
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            $"GRANT USAGE ON SCHEMA tenancy, widgets TO {AnalystRole}; GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA tenancy, widgets TO {AnalystRole};",
            Cancellation);
    }

    private static string AnalystClaims(Guid identity) => $$"""{"sub":"{{identity}}","role":"analyst"}""";
}
