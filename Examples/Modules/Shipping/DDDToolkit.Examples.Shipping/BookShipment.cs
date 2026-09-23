using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Examples.Ordering.Contracts;

namespace DDDToolkit.Examples.Shipping;

/// <summary>
/// Books a shipment when Ordering says an order was placed.
/// </summary>
/// <remarks>
/// Three things here are the whole point of the seam.
/// <para>
/// It is typed on <see cref="OrderPlacedV1"/>, the published contract, never on Ordering's
/// <c>OrderPlaced</c>. That is what keeps this project free of a reference to Ordering's domain.
/// </para>
/// <para>
/// It does not call <c>SaveChanges</c>. The module sink runs it inside the inbox, so the shipment and
/// the row that says "shipping.booker applied this message" are written by one save inside one
/// transaction. There is no instant where one exists without the other.
/// </para>
/// <para>
/// The name in the attribute is what the inbox keys on, together with the message id. Rename the class
/// without it and every row stops matching, so the handler replays its whole backlog.
/// </para>
/// </remarks>
[IntegrationEventConsumer("shipping.booker")]
public sealed class BookShipment(ShippingContext context) : IIntegrationEventHandler<OrderPlacedV1>
{
    public Task HandleAsync(OrderPlacedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        context.Shipments.Add(new Shipment(
            ShipmentId.CreateSequential(),
            contract.OrderId,
            $"{contract.PostalCode}, {contract.City}",
            message.OccurredAt));

        return Task.CompletedTask;
    }
}
