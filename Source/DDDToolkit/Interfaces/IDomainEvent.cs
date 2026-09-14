namespace DDDToolkit.Interfaces;

/// <summary>
/// Something that happened in the domain. Every event identifies itself and knows when it occurred,
/// so consumers never have to reconstruct that from context. Derive from <c>DomainEvent</c> for
/// sensible defaults.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Unique, stable identifier of this occurrence. Use it for idempotent handling.</summary>
    Guid EventId { get; }

    /// <summary>When the event occurred (UTC).</summary>
    DateTimeOffset OccurredAt { get; }
}
