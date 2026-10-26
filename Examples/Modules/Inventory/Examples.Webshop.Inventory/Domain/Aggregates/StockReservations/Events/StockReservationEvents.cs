using DDDToolkit.BaseTypes;
using Examples.Webshop.Ordering.Contracts;

namespace Examples.Webshop.Inventory.Domain.StockReservations;

/// <summary>Every line of the order is set aside. Published as <c>StockReservedV1</c>.</summary>
public sealed record StockReserved(OrderId OrderId) : DomainEvent;

/// <summary>The order cannot be filled. Published as <c>StockReservationFailedV1</c>.</summary>
public sealed record StockRefused(OrderId OrderId, string Reason) : DomainEvent;
