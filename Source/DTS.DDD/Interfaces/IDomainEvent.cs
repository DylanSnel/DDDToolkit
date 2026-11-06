using DTS.DDD.Mediator;

namespace DTS.DDD.Interfaces;

/// <summary>
/// Something that happened in the domain. Every event identifies itself and knows when it occurred,
/// so consumers never have to reconstruct that from context. Derive from <c>DomainEvent</c> for
/// sensible defaults.
/// <para>
/// Every domain event is an <see cref="INotification"/>. The toolkit's <see cref="IPublisher"/> hands it to the
/// <see cref="INotificationHandler{TNotification}"/> of its type, which includes the events of the supporting
/// packages.
/// </para>
/// </summary>
public interface IDomainEvent : INotification
{
    /// <summary>Unique, stable identifier of this occurrence. Use it for idempotent handling.</summary>
    Guid EventId { get; }

    /// <summary>When the event occurred (UTC).</summary>
    DateTimeOffset OccurredAt { get; }
}
