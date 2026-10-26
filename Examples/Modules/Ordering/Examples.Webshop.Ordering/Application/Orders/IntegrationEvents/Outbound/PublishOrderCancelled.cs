using DDDToolkit.EntityFramework.Integration;
using Examples.Webshop.Ordering.Contracts;

namespace Examples.Webshop.Ordering.Application.Orders;

/// <summary>An order was cancelled: tell Inventory and Payments to undo what they did for it.</summary>
public sealed class PublishOrderCancelled : IOutboundIntegrationEvent<OrderCancelled, OrderCancelledV1>
{
    public ValueTask<OrderCancelledV1?> CreateAsync(OrderCancelled cancelled, CancellationToken cancellationToken)
        => new(new OrderCancelledV1(cancelled.OrderId, cancelled.Reason));
}
