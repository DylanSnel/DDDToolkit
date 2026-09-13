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

    /// <summary>The last failure, or <see langword="null"/>.</summary>
    public string? LastError { get; set; }
}
