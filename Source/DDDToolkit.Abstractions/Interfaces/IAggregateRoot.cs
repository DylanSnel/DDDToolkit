namespace DDDToolkit.Abstractions.Interfaces;

/// <summary>
/// Marker for aggregate roots: the consistency boundary of a cluster of entities and value objects.
/// Only roots raise domain events and only roots carry an optimistic concurrency version.
/// </summary>
public interface IAggregateRoot : IEntity
{
    /// <summary>
    /// Optimistic concurrency version. Starts at 0 for a new aggregate and is incremented by the
    /// persistence layer every time the aggregate (or anything it owns) is saved.
    /// </summary>
    long Version { get; }
}
