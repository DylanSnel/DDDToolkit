using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Examples.Ordering.Contracts.Converters;
using DDDToolkit.Examples.Ordering.Converters;
using Microsoft.EntityFrameworkCore;

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
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
