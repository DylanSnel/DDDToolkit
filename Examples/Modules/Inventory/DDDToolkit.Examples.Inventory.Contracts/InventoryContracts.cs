using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Examples.Ordering.Contracts;

[assembly: Module("Inventory")]

namespace DDDToolkit.Examples.Inventory.Contracts;

/// <summary>Every line of the order is set aside. Payments may now take the money.</summary>
[IntegrationEvent("inventory.stock-reserved", Version = 1)]
public sealed record StockReservedV1(OrderId OrderId);

/// <summary>
/// The order cannot be filled, and nothing was set aside for it: a reservation is all of the lines or
/// none of them.
/// </summary>
[IntegrationEvent("inventory.stock-reservation-failed", Version = 1)]
public sealed record StockReservationFailedV1(OrderId OrderId, string Reason);
