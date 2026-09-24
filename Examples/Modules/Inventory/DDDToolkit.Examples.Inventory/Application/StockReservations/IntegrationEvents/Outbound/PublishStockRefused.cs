using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Examples.Inventory.Contracts;

namespace DDDToolkit.Examples.Inventory.Application.StockReservations;

/// <summary>The stock for an order could not be set aside: tell Ordering, which cancels it.</summary>
/// <remarks>
/// A refusal leaves the module like any other outcome, because it is one: <see cref="StockReservation"/>
/// records it and raises <see cref="StockRefused"/>, and this translates that. Nothing publishes a
/// failure without an aggregate behind it, so the refusal and its message commit together.
/// </remarks>
public sealed class PublishStockRefused : IOutboundIntegrationEvent<StockRefused, StockReservationFailedV1>
{
    public ValueTask<StockReservationFailedV1?> CreateAsync(StockRefused refused, CancellationToken cancellationToken)
        => new(new StockReservationFailedV1(refused.OrderId, refused.Reason));
}
