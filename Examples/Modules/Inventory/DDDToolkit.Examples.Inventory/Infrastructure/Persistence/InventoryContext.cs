using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Inventory.Converters;
using DDDToolkit.Examples.Ordering.Contracts.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDDToolkit.Examples.Inventory.Infrastructure.Persistence;

/// <summary>Inventory's own database: the stock, the reservations, an outbox and an inbox.</summary>
public sealed class InventoryContext(DbContextOptions<InventoryContext> options) : DbContext(options)
{
    public const string Schema = "inventory";

    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<StockItem>().HasIndex(item => item.Sku).IsUnique();

        // One reservation per order. The inbox already stops a message being applied twice; this stops
        // two different messages about the same order from reserving it twice.
        modelBuilder.Entity<StockReservation>().HasIndex(reservation => reservation.Order).IsUnique();

        modelBuilder.AddDomainEventOutbox(Database, schema: Schema);
        modelBuilder.AddDomainEventInbox(Database, schema: Schema);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddOrderingContractsConverters();
        configurationBuilder.AddInventoryConverters();
    }
}

[SupabaseMigrations]
public sealed class InventoryContextFactory : IDesignTimeDbContextFactory<InventoryContext>
{
    public InventoryContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InventoryContext>();
        ModuleDatabase.UsePostgres(options, "Host=unused", InventoryContext.Schema);
        return new InventoryContext(options.Options);
    }
}
