using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Interfaces;

namespace DDDToolkit.BaseTypes;

/// <summary>
/// Convenient base for domain events: assigns a time-ordered <see cref="EventId"/> and stamps
/// <see cref="OccurredAt"/> with the current UTC time. Both can be overridden with an initializer,
/// which is how tests and replays supply deterministic values.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Resolves the stable name of a domain event type (see <see cref="DomainEventNameAttribute"/>).</summary>
public static class DomainEventName
{
    public static string Of(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        var attribute = (DomainEventNameAttribute?)Attribute.GetCustomAttribute(eventType, typeof(DomainEventNameAttribute), inherit: false);
        return attribute?.Name ?? eventType.Name;
    }

    public static string Of<TEvent>() where TEvent : IDomainEvent => Of(typeof(TEvent));

    public static string Of(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return Of(domainEvent.GetType());
    }
}
