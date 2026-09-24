using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Examples.Ordering.Contracts;

namespace DDDToolkit.Examples.Ordering.Application.Orders;

/// <summary>An order was placed: tell Inventory and Payments what to set aside and what to charge.</summary>
/// <remarks>
/// <see cref="OrderPlaced"/> is Ordering's to change; <see cref="OrderPlacedV1"/> is what everyone
/// deployed against. This class is the only place that knows both.
/// <para>
/// It runs when the outbox delivers the row, not when the order was saved. Everything it publishes comes
/// from the event, and that is deliberate: the total has to be the total at the moment of placing, and a
/// query here would read whatever it is by the time the processor gets round to it, and again on a
/// retry. A class like this may read the module's own read models for something stable, such as a
/// product's name. Anything the contract needs to be right belongs in the domain event.
/// </para>
/// </remarks>
public sealed class PublishOrderPlaced : IOutboundIntegrationEvent<OrderPlaced, OrderPlacedV1>
{
    public ValueTask<OrderPlacedV1?> CreateAsync(OrderPlaced placed, CancellationToken cancellationToken)
        => new(new OrderPlacedV1(
            placed.OrderId,
            placed.ShipTo.City,
            placed.ShipTo.PostalCode,
            [.. placed.Lines.Select(line => new OrderedLineV1(line.Sku, line.Quantity))],
            placed.Total.Amount,
            placed.Total.Currency));
}
