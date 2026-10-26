namespace DDDToolkit.EntityFramework.EventLog;

/// <summary>
/// One kept domain event: written by the same <c>SaveChanges</c> that persists the aggregate which raised it,
/// and never changed afterwards. Map it with <c>modelBuilder.AddEventLog(Database)</c> and keep events in it
/// with <c>outbox.KeepEventLog()</c>.
/// <para>
/// It is the outbox's sibling, not the outbox kept longer. An outbox row is work: it is marked, retried, and
/// deleted once delivered. A log row is a record: it says what happened and who did it, and stays as it was
/// written, which is why it has none of the outbox's delivery columns and a table of its own that nothing
/// updates.
/// </para>
/// </summary>
public sealed class EventLogEntry
{
    /// <summary>Equals the event's <c>EventId</c>, and so the id of its outbox row.</summary>
    public Guid Id { get; set; }

    /// <summary>The stable event name (<c>[DomainEventName]</c> or the class name), as the outbox row has it.</summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>
    /// The shape <see cref="Payload"/> was written in, from <c>[IntegrationEvent(Version = n)]</c> on the event
    /// type; 1 when the type says nothing.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>The System.Text.Json serialized event, written with the outbox's serializer options.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>When the event occurred (from the event itself).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// When the row was written, by the application's clock. It is what the row's age is counted from: the
    /// guard lets a row be deleted once it is older than the table keeps it, and retention deletes by it.
    /// </summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>CLR type name of the aggregate that raised the event.</summary>
    public string? AggregateType { get; set; }

    /// <summary>The aggregate's id as text.</summary>
    public string? AggregateId { get; set; }

    /// <summary>
    /// What kind of actor the event is the work of: a user, the system, an anonymous caller, or a kind a package
    /// adds. From the <c>IActedByAccessor</c> the application registered, asked when the row is written.
    /// </summary>
    public string ActedByKind { get; set; } = string.Empty;

    /// <summary>Which actor of that kind, where the kind has more than one; <see langword="null"/> otherwise.</summary>
    public string? ActedById { get; set; }
}
