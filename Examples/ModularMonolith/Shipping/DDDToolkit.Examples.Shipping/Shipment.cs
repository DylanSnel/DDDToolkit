using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Examples.Ordering.Contracts;

namespace DDDToolkit.Examples.Shipping;

/// <summary>
/// Something to put in a van. Shipping's only aggregate.
/// </summary>
/// <remarks>
/// <see cref="Order"/> is an <see cref="OrderId"/>, not an <c>Order</c>. Holding the entity would make
/// Entity Framework map a navigation into Ordering's tables and put both modules in one transaction,
/// which is the change that quietly ends a modular monolith. The boundary analyzer reports that as
/// DDD00023, and this project has turned it into a build error.
/// <para>
/// The identifier is generated from the attribute, because nothing outside Shipping names a shipment.
/// Shipment raises no domain event either: nothing in this example reacts to one, and an event with no
/// reader is a promise you have to keep for nothing. The day it raises one, this context needs the
/// outbox table and a background service of its own.
/// </para>
/// </remarks>
[AggregateRoot<Guid>("SHP")]
public partial class Shipment
{
    public Shipment(ShipmentId id, OrderId order, string destination, DateTimeOffset orderedAt) : base(id)
    {
        Order = order;
        Destination = destination;
        OrderedAt = orderedAt;
    }

    public OrderId Order { get; private set; }

    public string Destination { get; private set; } = string.Empty;

    /// <summary>When the order was placed, not when this row was written. The envelope carries both.</summary>
    public DateTimeOffset OrderedAt { get; private set; }
}
