using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.SharedKernel;
using Mediator;

namespace DDDToolkit.Examples.Ordering.Domain.Orders;

/// <summary>
/// An order was placed. A <b>domain</b> event: it uses this module's own types, and only code inside
/// Ordering ever sees it.
/// </summary>
/// <remarks>
/// The wire shape everyone else reads is <see cref="OrderPlacedV1"/>, and <c>OrderingModule</c> says how
/// one becomes the other. Keeping the two apart is what lets this record grow a field without breaking
/// Inventory or Payments.
/// <para>
/// <c>[DomainEventName]</c> pins the name the outbox row stores, so this class can be renamed or moved
/// without orphaning the rows already written under the old name. <c>INotification</c> is Mediator's,
/// and it is what makes the event publishable by <c>DispatchWithMediator()</c>.
/// </para>
/// </remarks>
[DomainEventName("ordering.order-placed")]
public sealed record OrderPlaced(OrderId OrderId, Address ShipTo, IReadOnlyList<OrderPlaced.Line> Lines, Money Total)
    : DomainEvent, INotification
{
    /// <summary>A line as the event remembers it: the SKU and how many.</summary>
    public sealed record Line(string Sku, int Quantity);
}

/// <summary>Stock reserved and payment taken: the order will be delivered. Published as <see cref="OrderConfirmedV1"/>.</summary>
[DomainEventName("ordering.order-confirmed")]
public sealed record OrderConfirmed(OrderId OrderId, Address ShipTo) : DomainEvent, INotification;

/// <summary>The order will not be delivered. Published as <see cref="OrderCancelledV1"/>.</summary>
[DomainEventName("ordering.order-cancelled")]
public sealed record OrderCancelled(OrderId OrderId, string Reason) : DomainEvent, INotification;
