using System.Text.RegularExpressions;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// Given the context's database, <c>AddTenancy</c> keeps two rules of the aggregates in the database as well,
/// with filtered unique indexes written in each provider's SQL: a tenant's organization has a single root, and a
/// seat a single primary placement. A write that goes past the aggregates is refused by them, and a save through
/// the aggregates that moves the primary placement is not.
/// </summary>
public sealed class UniqueIndexTests : IDisposable
{
    private const string RootIndex = "IX_OrganizationUnits_TenantId_WhereRoot";
    private const string PrimaryIndex = "IX_SeatPlacements_SeatId_WherePrimary";

    private readonly TestServices _services = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Each provider, with the conditions of the two indexes in its SQL.</summary>
    public static TheoryData<string, string, string> Providers => new()
    {
        { "SQLite", "\"ParentId\" IS NULL", "\"IsPrimary\" = 1" },
        { "Npgsql", "\"ParentId\" IS NULL", "\"IsPrimary\"" },
        { "SQL Server", "[ParentId] IS NULL", "[IsPrimary] = CAST(1 AS bit)" },
    };

    public void Dispose() => _services.Dispose();

    [Theory]
    [MemberData(nameof(Providers))]
    public void With_the_database_the_root_and_primary_indexes_are_filtered_and_unique(string provider, string root, string primary)
    {
        // Built, never connected: the model and the create script need no server.
        using var context = new TestTenancyContext(Options(provider));

        Index(context.Model.FindEntityType(typeof(HostUnit))!, RootIndex).Should().Be("TenantId unique where " + root);
        Index(context.Model.FindEntityType(typeof(Placement<SeatId, OrganizationUnitId, RoleId>))!, PrimaryIndex).Should().Be("SeatId unique where " + primary);

        var script = context.Database.GenerateCreateScript();
        script.Should().MatchRegex("CREATE UNIQUE (NONCLUSTERED )?INDEX [\"\\[]" + RootIndex + "[\"\\]] ON [^;]+WHERE " + Escaped(root));
        script.Should().MatchRegex("CREATE UNIQUE (NONCLUSTERED )?INDEX [\"\\[]" + PrimaryIndex + "[\"\\]] ON [^;]+WHERE " + Escaped(primary));

        // Without the database, as before: the aggregates alone keep both rules.
        using var without = new WithoutDatabaseContext(new DbContextOptionsBuilder<WithoutDatabaseContext>().UseSqlite("DataSource=:memory:").Options);
        without.Model.GetEntityTypes().SelectMany(entityType => entityType.GetIndexes()).Should().NotContain(index => index.GetFilter() != null);
    }

    [Fact]
    public async Task A_second_root_or_a_second_primary_is_refused_by_the_index()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var grace = await _services.SeatAtAsync(harbor, "Grace", harbor.RootUnit);
        await _services.BySystemIn(harbor.Tenant, services => services.Seats().PlaceAsync(grace, north, primary: false, Cancellation));

        // Another tenant's root is its own, and another seat's primary placement too.
        await _services.ProvisionAsync("orchard");

        // Past the aggregates, with SQL of the application's own: North made a second root, and grace's
        // placement at North made a second primary.
        Refused("UPDATE \"OrganizationUnits\" SET \"ParentId\" = NULL WHERE \"ParentId\" IS NOT NULL", "OrganizationUnits.TenantId");
        Refused("UPDATE \"SeatPlacements\" SET \"IsPrimary\" = 1 WHERE \"IsPrimary\" = 0", "SeatPlacements.SeatId");

        Scalar("SELECT COUNT(*) FROM \"OrganizationUnits\" WHERE \"ParentId\" IS NULL").Should().Be(2, "harbor and orchard each keep a single root");
        Scalar("SELECT COUNT(*) FROM \"SeatPlacements\" WHERE \"IsPrimary\" = 1").Should().Be(3, "each seat keeps a single primary placement");
        Scalar("SELECT COUNT(*) FROM \"SeatPlacements\" WHERE \"IsPrimary\" = 0").Should().Be(1);
    }

    [Fact]
    public async Task Moving_the_primary_placement_saves_in_either_direction_under_the_index()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var south = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "South");
        var grace = await _services.SeatAtAsync(harbor, "Grace", harbor.RootUnit);
        await _services.BySystemIn(harbor.Tenant, services => services.Seats().PlaceAsync(grace, north, primary: false, Cancellation));

        // One save demotes one placement and promotes another, two rows of one seat. However the two are
        // ordered by key, the index never sees two primaries: to North, made after the root, and back.
        await _services.BySystemIn(harbor.Tenant, services => services.Seats().MakePrimaryAsync(grace, north, Cancellation));
        PrimaryOf(grace).Should().Be(north.Value);

        await _services.BySystemIn(harbor.Tenant, services => services.Seats().MakePrimaryAsync(grace, harbor.RootUnit, Cancellation));
        PrimaryOf(grace).Should().Be(harbor.RootUnit.Value);

        await _services.BySystemIn(harbor.Tenant, services => services.Seats().MakePrimaryAsync(grace, north, Cancellation));
        PrimaryOf(grace).Should().Be(north.Value);

        // And to a unit made after both.
        await _services.BySystemIn(harbor.Tenant, services => services.Seats().PlaceAsync(grace, south, primary: false, Cancellation));
        await _services.BySystemIn(harbor.Tenant, services => services.Seats().MakePrimaryAsync(grace, south, Cancellation));
        PrimaryOf(grace).Should().Be(south.Value);
        Scalar("SELECT COUNT(*) FROM \"SeatPlacements\" WHERE \"IsPrimary\" = 0").Should().Be(2, "the root and North, demoted");
    }

    private static DbContextOptions<TestTenancyContext> Options(string provider)
    {
        var options = new DbContextOptionsBuilder<TestTenancyContext>();
        return (provider switch
        {
            "SQLite" => options.UseSqlite("DataSource=:memory:"),
            "Npgsql" => options.UseNpgsql("Host=localhost;Database=never-opened"),
            "SQL Server" => options.UseSqlServer("Server=localhost;Database=never-opened"),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        }).Options;
    }

    private static string Index(IEntityType entityType, string name)
    {
        var index = entityType.GetIndexes().SingleOrDefault(candidate => candidate.GetDatabaseName() == name);
        index.Should().NotBeNull(entityType.DisplayName() + " has the index " + name);
        return string.Join(",", index!.Properties.Select(property => property.Name)) + (index.IsUnique ? " unique" : string.Empty) + " where " + index.GetFilter();
    }

    private static string Escaped(string sql) => Regex.Escape(sql);

    private void Refused(string sql, string column)
    {
        using var command = _services.Sqlite.Connection.CreateCommand();
        command.CommandText = sql;

        var refusal = FluentActions.Invoking(() => command.ExecuteNonQuery()).Should().Throw<SqliteException>().Which;
        refusal.SqliteExtendedErrorCode.Should().Be(2067, "SQLITE_CONSTRAINT_UNIQUE: " + refusal.Message);
        refusal.Message.Should().Contain(column);
    }

    /// <summary>The unit of the seat's primary placement, straight from the table.</summary>
    private Guid PrimaryOf(SeatId seat)
    {
        using var command = _services.Sqlite.Connection.CreateCommand();
        command.CommandText = "SELECT \"UnitId\" FROM \"SeatPlacements\" WHERE \"IsPrimary\" = 1 AND \"SeatId\" = $seat";
        command.Parameters.AddWithValue("$seat", seat.Value.ToString().ToUpperInvariant());
        return Guid.Parse((string)command.ExecuteScalar()!);
    }

    private long Scalar(string sql)
    {
        using var command = _services.Sqlite.Connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>The TestHost's classes mapped without the context's database: no filtered indexes.</summary>
    private sealed class WithoutDatabaseContext(DbContextOptions<WithoutDatabaseContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddTenancy<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
            configurationBuilder.StoreDateTimeOffsetsAsUtc();
        }
    }
}
