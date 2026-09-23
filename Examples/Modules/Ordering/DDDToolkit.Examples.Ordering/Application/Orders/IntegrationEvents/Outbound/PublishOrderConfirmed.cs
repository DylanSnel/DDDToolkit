using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Examples.Ordering.Contracts;

namespace DDDToolkit.Examples.Ordering.Application.Orders;

/// <summary>An order is confirmed, stock set aside and money taken: tell Shipping where it goes.</summary>
public sealed class PublishOrderConfirmed : IOutboundIntegrationEvent<OrderConfirmed, OrderConfirmedV1>
{
    public ValueTask<OrderConfirmedV1?> CreateAsync(OrderConfirmed confirmed, CancellationToken cancellationToken)
        => new(new OrderConfirmedV1(confirmed.OrderId, confirmed.ShipTo.City, confirmed.ShipTo.PostalCode));
}
