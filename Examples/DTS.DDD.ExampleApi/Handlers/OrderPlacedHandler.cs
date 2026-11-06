using DTS.DDD.ExampleApi.Domain.UserAggregate.Events;
using DTS.DDD.Mediator;

namespace DTS.DDD.ExampleApi.Handlers;

/// <summary>
/// The second handler in the example, for <see cref="OrderPlaced"/>. An event needs no handler: the toolkit's
/// publisher hands an event without one to nobody, and the save goes on.
/// </summary>
public sealed class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) : INotificationHandler<OrderPlaced>
{
    public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("User {UserId} placed order {OrderId} (event {EventId}).", notification.UserId, notification.OrderId, notification.EventId);
        return default;
    }
}
