using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Supabase;
using Examples.Webshop.Inventory.Converters;
using Examples.Webshop.Ordering.Contracts.Converters;
using Microsoft.EntityFrameworkCore;

namespace Examples.Webshop.Inventory.Infrastructure.Persistence;

/// <summary>Inventory's own database: the stock, the reservations, an outbox and an inbox.</summary>
/// <remarks>
/// <c>[SupabaseMigrations]</c> puts its migrations in <c>supabase/migrations</c> and has the build write its design-time
/// factory, <c>InventoryContextDesignTimeFactory</c>, as Ordering's context explains.
/// </remarks>
[SupabaseMigrations]
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
        configurationBuilder.AddOrderingConverters();
        configurationBuilder.AddInventoryConverters();
    }
}
