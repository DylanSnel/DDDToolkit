using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The application's operators under the policies: staff whose token carries a role the host mapped to a database
/// role of its own and listed as an operator's. That role reads every row of Tenancy's tables and of the access
/// history, in every tenant, and writes none; on a module's table it reads what a rule of the module admits and
/// writes nothing, whatever the rule says. Without operators, nothing is written for them.
/// </summary>
/// <remarks>
/// Roles are the server's, and the other tests of the run share the server: the operators' role has a name no
/// other test uses. For the same reason the class runs under the default names alone.
/// </remarks>
public sealed class OperatorPolicyTests(TenancyPostgres postgres)
{
    /// <summary>The verified identity an operator signs in with: a person with no seat anywhere.</summary>
    private static readonly Guid Operator = Guid.Parse("d0000000-0000-4000-8000-000000000077");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Claims => $$"""{"sub":"{{Operator}}","role":"{{TallyRules.OperatorTokenRole}}"}""";

    [Fact]
    public async Task An_operator_reads_every_tenant_and_writes_nothing()
    {
        var database = await TallyDatabase.CreateAsync(postgres, Cancellation, operators: true);
        await using var services = TallyDatabase.Services(database, operators: true);

        // The seed keeps no note on a tenant: one, so every table has a row to read and to try a write with.
        await using (var seeding = await AsCaller.OwnerAsync(database, Cancellation))
        {
            await seeding.ExecuteAsync("INSERT INTO tenancy.\"TenantNote\" (\"Id\", \"HostTenantId\", \"Text\") VALUES (gen_random_uuid(), 1, 'Moored at the quay')", Cancellation);
            await seeding.CommitAsync(Cancellation);
        }

        // An invitation, so its table and the table of its token's digest each hold a row.
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped =>
            scoped.Invitations().IssueAsync("wren@example.test", North, HarborRoles.Watcher, grantUntil: null, "Wren", lifetime: null, Cancellation));

        // What there is to read: Tenancy's tables, the table of the application's entity on a tenant, and the history.
        const string Digests = "tenancy.\"InvitationDigests\"";
        var rows = new Dictionary<string, long>(StringComparer.Ordinal);
        string digest;
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            var tables = await owner.ListAsync<string>(
                "SELECT pg_catalog.quote_ident(schemaname) || '.' || pg_catalog.quote_ident(tablename) FROM pg_catalog.pg_tables WHERE schemaname = 'tenancy' OR (schemaname = 'ddd' AND tablename = 'EventLog') ORDER BY 1",
                Cancellation);
            foreach (var table in tables)
            {
                rows[table] = await owner.ScalarAsync<long>($"SELECT count(*) FROM {table}", Cancellation);
            }

            rows.Keys.Should().Contain(["tenancy.\"Tenants\"", "tenancy.\"Seats\"", "tenancy.\"SeatRights\"", "tenancy.\"SeatRoleGrants\"", "tenancy.\"TenantNote\"", "ddd.\"EventLog\""]);
            rows["tenancy.\"Tenants\""].Should().Be(3);
            rows["tenancy.\"Invitations\""].Should().Be(1);
            rows[Digests].Should().Be(1);

            // The one table an operator reads no row of: its row as the owner reads it, to try a write with.
            digest = await owner.ScalarAsync<string>($"SELECT pg_catalog.row_to_json(held)::text FROM {Digests} AS held", Cancellation);
            rows["ddd.\"EventLog\""].Should().BeGreaterThan(20, "seeding three tenants raised that many events");

            // And what a change of status or of a parent would reach: seats in use, a seat that is not, units below a root.
            foreach (var reached in new[] { "tenancy.\"Seats\" WHERE \"Status\" = 'Active'", "tenancy.\"Seats\" WHERE \"Status\" <> 'Active'", "tenancy.\"OrganizationUnits\" WHERE \"ParentId\" IS NOT NULL" })
            {
                (await owner.ScalarAsync<long>($"SELECT count(*) FROM {reached}", Cancellation)).Should().BePositive(reached);
            }
        }

        // As a query of the application's own that forgot every condition would run: the mapped role, with every
        // privilege on the tables, and no tenant named on the connection.
        await using (var asOperator = await AsCaller.TokenRoleAsync(database, TallyRules.OperatorRole, TallyRules.OperatorTokenRole, Operator, tenant: null, Cancellation))
        {
            (await asOperator.ScalarAsync<string>("SELECT current_user::text", Cancellation)).Should().Be(TallyRules.OperatorRole);

            foreach (var (table, count) in rows)
            {
                // But for the digests of the invitations' tokens: nobody's role reads those, an operator's included.
                var read = table == Digests ? 0 : count;
                (await asOperator.ScalarAsync<long>($"SELECT count(*) FROM {table}", Cancellation)).Should().Be(read, $"an operator reads every row of {table}, in every tenant");
                (await asOperator.AttemptAsync($"DELETE FROM {table}", Cancellation)).Should().Be(0, $"and removes none of {table}");

                // A row the table holds already, added again: the operators' policy refuses it before any key or trigger is asked.
                count.Should().BePositive($"{table} has a row to try with");
                var held = table == Digests ? $"pg_catalog.json_populate_record(NULL::{Digests}, '{digest}')" : table + " LIMIT 1";
                await RefusedAsync(asOperator, $"INSERT INTO {table} SELECT * FROM {held}");
            }

            // Nor a row of its own making in the history, said to be the operator's own act.
            await RefusedAsync(
                asOperator,
                "INSERT INTO ddd.\"EventLog\" (\"Id\", \"EventName\", \"Version\", \"Payload\", \"OccurredAt\", \"RecordedAt\", \"ActedByKind\", \"ActedById\", \"TenantId\") " +
                $"VALUES (gen_random_uuid(), 'tenancy.seat-renamed', 1, '{{}}', now(), now(), 'operator', '{Operator}', 1)");

            await RefusedAsync(asOperator, "SELECT * FROM tenancy.invitation_of_digest('\\x00'::bytea)");

            (await asOperator.AttemptAsync("UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Renamed'", Cancellation)).Should().Be(0, "nor changes one");
            (await asOperator.AttemptAsync("UPDATE tenancy.\"Tenants\" SET \"Status\" = 'Closed'", Cancellation)).Should().Be(0);

            // Nor where a trigger and a policy hold a seat to what it manages: the trigger on a seat's status
            // holds seats alone, and an operator is none, so it is the operators' own policies that keep every
            // seat's rights where they are and every unit under its parent.
            (await asOperator.AttemptAsync("UPDATE tenancy.\"Seats\" SET \"Status\" = 'Suspended' WHERE \"Status\" = 'Active'", Cancellation)).Should().Be(0, "an operator suspends no seat");
            (await asOperator.AttemptAsync("UPDATE tenancy.\"Seats\" SET \"Status\" = 'Active' WHERE \"Status\" <> 'Active'", Cancellation)).Should().Be(0, "and gives no suspended seat its rights back");
            (await asOperator.AttemptAsync("UPDATE tenancy.\"OrganizationUnits\" SET \"ParentId\" = NULL WHERE \"ParentId\" IS NOT NULL", Cancellation)).Should().Be(0, "and makes no unit a root");
            await RefusedAsync(asOperator, "INSERT INTO tenancy.\"TenancyAccessRevisions\" (\"TenantId\", \"Revision\") VALUES (99, 0)");
            await RefusedAsync(asOperator, "INSERT INTO tenancy.\"SeatRights\" SELECT * FROM tenancy.\"SeatRights\" LIMIT 1");

            // The questions a seat asks are not an operator's to ask: it has no seat for them to be about.
            await RefusedAsync(asOperator, "SELECT tenancy.caller_seat()");
            await RefusedAsync(asOperator, "SELECT tenancy.holds_key('tenancy.seats.manage')");
        }

        // In the application: no seat is chosen for an operator, whatever tenant the request names, and the
        // tenants' directory answers it every tenant, read as its own database role.
        using (Callers.Begin(Callers.FromClaims(Claims)))
        {
            var selected = await services.InScopeAsync(scoped => scoped.GetRequiredService<TenantSelection<TenantId, SeatId>>().ResolveAsync(Callers.Ambient!, "harbor", Cancellation));
            selected.Should().Be(HostCaller.Nobody(TenancyRefusals.NotSeated));

            using (TenancyCallers.Begin(selected))
            {
                var (role, first) = await services.InScopeAsync(async scoped => (
                    await scoped.Tenancy().Database.SqlQueryRaw<string>("SELECT current_user::text AS \"Value\"").SingleAsync(Cancellation),
                    await scoped.TenantDirectory().ListAsync(after: null, size: 2, Cancellation)));

                role.Should().Be(TallyRules.OperatorRole);
                first.Items.Should().Equal(
                    new HostTenancy.TenantListing(Harbor, "harbor", "Harbor Works", TenantStatus.Active, ActiveSeats: 5),
                    new HostTenancy.TenantListing(Orchard, "orchard", "Orchard Works", TenantStatus.Active, ActiveSeats: 2));

                var second = await services.InScopeAsync(scoped => scoped.TenantDirectory().ListAsync(first.Next, size: 2, Cancellation));
                second.Items.Should().Equal(new HostTenancy.TenantListing(Quay, "quay", "Quay Works", TenantStatus.Suspended, ActiveSeats: 1));
                second.Next.Should().BeNull();
            }
        }

        // A seat is no operator, whatever it holds: Harbor's administrator is refused, and reads Harbor alone anyway.
        var refused = await FluentActions.Awaiting(() => services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.TenantDirectory().ListAsync(null, 10, Cancellation)))
            .Should().ThrowAsync<DDDToolkit.Exceptions.RefusalException>();
        refused.Which.Code.Should().Be(TenancyRefusals.OperatorsOnly);
        (await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Tenancy().Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM tenancy.\"Seats\"").SingleAsync(Cancellation)))
            .Should().Be(6, "Harbor's seats, the suspended one included, and no other tenant's");

        // And the start-up check finds the operators' policy where the options say there are operators.
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(services.Provider, Cancellation);
    }

    [Fact]
    public async Task A_consumer_table_lets_an_operator_read_only_what_the_module_admits()
    {
        var database = await TallyDatabase.CreateAsync(postgres, Cancellation, operators: true);
        await using var services = TallyDatabase.Services(database, operators: true);

        // The tallies' module shares some of its tallies with the operators, in two tenants, and keeps one to itself.
        await services.BySystemIn(
            Harbor,
            async scoped =>
            {
                scoped.Tallies().Tallies.AddRange(new Tally(TallyId.CreateSequential(), Harbor, North, "Crates", shared: true), new Tally(TallyId.CreateSequential(), Harbor, North, "Barrels"));
                await scoped.Tallies().SaveChangesAsync(Cancellation);
            },
            scope: TallyContext.Schema);
        await services.BySystemIn(
            Orchard,
            async scoped =>
            {
                scoped.Tallies().Tallies.Add(new Tally(TallyId.CreateSequential(), Orchard, OrchardRoot, "Apples", shared: true));
                await scoped.Tallies().SaveChangesAsync(Cancellation);
            },
            scope: TallyContext.Schema);

        var tallies = TallyDatabase.NamesOf(database);
        await using (var asOperator = await AsCaller.TokenRoleAsync(database, TallyRules.OperatorRole, TallyRules.OperatorTokenRole, Operator, tenant: null, Cancellation))
        {
            // What the module's rule admits, across tenants, and nothing of a module that has no rule for operators.
            (await asOperator.ListAsync<string>($"SELECT {tallies.Column("Name")} FROM {tallies.Table} ORDER BY 1", Cancellation)).Should().Equal("Apples", "Crates");
            (await asOperator.ScalarAsync<long>("SELECT count(*) FROM widgets.\"Widgets\"", Cancellation)).Should().Be(0, "the widgets' module admits no operator");
            (await asOperator.ScalarAsync<long>("SELECT count(*) FROM widgets.\"WidgetPart\"", Cancellation)).Should().Be(0);

            // The module's rule lets operators add, change and remove shared tallies. Tenancy's policies leave the reading.
            (await asOperator.AttemptAsync($"UPDATE {tallies.Table} SET {tallies.Column("Name")} = 'Renamed'", Cancellation)).Should().Be(0);
            (await asOperator.AttemptAsync($"DELETE FROM {tallies.Table}", Cancellation)).Should().Be(0);
            await RefusedAsync(
                asOperator,
                $"INSERT INTO {tallies.Table} ({tallies.Columns}) VALUES ({TallyNames.Values(Harbor, North, "Smuggled", seat: null, "operator", Operator, shared: true)})");
        }

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ListAsync<string>($"SELECT {tallies.Column("Name")} FROM {tallies.Table} ORDER BY 1", Cancellation)).Should().Equal("Apples", "Barrels", "Crates");
            (await owner.ListAsync<string>(
                $"SELECT policyname || ' ' || permissive FROM pg_catalog.pg_policies WHERE schemaname = 'tallies' AND roles = '{{{TallyRules.OperatorRole}}}' ORDER BY 1",
                Cancellation)).Should().Equal(
                    $"Operators only read (delete) for {TallyRules.OperatorRole} RESTRICTIVE",
                    $"Operators only read (insert) for {TallyRules.OperatorRole} RESTRICTIVE",
                    $"Operators only read (select) for {TallyRules.OperatorRole} RESTRICTIVE",
                    $"Operators only read (update) for {TallyRules.OperatorRole} RESTRICTIVE",
                    $"Operators work on shared tallies (delete) for {TallyRules.OperatorRole} PERMISSIVE",
                    $"Operators work on shared tallies (insert) for {TallyRules.OperatorRole} PERMISSIVE",
                    $"Operators work on shared tallies (select) for {TallyRules.OperatorRole} PERMISSIVE",
                    $"Operators work on shared tallies (update) for {TallyRules.OperatorRole} PERMISSIVE");
        }

        // In the application an operator acts in no tenant, so a query of the module's names that it looks across them.
        using (Callers.Begin(Callers.FromClaims(Claims)))
        using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.NotSeated)))
        {
            var (filtered, across) = await services.InScopeAsync(async scoped => (
                await scoped.Tallies().Tallies.Select(tally => tally.Name).ToListAsync(Cancellation),
                await scoped.Tallies().Tallies.IgnoreQueryFilters([TenancyQueryFilter.Name]).Select(tally => tally.Name).OrderBy(name => name).ToListAsync(Cancellation)));

            filtered.Should().BeEmpty("the tenant filter matches nothing for a caller in no tenant");
            across.Should().Equal("Apples", "Crates");
        }
    }

    [Fact]
    public void Without_operator_roles_no_operator_policy_is_written()
    {
        var plain = string.Concat(TenancyPostgres.AccessScripts());

        plain.Should().NotContain("Operators").And.NotContain(TallyRules.OperatorRole);
        string.Concat(TenancyPostgres.AccessScripts(operators: [])).Should().Be(plain, "no operators' token role, no change");
        string.Concat(TenancyPostgres.AccessScripts(roles: Mapped())).Should().NotContain("Operators", "a mapped token role nobody called an operator's is closed out like any other")
            .And.Contain($"CREATE POLICY \"Closed to token roles (all) for {TallyRules.OperatorRole}\" ON tenancy.\"Tenants\"");

        var script = string.Concat(TenancyPostgres.AccessScripts(roles: Mapped(), operators: [TallyRules.OperatorTokenRole]));

        script.Should().Contain(
            $"CREATE POLICY \"Operators read every tenant (select) for {TallyRules.OperatorRole}\" ON tenancy.\"Tenants\" FOR SELECT TO {TallyRules.OperatorRole}\n" +
            "    USING (true);\n");
        script.Should().Contain(
            $"CREATE POLICY \"Operators only read (update) for {TallyRules.OperatorRole}\" ON tenancy.\"Seats\" AS RESTRICTIVE FOR UPDATE TO {TallyRules.OperatorRole}\n" +
            "    USING (false)\n" +
            "    WITH CHECK (false);\n");
        script.Should().Contain(
            $"CREATE POLICY \"Operators only read (insert) for {TallyRules.OperatorRole}\" ON widgets.\"Widgets\" AS RESTRICTIVE FOR INSERT TO {TallyRules.OperatorRole}\n" +
            "    WITH CHECK (false);\n");
        script.Should().Contain(
            $"CREATE POLICY \"Operators only read (select) for {TallyRules.OperatorRole}\" ON widgets.\"Widgets\" AS RESTRICTIVE FOR SELECT TO {TallyRules.OperatorRole}\n" +
            "    USING (true);\n");
        script.Should().NotContain($"Closed to token roles (all) for {TallyRules.OperatorRole}", "an operator's role is held otherwise");
        script.Should().NotContain($"Operators read every tenant (select) for {TallyRules.OperatorRole}\" ON widgets.", "what an operator reads of a module is the module's to say");
        script.Should().Contain($"            CREATE ROLE {TallyRules.OperatorRole} NOLOGIN NOINHERIT;\n", "the access file makes the role its policies are for");

        // On each of Tenancy's own tables, the invitations, the table of the application's entity and the history:
        // thirteen tables. Not on the digests of the invitations' tokens, which no caller's role reads.
        Count(script, "CREATE POLICY \"Operators read every tenant (select) for ").Should().Be(13);
        script.Should().Contain($"CREATE POLICY \"Operators read every tenant (select) for {TallyRules.OperatorRole}\" ON tenancy.\"Invitations\"");
        script.Should().NotContain($"CREATE POLICY \"Operators read every tenant (select) for {TallyRules.OperatorRole}\" ON tenancy.\"InvitationDigests\"");
        script.Should().Contain($"CREATE POLICY \"Operators only read (select) for {TallyRules.OperatorRole}\" ON tenancy.\"InvitationDigests\"");
        foreach (var command in new[] { "select", "insert", "update", "delete" })
        {
            Count(script, $"CREATE POLICY \"Operators only read ({command}) for {TallyRules.OperatorRole}\"").Should().Be(
                Count(script, "CREATE POLICY \"Closed to anonymous callers (all) for anon\""),
                "wherever anonymous callers are kept out, an operator's role only reads");
        }

        static int Count(string text, string part) => (text.Length - text.Replace(part, "", StringComparison.Ordinal).Length) / part.Length;
    }

    [Fact]
    public void An_unmapped_operator_role_is_refused_by_the_export()
    {
        // The contribution names the role, and the host mapped it to no database role: no query would ever run as it.
        FluentActions.Invoking(() => TenancyPostgres.AccessScripts(operators: [TallyRules.OperatorTokenRole]))
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*of the row access contribution {typeof(TenancyRowAccessContribution).FullName} is for '@token:{TallyRules.OperatorTokenRole}', which no policy or grant can be for.*mapped to no database role*");

        FluentActions.Invoking(() => new TenancyRowAccessContribution(TenancyPostgres.Catalogue, [" "])).Should().Throw<ArgumentException>("an operator's token role has a name");
        new TenancyRowAccessContribution(TenancyPostgres.Catalogue, ["operator", "auditor", "operator"]).OperatorTokenRoles.Should().Equal("auditor", "operator");
        new TenancyRowAccessContribution(TenancyPostgres.Catalogue).OperatorTokenRoles.Should().BeEmpty();
    }

    [Fact]
    public void An_operators_token_role_is_mapped_to_a_role_of_its_own()
    {
        // The role of a signed-in user: the policies that let an operator read every tenant would let every seat.
        var asAUser = RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { [TallyRules.OperatorTokenRole] = "authenticated" } };
        FluentActions.Invoking(() => TenancyPostgres.AccessScripts(roles: asAUser, operators: [TallyRules.OperatorTokenRole]))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The operator token role 'operator' is mapped to 'authenticated', the role of a signed-in user:*Map it to a database role of its own*");

        // A role another token role shares, which is no operator's: its holders would read every tenant as well.
        var shared = RowAccessRoleNames.Default with
        {
            TokenRoles = new Dictionary<string, string> { [TallyRules.OperatorTokenRole] = TallyRules.OperatorRole, ["analyst"] = TallyRules.OperatorRole },
        };
        FluentActions.Invoking(() => TenancyPostgres.AccessScripts(roles: shared, operators: [TallyRules.OperatorTokenRole]))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The operator token role 'operator' and the token role 'analyst', which is no operator's, are both mapped to 'tenancy_operator':*");

        // Two operators' token roles on one role are that one role, with one set of policies.
        var both = string.Concat(TenancyPostgres.AccessScripts(roles: shared, operators: [TallyRules.OperatorTokenRole, "analyst"]));
        both.Should().Be(string.Concat(TenancyPostgres.AccessScripts(roles: Mapped(), operators: [TallyRules.OperatorTokenRole])));
    }

    [Fact]
    public async Task The_start_up_check_names_operators_the_database_does_not_let_read()
    {
        // The seeded template: its access files were written without operators.
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);

        // The options name an operator's token role the host mapped to no database role.
        await using (var unmapped = new TenancyServices(database, operators: [TallyRules.OperatorTokenRole]))
        {
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(unmapped.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("The operator token roles operator are mapped to no database role of their own*");
        }

        await using (var asAUser = new TenancyServices(database, roles: roles => roles.TokenRoles[TallyRules.OperatorTokenRole] = roles.UserRole, operators: [TallyRules.OperatorTokenRole]))
        {
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(asAUser.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("The operator token roles operator are mapped to no database role of their own*");
        }

        // Mapped, and the contribution was never told: an operator would be answered an empty directory.
        await using (var untold = new TenancyServices(database, roles: TallyDatabase.WithOperators, operators: [TallyRules.OperatorTokenRole]))
        {
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(untold.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage($"No policy lets the operators' database roles {TallyRules.OperatorRole}* read the tenants, so an operator would be answered an empty directory.*[TenancyOperators]*");
        }

        // Without operators, the check asks nothing about them.
        await using var none = new TenancyServices(database, roles: TallyDatabase.WithOperators);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(none.Provider, Cancellation);
    }

    /// <summary>The roles of a host that maps the operators' token role to a role of its own.</summary>
    private static RowAccessRoleNames Mapped()
        => RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { [TallyRules.OperatorTokenRole] = TallyRules.OperatorRole } };

    private static async Task RefusedAsync(AsCaller caller, string sql)
    {
        var refusal = await FluentActions.Awaiting(() => caller.AttemptAsync(sql, Cancellation)).Should().ThrowAsync<PostgresException>(sql);
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, sql);
    }
}
