using System.Collections.Concurrent;

namespace DDDToolkit.Mediator;

/// <summary>
/// The handlers of every notification type, in the order they were registered. One per container.
/// <para>
/// A runtime type's handlers are the registrations whose notification type it derives from or implements. They
/// are worked out the first time the type is published and kept, so every later publish of the type is one
/// dictionary lookup.
/// </para>
/// </summary>
/// <param name="registrations">Every handler registration of the container, in registration order.</param>
internal sealed class NotificationRoutes(IEnumerable<NotificationHandlerRegistration> registrations)
{
    private readonly NotificationHandlerRegistration[] _registrations = [.. registrations];
    private readonly ConcurrentDictionary<Type, NotificationHandlerRegistration[]> _byType = new();

    /// <summary>The handlers that run for a notification of <paramref name="notificationType"/>, in registration order.</summary>
    /// <param name="notificationType">The notification's runtime type.</param>
    public NotificationHandlerRegistration[] For(Type notificationType)
        => _byType.GetOrAdd(
            notificationType,
            static (type, all) => Array.FindAll(all, registration => registration.NotificationType.IsAssignableFrom(type)),
            _registrations);
}
