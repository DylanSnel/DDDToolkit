using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Examples.Ordering.Contracts;

namespace DDDToolkit.Examples.Inventory.Domain.StockReservations;

/// <summary>Every line of the order is set aside. Published as <c>StockReservedV1</c>.</summary>
[DomainEventName("inventory.stock-reserved")]
public sealed record StockReserved(OrderId OrderId) : DomainEvent;

/// <summary>The order cannot be filled. Published as <c>StockReservationFailedV1</c>.</summary>
[DomainEventName("inventory.stock-refused")]
public sealed record StockRefused(OrderId OrderId, string Reason) : DomainEvent;
