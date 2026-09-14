using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;

namespace DDDToolkit.Testing.Tests.Domain;

/// <summary>Raised by the <see cref="Order"/> constructor.</summary>
/// <param name="OrderId">The order that was placed.</param>
/// <param name="Customer">Who placed it.</param>
[DomainEventName("testing.order-placed")]
public sealed record OrderPlaced(OrderId OrderId, CustomerId Customer) : DomainEvent;

/// <summary>Raised by <see cref="Order.AddLine"/>.</summary>
/// <param name="OrderId">The order the line was added to.</param>
/// <param name="Sku">What was ordered.</param>
/// <param name="Quantity">How many.</param>
[DomainEventName("testing.line-added")]
public sealed record LineAdded(OrderId OrderId, Sku Sku, int Quantity) : DomainEvent;

/// <summary>Raised by <see cref="Order.Confirm"/>.</summary>
/// <param name="OrderId">The order that was confirmed.</param>
/// <param name="LineCount">How many lines it had at that moment.</param>
[DomainEventName("testing.order-confirmed")]
public sealed record OrderConfirmed(OrderId OrderId, int LineCount) : DomainEvent;

/// <summary>Raised by <see cref="Order.Ship"/>.</summary>
/// <param name="OrderId">The order that shipped.</param>
/// <param name="TrackingCode">The carrier's tracking code.</param>
[DomainEventName("testing.order-shipped")]
public sealed record OrderShipped(OrderId OrderId, string TrackingCode) : DomainEvent;

/// <summary>Raised by <see cref="Order.Cancel"/>.</summary>
/// <param name="OrderId">The order that was cancelled.</param>
/// <param name="Reason">Why.</param>
[DomainEventName("testing.order-cancelled")]
public record OrderCancelled(OrderId OrderId, string Reason) : DomainEvent;

/// <summary>
/// A cancellation that also records the customer tier. It exists to pin the difference between the
/// presence assertions, which accept a derived event, and <c>RaisedExactly</c>, which compares the
/// runtime type.
/// </summary>
/// <param name="OrderId">The order that was cancelled.</param>
/// <param name="Reason">Why.</param>
/// <param name="Tier">The customer tier the cancellation was handled under.</param>
[DomainEventName("testing.special-order-cancelled")]
public sealed record SpecialOrderCancelled(OrderId OrderId, string Reason, string Tier)
    : OrderCancelled(OrderId, Reason);

/// <summary>
/// An event that does not derive from <see cref="DomainEvent"/>, so the payload comparison is
/// exercised against a type that implements <see cref="IDomainEvent"/> itself.
/// </summary>
/// <param name="EventId">The event identity.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Note">The payload.</param>
public sealed record HandRolledEvent(Guid EventId, DateTimeOffset OccurredAt, string Note) : IDomainEvent;

/// <summary>An event with a collection payload, to pin that collections compare element by element.</summary>
/// <param name="OrderId">The order the batch belongs to.</param>
/// <param name="Skus">The items in the batch.</param>
public sealed record BatchShipped(OrderId OrderId, IReadOnlyList<Sku> Skus) : DomainEvent;
