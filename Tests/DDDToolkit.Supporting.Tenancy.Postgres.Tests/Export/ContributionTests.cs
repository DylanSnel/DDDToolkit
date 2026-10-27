using System.Text.Json;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Tenancy's contribution as the export writes it: functions, policies and triggers from the application's model
/// and catalogue, that apply to a database the application's migrations made, run again unchanged, keep Tenancy's
/// tables to themselves, and follow the catalogue's marks when they change.
/// </summary>
public sealed class ContributionTests(TenancyPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_access_files_apply_the_use_cases_seed_under_them_and_they_run_again()
    {
        // The secured template is the plain one with the access files applied and the seed written through the
        // use cases, as system work, under the policies and the triggers.
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);

        await using (var owner = await OwnerAsync(database))
        {
            (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Tenants\"", Cancellation)).Should().Be(3);
            (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Seats\"", Cancellation)).Should().Be(9);
            (await owner.ScalarAsync<long>("SELECT count(*) FROM widgets.\"Widgets\"", Cancellation)).Should().Be(4);
        }

        foreach (var script in TenancyPostgres.AccessScripts())
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await using var again = await OwnerAsync(database);
        (await again.ListAsync<string>(
            """
            SELECT c.relname FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname IN ('tenancy', 'widgets') AND c.relkind = 'r' AND NOT c.relrowsecurity ORDER BY 1
            """,
            Cancellation)).Should().BeEmpty("every table of Tenancy's, the application's entities on them and the widgets' has row level security");
    }

    [Fact]
    public async Task Every_function_is_the_callers_and_the_scoped_system_roles_alone_and_runs_with_an_empty_path()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await using var owner = await OwnerAsync(database);

        // Each function: whether it runs as its owner, its settings, and whether a signed-in user, system work in a
        // tenant and an anonymous caller may execute it. The questions are both callers'; what answers about other
        // seats' rights, and the questions that take the tenant as an argument, are a signed-in user's alone; what
        // writes a tenant's rights again and what reads across tenants is system work's alone; and what only the
        // functions that run as their owner ask, and the triggers' functions, are nobody's. The rows a module reads
        // are both callers' too, and have no settings: Postgres folds them into the module's query.
        var functions = await owner.ListAsync<string>(
            """
            SELECT p.proname || ' ' || p.prosecdef || ' ' || coalesce(pg_catalog.array_to_string(p.proconfig, ','), 'none')
                   || ' ' || pg_catalog.has_function_privilege('authenticated', p.oid, 'EXECUTE')
                   || ' ' || pg_catalog.has_function_privilege('ddd_system_in', p.oid, 'EXECUTE')
                   || ' ' || pg_catalog.has_function_privilege('anon', p.oid, 'EXECUTE')
            FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'tenancy' ORDER BY p.proname
            """,
            Cancellation);

        functions.Should().Equal(
            "administrator_remains true search_path=\"\" false false false",
            "caller_permissions false none true true false",
            "caller_seat true search_path=\"\" true true false",
            "caller_seat_row true search_path=\"\" true true false",
            "caller_tenant true search_path=\"\" true true false",
            "grant_is_fixed false search_path=\"\" false false false",
            "holds_at_tenant true search_path=\"\" true true false",
            "holds_permission true search_path=\"\" true true false",
            "holds_permission_in_tenant true search_path=\"\" true false false",
            "identity_tenant_names true search_path=\"\" true false false",
            "identity_tenants true search_path=\"\" true true false",
            "invitation_of_digest true search_path=\"\" false true false",
            "invitation_terms_are_fixed false search_path=\"\" false false false",
            "paths_follow_the_tree true search_path=\"\" false false false",
            "permission_is_contained false search_path=\"\" true true false",
            "permission_is_live false search_path=\"\" false false false",
            "permission_manages_access false search_path=\"\" true true false",
            "permissions_a_move_changes true search_path=\"\" true false false",
            "placement_is_fixed false search_path=\"\" false false false",
            "readable_units true search_path=\"\" true true false",
            "rewrite_tenant_rights true search_path=\"\" false true false",
            "rights_backed_by_grants true search_path=\"\" false false false",
            "rights_follow_grants true search_path=\"\" false false false",
            "role_follows_its_starter_role true search_path=\"\" false false false",
            "role_permissions_in_use true search_path=\"\" false true false",
            "role_starter_role_is_fixed false search_path=\"\" false false false",
            "roles_with_permission true search_path=\"\" true true false",
            "roles_with_permission_in_tenant true search_path=\"\" true false false",
            "seat_identity_is_fixed false search_path=\"\" false false false",
            "seat_in_tenant true search_path=\"\" true false false",
            "seat_status_is_managed true search_path=\"\" false false false",
            "seated_in_tenant true search_path=\"\" true false false",
            "seats_holding_permission_at true search_path=\"\" true false false",
            "seats_in_my_units true search_path=\"\" true true false",
            "seats_of_identity true search_path=\"\" false true false",
            "starter_role_permissions false search_path=\"\" true true false",
            "system_tenant false search_path=\"\" true true false",
            "tenant_administrators true search_path=\"\" true false false",
            "tenant_placements false none true true false",
            "tenant_roles_with_starter_roles false none true true false",
            "tenant_seats false none true true false",
            "tenant_unit_paths false none true true false",
            "tenant_units false none true true false",
            "tenants_to_sweep true search_path=\"\" false true false",
            "unit_parent true search_path=\"\" true true false",
            "units_named true search_path=\"\" true false false",
            "units_where_i_hold_permission true search_path=\"\" true true false",
            "units_where_i_hold_permission_in_tenant true search_path=\"\" true false false");

        // Only what is folded into its caller's query runs without an empty search path, and nothing that does runs as
        // its owner: a function that runs with its owner's rights never looks a name up along the caller's path.
        string[] folded =
        [
            TenancyFunctionNames.CallerPermissions, TenancyFunctionNames.TenantPlacements, TenancyFunctionNames.TenantRoles,
            TenancyFunctionNames.TenantSeats, TenancyFunctionNames.TenantUnitPaths, TenancyFunctionNames.TenantUnits,
        ];
        functions.Where(function => function.Contains(" none ", StringComparison.Ordinal)).Select(function => function.Split(' ')[0]).Should().Equal(folded);
        functions.Should().NotContain(function => function.Contains(" true none ", StringComparison.Ordinal));
        (await owner.ListAsync<string>(
                """
                SELECT p.proname || ' ' || p.provolatile::pg_catalog.text || ' ' || p.proisstrict || ' ' || p.proretset || ' ' || l.lanname
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace JOIN pg_catalog.pg_language l ON l.oid = p.prolang
                WHERE n.nspname = 'tenancy' AND p.proname = ANY ($1) ORDER BY 1
                """,
                Cancellation,
                (object)folded))
            .Should().Equal(folded.Select(function => function + " s false true sql"), "each is a stable SQL function that answers rows and is not strict, which is what Postgres folds");
    }

    [Fact]
    public async Task The_function_that_says_which_permissions_manage_access_follows_the_catalogues_marks()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);

        await using (var owner = await OwnerAsync(database))
        {
            (await owner.ScalarAsync<bool>("SELECT tenancy.permission_manages_access('tenancy.grants.manage')", Cancellation)).Should().BeTrue("Tenancy's own permissions manage access, all but the one that reads the history");
            (await owner.ScalarAsync<bool>("SELECT tenancy.permission_manages_access('tenancy.history.view')", Cancellation)).Should().BeFalse("reading what happened manages no access");
            (await owner.ScalarAsync<bool>("SELECT tenancy.permission_manages_access('widget.create')", Cancellation)).Should().BeFalse("the TestHost marks none of its own");
        }

        // The application marks one of its permissions: the next export writes the function again, and that access file is
        // the migration.
        var marked = TenancyCatalogue.Build(HostCatalogue.Application with { AccessManagingPermissions = [HostCatalogue.WidgetCreate] }, []);
        foreach (var script in TenancyPostgres.AccessScripts(marked))
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await using var after = await OwnerAsync(database);
        (await after.ScalarAsync<bool>("SELECT tenancy.permission_manages_access('widget.create')", Cancellation)).Should().BeTrue();
        (await after.ScalarAsync<bool>("SELECT tenancy.permission_manages_access('widget.read')", Cancellation)).Should().BeFalse();
        (await after.ScalarAsync<bool>("SELECT tenancy.permission_manages_access('tenancy.roles.manage')", Cancellation)).Should().BeTrue();
    }

    [Fact]
    public async Task The_function_that_says_which_permissions_are_contained_follows_the_catalogues_setting()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);

        // On by default: the permissions that manage access, and those alone.
        await using (var owner = await OwnerAsync(database))
        {
            (await owner.ScalarAsync<bool>("SELECT tenancy.permission_is_contained('tenancy.grants.manage')", Cancellation)).Should().BeTrue();
            (await owner.ScalarAsync<bool>("SELECT tenancy.permission_is_contained('tenancy.history.view')", Cancellation)).Should().BeFalse();
            (await owner.ScalarAsync<bool>("SELECT tenancy.permission_is_contained('widget.create')", Cancellation)).Should().BeFalse();
        }

        // Turned off, the next export writes the function again to answer no permission, and the marks stay as they were.
        var off = TenancyCatalogue.Build(HostCatalogue.Application with { ContainAccessManagingPermissions = false }, []);
        foreach (var script in TenancyPostgres.AccessScripts(off))
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await using var after = await OwnerAsync(database);
        (await after.ScalarAsync<bool>("SELECT tenancy.permission_is_contained('tenancy.grants.manage')", Cancellation)).Should().BeFalse();
        (await after.ScalarAsync<bool>("SELECT tenancy.permission_is_contained('tenancy.roles.manage')", Cancellation)).Should().BeFalse();
        (await after.ScalarAsync<bool>("SELECT tenancy.permission_manages_access('tenancy.grants.manage')", Cancellation)).Should().BeTrue();
    }

    [Fact]
    public async Task The_function_that_says_which_permissions_a_copy_of_a_starter_role_holds_follows_the_catalogue()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);

        await using (var owner = await OwnerAsync(database))
        {
            (await owner.ScalarAsync<string[]>("SELECT tenancy.starter_role_permissions('operator')", Cancellation)).Should().Equal("widget.change", "widget.create", "widget.read");
            (await owner.ScalarAsync<string[]>("SELECT tenancy.starter_role_permissions('host-admin')", Cancellation)).Should().BeEquivalentTo(TenancyPostgres.Catalogue.LivePermissions, "an administrators' starter role holds every live permission");
            (await owner.ScalarAsync<bool>("SELECT tenancy.starter_role_permissions('lookout') IS NULL", Cancellation)).Should().BeTrue("a starter role the catalogue does not have holds nothing");
        }

        // A permission added to a starter role: the next export writes the function again.
        var wider = TenancyCatalogue.Build(
            HostCatalogue.Application with
            {
                StarterRoles = [.. HostCatalogue.Application.StarterRoles.Select(starterRole => starterRole.Key == HostCatalogue.WatcherStarterRole ? starterRole with { Permissions = [HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate] } : starterRole)],
            },
            []);
        foreach (var script in TenancyPostgres.AccessScripts(wider))
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await using (var after = await OwnerAsync(database))
        {
            (await after.ScalarAsync<string[]>("SELECT tenancy.starter_role_permissions('watcher')", Cancellation)).Should().Equal("widget.create", "widget.read");
        }

        // A starter role may hold no permission at all, a role people are given before it is decided what it may do: its copy
        // holds none, which is not the same as a starter role the catalogue does not have.
        var keyless = TenancyCatalogue.Build(
            HostCatalogue.Application with
            {
                StarterRoles = [.. HostCatalogue.Application.StarterRoles.Select(starterRole => starterRole.Key == HostCatalogue.WatcherStarterRole ? starterRole with { Permissions = [] } : starterRole)],
            },
            []);
        var scripts = TenancyPostgres.AccessScripts(keyless);
        string.Concat(scripts).Should().Contain("WHEN 'watcher' THEN ARRAY[]::pg_catalog.text[]");
        foreach (var script in scripts)
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await using var emptied = await OwnerAsync(database);
        (await emptied.ScalarAsync<string[]>("SELECT tenancy.starter_role_permissions('watcher')", Cancellation)).Should().BeEmpty();
        (await emptied.ScalarAsync<bool>("SELECT tenancy.starter_role_permissions('watcher') IS NULL", Cancellation)).Should().BeFalse();
    }

    [Theory]
    [InlineData("jsonb")]
    [InlineData("json")]
    public async Task Permissions_stored_as_a_json_document_give_access_files_that_apply(string storeType)
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation);
        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            $"ALTER TABLE tenancy.\"Roles\" ALTER COLUMN \"Permissions\" TYPE {storeType} USING pg_catalog.to_jsonb(\"Permissions\")::{storeType}",
            Cancellation);

        using (PermissionsAsDocumentContext tenancy = storeType == "jsonb" ? new PermissionsAsJsonbContext() : new PermissionsAsJsonContext())
        {
            tenancy.Model.GetEntityTypes().Single(entity => entity.ClrType == typeof(HostRole)).FindProperty(nameof(HostRole.Permissions))!.GetColumnType().Should().Be(storeType);
            foreach (var script in PostgresRowAccess.Scripts([tenancy], [], [], new RowAccessExport { Contributions = [new TenancyRowAccessContribution(TenancyPostgres.Catalogue)] }))
            {
                await TenancyPostgres.ExecuteAsync(database.ConnectionString, script.Script, Cancellation);
            }
        }

        // A role's permissions change: the trigger that keeps rights backed compares them, as the document they are, and the
        // one that writes the rights reads them from it.
        await using var owner = await OwnerAsync(database);
        (await owner.ExecuteAsync(
                $"INSERT INTO tenancy.\"Roles\" (\"Id\", \"NormalizedName\", \"Version\", \"TenantId\", \"Name\", \"Description\", \"FromStarterRole\", \"Status\", \"Permissions\") VALUES (gen_random_uuid(), 'CLERK', 0, 1, 'Clerk', '', 'watcher', 'Active', '[\"{HostCatalogue.WidgetRead}\"]'::{storeType})",
                Cancellation))
            .Should().Be(1);
        (await owner.ExecuteAsync($"UPDATE tenancy.\"Roles\" SET \"Permissions\" = '[\"{HostCatalogue.WidgetRead}\", \"{HostCatalogue.WidgetCreate}\"]'::{storeType}", Cancellation)).Should().Be(1);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.roles_with_permission('widget.read')", Cancellation)).Should().Be(0, "the owner has no seat to ask for");

        // A module reads the permissions as an array of text all the same, as they are in the document.
        (await owner.ScalarAsync<string[]>("SELECT \"Permissions\" FROM tenancy.tenant_roles_with_starter_roles()", Cancellation)).Should().Equal(HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate);

        // A seat granted the role holds each permission of the document, once, and the ones the catalogue has live alone.
        (await owner.ExecuteAsync(
                $"""
                INSERT INTO tenancy."Seats" ("Id", "Version", "TenantId", "Identity", "DisplayName", "Kind", "Status") VALUES ('{TenancySeed.Oli.Seat.Value}', 0, 1, gen_random_uuid(), 'Oli', 0, 'Active');
                INSERT INTO tenancy."SeatPlacements" ("UnitId", "SeatId", "IsPrimary", "PlacedAt", "TenantId") VALUES ('{TenancySeed.HarborRoot.Value}', '{TenancySeed.Oli.Seat.Value}', true, now(), 1);
                UPDATE tenancy."Roles" SET "Permissions" = '["{HostCatalogue.WidgetRead}", "{HostCatalogue.WidgetCreate}", "{HostCatalogue.WidgetRead}", "widget.polish"]'::{storeType};
                INSERT INTO tenancy."SeatRoleGrants" ("RoleId", "SeatId", "UnitId", "StartsAt", "EndsAt", "GrantedBy", "Reason")
                SELECT r."Id", '{TenancySeed.Oli.Seat.Value}', '{TenancySeed.HarborRoot.Value}', now(), NULL, NULL, NULL FROM tenancy."Roles" r;
                """,
                Cancellation))
            .Should().BePositive();
        (await owner.ListAsync<string>("SELECT \"Permission\" FROM tenancy.\"SeatRights\" ORDER BY 1", Cancellation)).Should().Equal(HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
        await owner.CommitAsync(Cancellation);
    }

    [Fact]
    public void A_modules_rule_asks_tenancys_questions_once_per_statement_in_tenancys_schema()
    {
        var scripts = TenancyPostgres.AccessScripts();

        scripts.Should().HaveCount(2);
        scripts[0].Should().Contain("CREATE OR REPLACE FUNCTION \"tenancy\".units_where_i_hold_permission(permission text)", "Tenancy's context comes first: the widgets' policies ask its functions");
        scripts[1].Should().Contain("\"UnitId\" = ANY (ARRAY(SELECT \"tenancy\".units_where_i_hold_permission('widget.read')))");
        scripts[1].Should().NotContain("CREATE OR REPLACE FUNCTION tenancy.", "only the context that maps Tenancy's tables makes its functions");
    }

    [Fact]
    public void The_trigger_functions_name_the_types_they_declare_with_their_schema()
    {
        // They run as their owner with an empty search path, where a type's name is still looked for in the caller's
        // temporary schema first: a type of Postgres's own is written with pg_catalog in front, unless the grammar
        // spells it itself, as it does bigint, the TestHost's tenant ids.
        var tenancy = TenancyPostgres.AccessScripts()[0];

        tenancy.Should().Contain("    tenant bigint;\n").And.Contain("    touched pg_catalog.uuid[];\n").And.Contain("    checked pg_catalog.uuid;\n")
            .And.Contain("    of_seat pg_catalog.uuid;\n").And.Contain("    at_unit pg_catalog.uuid;\n").And.Contain("    of_role pg_catalog.uuid;\n");
        tenancy.Should().NotMatchRegex(@"\n    \w+ (uuid|text)(\[\])?;", "every declaration of a type that is not a keyword names its schema");
    }

    [Fact]
    public void A_rule_or_another_contribution_cannot_add_a_policy_to_tenancys_tables()
    {
        using var tenancy = TenancyPostgres.TenancyModel();
        var seats = tenancy.Model.GetEntityTypes().Single(entity => entity.GetTableName() == "Seats");
        var intruder = new Intruder(seats.ClrType);

        var writing = () => PostgresRowAccess.Scripts(
            [tenancy],
            [],
            [],
            new RowAccessExport { Contributions = [new TenancyRowAccessContribution(TenancyPostgres.Catalogue), intruder] });

        writing.Should().Throw<InvalidOperationException>().WithMessage("*keeps to itself*");
    }

    [Fact]
    public void Every_table_kept_to_a_tenant_keeps_every_caller_to_it_and_lets_system_work_of_any_scope_in()
    {
        using var widgets = TenancyPostgres.WidgetModel();
        var result = new TenancyRowAccessContribution(TenancyPostgres.Catalogue).Contribute(widgets, new RowAccessExport())!;

        result.Functions.Should().BeEmpty("a module's context defines none of Tenancy's functions");
        result.ExclusiveTables.Should().BeEmpty("the module's rules still add their policies");
        result.Policies.Select(policy => (policy.Table.GetTableName(), policy.Name, policy.Command, policy.Role, policy.Restrictive))
            .Should().BeEquivalentTo(
            [
                ("Widgets", "System work in its tenant", "ALL", "@system-in", false),
                ("Widgets", "Kept to its tenant", "ALL", "@system-in", true),
                ("Widgets", "Kept to its tenant", "ALL", "@user", true),
                ("Widgets", "Closed to anonymous callers", "ALL", "@anonymous", true),
                ("WidgetPart", "System work in its tenant", "ALL", "@system-in", false),
                ("WidgetPart", "Kept to its tenant", "ALL", "@system-in", true),
                ("WidgetPart", "Kept to its tenant", "ALL", "@user", true),
                ("WidgetPart", "Closed to anonymous callers", "ALL", "@anonymous", true),
            ]);
        result.Policies.Single(policy => policy.Table.GetTableName() == "WidgetPart" && policy.Role == "@user").Using
            .Should().Be("EXISTS (SELECT 1 FROM \"widgets\".\"Widgets\" r WHERE r.\"Id\" = \"widgets\".\"WidgetPart\".\"WidgetId\" AND r.\"TenantId\" = (SELECT {fn:tenancy/caller_tenant}()))",
                "a table of the aggregate's entities is kept to the tenant through the row it belongs to");
    }

    private static Task<AsCaller> OwnerAsync(TestDatabase database) => AsCaller.OwnerAsync(database, Cancellation);

    /// <summary>
    /// Tenancy's context as <see cref="TestTenancyContext"/> maps it, but with a role's permissions stored as a JSON document, as
    /// an application may choose: the model the contribution writes from, which never connects.
    /// </summary>
    private abstract class PermissionsAsDocumentContext(DbContextOptions options, string storeType) : DbContext(options)
    {
        protected static DbContextOptions ModelOnly<TContext>()
            where TContext : DbContext
            => new DbContextOptionsBuilder<TContext>().UseNpgsql("Host=model-only").Options;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(TestTenancyContext.Schema);
            modelBuilder.AddTenancy<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>(database: Database);
            modelBuilder.Entity<HostRole>().Property(role => role.Permissions)
                .HasConversion(new ValueConverter<IReadOnlyList<string>, string>(
                    permissions => JsonSerializer.Serialize(permissions, (JsonSerializerOptions?)null),
                    document => JsonSerializer.Deserialize<string[]>(document, (JsonSerializerOptions?)null)!))
                .HasColumnType(storeType);
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
            configurationBuilder.StoreDateTimeOffsetsAsUtc();
        }
    }

    private sealed class PermissionsAsJsonbContext() : PermissionsAsDocumentContext(ModelOnly<PermissionsAsJsonbContext>(), "jsonb");

    private sealed class PermissionsAsJsonContext() : PermissionsAsDocumentContext(ModelOnly<PermissionsAsJsonContext>(), "json");

    /// <summary>A contribution that would write a policy of its own on the seats' table.</summary>
    private sealed class Intruder(Type seat) : IRowAccessContribution
    {
        public string Owner => "intruder";

        public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
            => context.Model.FindEntityType(seat) is { } seats
                ? new RowAccessContributionResult([], [new ContributedPolicy(seats, "Everyone reads seats", "SELECT", "@user", "true", null)], [])
                : null;
    }
}
