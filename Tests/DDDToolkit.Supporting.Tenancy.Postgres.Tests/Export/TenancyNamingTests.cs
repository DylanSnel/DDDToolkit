using System.Text.RegularExpressions;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Tenancy under a naming of the application's own: a context that uses <c>UseSnakeCaseNamingConvention()</c>, names
/// Tenancy's tables with <see cref="TenancyTableNames.SnakeCase"/> and stores every enum as snake_case text. Every
/// name and every stored value in the SQL Tenancy writes is the model's, down to the indexes it names itself; the
/// one thing that stays as it is, is what its functions answer under, which is theirs and the same for every
/// application. The suites that run under both namings (the classes ending in <c>OnSnakeCase</c>) prove the
/// behavior; these prove that the second naming is what it says.
/// </summary>
public sealed partial class TenancyNamingTests(TenancyPostgres postgres)
{
    /// <summary>The members of Tenancy's enums: the names its functions answer, whatever the tables store.</summary>
    private static readonly string[] EnumNames =
    [
        .. Enum.GetNames<TenantStatus>(), .. Enum.GetNames<TenantShape>(), .. Enum.GetNames<SeatStatus>(), .. Enum.GetNames<RoleStatus>(), .. Enum.GetNames<UnitStatus>(),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void Snake_case_reaches_every_table_column_index_and_constraint_the_script_names()
    {
        var snake = string.Join("\n", TenancyPostgres.AccessScripts(names: TenancyNaming.SnakeCase));
        var plain = string.Join("\n", TenancyPostgres.AccessScripts());

        // What is left once the names the files give themselves are set aside, a policy's name and the columns a
        // function answers under, is what they take from the model: tables, columns, and the keys they name.
        NamesWithACapital(snake).Should().BeEmpty("every name the files take from the model is the model's, and under this naming none has a capital");
        NamesWithACapital(plain).Should().Contain(["SeatRights", "SeatId", "IsPrimary"], "the same reading of the default files finds the model's names");

        // And the model's names are in them: every table, by what this naming calls it.
        using (var tenancy = TenancyNaming.SnakeCase.TenancyModel())
        using (var widgets = TenancyNaming.SnakeCase.WidgetModel())
        {
            var tables = TablesOf(tenancy).Concat(TablesOf(widgets)).Where(table => !table.StartsWith("ddd.", StringComparison.Ordinal)).ToList();
            tables.Should().HaveCount(15).And.OnlyContain(table => table == table.ToLowerInvariant());
            snake.Should().ContainAll(tables.Select(table => table.Split('.')[1]));
        }

        // What the functions answer under does not follow the naming: it is the same text in both.
        FunctionResults().Matches(snake).Select(match => match.Value).Should().Equal(FunctionResults().Matches(plain).Select(match => match.Value))
            .And.HaveCount(11, "the six rows a module reads, and the five answers the store reads");
    }

    [Fact]
    public async Task Under_snake_case_the_database_has_no_name_with_a_capital()
    {
        // Every table, index, column, constraint and trigger of the application's schemas, as the catalog has them.
        const string Named =
            """
            SELECT 'relation ' || c.relname FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname IN ('tenancy', 'widgets', 'ddd')
            UNION ALL
            SELECT 'column ' || c.relname || '.' || a.attname FROM pg_catalog.pg_attribute a
            JOIN pg_catalog.pg_class c ON c.oid = a.attrelid JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname IN ('tenancy', 'widgets', 'ddd') AND a.attnum > 0 AND NOT a.attisdropped
            UNION ALL
            SELECT 'constraint ' || o.conname FROM pg_catalog.pg_constraint o JOIN pg_catalog.pg_namespace n ON n.oid = o.connamespace
            WHERE n.nspname IN ('tenancy', 'widgets', 'ddd')
            UNION ALL
            SELECT 'trigger ' || t.tgname FROM pg_catalog.pg_trigger t
            JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname IN ('tenancy', 'widgets', 'ddd') AND NOT t.tgisinternal
            """;

        var snake = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, TenancyNaming.SnakeCase);
        await using (var owner = await AsCaller.OwnerAsync(snake, Cancellation))
        {
            var named = await owner.ListAsync<string>(Named, Cancellation);
            named.Should().HaveCountGreaterThan(100).And.Contain(["relation seat_rights", "column seat_role_grants.granted_by", "relation ix_seat_rights_seat_id_key", "constraint pk_seat_role_grants"]);
            named.Where(name => name.Any(char.IsUpper)).Should().BeEmpty("the convention named what was left to it, and Tenancy named the rest as it was told");
        }

        // The same question finds the capitals of the default names, so an empty answer above says something.
        var plain = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await using var plainOwner = await AsCaller.OwnerAsync(plain, Cancellation);
        (await plainOwner.ListAsync<string>(Named, Cancellation)).Should().Contain(["relation SeatRights", "column SeatRoleGrants.GrantedBy"]);
    }

    [Fact]
    public async Task Enums_stored_as_snake_case_text_are_the_policies_literals()
    {
        var script = string.Join("\n", TenancyPostgres.AccessScripts(names: TenancyNaming.SnakeCase));

        // A policy, a trigger and a function compare a status with what the model says is stored. The one place an
        // enum's name is written is where a read function answers it, for each value the column can hold.
        var compared = FunctionAnswers().Replace(script, "THEN");
        compared.Should().NotContainAny(EnumNames.Select(name => "'" + name + "'"), "nothing compares a column with the name of an enum's member");
        compared.Should().Contain("\"status\" = 'active'").And.Contain("'suspended'");
        script.Should().Contain("CASE t.\"status\" WHEN 'active' THEN 'Active' WHEN 'suspended' THEN 'Suspended' WHEN 'deactivated' THEN 'Deactivated' END");

        // In the database: the seeded rows hold the snake_case text, the policies on them compare with it, and a
        // module is still answered the names.
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, TenancyNaming.SnakeCase);
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ListAsync<string>("SELECT DISTINCT \"Status\" FROM tenancy.\"Seats\" ORDER BY 1", Cancellation)).Should().Equal("active", "suspended");
        (await owner.ListAsync<string>("SELECT DISTINCT \"Status\" || ' ' || \"Shape\" FROM tenancy.\"Tenants\" ORDER BY 1", Cancellation)).Should().Equal("active hierarchical", "suspended hierarchical");
        (await owner.ListAsync<string>("SELECT DISTINCT \"Status\" FROM tenancy.tenant_seats() ORDER BY 1", Cancellation)).Should().Equal("Active", "Suspended");

        const string Policies = "SELECT count(*) FROM pg_catalog.pg_policies WHERE schemaname = 'tenancy' AND coalesce(qual, '') || coalesce(with_check, '') LIKE ";
        (await owner.ScalarAsync<long>(Policies + "'%''active''%'", Cancellation)).Should().BePositive("policies compare a status with the stored text");
        (await owner.ScalarAsync<long>(Policies + "'%''Active''%'", Cancellation)).Should().Be(0, "and none with the name of the enum's member");

        // The application reads and writes through the same converters: Sue is suspended, as the use cases left her.
        await using var services = new TenancyServices(database);
        (await services.BySystemIn(Harbor, scoped => scoped.Tenancy().Set<HostSeat>().Where(seat => seat.Status == SeatStatus.Suspended).Select(seat => seat.Id).ToListAsync(Cancellation)))
            .Should().Equal(Sue.Seat);
    }

    [Fact]
    public void A_naming_convention_makes_no_table_of_a_row_read_from_a_function()
    {
        // A convention gives every entity type a table name as it is added to the model. The rows the store and a
        // module read from Tenancy's functions are mapped to nothing all the same, so a migration makes no table
        // for them, in Tenancy's schema or in a module's.
        foreach (var names in new[] { TenancyNaming.Default, TenancyNaming.SnakeCase })
        {
            using var tenancy = names.TenancyModel();
            using var widgets = names.WidgetModel();

            TablesOf(tenancy).Should().BeEquivalentTo(
                new[]
                {
                    "Tenants", "Organizations", "OrganizationUnits", "OrganizationUnitPaths", "Seats", "SeatPlacements", "SeatRoleGrants", "SeatRights", "Roles",
                    "TenancyAccessRevisions", "TenantNote", "Invitations", "InvitationDigests",
                }.Select(table => "tenancy." + names.Of(table)).Append("ddd." + names.Of("OutboxMessages")).Append("ddd." + names.Of("EventLog")),
                "under {0}", names);
            TablesOf(widgets).Should().BeEquivalentTo(["widgets." + names.Of("Widgets"), "widgets." + names.Of("WidgetPart")], "under {0}", names);
        }
    }

    [Fact]
    public async Task The_indexes_tenancy_names_keep_their_names_and_filter_on_the_columns_as_they_are_named()
    {
        foreach (var names in new[] { TenancyNaming.Default, TenancyNaming.SnakeCase })
        {
            var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation, names);
            await using var owner = await AsCaller.OwnerAsync(database, Cancellation);

            // The two indexes with a condition, as Postgres made them from the tables' script.
            (await owner.ListAsync<string>("SELECT indexdef FROM pg_catalog.pg_indexes WHERE schemaname = 'tenancy' AND indexdef LIKE '% WHERE %' ORDER BY indexname", Cancellation))
                .Should().Equal(
                [
                    $"CREATE UNIQUE INDEX {names.Shown("IX_OrganizationUnits_TenantId_WhereRoot")} ON tenancy.{names.Shown("OrganizationUnits")} USING btree ({names.Shown("TenantId")}) WHERE ({names.Shown("ParentId")} IS NULL)",
                    $"CREATE UNIQUE INDEX {names.Shown("IX_SeatPlacements_SeatId_WherePrimary")} ON tenancy.{names.Shown("SeatPlacements")} USING btree ({names.Shown("SeatId")}) WHERE {names.Shown("IsPrimary")}",
                ],
                "under {0}", names);
        }

        // A convention names an index after its table and columns, which is the name of the plain index on a unit's
        // tenant: these two keep the names the table names give them.
        TenancyNaming.SnakeCase.Of("IX_OrganizationUnits_TenantId_WhereRoot").Should().Be(TenancyTableNames.SnakeCase.RootIndex).And.Be("ix_organization_units_tenant_id_where_root");
        TenancyNaming.SnakeCase.Of("IX_SeatPlacements_SeatId_WherePrimary").Should().Be(TenancyTableNames.SnakeCase.PrimaryIndex).And.Be("ix_seat_placements_seat_id_where_primary");
        TenancyNaming.SnakeCase.Of("IX_OrganizationUnits_TenantId").Should().Be("ix_organization_units_tenant_id");
    }

    [Fact]
    public async Task The_start_up_checks_read_the_database_by_the_names_of_the_model()
    {
        // On a database named in snake_case the checks pass, each reading Postgres's catalog for the tables, the
        // trigger and the functions as the model names them.
        var secured = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, TenancyNaming.SnakeCase);
        await using (var services = new TenancyServices(secured))
        {
            TenancyPostgresChecks.EnsureExplicitCallers(services.Provider);
            await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(services.Provider, Cancellation);
            await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services.Provider, Cancellation);
            await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);

            // And where something is missing, they name the table as the database has it.
            await TenancyPostgres.ExecuteAsync(secured.ConnectionString, secured.Names.Sql("DROP TRIGGER tenancy_rights_follow_grants ON tenancy.\"Roles\""), Cancellation);
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenancy_rights_follow_grants, which writes the rights, is missing or disabled on tenancy.roles,*");
        }

        // The access files were never applied: no row level security on Tenancy's tables, whatever they are called.
        var plain = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation, TenancyNaming.SnakeCase);
        await using var withoutPolicies = new TenancyServices(plain);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(withoutPolicies.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*Row level security is off on Tenancy's tables*tenancy.seat_rights,*tenancy.seats,*references DDDToolkit.Supporting.Tenancy.Postgres*");
    }

    [Fact]
    public async Task Every_context_of_a_pool_has_the_model_of_its_naming()
    {
        // The naming is in a context's options and in the model built from them, both made once: a pool hands out
        // instances that share them, as the contexts of requests do.
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, TenancyNaming.SnakeCase);
        await using var pooled = new TenancyServices(database, pooled: true);
        await using var requests = new TenancyServices(database);

        var factory = pooled.Provider.GetRequiredService<IDbContextFactory<TestTenancyContext>>();
        await using var first = await factory.CreateDbContextAsync(Cancellation);
        await using var second = await factory.CreateDbContextAsync(Cancellation);
        await using var scope = requests.Scope();
        var ofRequest = scope.ServiceProvider.Tenancy();

        second.Should().NotBeSameAs(first);
        second.Model.Should().BeSameAs(first.Model, "one model for every context of the pool");
        foreach (var context in new[] { first, second, ofRequest })
        {
            var tenants = context.Model.FindEntityType(typeof(HostTenant))!;
            tenants.GetTableName().Should().Be("tenants");
            tenants.FindProperty(nameof(HostTenant.StatusReason))!.GetColumnName().Should().Be("status_reason");
            tenants.FindProperty(nameof(HostTenant.Shape))!.GetTypeMapping().Converter!.ConvertToProvider(TenantShape.Hierarchical).Should().Be("hierarchical");
        }

        // And a module's context from a pool reads Tenancy's functions under their own names, next to its own tables
        // under the convention's.
        await using var widgets = await pooled.Provider.GetRequiredService<IDbContextFactory<WidgetContext>>().CreateDbContextAsync(Cancellation);
        widgets.Model.FindEntityType(typeof(Widget))!.GetTableName().Should().Be("widgets");
        var rights = widgets.Model.FindEntityType(typeof(SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>))!;
        rights.GetFunctionName().Should().Be(TenancyFunctionNames.CallerRights);
        var function = StoreObjectIdentifier.DbFunction(widgets.Model.FindDbFunction(TenancyFunctionNames.CallerRights)!.ModelName);
        rights.GetProperties().Select(property => property.GetColumnName(function)).Should().BeEquivalentTo(["TenantId", "SeatId", "UnitId", "RoleId", "Key", "StartsAt", "EndsAt"]);
    }

    [Fact]
    public void A_statement_in_the_default_names_is_rewritten_from_the_two_models()
    {
        var (plain, snake) = (TenancyNaming.Default, TenancyNaming.SnakeCase);
        const string Suspending = "UPDATE tenancy.\"Tenants\" SET \"Status\" = 'Suspended', \"StatusReason\" = 'the \"Tenants\" table' WHERE \"Id\" = $1";

        // Under the default names, as written. Under another naming: the table, its columns and the stored value,
        // each as that model has it, and a name inside a text left alone.
        plain.Sql(Suspending).Should().Be(Suspending);
        snake.Sql(Suspending).Should().Be("UPDATE tenancy.\"tenants\" SET \"status\" = 'suspended', \"status_reason\" = 'the \"Tenants\" table' WHERE \"id\" = $1");
        snake.Sql("INSERT INTO widgets.\"WidgetPart\" (\"Id\", \"WidgetId\", \"Name\") VALUES ($1, $2, 'Active')")
            .Should().Be("INSERT INTO widgets.\"widget_part\" (\"id\", \"widget_id\", \"name\") VALUES ($1, $2, 'active')", "a text that is a stored value is taken for one, which a test's own texts keep clear of");

        // A statement that names no table asks Tenancy's functions, which answer under the same names everywhere.
        const string Asking = "SELECT \"SeatId\", \"Status\" FROM tenancy.tenant_seats() WHERE \"Status\" = 'Active'";
        snake.Sql(Asking).Should().Be(Asking);

        // One name, where a test compares with it or reads it in a plan.
        (plain.Of("PK_SeatRights"), snake.Of("PK_SeatRights")).Should().Be(("PK_SeatRights", "pk_seat_rights"));
        (plain.Shown("SeatId"), snake.Shown("SeatId")).Should().Be(("\"SeatId\"", "seat_id"));
        (plain.Stored(TenantShape.Hierarchical), snake.Stored(TenantShape.Hierarchical)).Should().Be(("Hierarchical", "hierarchical"));
        FluentActions.Invoking(() => snake.Of("Chairs")).Should().Throw<ArgumentException>().WithMessage("*no table, column, key or index called 'Chairs'*");
    }

#if NAMING_CONVENTION
    [Fact]
    public void Named_by_hand_the_model_is_named_as_the_convention_names_it()
    {
        // Where the convention's package cannot be restored, the suite runs under names given by hand. They are the
        // same names: every table, view and function, every column, key and index, and every stored enum value.
        var (convention, byHand) = (TenancyNaming.SnakeCase, TenancyNaming.SnakeCaseByHand);
        using (var named = convention.TenancyModel())
        using (var hand = byHand.TenancyModel())
        {
            Described(hand.Model).Should().Equal(Described(named.Model)).And.HaveCountGreaterThan(100);
        }

        using (var named = convention.WidgetModel())
        using (var hand = byHand.WidgetModel())
        {
            Described(hand.Model).Should().Equal(Described(named.Model)).And.HaveCountGreaterThan(30);
        }
    }

    /// <summary>What <paramref name="model"/> calls everything it maps, one line each, in a fixed order.</summary>
    private static List<string> Described(IModel model)
    {
        var lines = new List<string>();
        foreach (var entityType in model.GetEntityTypes().OrderBy(entityType => entityType.Name, StringComparer.Ordinal))
        {
            var mapped = entityType.GetTableName() is { } table ? "table " + table
                : entityType.GetViewName() is { } view ? "view " + view
                : entityType.GetFunctionName() is { } function ? "function " + function
                : "nothing";
            lines.Add($"{entityType.DisplayName()}: {mapped}");
            lines.AddRange(entityType.GetProperties().Select(property =>
                $"{entityType.DisplayName()}.{property.Name}: column {property.GetColumnName()}"
                + (property.ClrType.IsEnum ? ", stored as " + string.Join("/", Enum.GetValues(property.ClrType).Cast<object>().Select(value => property.GetTypeMapping().Converter?.ConvertToProvider(value) ?? value)) : string.Empty)));

            // A foreign key is left out: the convention names it after the class it points to, as the model had it
            // when the key was added, and the default it replaces after that class's table.
            if (entityType.GetTableName() is not null)
            {
                lines.AddRange(entityType.GetKeys().Select(key => $"{entityType.DisplayName()}: key {key.GetName()}"));
                lines.AddRange(entityType.GetIndexes().Select(index => $"{entityType.DisplayName()}: index {index.GetDatabaseName()} where {index.GetFilter()}").Order(StringComparer.Ordinal));
            }
        }

        return lines;
    }
#endif

    /// <summary>The tables a migration makes for <paramref name="context"/>'s model, each with its schema.</summary>
    private static List<string> TablesOf(DbContext context)
        => [.. context.Model.GetRelationalModel().Tables.Select(table => table.Schema + "." + table.Name)];

    /// <summary>
    /// The names in double quotes that have a capital and that <paramref name="script"/> takes from the model:
    /// outside its comments and texts, and apart from what a policy is called and what a function answers under.
    /// </summary>
    private static List<string> NamesWithACapital(string script)
    {
        var taken = PolicyNames().Replace(FunctionResults().Replace(Comments().Replace(script, string.Empty), "RETURNS TABLE"), "POLICY");
        return [.. NamesAndTexts().Matches(taken)
            .Select(match => match.Groups["name"].Value)
            .Where(name => name.Any(char.IsUpper))
            .Distinct(StringComparer.Ordinal)];
    }

    [GeneratedRegex("--[^\n]*")]
    private static partial Regex Comments();

    /// <summary>What a function says it answers: <c>RETURNS TABLE ("SeatId" uuid, "RoleId" uuid)</c>.</summary>
    [GeneratedRegex(@"RETURNS TABLE \([^)]*\)")]
    private static partial Regex FunctionResults();

    [GeneratedRegex("POLICY (IF EXISTS )?\"[^\"]+\"")]
    private static partial Regex PolicyNames();

    /// <summary>Where a read function answers the name of an enum's member: <c>THEN 'Active'</c>.</summary>
    [GeneratedRegex("THEN '[A-Za-z]+'")]
    private static partial Regex FunctionAnswers();

    [GeneratedRegex("'(?<text>[^']*)'|\"(?<name>[^\"]+)\"")]
    private static partial Regex NamesAndTexts();
}
