namespace DDDToolkit.EntityFramework.Outbox;

/// <summary>
/// One row per domain event, written by the same <c>SaveChanges</c> that persists the aggregate and
/// delivered later by <see cref="OutboxProcessor{TContext}"/>. Map it with
/// <c>modelBuilder.AddDomainEventOutbox()</c>.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Equals the event's <c>EventId</c>; handlers use it for idempotency.</summary>
    public Guid Id { get; set; }

    /// <summary>The stable event name (<c>[DomainEventName]</c> or the class name).</summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>The System.Text.Json serialized event.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// The shape <see cref="Payload"/> was written in, from <c>[IntegrationEvent(Version = n)]</c> on the
    /// event type. 1 when the type says nothing, which is every event that never had to change shape.
    /// <para>
    /// The processor compares it with the version of the type registered under <see cref="EventName"/>
    /// today. When they differ it reads the row as the older shape and upcasts, so rows written before a
    /// deployment are still deliverable after it. See <c>IntegrationEventContractRegistry</c>.
    /// </para>
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>When the event occurred (from the event itself).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>CLR type name of the aggregate that raised the event.</summary>
    public string? AggregateType { get; set; }

    /// <summary>The aggregate's id as text.</summary>
    public string? AggregateId { get; set; }

    /// <summary>When the row was written.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the handlers completed successfully; <see langword="null"/> while pending.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>How often delivery was attempted.</summary>
    public int Attempts { get; set; }

    /// <summary>
    /// When the processor may try again after a failure, <see cref="Attempts"/> and
    /// <c>OutboxOptions.RetryDelay</c> after it; the processor does not load the row before then.
    /// <see langword="null"/> when the row is due now: it was never tried, it was delivered, or it used
    /// its last attempt, in which case resetting <see cref="Attempts"/> makes it due at once.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>The last failure, or <see langword="null"/>.</summary>
    public string? LastError { get; set; }
}
