using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.Postgres.Tests.Converters;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>A tally's id.</summary>
[EntityId<Guid>]
public readonly partial record struct TallyId;

/// <summary>
/// What a second module of the application keeps in a tenant, at a unit: a count somebody keeps of something. The
/// module keeps on each tally who wrote it first and who changed it last, and shows the application's operators
/// the tallies their keepers marked as shared.
/// </summary>
[AggregateRoot<TallyId>]
public sealed partial class Tally
{
    /// <summary>Creates a tally.</summary>
    public Tally(TallyId id, TenantId tenantId, OrganizationUnitId unitId, string name, bool shared = false)
        : base(id)
    {
        TenantId = tenantId;
        UnitId = unitId;
        Name = name;
        Shared = shared;
    }

    /// <summary>The tenant it belongs to.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The unit it is kept at.</summary>
    public OrganizationUnitId UnitId { get; private set; }

    /// <summary>What is counted.</summary>
    public string Name { get; private set; }

    /// <summary>Whether the application's operators may look at it.</summary>
    public bool Shared { get; private set; }

    /// <summary>Renames the tally.</summary>
    public void Rename(string name) => Name = name;
}

/// <summary>A seat reads, adds, changes and removes the tallies at the units where it holds the key to change widgets.</summary>
[RowAccess<Tally>(RowOperations.All, To = [RowAccessRoles.User])]
public static partial class TalliesAreKeptWhereWidgetsAreChanged
{
    public static bool Allows(Tally tally, Caller caller)
        => TenancyRowAccess.UnitsWhereIHold<OrganizationUnitId>(HostCatalogue.WidgetChange).Contains(tally.UnitId);
}

/// <summary>
/// The module's rule for the application's operators, which names no tenant: every shared tally is theirs, to read
/// and, as far as this rule goes, to add, change and remove. What Tenancy leaves of it is the reading.
/// </summary>
[RowAccess<Tally>(RowOperations.All, To = [TallyRules.Operators])]
public static partial class OperatorsWorkOnSharedTallies
{
    public static bool Allows(Tally tally, Caller caller) => tally.Shared;
}

/// <summary>The tallies' rules, as a script is written from them.</summary>
public static class TallyRules
{
    /// <summary>The token role of the application's operators.</summary>
    public const string OperatorTokenRole = "operator";

    /// <summary>The database role the host maps that token role to. A role is the server's: no other test uses this name.</summary>
    public const string OperatorRole = "tenancy_operator";

    /// <summary>The operators, as a rule names them.</summary>
    public const string Operators = RowAccessRoles.TokenPrefix + OperatorTokenRole;

    public static readonly RowAccessRule Kept = RowAccessRule.For<Tally>(
        "Kept where widgets are changed", RowOperations.All, TalliesAreKeptWhereWidgetsAreChanged.RowAccessSql, RowAccessRoles.User);

    public static readonly RowAccessRule Shared = RowAccessRule.For<Tally>(
        "Operators work on shared tallies", RowOperations.All, OperatorsWorkOnSharedTallies.RowAccessSql, Operators);
}

/// <summary>
/// The tallies' module context: a schema of its own, Tenancy's read model as Tenancy's read functions, and its
/// tallies kept to a tenant, each with who wrote it first and who changed it last on its row.
/// </summary>
public sealed class TallyContext(DbContextOptions<TallyContext> options) : DbContext(options)
{
    /// <summary>The tallies' schema.</summary>
    public const string Schema = "tallies";

    public DbSet<Tally> Tallies => Set<Tally>();

    /// <summary>The tallies' context on Npgsql under <paramref name="names"/>, for its model: it never connects.</summary>
    public static TallyContext ForModel(TenancyNaming names)
        => new(((DbContextOptionsBuilder<TallyContext>)names.Configure(new DbContextOptionsBuilder<TallyContext>().UseNpgsql("Host=model-only"))).Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema);

        modelBuilder.Entity<Tally>(tally =>
        {
            tally.Property(row => row.Name).HasMaxLength(100);
            tally.ScopeToTenant(row => row.TenantId);

            // A module of its own names the seat's id; Tenancy's own module gets the call without it.
            tally.RecordsWhoChanged<Tally, SeatId>();
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddTenancyConverters();
        configurationBuilder.AddTenancyPostgresTestsConverters();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
    }
}

/// <summary>
/// A test's own database with the tallies' module next to Tenancy and the widgets: a copy of the seeded template,
/// the tallies' table made as a migration would make it, and the access files written again for the three
/// contexts, with operators where the test has them.
/// </summary>
public static class TallyDatabase
{
    /// <summary>The roles of a host whose operators' token role is mapped to a database role of its own.</summary>
    public static void WithOperators(PostgresRowLevelSecurityOptions roles) => roles.TokenRoles[TallyRules.OperatorTokenRole] = TallyRules.OperatorRole;

    /// <summary>
    /// A copy of the seeded template under <paramref name="names"/>, with the tallies' table and the access files
    /// of all three contexts. With <paramref name="operators"/>, the host maps its operators' token role, the
    /// contribution is told about it, and the module's rule for operators is among the rules.
    /// </summary>
    public static async Task<TestDatabase> CreateAsync(TenancyPostgres postgres, CancellationToken cancellationToken, TenancyNaming? names = null, bool operators = false)
    {
        names ??= TenancyNaming.Default;
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, cancellationToken, names);

        await TenancyPostgres.ExecuteAsync(database.SuperuserConnectionString, $"CREATE SCHEMA {TallyContext.Schema} AUTHORIZATION {TenancyPostgres.LoginRole}", cancellationToken);
        await using var tallies = TallyContext.ForModel(names);
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, tallies.Database.GenerateCreateScript(), cancellationToken);

        var options = new PostgresRowLevelSecurityOptions();
        var callers = "authenticated, anon, ddd_system_in";
        if (operators)
        {
            // The setup script makes the mapped role, and lets the login role switch to it.
            WithOperators(options);
            await TenancyPostgres.ExecuteAsync(database.SuperuserConnectionString, PostgresRowAccess.SetupScript(options, TenancyPostgres.LoginRole), cancellationToken);
            callers += ", " + TallyRules.OperatorRole;
        }

        foreach (var script in Scripts(names, operators))
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, cancellationToken);
        }

        // Every privilege on the tables, for every caller's role: only the policies and the triggers stand in the way.
        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            $"""
            GRANT USAGE ON SCHEMA ddd, {TestTenancyContext.Schema}, {WidgetContext.Schema}, {TallyContext.Schema} TO {callers};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA ddd, {TestTenancyContext.Schema}, {WidgetContext.Schema}, {TallyContext.Schema} TO {callers};
            """,
            cancellationToken);

        return database;
    }

    /// <summary>The access files of Tenancy, the widgets and the tallies, as <see cref="CreateAsync"/> applies them.</summary>
    public static IReadOnlyList<string> Scripts(TenancyNaming? names = null, bool operators = false, Func<RowAccessExport, RowAccessExport>? export = null)
    {
        names ??= TenancyNaming.Default;
        var options = new PostgresRowLevelSecurityOptions();
        if (operators)
        {
            WithOperators(options);
        }

        using var tallies = TallyContext.ForModel(names);
        return TenancyPostgres.AccessScripts(
            rules: [.. WidgetRules.All, TallyRules.Kept, .. operators ? new[] { TallyRules.Shared } : []],
            names: names,
            roles: RowAccessRoleNames.Of(options),
            operators: operators ? [TallyRules.OperatorTokenRole] : null,
            more: [tallies],
            export: export);
    }

    /// <summary>Services over <paramref name="database"/> with the tallies' context registered next to the other two.</summary>
    public static TenancyServices Services(TestDatabase database, bool operators = false, Action<DbContextOptionsBuilder>? contexts = null)
        => new(
            database,
            roles: operators ? WithOperators : null,
            contexts: contexts,
            operators: operators ? [TallyRules.OperatorTokenRole] : null,
            more: (services, configure) => services.AddDbContext<TallyContext>(configure));

    /// <summary>The tallies' table and columns as SQL names them under the naming of <paramref name="database"/>, read from the model.</summary>
    public static TallyNames NamesOf(TestDatabase database)
    {
        using var tallies = TallyContext.ForModel(database.Names);
        var tally = tallies.Model.FindEntityType(typeof(Tally))!;
        return new TallyNames(RowAccessModel.Table(tally), property => RowAccessModel.Column(tally, property));
    }
}

/// <summary>The tallies' table, and each of its columns by the property it stores, quoted as SQL names them.</summary>
public sealed record TallyNames(string Table, Func<string, string> Column)
{
    /// <summary>The columns of a new tally in the order <see cref="Values"/> gives their values.</summary>
    public string Columns => string.Join(", ", new[]
    {
        "Id", "TenantId", "UnitId", "Name", "Shared", "Version",
        TenancyAttribution.CreatedBySeat, TenancyAttribution.CreatedByKind, TenancyAttribution.CreatedByIdentity,
        TenancyAttribution.ChangedBySeat, TenancyAttribution.ChangedByKind, TenancyAttribution.ChangedByIdentity,
    }.Select(Column));

    /// <summary>The values of a new tally of <paramref name="tenant"/> at <paramref name="unit"/>, written and changed by one actor.</summary>
    public static string Values(TenantId tenant, OrganizationUnitId unit, string name, SeatId? seat, string kind, Guid? identity = null, bool shared = false)
    {
        var by = (seat is { } bySeat ? $"'{bySeat.Value}'" : "NULL") + $", '{kind}', " + (identity is { } operatorIdentity ? $"'{operatorIdentity}'" : "NULL");
        return $"gen_random_uuid(), {tenant.Value}, '{unit.Value}', '{name}', {(shared ? "true" : "false")}, 0, {by}, {by}";
    }
}
