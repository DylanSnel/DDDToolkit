using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Examples.Ordering.Contracts;
using Mediator;

namespace DDDToolkit.Examples.Ordering;

/// <summary>
/// An order was placed. A <b>domain</b> event: it uses this module's own types, and only code inside
/// Ordering ever sees it.
/// </summary>
/// <remarks>
/// The wire shape everyone else reads is <see cref="OrderPlacedV1"/>, and the host says how one becomes
/// the other. Keeping the two apart is what lets this record grow a field without breaking Shipping.
/// <para>
/// <c>[DomainEventName]</c> pins the name the outbox row stores, so this class can be renamed or moved
/// without orphaning the rows already written under the old name. <c>INotification</c> is Mediator's,
/// and it is what makes the event publishable by <c>DispatchWithMediator()</c>.
/// </para>
/// </remarks>
[DomainEventName("ordering.order-placed")]
public sealed record OrderPlaced(OrderId OrderId, Address ShipTo, int LineCount) : DomainEvent, INotification;
