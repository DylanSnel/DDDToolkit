using DDDToolkit.ExampleApi.Domain.UserAggregate.Events;
using Mediator;

namespace DDDToolkit.ExampleApi.Handlers;

/// <summary>
/// The second handler in the example, for <see cref="OrderPlaced"/>. Every notification wants one:
/// Mediator's generator reports <c>MSG0005</c> at build time for a notification nobody handles, which
/// is a useful nudge but also means an event you deliberately leave unhandled needs that warning
/// suppressed.
/// </summary>
public sealed class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) : INotificationHandler<OrderPlaced>
{
    public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("User {UserId} placed order {OrderId} (event {EventId}).", notification.UserId, notification.OrderId, notification.EventId);
        return default;
    }
}
