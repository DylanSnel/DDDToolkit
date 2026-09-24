using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Examples.Inventory.Contracts;

namespace DDDToolkit.Examples.Inventory.Application.StockReservations;

/// <summary>The stock for an order is set aside: tell Ordering and Payments.</summary>
public sealed class PublishStockReserved : IOutboundIntegrationEvent<StockReserved, StockReservedV1>
{
    public ValueTask<StockReservedV1?> CreateAsync(StockReserved reserved, CancellationToken cancellationToken)
        => new(new StockReservedV1(reserved.OrderId));
}
