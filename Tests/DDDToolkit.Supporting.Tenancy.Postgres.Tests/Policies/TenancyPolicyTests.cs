using System.Collections.Concurrent;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The policies Tenancy's contribution writes, each proven by a query as the caller: on Tenancy's own tables, the
/// key where the use case asks it and the tenant always, and reads across tenants only where a person finds their
/// own seats; on every table kept to a tenant, every caller kept to its tenant whatever the rules add; and no cell
/// stricter than C#, which every use case run under the policies shows. Who reads the grants and the rights is
/// <see cref="RightsVisibilityTests"/>.
/// </summary>
public abstract class TenancyPolicyTests(TenancyPostgres postgres, TenancyNaming names)
{
    /// <summary>
    /// The database the cells of the matrix share under one naming, where they change nothing for good: each runs in a
    /// transaction it never commits, and a class's tests run one at a time.
    /// </summary>
    private static readonly ConcurrentDictionary<(TenancyPostgres, TenancyNaming), Lazy<Task<TestDatabase>>> Shared = new();

    private static readonly string[] TenancyTables =
    [
        "Tenants", "Organizations", "OrganizationUnits", "OrganizationUnitPaths", "Seats", "SeatPlacements", "SeatRoleGrants",
        "SeatRights", "Roles", "TenancyAccessRevisions", "TenantNote", "Invitations", "InvitationDigests",
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Cells => [.. PolicyMatrix.Cells.Keys];

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task The_policy_matrix_holds(string cell)
    {
        var asked = PolicyMatrix.Cells[cell];
        var database = asked.Caller.At is { } unit ? await HoldingAtAsync(asked.Caller.Person, unit, asked.Caller.Keys) : await SharedAsync();

        await using var caller = await AsCaller.PersonAsync(database, asked.Caller.Person.Identity, asked.Caller.Tenant, Cancellation);
        foreach (var before in asked.Before)
        {
            (await caller.ExecuteAsync(PolicyMatrix.Fill(before), Cancellation)).Should().BePositive(before);
        }

        var sql = PolicyMatrix.Fill(asked.Sql);
        if (asked.Command == "SELECT")
        {
            var count = await caller.ScalarAsync<long>(sql, Cancellation);
            (count > 0 ? Expectation.Rows : Expectation.NoRows).Should().Be(asked.Expected, sql);
            return;
        }

        switch (asked.Expected)
        {
            case Expectation.Refused:
                (await FluentActions.Awaiting(() => caller.ExecuteAsync(sql, Cancellation)).Should().ThrowAsync<PostgresException>(sql))
                    .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
                break;
            case Expectation.Fixed:
                var fixedRow = (await FluentActions.Awaiting(() => caller.ExecuteAsync(sql, Cancellation)).Should().ThrowAsync<PostgresException>(sql)).Which;
                fixedRow.SqlState.Should().Be(PostgresErrorCodes.CheckViolation, sql);
                fixedRow.ConstraintName.Should().EndWith("_is_fixed", sql);
                break;
            case Expectation.Rows:
                (await caller.ExecuteAsync(sql, Cancellation)).Should().BePositive(sql);
                break;
            default:
                (await caller.ExecuteAsync(sql, Cancellation)).Should().Be(0, sql);
                break;
        }
    }

    [Fact]
    public async Task The_directory_finds_own_seats_across_tenants_and_nothing_else()
    {
        var database = await SharedAsync();

        // Signed in, before picking a tenant: the tenant setting is empty.
        foreach (var (person, seats, tenants) in new[]
                 {
                     (Oli, new[] { Oli.Seat.Value, OliInOrchard.Value }, new[] { 1L, 2L }),
                     (Sue, [Sue.Seat.Value], [1L]),
                     (Quin, [Quin.Seat.Value], [3L]),
                 })
        {
            await using var caller = await AsCaller.PersonAsync(database, person.Identity, tenant: null, Cancellation);
            (await caller.ListAsync<Guid>("SELECT \"Id\" FROM tenancy.\"Seats\"", Cancellation)).Should().BeEquivalentTo(seats, "{0} finds their own seats, whatever their status or their tenant's", person.Name);
            (await caller.ListAsync<long>("SELECT \"Id\" FROM tenancy.\"Tenants\"", Cancellation)).Should().BeEquivalentTo(tenants);
            (await caller.ListAsync<long>("SELECT \"Id\" FROM tenancy.\"Organizations\"", Cancellation)).Should().BeEquivalentTo(tenants);

            foreach (var table in TenancyTables.Except(["Seats", "Tenants", "Organizations"]))
            {
                (await caller.ScalarAsync<long>($"SELECT count(*) FROM tenancy.\"{table}\"", Cancellation)).Should().Be(0, "{0} reads nothing of a tenant before picking one", table);
            }

            (await caller.ScalarAsync<long>("SELECT count(*) FROM widgets.\"Widgets\"", Cancellation)).Should().Be(0);
        }
    }

    [Fact]
    public async Task System_in_stays_in_its_tenant_and_has_no_bypass()
    {
        var database = await SharedAsync();

        await using (var harbor = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            (await harbor.ScalarAsync<string>("SELECT current_user", Cancellation)).Should().Be(PostgresRowLevelSecurityOptions.DefaultSystemInRole);
            (await harbor.ScalarAsync<string>("SELECT rolsuper || ' ' || rolbypassrls || ' ' || rolcanlogin FROM pg_catalog.pg_roles WHERE rolname = current_user", Cancellation))
                .Should().Be("false false false");

            (await harbor.ListAsync<long>("SELECT DISTINCT \"TenantId\" FROM tenancy.\"Seats\"", Cancellation)).Should().Equal(1L);
            (await harbor.ListAsync<long>("SELECT \"Id\" FROM tenancy.\"Tenants\"", Cancellation)).Should().Equal(1L);
            (await harbor.ListAsync<string>("SELECT \"Name\" FROM widgets.\"Widgets\" ORDER BY 1", Cancellation)).Should().Equal("Gauge", "Pump", "Valve");

            (await harbor.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Odette' WHERE \"Id\" = $1", Cancellation, Odette.Seat.Value)).Should().Be(0);
            (await harbor.ExecuteAsync("DELETE FROM widgets.\"Widgets\" WHERE \"Id\" = $1", Cancellation, Crate.Value)).Should().Be(0);
            await RefusedAsync(harbor, "INSERT INTO tenancy.\"TenancyAccessRevisions\" (\"TenantId\", \"Revision\") VALUES (2, 0)");
            await RefusedAsync(harbor, $"INSERT INTO widgets.\"Widgets\" (\"Id\", \"TenantId\", \"UnitId\", \"Name\", \"Version\") VALUES (gen_random_uuid(), 2, '{OrchardRoot.Value}', 'Barrel', 0)");
        }

        // Outside any tenant it acts in none.
        await using var nowhere = await AsCaller.SystemInAsync(database, tenant: null, TenancyWork.SystemScope, Cancellation);
        foreach (var table in TenancyTables)
        {
            (await nowhere.ScalarAsync<long>($"SELECT count(*) FROM tenancy.\"{table}\"", Cancellation)).Should().Be(0, table);
        }
    }

    [Fact]
    public async Task A_system_in_of_another_scope_cannot_write_tenancy_tables()
    {
        var database = await SharedAsync();
        await using var widgets = await AsCaller.SystemInAsync(database, Harbor, "widgets", Cancellation);

        // It reads Tenancy's rows of its tenant, as a module's read model does.
        (await widgets.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Seats\"", Cancellation)).Should().Be(6);
        (await widgets.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\"", Cancellation)).Should().BePositive();

        // It writes none of them.
        (await widgets.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Oliver' WHERE \"Id\" = $1", Cancellation, Oli.Seat.Value)).Should().Be(0);
        (await widgets.ExecuteAsync("DELETE FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, Oli.Seat.Value)).Should().Be(0);
        (await widgets.ExecuteAsync("UPDATE tenancy.\"TenancyAccessRevisions\" SET \"Revision\" = \"Revision\" + 1", Cancellation)).Should().Be(0);
        await RefusedAsync(widgets, $"INSERT INTO tenancy.\"SeatRoleGrants\" (\"RoleId\", \"SeatId\", \"UnitId\", \"StartsAt\", \"GrantedBy\") VALUES ('{HarborRoles.Administrator.Value}', '{Oli.Seat.Value}', '{NorthPier.Value}', now(), NULL)");

        // Its own module's rows it writes.
        (await widgets.ExecuteAsync("UPDATE widgets.\"Widgets\" SET \"Name\" = 'Big pump' WHERE \"Id\" = $1", Cancellation, Pump.Value)).Should().Be(1);
    }

    [Fact]
    public async Task A_grants_manager_at_one_unit_cannot_grant_at_the_root_by_a_query_as_the_caller()
    {
        var database = await SharedAsync();
        await using var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation);

        // Ada is placed at the root; Hiro gives roles at North and below. A role that manages no access, in his own name.
        await RefusedAsync(hiro, Give(HarborRoles.Watcher, Ada.Seat, HarborRoot, Hiro.Seat));
        (await hiro.ExecuteAsync(Give(HarborRoles.Watcher, Seth.Seat, North, Hiro.Seat), Cancellation)).Should().Be(1, "at North he may");
    }

    [Fact]
    public async Task A_grant_naming_a_role_of_another_tenant_is_refused()
    {
        var database = await SharedAsync();

        // Orchard's Watcher, given in Harbor: by a grants manager at the unit, and by the administrator.
        foreach (var person in new[] { Hiro, Ada })
        {
            await using var caller = await AsCaller.PersonAsync(database, person.Identity, Harbor, Cancellation);
            await RefusedAsync(caller, Give(OrchardRoles.Watcher, Oli.Seat, NorthPier, person.Seat));
        }
    }

    [Fact]
    public async Task A_units_manager_cannot_add_a_closure_row_that_widens_a_subtree()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // A row that puts South below North would give Seth's keys at North over South's widgets.
        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            (await seth.ExecuteAsync(
                    "INSERT INTO tenancy.\"OrganizationUnitPaths\" (\"AncestorId\", \"DescendantId\", \"TenantId\", \"Distance\") VALUES ($1, $2, 1, 1)",
                    Cancellation,
                    North.Value,
                    South.Value))
                .Should().Be(1, "the policy lets a units manager write the closure");
            (await seth.ListAsync<Guid>("SELECT tenancy.units_where_i_hold('widget.read')", Cancellation)).Should().Contain(South.Value, "inside the transaction it widens");

            var failure = await FluentActions.Awaiting(() => seth.CommitAsync(Cancellation)).Should().ThrowAsync<PostgresException>();
            failure.Which.ConstraintName.Should().Be("tenancy_paths_follow_the_tree");
        }

        await using var after = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation);
        (await after.ListAsync<Guid>("SELECT tenancy.units_where_i_hold('widget.read')", Cancellation)).Should().BeEquivalentTo([North.Value, NorthPier.Value]);
    }

    [Fact]
    public async Task A_units_manager_cannot_make_its_unit_a_root()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Without the index that keeps an organization to a single root, which a host is refused at start-up for:
        // the policies alone stand between a query and a second root, and they hold a seat by themselves.
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, $"DROP INDEX tenancy.\"{names.Of("IX_OrganizationUnits_TenantId_WhereRoot")}\"", Cancellation);

        // Seth manages units at North. A key held at a unit without a parent is held for the whole tenant, so North,
        // made a second root, would make him a manager of all of Harbor. He may take North's subtree out from under the
        // root in the closure, which the tree would then have to agree with at commit; the unit itself he may not
        // leave without a parent.
        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            (await seth.ScalarAsync<bool>("SELECT tenancy.holds_tenant_wide('tenancy.units.manage')", Cancellation)).Should().BeFalse();
            (await seth.ExecuteAsync(
                    "DELETE FROM tenancy.\"OrganizationUnitPaths\" WHERE \"AncestorId\" = $1 AND \"DescendantId\" = ANY ($2)",
                    Cancellation,
                    HarborRoot.Value,
                    new[] { North.Value, NorthPier.Value }))
                .Should().Be(2);
            await RefusedAsync(seth, $"UPDATE tenancy.\"OrganizationUnits\" SET \"ParentId\" = NULL WHERE \"Id\" = '{North.Value}'");
            (await seth.ScalarAsync<bool>("SELECT tenancy.holds_tenant_wide('tenancy.units.manage')", Cancellation)).Should().BeFalse();

            // What the unit says besides, he changes, and it keeps its parent.
            (await seth.ExecuteAsync("UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'North region' WHERE \"Id\" = $1", Cancellation, North.Value)).Should().Be(1);
        }

        // The root has no parent and keeps none: Ada, who manages units there, changes it as before.
        await using var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation);
        (await ada.ExecuteAsync("UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'Harbor Works' WHERE \"Id\" = $1", Cancellation, HarborRoot.Value)).Should().Be(1);
        await RefusedAsync(ada, $"UPDATE tenancy.\"OrganizationUnits\" SET \"ParentId\" = NULL WHERE \"Id\" = '{South.Value}'");
        await ada.CommitAsync(Cancellation);
    }

    [Fact]
    public async Task Making_a_placement_primary_demotes_the_old_one_elsewhere()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        // Eve's primary placement is at South, where Seth manages no seats; he places her at North and makes it primary.
        await services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => scoped.Seats().PlaceAsync(Eve.Seat, North, primary: false, Cancellation));
        await services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => scoped.Seats().MakePrimaryAsync(Eve.Seat, North, Cancellation));

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ListAsync<string>(
                    "SELECT u.\"Name\" || ' ' || p.\"IsPrimary\" FROM tenancy.\"SeatPlacements\" p JOIN tenancy.\"OrganizationUnits\" u ON u.\"Id\" = p.\"UnitId\" WHERE p.\"SeatId\" = $1 ORDER BY 1",
                    Cancellation,
                    Eve.Seat.Value))
                .Should().Equal("North true", "South false");
        }

        // Making South primary again takes the seats key at South, which he does not hold.
        await using var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation);
        (await seth.ExecuteAsync("UPDATE tenancy.\"SeatPlacements\" SET \"IsPrimary\" = false WHERE \"SeatId\" = $1 AND \"UnitId\" = $2", Cancellation, Eve.Seat.Value, North.Value)).Should().Be(1);
        await RefusedAsync(seth, $"UPDATE tenancy.\"SeatPlacements\" SET \"IsPrimary\" = true WHERE \"SeatId\" = '{Eve.Seat.Value}' AND \"UnitId\" = '{South.Value}'");
    }

    [Fact]
    public async Task A_consumer_table_keeps_every_caller_to_its_tenant()
    {
        var database = await SharedAsync();

        // Ada changes widgets at every unit of Harbor. A query that forgot the tenant sees Harbor's widgets and parts.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ListAsync<string>("SELECT \"Name\" FROM widgets.\"Widgets\" ORDER BY 1", Cancellation)).Should().Equal("Gauge", "Pump", "Valve");
            (await ada.ListAsync<string>("SELECT \"Name\" FROM widgets.\"WidgetPart\"", Cancellation)).Should().Equal("Gasket");
            (await ada.ExecuteAsync("UPDATE widgets.\"Widgets\" SET \"Name\" = 'Box' WHERE \"Id\" = $1", Cancellation, Crate.Value)).Should().Be(0);

            // At a unit where her rule lets her write, a widget of another tenant is refused by the restrictive policy alone.
            await RefusedAsync(ada, $"INSERT INTO widgets.\"Widgets\" (\"Id\", \"TenantId\", \"UnitId\", \"Name\", \"Version\") VALUES (gen_random_uuid(), 2, '{North.Value}', 'Barrel', 0)");
            await RefusedAsync(ada, $"UPDATE widgets.\"Widgets\" SET \"TenantId\" = 2 WHERE \"Id\" = '{Pump.Value}'");
        }

        // Oli reads by his rule where he holds the key, in the tenant he acts in.
        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ListAsync<string>("SELECT \"Name\" FROM widgets.\"Widgets\"", Cancellation)).Should().Equal("Valve");
        }

        await using var inOrchard = await AsCaller.PersonAsync(database, Oli.Identity, Orchard, Cancellation);
        (await inOrchard.ListAsync<string>("SELECT \"Name\" FROM widgets.\"Widgets\"", Cancellation)).Should().Equal("Crate");
    }

    [Fact]
    public async Task An_anonymous_rule_on_a_scoped_table_is_kept_closed()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await ApplyAsync(database, [.. WidgetRules.All, RowAccessRule.For<Widget>("Widgets for all to read", RowOperations.Read, "true", RowAccessRoles.Anonymous)]);

        await using var anonymous = await AsCaller.AnonymousAsync(database, Cancellation);
        (await anonymous.ScalarAsync<long>(
                $"SELECT count(*) FROM pg_catalog.pg_policies WHERE schemaname = 'widgets' AND tablename = '{names.Of("Widgets")}' AND 'anon' = ANY (roles) AND permissive = 'PERMISSIVE'",
                Cancellation))
            .Should().Be(1, "the rule is written");
        (await anonymous.ScalarAsync<long>("SELECT count(*) FROM widgets.\"Widgets\"", Cancellation)).Should().Be(0, "and the restrictive policy keeps it closed");
    }

    [Fact]
    public async Task A_system_in_rule_cannot_widen_past_its_tenant()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await ApplyAsync(database, [.. WidgetRules.All, RowAccessRule.For<Widget>("System work reads every widget", RowOperations.All, "true", RowAccessRoles.SystemIn)]);

        await using var harbor = await AsCaller.SystemInAsync(database, Harbor, "widgets", Cancellation);
        (await harbor.ScalarAsync<long>(
                $"SELECT count(*) FROM pg_catalog.pg_policies WHERE schemaname = 'widgets' AND tablename = '{names.Of("Widgets")}' AND 'ddd_system_in' = ANY (roles) AND permissive = 'PERMISSIVE' AND qual LIKE '%true%'",
                Cancellation))
            .Should().BePositive("the rule is written into the permissive policies of system work");
        (await harbor.ListAsync<string>("SELECT \"Name\" FROM widgets.\"Widgets\" ORDER BY 1", Cancellation)).Should().Equal("Gauge", "Pump", "Valve");
        (await harbor.ExecuteAsync("UPDATE widgets.\"Widgets\" SET \"Name\" = 'Box' WHERE \"Id\" = $1", Cancellation, Crate.Value)).Should().Be(0);
        await RefusedAsync(harbor, $"INSERT INTO widgets.\"Widgets\" (\"Id\", \"TenantId\", \"UnitId\", \"Name\", \"Version\") VALUES (gen_random_uuid(), 2, '{OrchardRoot.Value}', 'Barrel', 0)");
    }

    [Fact]
    public async Task A_scoped_table_without_a_rule_is_closed_to_seats()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await ApplyAsync(database, []);

        // Ada holds every key of Harbor; with no rule for signed-in users, the widgets are closed to her.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ScalarAsync<long>("SELECT count(*) FROM widgets.\"Widgets\"", Cancellation)).Should().Be(0);
            (await ada.ScalarAsync<long>("SELECT count(*) FROM widgets.\"WidgetPart\"", Cancellation)).Should().Be(0);
            (await ada.ExecuteAsync("UPDATE widgets.\"Widgets\" SET \"Name\" = 'Big pump' WHERE \"Id\" = $1", Cancellation, Pump.Value)).Should().Be(0);
            await RefusedAsync(ada, $"INSERT INTO widgets.\"Widgets\" (\"Id\", \"TenantId\", \"UnitId\", \"Name\", \"Version\") VALUES (gen_random_uuid(), 1, '{North.Value}', 'Barrel', 0)");
        }

        // The module's own system work still reads its tenant's.
        await using var widgets = await AsCaller.SystemInAsync(database, Harbor, "widgets", Cancellation);
        (await widgets.ScalarAsync<long>("SELECT count(*) FROM widgets.\"Widgets\"", Cancellation)).Should().Be(3);
    }

    [Fact]
    public async Task Anonymous_reads_and_writes_nothing()
    {
        var database = await SharedAsync();
        await using var anonymous = await AsCaller.AnonymousAsync(database, Cancellation);

        var tables = await anonymous.ListAsync<string>(
            "SELECT schemaname || '.\"' || tablename || '\"' FROM pg_catalog.pg_tables WHERE schemaname IN ('tenancy', 'widgets') ORDER BY 1",
            Cancellation);
        tables.Should().HaveCount(TenancyTables.Length + 2);
        foreach (var table in tables)
        {
            (await anonymous.ScalarAsync<long>($"SELECT count(*) FROM {table}", Cancellation)).Should().Be(0, "anonymous callers read nothing of {0}", table);
        }

        (await anonymous.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Nobody'", Cancellation)).Should().Be(0);
        (await anonymous.ExecuteAsync("DELETE FROM widgets.\"Widgets\"", Cancellation)).Should().Be(0);
        await RefusedAsync(anonymous, "INSERT INTO tenancy.\"TenancyAccessRevisions\" (\"TenantId\", \"Revision\") VALUES (9, 0)");

        // Nor may it ask Tenancy's functions.
        await RefusedAsync(anonymous, "SELECT tenancy.caller_seat()");
    }

    [Fact]
    public async Task A_host_entity_on_a_tenancy_class_follows_its_root()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        const string Note = "INSERT INTO tenancy.\"TenantNote\" (\"Id\", \"HostTenantId\", \"Text\") VALUES (gen_random_uuid(), {0}, 'Moored at the quay')";

        // A note on a tenant is written by whoever may change the tenant: the settings key for the whole tenant.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, Note, 1), Cancellation)).Should().Be(1);
            await RefusedAsync(ada, string.Format(System.Globalization.CultureInfo.InvariantCulture, Note, 2));
            await ada.CommitAsync(Cancellation);
        }

        await using (var odette = await AsCaller.PersonAsync(database, Odette.Identity, Orchard, Cancellation))
        {
            (await odette.ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, Note, 2), Cancellation)).Should().Be(1);
            await odette.CommitAsync(Cancellation);
        }

        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            await RefusedAsync(seth, string.Format(System.Globalization.CultureInfo.InvariantCulture, Note, 1));
            (await seth.ExecuteAsync("UPDATE tenancy.\"TenantNote\" SET \"Text\" = 'Gone'", Cancellation)).Should().Be(0);
            (await seth.ExecuteAsync("DELETE FROM tenancy.\"TenantNote\"", Cancellation)).Should().Be(0);
        }

        // It is read with the tenant, in the tenant the caller acts in: Oli's seat in Orchard shows him Orchard's
        // tenant row, not its notes.
        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ListAsync<long>("SELECT \"HostTenantId\" FROM tenancy.\"TenantNote\"", Cancellation)).Should().Equal(1L);
        }

        await using (var anonymous = await AsCaller.AnonymousAsync(database, Cancellation))
        {
            (await anonymous.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"TenantNote\"", Cancellation)).Should().Be(0);
        }

        // System work reads its tenant's in any scope, and writes them in Tenancy's own.
        await using (var widgets = await AsCaller.SystemInAsync(database, Harbor, "widgets", Cancellation))
        {
            (await widgets.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"TenantNote\"", Cancellation)).Should().Be(1);
            await RefusedAsync(widgets, string.Format(System.Globalization.CultureInfo.InvariantCulture, Note, 1));
        }

        await using var tenancy = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation);
        (await tenancy.ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, Note, 1), Cancellation)).Should().Be(1);
        await RefusedAsync(tenancy, string.Format(System.Globalization.CultureInfo.InvariantCulture, Note, 2));
    }

    [Fact]
    public async Task Every_use_case_passes_the_policies()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        // What a seat reads about its tenant, the same as the application reads it past the policies.
        await using (var unsecured = new TenancyServices(database, rowLevelSecurity: false))
        {
            foreach (var person in new[] { Oli, Seth, Ada })
            {
                async Task<object[]> ReadAsync(TenancyServices over)
                    => await over.BySeat(person.Identity, Harbor, person.Seat, async scoped => new object[]
                    {
                        await scoped.Directory().WhoAmIAsync(Cancellation),
                        await scoped.Directory().ListSeatsAsync(Cancellation),
                        await scoped.Directory().ListRolesAsync(Cancellation),
                        await scoped.Directory().ListUnitsAsync(Cancellation),
                    });

                var read = await ReadAsync(services);
                read.Should().BeEquivalentTo(await ReadAsync(unsecured), "the policies change nothing {0} reads through the use cases", person.Name);
                ((System.Collections.IEnumerable)read[1]).Cast<object>().Should().HaveCount(6, "every seat of Harbor is listed");
            }
        }

        var pat = await EveryUseCase.RunAsync(services, Cancellation);

        // What they did is there, as the owner reads it.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<string>("SELECT \"DisplayName\" FROM tenancy.\"Seats\" WHERE \"Id\" = $1", Cancellation, Oli.Seat.Value)).Should().Be("Oliver");
        (await owner.ScalarAsync<Guid>("SELECT \"UnitId\" FROM tenancy.\"SeatPlacements\" WHERE \"SeatId\" = $1 AND \"IsPrimary\"", Cancellation, Eve.Seat.Value)).Should().Be(North.Value);
        (await owner.ScalarAsync<string>("SELECT \"Status\" FROM tenancy.\"Roles\" WHERE \"Id\" = $1", Cancellation, HarborRoles.Watcher.Value)).Should().Be(names.Stored(RoleStatus.Archived));
        (await owner.ScalarAsync<Guid?>("SELECT \"ParentId\" FROM tenancy.\"OrganizationUnits\" WHERE \"Id\" = $1", Cancellation, NorthPier.Value)).Should().Be(South.Value);
        (await owner.ScalarAsync<string>("SELECT \"Shape\" || ' ' || \"Status\" FROM tenancy.\"Tenants\" WHERE \"Id\" = 40", Cancellation)).Should().Be(names.Stored(TenantShape.Hierarchical) + " " + names.Stored(TenantStatus.Closed));
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, Hiro.Seat.Value, HarborRoles.Operator.Value))
            .Should().Be(1, "his own Operator role now gives widget.read alone");
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = ANY ($1)", Cancellation, new[] { Oli.Seat.Value, Hiro.Seat.Value, Seth.Seat.Value }))
            .Should().Be(2, "Oli keeps his Operator role and Hiro the one he gave himself; what was taken away, Seth's own included, is gone");
        (await owner.ScalarAsync<string>("SELECT \"Status\" FROM tenancy.\"Seats\" WHERE \"Id\" = $1", Cancellation, pat.Value)).Should().Be(names.Stored(SeatStatus.Deactivated));
        (await owner.ScalarAsync<string>("SELECT \"Name\" FROM tenancy.\"OrganizationUnits\" WHERE \"Id\" = $1", Cancellation, North.Value)).Should().Be("North Coast");
    }

    [Fact]
    public async Task The_seat_a_request_acts_as_is_found_under_the_policies()
    {
        var database = await SharedAsync();
        await using var services = new TenancyServices(database);

        // Signed in, the tenant named but none chosen yet: the directory is read as the user, by the token's identity.
        foreach (var (person, slug, seat) in new[] { (Oli, "harbor", (SeatId?)Oli.Seat), (Oli, "orchard", OliInOrchard), (Ada, "harbor", Ada.Seat), (Sue, "harbor", null), (Quin, "quay", null), (Ada, "orchard", null) })
        {
            using (Callers.Begin(Caller.User(person.Identity)))
            {
                var caller = await services.InScopeAsync(scoped => scoped.GetRequiredService<TenantSelection<TenantId, SeatId>>().ResolveAsync(Caller.User(person.Identity), slug, Cancellation));
                caller.Seat.Should().Be(seat, "{0} in {1}: a suspended seat or tenant, or none, is nobody", person.Name, slug);
                caller.Kind.Should().Be(seat is null ? TenancyCallerKind.Nobody : TenancyCallerKind.Seat);
            }
        }
    }

    [Fact]
    public async Task A_grant_of_an_archived_role_is_taken_away_and_never_changed_by_a_query_as_the_caller()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ExecuteAsync("UPDATE tenancy.\"Roles\" SET \"Status\" = 'Archived' WHERE \"Id\" = $1", Cancellation, HarborRoles.Operator.Value)).Should().Be(1);
            await ada.CommitAsync(Cancellation);
        }

        // Oli's Operator grant at North Pier names a role that is archived: Hiro, who gives roles there, changes nothing of
        // it, and takes it away, as the use case lets him.
        await using var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation);
        (await hiro.ExecuteAsync("UPDATE tenancy.\"SeatRoleGrants\" SET \"Reason\" = 'Standing in' WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, Oli.Seat.Value, HarborRoles.Operator.Value))
            .Should().Be(0);
        (await hiro.ExecuteAsync("DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, Oli.Seat.Value, HarborRoles.Operator.Value))
            .Should().Be(1);
        await hiro.CommitAsync(Cancellation);
    }

    /// <summary>The database the matrix's cells share.</summary>
    private Task<TestDatabase> SharedAsync()
        => Shared.GetOrAdd((postgres, names), shared => new Lazy<Task<TestDatabase>>(() => shared.Item1.CreateDatabaseAsync(TenancyPostgres.Template.Secured, CancellationToken.None, shared.Item2))).Value;

    /// <summary>
    /// A database of its own where <paramref name="person"/> holds a role at <paramref name="unit"/>, placed there first
    /// where they are not: Harbor's Administrator, or one holding <paramref name="keys"/> alone.
    /// </summary>
    private async Task<TestDatabase> HoldingAtAsync(Person person, OrganizationUnitId unit, string[]? keys)
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await HoldAtAsync(database, person, unit, keys, Cancellation);
        return database;
    }

    /// <summary>Applies the access files with <paramref name="rules"/> as the widgets' rules.</summary>
    private static async Task ApplyAsync(TestDatabase database, IReadOnlyList<RowAccessRule> rules)
    {
        foreach (var script in TenancyPostgres.AccessScripts(rules: rules, names: database.Names))
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }
    }

    /// <summary>A grant of <paramref name="role"/> to <paramref name="seat"/> at <paramref name="unit"/>, as a statement.</summary>
    private static string Give(RoleId role, SeatId seat, OrganizationUnitId unit, SeatId by)
        => $"INSERT INTO tenancy.\"SeatRoleGrants\" (\"RoleId\", \"SeatId\", \"UnitId\", \"StartsAt\", \"EndsAt\", \"GrantedBy\", \"Reason\") VALUES ('{role.Value}', '{seat.Value}', '{unit.Value}', now(), NULL, '{by.Value}', NULL)";

    /// <summary>
    /// Runs <paramref name="sql"/> as <paramref name="caller"/>, expecting a policy to refuse it with 42501, in a savepoint,
    /// so the caller's transaction goes on.
    /// </summary>
    private static async Task RefusedAsync(AsCaller caller, string sql)
    {
        var refusal = await FluentActions.Awaiting(() => caller.AttemptAsync(sql, Cancellation)).Should().ThrowAsync<PostgresException>(sql);
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, sql);
    }
}

/// <summary>The policies on Tenancy's tables and on the tables kept to a tenant, under the names Entity Framework gives the tables and columns.</summary>
public sealed class TenancyPolicyTestsOnDefaultNames(TenancyPostgres postgres) : TenancyPolicyTests(postgres, TenancyNaming.Default);

/// <summary>The policies on Tenancy's tables and on the tables kept to a tenant, under snake_case names with enums stored as snake_case text.</summary>
public sealed class TenancyPolicyTestsOnSnakeCase(TenancyPostgres postgres) : TenancyPolicyTests(postgres, TenancyNaming.SnakeCase);
