#if NET
namespace DTS.DDD.Mediator;

/// <summary>
/// Marks a message that says something happened. <see cref="IPublisher"/> hands it to every
/// <see cref="INotificationHandler{TNotification}"/> registered for its type or for one of its base types.
/// <para>
/// Every domain event is a notification, because <c>IDomainEvent</c> derives from this interface. The events of the
/// supporting packages are notifications for the same reason, so an application handles them like its own.
/// </para>
/// </summary>
public interface INotification;
#endif
