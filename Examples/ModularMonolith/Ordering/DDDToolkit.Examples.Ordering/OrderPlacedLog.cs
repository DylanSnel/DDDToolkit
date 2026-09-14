using Mediator;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.Examples.Ordering;

/// <summary>
/// A handler inside the producing module, reached through Mediator.
/// </summary>
/// <remarks>
/// It is typed on the domain event, and that is allowed precisely because it lives in Ordering. A
/// handler in another module may not do this; it gets <see cref="Contracts.OrderPlacedV1"/> through the
/// module sink instead. The host turns both on at once with <c>outbox.AlsoDispatchInProcess = true</c>,
/// which runs this first and the sinks afterwards.
/// </remarks>
public sealed class OrderPlacedLog(ILogger<OrderPlacedLog> logger) : INotificationHandler<OrderPlaced>
{
    public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Order {OrderId} was placed with {LineCount} line(s) at {OccurredAt:O} (event {EventId}).",
            notification.OrderId,
            notification.LineCount,
            notification.OccurredAt,
            notification.EventId);

        return default;
    }
}
