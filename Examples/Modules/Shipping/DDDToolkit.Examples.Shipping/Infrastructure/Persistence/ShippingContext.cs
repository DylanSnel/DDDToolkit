using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.Examples.Ordering.Contracts.Converters;
using DDDToolkit.Examples.Shipping.Converters;
using DDDToolkit.EntityFramework.Supabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DDDToolkit.Examples.Shipping.Infrastructure.Persistence;

/// <summary>
/// Shipping's own database, next to Ordering's and separate from it.
/// </summary>
/// <remarks>
/// The inbox is the only extra table. It is what makes <see cref="BookShipment"/> idempotent: delivery
/// is at-least-once, so this module will be handed the same message twice eventually, and the row keyed
/// on (message id, consumer name) is how it knows.
/// <para>
/// Two calls register converters because two assemblies declare identifiers this context stores:
/// <c>ShipmentId</c> here, and <c>OrderId</c> in Ordering's contracts.
/// </para>
/// </remarks>
public sealed class ShippingContext(DbContextOptions<ShippingContext> options) : DbContext(options)
{
    /// <summary>
    /// The schema Shipping's tables and migration history live in, next to Ordering's when the two share
    /// one Postgres database.
    /// </summary>
    public const string Schema = "shipping";

    public DbSet<Shipment> Shipments => Set<Shipment>();

    /// <summary>Postgres, with the migration history in <see cref="Schema"/>.</summary>
    public static void UsePostgres(DbContextOptionsBuilder options, string connectionString)
        => options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // Database tells the toolkit which provider this is, so the timestamp column gets the
        // provider's own instant type rather than SQLite's lowest common denominator.
        modelBuilder.AddDomainEventInbox(Database, schema: Schema);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddOrderingContractsConverters();
        configurationBuilder.AddShippingConverters();
    }
}

/// <summary>
/// How <c>dotnet ef migrations add</c> and the Supabase export build a <see cref="ShippingContext"/>:
/// on Postgres, and pointing nowhere, because neither of them opens a connection.
/// </summary>
[SupabaseMigrations]
public sealed class ShippingContextFactory : IDesignTimeDbContextFactory<ShippingContext>
{
    public ShippingContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShippingContext>();
        ShippingContext.UsePostgres(options, "Host=unused");
        return new ShippingContext(options.Options);
    }
}
