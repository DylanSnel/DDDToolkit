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
/// The outbox stores it as <c>ordering.order-placed</c>, the module and the class name in kebab case, which
/// is also <c>OrderingEventNames.OrderPlaced</c>. Moving the class changes nothing; renaming it would, so a
/// rename puts the old name in a <c>[DomainEventName]</c> for the rows already written under it.
/// <c>INotification</c> is Mediator's, and it is what makes the event publishable by
/// <c>DispatchWithMediator()</c>.
/// </para>
/// </remarks>
public sealed record OrderPlaced(OrderId OrderId, Address ShipTo, IReadOnlyList<OrderPlaced.Line> Lines, Money Total)
    : DomainEvent, INotification
{
    /// <summary>A line as the event remembers it: the SKU and how many.</summary>
    public sealed record Line(string Sku, int Quantity);
}

/// <summary>Stock reserved and payment taken: the order will be delivered. Published as <see cref="OrderConfirmedV1"/>.</summary>
public sealed record OrderConfirmed(OrderId OrderId, Address ShipTo) : DomainEvent, INotification;

/// <summary>The order will not be delivered. Published as <see cref="OrderCancelledV1"/>.</summary>
public sealed record OrderCancelled(OrderId OrderId, string Reason) : DomainEvent, INotification;
