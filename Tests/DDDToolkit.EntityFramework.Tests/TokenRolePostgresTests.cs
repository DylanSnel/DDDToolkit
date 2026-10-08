using System.Data;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The token role list against a real Postgres. The depot's host maps the token role <c>analyst</c> to a database
/// role of its own, which the setup script makes and an access script writes a policy for: analysts read every
/// pallet and change none. A token that names any other role, the roles the database happens to have included,
/// runs as none of them.
/// </summary>
[Collection(PalletDepotDatabase.Collection)]
public sealed class TokenRolePostgresTests(PalletDepotDatabase database) : IAsyncLifetime
{
    private const string Analyst = "analyst";

    private const string AnalystRole = "depot_analyst";

    private const string Pallets = PalletContext.Schema + ".\"Pallets\"";

    private static readonly Guid Carol = Guid.Parse("ca401000-0000-4000-8000-000000000013");

    private static readonly RowAccessRule AnalystsReadEveryPallet = RowAccessRule.For<Pallet>(
        "Analysts read every pallet", RowOperations.Read, "TRUE", RowAccessRoles.Token(Analyst));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// The depot as a host that maps a token role sets itself up: the setup script with its options, which makes
    /// the mapped role and lets the login role switch to it; the access script, written with the same roles;
    /// and the privileges on the tables, which are the host's to give.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        if (!database.Available)
        {
            return;
        }

        await database.RunAsOwnerAsync(PostgresRowAccess.SetupScript(Mapped(), PalletDepotDatabase.LoginRole), Cancellation);
        await database.RunAsOwnerAsync(ScriptWith(Mapped(), AnalystsReadEveryPallet), Cancellation);
        await database.RunAsOwnerAsync(
            $"GRANT USAGE ON SCHEMA {PalletContext.Schema} TO {AnalystRole}; GRANT SELECT ON ALL TABLES IN SCHEMA {PalletContext.Schema} TO {AnalystRole};",
            Cancellation);
    }

    /// <summary>
    /// The depot's policies as the other classes of the collection found them, its own two rules alone, and its
    /// pallets as they were seeded.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!database.Available)
        {
            return;
        }

        using var model = PalletDepotDatabase.Model();
        await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, PalletDepotDatabase.Rules, []), CancellationToken.None);
        await database.RestoreAsync();
    }

    [Fact]
    public async Task A_mapped_token_role_runs_as_its_database_role()
    {
        database.Require();

        var caller = new CallerOfTheTest { Current = Signed(Carol, Analyst) };
        await using var context = database.CreateContext(caller, Mapped());

        (await ScalarAsync(context, "SELECT current_user")).Should().Be(AnalystRole, "the token role picked the role the host mapped it to");
        (await ScalarAsync(context, "SELECT ddd.caller_role()")).Should().Be(Analyst, "the claims are the token's own, so a policy may ask the role the token carries");
        (await ScalarAsync(context, "SELECT ddd.caller_id()::text")).Should().Be(Carol.ToString(), "and whose token it is");
        (await context.Pallets.CountAsync(Cancellation)).Should().Be(2, "the analysts' rule lets them read every pallet");

        // The role is held to its policies, and to the privileges the host gave it.
        (await ScalarAsync(context, "SELECT (SELECT (rolbypassrls OR rolsuper OR rolcanlogin)::text FROM pg_catalog.pg_roles WHERE rolname = current_user)")).Should().Be("false");
        var remove = () => context.Database.ExecuteSqlRawAsync($"DELETE FROM {Pallets}", Cancellation);
        (await remove.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, "analysts read, and no more");

        // Signed-in users and anonymous callers are what they were.
        caller.Current = PalletDepotDatabase.Alice;
        await using var alice = database.CreateContext(caller, Mapped());
        (await ScalarAsync(alice, "SELECT current_user")).Should().Be("authenticated");
        (await alice.Pallets.CountAsync(Cancellation)).Should().Be(2);

        caller.Current = Caller.Anonymous;
        await using var nobody = database.CreateContext(caller, Mapped());
        (await ScalarAsync(nobody, "SELECT current_user")).Should().Be("anon");
        (await nobody.Pallets.CountAsync(Cancellation)).Should().Be(0);

        // A host that maps nothing refuses the same token: the role is on its list or it is on none.
        caller.Current = Signed(Carol, Analyst);
        await using var unmapped = database.CreateContext(caller, new PostgresRowLevelSecurityOptions());
        await FluentActions.Awaiting(() => unmapped.Pallets.CountAsync(Cancellation)).Should().ThrowAsync<RefusalException>();
    }

    [Fact]
    public async Task An_unknown_token_role_runs_as_anonymous_when_the_host_says_so()
    {
        database.Require();

        // Alice owns a pallet and, signed in, reads both. This token names her, with a role nobody listed.
        var caller = new CallerOfTheTest { Current = Signed(PalletDepotDatabase.AliceId, "intern") };
        await using var context = database.CreateContext(caller, Mapped(UnknownTokenRole.Anonymous));

        (await ScalarAsync(context, "SELECT current_user")).Should().Be("anon");
        (await ScalarAsync(context, "SELECT current_setting('request.jwt.claims', true)")).Should().Be("""{"role":"anon"}""", "the token's claims stayed behind");
        (await ScalarAsync(context, "SELECT ddd.caller_role()")).Should().Be("anon");
        (await ScalarAsync(context, "SELECT coalesce(ddd.caller_id()::text, 'nobody')")).Should().Be("nobody", "the database sees no user, so a rule about a user matches nobody");
        (await context.Pallets.CountAsync(Cancellation)).Should().Be(0, "an anonymous caller reads no pallet, whoever the token names");

        // Without the host's say-so the same token is refused, and nothing runs for it.
        await using var refusing = database.CreateContext(caller, Mapped());
        var refused = (await FluentActions.Awaiting(() => refusing.Pallets.CountAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refused.Code.Should().Be(ToolkitRefusals.RoleNotAllowed);
        refused.Arguments["Role"].Should().Be("intern");
        refusing.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData(PalletDepotDatabase.LoginRole)]
    [InlineData("ddd_system_in")]
    [InlineData(AnalystRole)]
    [InlineData("pg_read_all_data")]
    [InlineData("none")]
    public async Task A_forged_role_claim_never_runs_as_the_role_it_names(string forged)
    {
        database.Require();

        // Every one of these is a role this database has, or what resets the role: the superuser, the login role
        // that owns the tables, the scoped system role, the mapped role by its database name, one of Postgres's
        // own. A token that names it gets nothing, and where the host has unknown roles run as anonymous, that.
        (await database.ScalarAsOwnerAsync($"SELECT (pg_catalog.to_regrole('{forged}') IS NOT NULL OR '{forged}' = 'none')::text", Cancellation)).Should().Be("true");

        var caller = new CallerOfTheTest { Current = Signed(PalletDepotDatabase.AliceId, forged) };
        await using var refusing = database.CreateContext(caller, Mapped());

        var refused = (await FluentActions.Awaiting(() => refusing.Pallets.CountAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refused.Code.Should().Be(ToolkitRefusals.RoleNotAllowed);
        refused.Kind.Should().Be(RefusalKind.NotPermitted);
        refusing.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed, "nothing connected for it");

        await using var lenient = database.CreateContext(caller, Mapped(UnknownTokenRole.Anonymous));
        (await ScalarAsync(lenient, "SELECT current_user")).Should().Be("anon", "the least any caller gets, never the role the token names");
        (await ScalarAsync(lenient, "SELECT ddd.caller_role()")).Should().Be("anon", "and no policy that asks the role hears the forged one");
        (await lenient.Pallets.CountAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task A_caller_whose_token_role_is_on_no_list_is_refused_on_an_open_connection_too()
    {
        database.Require();

        var caller = new CallerOfTheTest { Current = PalletDepotDatabase.Alice };
        await using var context = database.CreateContext(caller, Mapped());
        await context.Database.OpenConnectionAsync(Cancellation);
        (await context.Pallets.CountAsync(Cancellation)).Should().Be(2);

        // The caller changes while the connection stays open, as a host that keeps one for several pieces of work has it.
        caller.Current = Signed(PalletDepotDatabase.BobId, "postgres");
        var refused = (await FluentActions.Awaiting(() => context.Pallets.CountAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refused.Code.Should().Be(ToolkitRefusals.RoleNotAllowed);
        FluentActions.Invoking(() => context.Pallets.Count()).Should().Throw<RefusalException>("the command without await is refused before it is sent as well");
        await FluentActions.Awaiting(() => context.Database.BeginTransactionAsync(Cancellation)).Should().ThrowAsync<RefusalException>("and so is a transaction");

        // Nothing was set for the refused caller: the connection is still the caller's it was opened for.
        caller.Current = PalletDepotDatabase.Alice;
        (await ScalarAsync(context, "SELECT current_user || ' ' || ddd.caller_id()::text")).Should().Be($"authenticated {PalletDepotDatabase.AliceId}");

        // A mapped role begun on the open connection is set before the next command, like any other caller.
        caller.Current = Signed(Carol, Analyst);
        (await ScalarAsync(context, "SELECT current_user")).Should().Be(AnalystRole);
    }

    [Fact]
    public async Task A_caller_whose_token_role_is_on_no_list_is_refused_inside_a_transaction_too()
    {
        database.Require();

        var caller = new CallerOfTheTest { Current = PalletDepotDatabase.Alice };
        await using var context = database.CreateContext(caller, Mapped());
        await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
        (await context.Pallets.CountAsync(Cancellation)).Should().Be(2);

        // A caller that changes inside a transaction is not set, and the transaction goes on as the caller it
        // began with. One whose role is on no list does not go on as that caller: its command is refused.
        caller.Current = Signed(PalletDepotDatabase.BobId, "postgres");
        var refused = (await FluentActions.Awaiting(() => context.Pallets.CountAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refused.Code.Should().Be(ToolkitRefusals.RoleNotAllowed);
        FluentActions.Invoking(() => context.Pallets.Count()).Should().Throw<RefusalException>();

        // Nothing was sent for it, so the transaction is whole and still the caller's who began it.
        caller.Current = PalletDepotDatabase.Alice;
        (await ScalarAsync(context, "SELECT current_user || ' ' || ddd.caller_id()::text")).Should().Be($"authenticated {PalletDepotDatabase.AliceId}");
        await transaction.CommitAsync(Cancellation);
    }

    [Fact]
    public async Task A_save_for_a_token_role_on_no_list_is_refused_and_writes_nothing()
    {
        database.Require();

        var before = await database.CountAsync(Cancellation);

        // A context that saves as UseDDDToolkit has it save, for a token of Alice's that names the superuser.
        await using (var context = database.CreateSavingContext(Signed(PalletDepotDatabase.AliceId, "postgres")))
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 91, "Smuggled", PalletDepotDatabase.AliceId));

            var refused = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
            refused.Code.Should().Be(ToolkitRefusals.RoleNotAllowed, "the caller is told its role gives no access, not that the database refused a change");
            FluentActions.Invoking(() => context.SaveChanges()).Should().Throw<RefusalException>().Which.Code.Should().Be(ToolkitRefusals.RoleNotAllowed);
            context.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed, "the save never connected");
        }

        // Inside a transaction begun for Alice, the caller changes to that token and saves. The refusal then comes
        // from the save's own command, which Entity Framework may hand on inside its own exception: either way
        // the save fails for the role, and nothing is sent that could be written.
        var caller = new CallerOfTheTest { Current = PalletDepotDatabase.Alice };
        await using (var open = database.CreateContext(caller, Mapped()))
        {
            await using var transaction = await open.Database.BeginTransactionAsync(Cancellation);
            caller.Current = Signed(PalletDepotDatabase.AliceId, "postgres");
            open.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 92, "Smuggled too", PalletDepotDatabase.AliceId));

            var failed = (await FluentActions.Awaiting(() => open.SaveChangesAsync(Cancellation)).Should().ThrowAsync<Exception>()).Which;
            (failed as RefusalException ?? failed.InnerException as RefusalException).Should().NotBeNull("the save failed because of the role, not for a reason of the database's")
                .And.Subject.As<RefusalException>().Code.Should().Be(ToolkitRefusals.RoleNotAllowed);

            caller.Current = PalletDepotDatabase.Alice;
            (await open.Pallets.AsNoTracking().CountAsync(Cancellation)).Should().Be(2, "inside the transaction there is nothing new either");
            await transaction.CommitAsync(Cancellation);
        }

        (await database.CountAsync(Cancellation)).Should().Be(before, "and no pallet was added");
    }

    [Fact]
    public async Task The_setup_script_makes_the_mapped_role_and_lets_the_login_role_switch_to_it()
    {
        database.Require();

        (await database.ScalarAsOwnerAsync(
            $"""
            SELECT concat_ws(' ', r.rolcanlogin::text, r.rolinherit::text, r.rolbypassrls::text, r.rolsuper::text,
                             pg_catalog.pg_has_role('{PalletDepotDatabase.LoginRole}', r.oid, 'MEMBER')::text,
                             pg_catalog.has_schema_privilege(r.oid, 'ddd', 'USAGE')::text,
                             pg_catalog.has_function_privilege(r.oid, 'ddd.written_in_this_transaction(xid)', 'EXECUTE')::text)
            FROM pg_catalog.pg_roles r WHERE r.rolname = '{AnalystRole}'
            """,
            Cancellation)).Should().Be("false false false false true true true", "no login, no inheriting, no bypass, not a superuser; the login role may switch to it; it may ask what the policies ask");

        // It can run again, as the first migration of a host does on every deployment.
        await database.RunAsOwnerAsync(PostgresRowAccess.SetupScript(Mapped(), PalletDepotDatabase.LoginRole), Cancellation);
        (await database.ScalarAsOwnerAsync($"SELECT count(*)::text FROM pg_catalog.pg_policies WHERE schemaname = '{PalletContext.Schema}' AND '{AnalystRole}' = ANY (roles)", Cancellation))
            .Should().Be("2", "the analysts' policy on the pallets, and the one that follows it on their stamps");
    }

    [Theory]
    [InlineData("LOGIN")]
    [InlineData("BYPASSRLS")]
    [InlineData("SUPERUSER")]
    public async Task A_role_that_could_pass_by_its_policies_is_refused_as_a_mapped_role(string attribute)
    {
        database.Require();

        var role = "depot_unsafe_" + attribute.ToLowerInvariant();
        var options = new PostgresRowLevelSecurityOptions { TokenRoles = { ["unsafe"] = role } };
        var says = $"The role {role} exists, but it can bypass row level security or log in. It must be NOLOGIN, without BYPASSRLS and not a superuser.";
        await database.RunAsOwnerAsync($"DROP ROLE IF EXISTS {role}; CREATE ROLE {role} NOINHERIT {attribute};", Cancellation);
        try
        {
            var setup = () => database.RunAsOwnerAsync(PostgresRowAccess.SetupScript(options, PalletDepotDatabase.LoginRole), Cancellation);
            (await setup.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(says);

            // An access script whose policy names the role checks it again, for a database the setup script never saw.
            var script = () => database.RunAsOwnerAsync(
                ScriptWith(options, RowAccessRule.For<Pallet>("The unsafe read every pallet", RowOperations.Read, "TRUE", RowAccessRoles.Token("unsafe"))),
                Cancellation);
            (await script.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(says);

            (await database.ScalarAsOwnerAsync($"SELECT pg_catalog.pg_has_role('{PalletDepotDatabase.LoginRole}', '{role}', 'MEMBER')::text", Cancellation))
                .Should().Be("false", "the script failed as a whole, so the login role was not given the role");
            (await database.ScalarAsOwnerAsync($"SELECT count(*)::text FROM pg_catalog.pg_policies WHERE schemaname = '{PalletContext.Schema}' AND '{role}' = ANY (roles)", Cancellation))
                .Should().Be("0", "and no policy was written for it");
        }
        finally
        {
            await database.RunAsOwnerAsync($"DROP ROLE IF EXISTS {role};", CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_mapped_role_that_owns_tables_or_reaches_other_callers_is_refused()
    {
        database.Require();

        await database.RunAsOwnerAsync(
            """
            CREATE ROLE depot_owning NOLOGIN NOINHERIT;
            CREATE TABLE public.kept_by_the_owning_role (id integer);
            ALTER TABLE public.kept_by_the_owning_role OWNER TO depot_owning;
            CREATE ROLE depot_shared NOLOGIN NOINHERIT;
            GRANT depot_shared TO authenticated WITH INHERIT TRUE;
            """,
            Cancellation);
        try
        {
            // Postgres does not hold a table's owner to its policies.
            var owning = () => database.RunAsOwnerAsync(
                PostgresRowAccess.SetupScript(new PostgresRowLevelSecurityOptions { TokenRoles = { ["owner"] = "depot_owning" } }, PalletDepotDatabase.LoginRole), Cancellation);
            (await owning.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().StartWith("The role depot_owning has the privileges of a role that owns tables");

            // A policy for a role is a policy for every role that has its privileges.
            var shared = () => database.RunAsOwnerAsync(
                PostgresRowAccess.SetupScript(new PostgresRowLevelSecurityOptions { TokenRoles = { ["shared"] = "depot_shared" } }, PalletDepotDatabase.LoginRole), Cancellation);
            (await shared.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(
                "The role depot_shared is granted to authenticated or anon, so their callers would get every policy written for it. Grant it only to the role the application logs in as.");

            // And the scoped system role's privileges must not reach the holders of a token.
            await database.RunAsOwnerAsync($"GRANT ddd_system_in TO {AnalystRole} WITH INHERIT TRUE;", Cancellation);
            var scoped = () => database.RunAsOwnerAsync(PostgresRowAccess.SetupScript(Mapped(), PalletDepotDatabase.LoginRole), Cancellation);
            (await scoped.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(
                $"The role ddd_system_in is granted to authenticated, anon or {AnalystRole}, so their callers would get every policy written for it. Grant it only to the role the application logs in as.");
        }
        finally
        {
            await database.RunAsOwnerAsync(
                $"""
                REVOKE ddd_system_in FROM {AnalystRole};
                REVOKE depot_shared FROM authenticated;
                DROP TABLE IF EXISTS public.kept_by_the_owning_role;
                DROP ROLE IF EXISTS depot_owning;
                DROP ROLE IF EXISTS depot_shared;
                """,
                CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("authenticated")]
    [InlineData("anon")]
    public async Task The_setup_script_refuses_a_mapped_role_that_has_another_callers_privileges(string callers)
    {
        database.Require();

        // The other way round from a mapped role granted to a caller's role: here the caller's role was granted to
        // the mapped role, so the holder of an appraiser's token would be a signed-in user, or an anonymous
        // caller, besides, with every policy and every privilege written for them.
        var role = "depot_appraiser_as_" + callers;
        var options = new PostgresRowLevelSecurityOptions { TokenRoles = { ["appraiser"] = role } };
        await database.RunAsOwnerAsync($"CREATE ROLE {role} NOLOGIN NOINHERIT; GRANT {callers} TO {role} WITH INHERIT TRUE;", Cancellation);
        try
        {
            var setup = () => database.RunAsOwnerAsync(PostgresRowAccess.SetupScript(options, PalletDepotDatabase.LoginRole), Cancellation);
            (await setup.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(HasTheirPrivileges(role));
            (await database.ScalarAsOwnerAsync($"SELECT pg_catalog.pg_has_role('{PalletDepotDatabase.LoginRole}', '{role}', 'MEMBER')::text", Cancellation))
                .Should().Be("false", "the script failed as a whole, so the login role cannot switch to the role");

            // What is refused is having the privileges, not the membership: one that hands nothing on leaves the
            // role with what was written for it alone, and the script runs.
            await database.RunAsOwnerAsync($"GRANT {callers} TO {role} WITH INHERIT FALSE;", Cancellation);
            await setup.Should().NotThrowAsync();
            (await database.ScalarAsOwnerAsync($"SELECT pg_catalog.pg_has_role('{PalletDepotDatabase.LoginRole}', '{role}', 'MEMBER')::text", Cancellation)).Should().Be("true");
        }
        finally
        {
            await DropAsync(role);
        }
    }

    [Theory]
    [InlineData("authenticated")]
    [InlineData("anon")]
    public async Task An_access_script_refuses_a_mapped_role_that_has_another_callers_privileges(string callers)
    {
        database.Require();

        // A database whose setup script never saw the map, as one on Supabase: the access script checks the role
        // its policy names itself, in its prelude.
        var role = "depot_surveyor_as_" + callers;
        var options = new PostgresRowLevelSecurityOptions { TokenRoles = { ["surveyor"] = role } };
        var surveyors = RowAccessRule.For<Pallet>("Surveyors read every pallet", RowOperations.Read, "TRUE", RowAccessRoles.Token("surveyor"));
        await database.RunAsOwnerAsync($"CREATE ROLE {role} NOLOGIN NOINHERIT; GRANT {callers} TO {role} WITH INHERIT TRUE;", Cancellation);
        try
        {
            var script = () => database.RunAsOwnerAsync(ScriptWith(options, surveyors), Cancellation);
            (await script.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(HasTheirPrivileges(role));

            // A script that writes the privileges too checks every mapped role, whether a policy names it or not:
            // it takes privileges back from the role, and would leave it those of the caller's role.
            using (var model = PalletDepotDatabase.Model())
            {
                var granting = PostgresRowAccess.Script(model, PalletDepotDatabase.Rules, [], new RowAccessExport { Roles = RowAccessRoleNames.From(options), WriteGrants = true });
                var unnamed = () => database.RunAsOwnerAsync(granting, Cancellation);
                (await unnamed.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(HasTheirPrivileges(role));
            }

            (await database.ScalarAsOwnerAsync($"SELECT count(*)::text FROM pg_catalog.pg_policies WHERE schemaname = '{PalletContext.Schema}' AND '{role}' = ANY (roles)", Cancellation))
                .Should().Be("0", "each script failed as a whole, so no policy was written for the role");
            (await database.ScalarAsOwnerAsync($"SELECT count(*)::text FROM pg_catalog.pg_policies WHERE schemaname = '{PalletContext.Schema}' AND '{AnalystRole}' = ANY (roles)", Cancellation))
                .Should().Be("2", "and the policies that were there were not dropped");
            (await database.ScalarAsOwnerAsync($"SELECT pg_catalog.pg_has_role('{PalletDepotDatabase.LoginRole}', '{role}', 'MEMBER')::text", Cancellation))
                .Should().Be("false", "nor can the login role switch to it");

            // Taken back, as the message says, the same script runs and writes the role its policy.
            await database.RunAsOwnerAsync($"REVOKE {callers} FROM {role};", Cancellation);
            await script.Should().NotThrowAsync();
            (await database.ScalarAsOwnerAsync($"SELECT count(*)::text FROM pg_catalog.pg_policies WHERE schemaname = '{PalletContext.Schema}' AND '{role}' = ANY (roles)", Cancellation))
                .Should().Be("2", "the surveyors' policy on the pallets, and the one that follows it on their stamps");
        }
        finally
        {
            using var model = PalletDepotDatabase.Model();
            await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, PalletDepotDatabase.Rules, []), CancellationToken.None);
            await DropAsync(role);
        }
    }

    [Fact]
    public async Task An_access_script_makes_the_mapped_role_its_policy_names()
    {
        database.Require();

        // A host that runs no setup script, as one on Supabase does: the access script makes the role it writes a
        // policy for, where the database does not have it.
        var options = new PostgresRowLevelSecurityOptions { TokenRoles = { [Analyst] = AnalystRole, ["examiner"] = "depot_examiner" } };
        var examiners = RowAccessRule.For<Pallet>("Examiners read every pallet", RowOperations.Read, "TRUE", RowAccessRoles.Token("examiner"));
        try
        {
            (await database.ScalarAsOwnerAsync("SELECT (pg_catalog.to_regrole('depot_examiner') IS NULL)::text", Cancellation)).Should().Be("true");

            await database.RunAsOwnerAsync(ScriptWith(options, AnalystsReadEveryPallet, examiners), Cancellation);
            await database.RunAsOwnerAsync(ScriptWith(options, AnalystsReadEveryPallet, examiners), Cancellation);

            (await database.ScalarAsOwnerAsync(
                "SELECT concat_ws(' ', rolcanlogin::text, rolinherit::text, rolbypassrls::text, rolsuper::text, pg_catalog.has_schema_privilege(oid, 'ddd', 'USAGE')::text) FROM pg_catalog.pg_roles WHERE rolname = 'depot_examiner'",
                Cancellation)).Should().Be("false false false false true");
            (await database.ScalarAsOwnerAsync(
                $"SELECT string_agg(policyname, ', ' ORDER BY policyname) FROM pg_catalog.pg_policies WHERE schemaname = '{PalletContext.Schema}' AND tablename = 'Pallets' AND roles = '{{depot_examiner}}'",
                Cancellation)).Should().Be("Examiners read every pallet (select) for depot_examiner");
        }
        finally
        {
            using var model = PalletDepotDatabase.Model();
            await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, PalletDepotDatabase.Rules, []), CancellationToken.None);
            await database.RunAsOwnerAsync(
                "DO $$ BEGIN IF pg_catalog.to_regrole('depot_examiner') IS NOT NULL THEN DROP OWNED BY depot_examiner; DROP ROLE depot_examiner; END IF; END $$;",
                CancellationToken.None);
        }
    }

    /// <summary>The roles of the depot's host: the defaults, and the analysts' token role mapped to a role of its own.</summary>
    private static PostgresRowLevelSecurityOptions Mapped(UnknownTokenRole unknown = UnknownTokenRole.Refuse)
        => new() { TokenRoles = { [Analyst] = AnalystRole }, UnknownTokenRole = unknown };

    /// <summary>The depot's access script with its own rules and <paramref name="rules"/>, written with the roles of <paramref name="options"/>.</summary>
    private static string ScriptWith(PostgresRowLevelSecurityOptions options, params RowAccessRule[] rules)
    {
        using var model = PalletDepotDatabase.Model();
        return PostgresRowAccess.Script(model, [.. PalletDepotDatabase.Rules, .. rules], [], new RowAccessExport { Roles = RowAccessRoleNames.From(options) });
    }

    /// <summary>What a script says of the mapped role <paramref name="role"/> that has the privileges of the user's or the anonymous caller's role.</summary>
    private static string HasTheirPrivileges(string role)
        => $"The role {role} has the privileges of authenticated or anon, so the holder of a token mapped to it would get every policy and every privilege written for those callers. Take that grant back: a mapped role has what is written for it and no more.";

    /// <summary>Takes <paramref name="role"/>, a role one test made, off the server with what it was given, whether the test got as far as making it or not.</summary>
    private Task DropAsync(string role)
        => database.RunAsOwnerAsync(
            $"DO $$ BEGIN IF pg_catalog.to_regrole('{role}') IS NOT NULL THEN DROP OWNED BY {role}; DROP ROLE {role}; END IF; END $$;",
            CancellationToken.None);

    /// <summary>The caller a validated token makes, signed for <paramref name="user"/> with <paramref name="role"/> as its role claim.</summary>
    private static Caller Signed(Guid user, string role)
        => Callers.FromClaims($$"""{"sub":"{{user}}","role":"{{role}}"}""");

    /// <summary>One value, from SQL the tests themselves write; nothing here comes from outside.</summary>
    private static async Task<string?> ScalarAsync(DbContext context, string sql)
    {
#pragma warning disable EF1003
        return await context.Database.SqlQueryRaw<string>(sql + " AS \"Value\"").SingleAsync(Cancellation);
#pragma warning restore EF1003
    }
}
