using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Examples.Ordering.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Examples.Inventory.Application.IntegrationEvents;

/// <summary>An order was placed: set its stock aside, or say it cannot be.</summary>
/// <remarks>
/// The handler loads, the domain service decides, the save writes. The reservation, every stock item it
/// touched, its event in the outbox and the inbox row are one transaction, so Ordering hears about
/// exactly the stock that was set aside, once.
/// </remarks>
[IntegrationEventConsumer("inventory.reserver")]
public sealed class ReserveStock(InventoryContext context) : IIntegrationEventHandler<OrderPlacedV1>
{
    public async Task HandleAsync(OrderPlacedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        var skus = contract.Lines.Select(line => line.Sku).Distinct().ToList();
        var stock = await context.StockItems
            .Where(item => skus.Contains(item.Sku))
            .ToDictionaryAsync(item => item.Sku, cancellationToken);

        context.StockReservations.Add(StockAllocator.Allocate(
            contract.OrderId,
            [.. contract.Lines.Select(line => (line.Sku, line.Quantity))],
            stock));
    }
}

/// <summary>An order was cancelled: put back whatever was set aside for it.</summary>
/// <remarks>
/// The compensating action. There may be nothing to undo: an order cancelled because its stock was
/// refused has a refused reservation, and releasing that changes nothing.
/// </remarks>
[IntegrationEventConsumer("inventory.releaser")]
public sealed class ReleaseStock(InventoryContext context) : IIntegrationEventHandler<OrderCancelledV1>
{
    public async Task HandleAsync(OrderCancelledV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        var reservation = await context.StockReservations.SingleOrDefaultAsync(r => r.Order == contract.OrderId, cancellationToken);
        if (reservation is null)
        {
            // Cancelled before Inventory ever heard of it. OrderPlacedV1 is still on its way, and will be
            // reserved and then never released. Throwing makes the cancellation wait for it: the message
            // is retried until the reservation exists.
            throw new InvalidOperationException($"No reservation for order {contract.OrderId} yet.");
        }

        var skus = reservation.Lines.Select(line => line.Sku).ToList();
        var stock = await context.StockItems
            .Where(item => skus.Contains(item.Sku))
            .ToDictionaryAsync(item => item.Sku, cancellationToken);

        reservation.Release(stock);
    }
}
