using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Examples.Inventory.Contracts;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Payments.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Examples.Ordering.Application.Orders;

// The checkout process, as Ordering hears it. Each class is one policy: "when this happens elsewhere, do
// this to the order". None of them decides anything. They load the order and tell it what happened, and
// the order decides what that means: whether it is now confirmed, whether it was already cancelled.
//
// Each has a consumer name of its own, because the inbox remembers what it applied per (message,
// consumer), and a module may have one handler per name.
//
// None of them calls SaveChanges. The inbox saves the order together with the row that says the message
// was applied, in one transaction, and the save writes any event the order raised into Ordering's outbox
// in that same transaction. A confirmation is therefore never lost between "the order is confirmed" and
// "Shipping was told".

/// <summary>Inventory set the stock aside.</summary>
[IntegrationEventConsumer("ordering.checkout.stock-reserved")]
public sealed class RecordStockReservation(OrderingContext context) : IIntegrationEventHandler<StockReservedV1>
{
    public async Task HandleAsync(StockReservedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
        => (await Checkout.OrderAsync(context, contract.OrderId, cancellationToken)).RecordStockReserved(message.OccurredAt);
}

/// <summary>Payments took the money.</summary>
[IntegrationEventConsumer("ordering.checkout.payment-succeeded")]
public sealed class RecordPayment(OrderingContext context) : IIntegrationEventHandler<PaymentSucceededV1>
{
    public async Task HandleAsync(PaymentSucceededV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
        => (await Checkout.OrderAsync(context, contract.OrderId, cancellationToken)).RecordPayment(message.OccurredAt);
}

/// <summary>
/// No stock, no order. The cancellation is published, and Payments voids the payment it was holding.
/// </summary>
[IntegrationEventConsumer("ordering.checkout.stock-refused")]
public sealed class CancelWithoutStock(OrderingContext context) : IIntegrationEventHandler<StockReservationFailedV1>
{
    public async Task HandleAsync(StockReservationFailedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
        => (await Checkout.OrderAsync(context, contract.OrderId, cancellationToken)).Cancel(contract.Reason);
}

/// <summary>
/// No payment, no order. The cancellation is published, and Inventory puts the stock back on the shelf:
/// this is the compensating half of the process.
/// </summary>
[IntegrationEventConsumer("ordering.checkout.payment-failed")]
public sealed class CancelWithoutPayment(OrderingContext context) : IIntegrationEventHandler<PaymentFailedV1>
{
    public async Task HandleAsync(PaymentFailedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
        => (await Checkout.OrderAsync(context, contract.OrderId, cancellationToken)).Cancel(contract.Reason);
}

internal static class Checkout
{
    /// <summary>
    /// The order the message is about. It exists: every message these handlers receive was caused by the
    /// order being placed. If it cannot be found, throwing is right. The message fails, stays in the
    /// producer's outbox, and is retried.
    /// </summary>
    public static async Task<Order> OrderAsync(OrderingContext context, OrderId id, CancellationToken cancellationToken)
        => await context.Orders.SingleOrDefaultAsync(order => order.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Order {id} is not known to Ordering.");
}
