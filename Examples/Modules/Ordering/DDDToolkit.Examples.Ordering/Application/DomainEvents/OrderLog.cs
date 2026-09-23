using Mediator;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.Examples.Ordering.Application.DomainEvents;

/// <summary>
/// A handler inside the producing module, reached through Mediator.
/// </summary>
/// <remarks>
/// It is typed on the domain events, and that is allowed precisely because it lives in Ordering. A
/// handler in another module may not do this; it gets <see cref="Contracts.OrderPlacedV1"/> and the
/// other contracts through the module sink instead. <c>OrderingModule</c> turns both on at once with
/// <c>outbox.AlsoDispatchInProcess = true</c>, which runs this first and the sinks afterwards.
/// <para>
/// Ordering is the only module that dispatches in process as well, and so the only one whose events
/// implement Mediator's <c>INotification</c>. The events of Catalog, Inventory and Payments go to the
/// other modules and nowhere else, so they are plain domain events.
/// </para>
/// </remarks>
public sealed class OrderLog(ILogger<OrderLog> logger)
    : INotificationHandler<OrderPlaced>, INotificationHandler<OrderConfirmed>, INotificationHandler<OrderCancelled>
{
    public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Order {OrderId} was placed with {LineCount} line(s) for {Total} at {OccurredAt:O} (event {EventId}).",
            notification.OrderId,
            notification.Lines.Count,
            notification.Total,
            notification.OccurredAt,
            notification.EventId);

        return default;
    }

    public ValueTask Handle(OrderConfirmed notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Order {OrderId} is confirmed.", notification.OrderId);
        return default;
    }

    public ValueTask Handle(OrderCancelled notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Order {OrderId} was cancelled: {Reason}", notification.OrderId, notification.Reason);
        return default;
    }
}
