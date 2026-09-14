using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.Examples.Ordering.Contracts.Converters;
using DDDToolkit.Examples.Shipping.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Examples.Shipping;

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
    public DbSet<Shipment> Shipments => Set<Shipment>();

    // Database tells the toolkit which provider this is, so the timestamp column gets the
    // provider's own instant type rather than SQLite's lowest common denominator.
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddDomainEventInbox(Database);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddOrderingContractsConverters();
        configurationBuilder.AddShippingConverters();
    }
}
