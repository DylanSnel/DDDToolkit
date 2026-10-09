using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using Examples.Webshop.Shipping.Converters;
using DDDToolkit.EntityFramework.Supabase;
using Microsoft.EntityFrameworkCore;

namespace Examples.Webshop.Shipping.Infrastructure.Persistence;

/// <summary>
/// Shipping's own database, next to Ordering's and separate from it.
/// </summary>
/// <remarks>
/// The inbox is the only extra table. It is what makes <see cref="BookShipment"/> idempotent: delivery
/// is at-least-once, so this module will be handed the same message twice eventually, and the row keyed
/// on (message id, consumer name) is how it knows.
/// <para>
/// One call registers the converters of every identifier this context stores: <c>ShipmentId</c> here, and
/// <c>OrderId</c>, which Ordering's contracts publish.
/// </para>
/// <para>
/// <c>[SupabaseMigrations]</c> puts its migrations in <c>supabase/migrations</c> and has the build write its design-time
/// factory, <c>ShippingContextDesignTimeFactory</c>, as Ordering's context explains.
/// </para>
/// </remarks>
[SupabaseMigrations]
public sealed class ShippingContext(DbContextOptions<ShippingContext> options) : DbContext(options)
{
    /// <summary>
    /// The schema Shipping's tables and migration history live in, next to Ordering's when the two share
    /// one Postgres database.
    /// </summary>
    public const string Schema = "shipping";

    public DbSet<Shipment> Shipments => Set<Shipment>();

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
        configurationBuilder.AddShippingConverters();
    }
}
