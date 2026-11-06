#if NET
namespace DDDToolkit.Mediator;

/// <summary>
/// Publishes a notification to its handlers. Resolve it from a scope. The handlers come from the same scope.
/// <para>
/// The handlers are found by the notification's runtime type. Every handler declared for that type, or for one of
/// its base classes or interfaces, runs once, in the order the handlers were registered. They run one after
/// another. The first exception stops the rest and propagates to the caller. A notification without a handler is
/// published to nobody, and that is not an error.
/// </para>
/// </summary>
public interface IPublisher
{
    /// <summary>Publishes <paramref name="notification"/> to the handlers of its runtime type.</summary>
    /// <typeparam name="TNotification">The notification's static type. The handlers are found by its runtime type.</typeparam>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancels the handlers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> is null.</exception>
    ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Publishes <paramref name="notification"/> to the handlers of its runtime type. This overload is for a
    /// notification held as <see cref="object"/> or as an interface that is no notification, such as a domain event
    /// read back from an outbox.
    /// </summary>
    /// <param name="notification">The notification. It must implement <see cref="INotification"/>.</param>
    /// <param name="cancellationToken">Cancels the handlers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="notification"/> does not implement <see cref="INotification"/>.</exception>
    ValueTask Publish(object notification, CancellationToken cancellationToken = default);
}
#endif
