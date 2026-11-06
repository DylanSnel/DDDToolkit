namespace DTS.DDD.Mediator;

/// <summary>
/// The toolkit's <see cref="IPublisher"/>. It is scoped, and resolves every handler and behavior from its own scope.
/// </summary>
/// <param name="services">The scope's provider.</param>
/// <param name="routes">The handlers of every notification type.</param>
internal sealed class Publisher(IServiceProvider services, NotificationRoutes routes) : IPublisher
{
    /// <inheritdoc />
    public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (notification is null)
        {
            throw new ArgumentNullException(nameof(notification));
        }

        return PublishToHandlers(notification, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification is not INotification)
        {
            throw new ArgumentException(
                $"'{notification.GetType().FullName}' does not implement {typeof(INotification).FullName}, so no handler can be registered for it.",
                nameof(notification));
        }

        return PublishToHandlers(notification, cancellationToken);
    }

    private ValueTask PublishToHandlers(object notification, CancellationToken cancellationToken)
    {
        var handlers = routes.For(notification.GetType());

        return handlers.Length switch
        {
            0 => default,
            1 => handlers[0].Invoke(services, notification, cancellationToken),
            _ => PublishInTurn(handlers, notification, cancellationToken),
        };
    }

    private async ValueTask PublishInTurn(NotificationHandlerRegistration[] handlers, object notification, CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
        {
            await handler.Invoke(services, notification, cancellationToken).ConfigureAwait(false);
        }
    }
}
