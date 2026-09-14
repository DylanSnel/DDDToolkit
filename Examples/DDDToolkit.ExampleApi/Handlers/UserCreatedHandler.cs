using DDDToolkit.ExampleApi.Domain.UserAggregate.Events;
using Mediator;

namespace DDDToolkit.ExampleApi.Handlers;

/// <summary>
/// A domain event handler. With in-process dispatch it runs inside <c>SaveChanges</c>, before the user
/// row is written; with the outbox it runs later from the background service. Either way it receives
/// the same <see cref="UserCreated"/> instance shape, so handlers are written once. Keyed side effects
/// should use <c>notification.EventId</c> to stay idempotent under outbox redelivery.
/// <para>
/// Mediator's handler interface returns <see cref="ValueTask"/>, so a handler with nothing to await
/// returns <c>default</c> rather than allocating a task.
/// </para>
/// </summary>
public sealed class UserCreatedHandler(ILogger<UserCreatedHandler> logger) : INotificationHandler<UserCreated>
{
    public ValueTask Handle(UserCreated notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("User {UserId} was created (event {EventId} at {OccurredAt:O}).", notification.UserId, notification.EventId, notification.OccurredAt);
        return default;
    }
}
