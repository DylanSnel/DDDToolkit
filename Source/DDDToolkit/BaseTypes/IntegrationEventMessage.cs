using DDDToolkit.Interfaces;

namespace DDDToolkit.BaseTypes;

/// <summary>
/// One message on its way out of this process: the payload plus everything a transport needs to route
/// it. This is what an <see cref="IIntegrationEventSink"/> receives.
/// <para>
/// It is deliberately not a domain event and not a persistence row. A domain event is an internal type
/// you may rename tomorrow; an outbox row carries delivery bookkeeping (<c>Attempts</c>,
/// <c>ProcessedAt</c>) that no transport should see or change. The envelope is the seam between the
/// two: text, a name, a version and an identity, and no Entity Framework types at all, so a sink can
/// live in a project that references only <c>DDDToolkit</c>.
/// </para>
/// </summary>
public sealed record IntegrationEventMessage
{
    /// <summary>
    /// The idempotency key, stable across every redelivery of this message. It is the
    /// <see cref="IDomainEvent.EventId"/> of the domain event that caused it, which is also the outbox
    /// row's key, so one identifier runs from the aggregate through the outbox to the consumer's inbox.
    /// </summary>
    public required Guid MessageId { get; init; }

    /// <summary>
    /// The published name consumers route on, from <c>[IntegrationEvent]</c>, otherwise
    /// <c>[DomainEventName]</c>, otherwise the class name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The schema version of <see cref="Payload"/>, from <c>[IntegrationEvent]</c>. Defaults to 1.</summary>
    public int Version { get; init; } = 1;

    /// <summary>The serialized message body.</summary>
    public required string Payload { get; init; }

    /// <summary>How to read <see cref="Payload"/>. Defaults to <c>application/json</c>.</summary>
    public string ContentType { get; init; } = "application/json";

    /// <summary>When the thing happened, taken from the domain event, not when delivery was attempted.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>CLR type name of the aggregate the event came from, or <see langword="null"/>.</summary>
    public string? AggregateType { get; init; }

    /// <summary>The aggregate's key as text, or <see langword="null"/>. Useful as a partition key.</summary>
    public string? AggregateId { get; init; }

    /// <summary>
    /// The object <see cref="Payload"/> was serialized from: the mapped contract, or the domain event
    /// itself when it is published directly. A sink whose transport speaks CLR objects can use this
    /// instead of parsing the payload. It is <see langword="null"/> for an envelope that was rebuilt
    /// from text rather than produced by the outbox.
    /// </summary>
    public object? Body { get; init; }
}
