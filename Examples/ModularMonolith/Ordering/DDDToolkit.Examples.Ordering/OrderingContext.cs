using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Examples.Ordering.Contracts.Converters;
using DDDToolkit.Examples.Ordering.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DDDToolkit.Examples.Ordering;

/// <summary>
/// Ordering's own database. A module owns its tables; nothing in Shipping can query one of these, and
/// no query can join across the two by accident.
/// </summary>
/// <remarks>
/// There is no configuration for the domain model at all. <c>OrderLine</c> is an owned type because
/// <c>[Entity]</c> generated <c>[Owned]</c>, <c>Order.Lines</c> is discovered as an owned collection
/// through the generated backing field, <c>Address</c> is stored inline because <c>[ValueObject]</c>
/// generated <c>[ComplexType]</c>, <c>OrderId</c> and <c>OrderLineId</c> map through generated value
/// converters, and <c>Order.Version</c> is a concurrency token.
/// </remarks>
public sealed class OrderingContext(DbContextOptions<OrderingContext> options) : DbContext(options)
{
    /// <summary>
    /// The schema Ordering's tables and migration history live in. On Postgres the two modules can share
    /// one database, as they do on one Supabase project, and still own their tables: each has its own
    /// schema and its own history table, so neither module's migrations can see the other's.
    /// </summary>
    public const string Schema = "ordering";

    public DbSet<Order> Orders => Set<Order>();

    /// <summary>
    /// Postgres, with the migration history in <see cref="Schema"/>. The host and the design-time factory
    /// both call this, so the application and <c>dotnet ef</c> agree on where the history is.
    /// </summary>
    public static void UsePostgres(DbContextOptionsBuilder options, string connectionString)
        => options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // This module produces integration events, so it needs the outbox table: SaveChanges writes one
        // row per domain event in the same transaction as the order.
        // Database tells the toolkit which provider this is, so the timestamp columns get the
        // provider's own instant type rather than SQLite's lowest common denominator.
        modelBuilder.AddDomainEventOutbox(Database);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The same three conventions in every context: read-only collections, [Internal] members, Version.
        configurationBuilder.AddDDDToolkitConventions();

        // One generated call per assembly that declares identifiers or single value objects.
        configurationBuilder.AddOrderingContractsConverters();
        configurationBuilder.AddOrderingConverters();
    }
}

/// <summary>
/// How <c>dotnet ef migrations add</c> and the Supabase export build an <see cref="OrderingContext"/>:
/// on Postgres, and pointing nowhere, because neither of them opens a connection. Without it the tools
/// would build the host, and the host picks SQLite unless it is given a Supabase connection string.
/// </summary>
public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
{
    public OrderingContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrderingContext>();
        OrderingContext.UsePostgres(options, "Host=unused");
        return new OrderingContext(options.Options);
    }
}
