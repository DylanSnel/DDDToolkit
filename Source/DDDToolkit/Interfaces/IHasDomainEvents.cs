using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Interfaces;

/// <summary>
/// Implemented (explicitly) by aggregate roots. Raising events is protected on the root; draining
/// them is done through this interface by the persistence layer, and by nobody else.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>The pending events, in the order they were raised.</summary>
    [Internal]
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    /// <summary>Returns the pending events and removes them from the aggregate.</summary>
    IReadOnlyList<IDomainEvent> DequeueDomainEvents();

    /// <summary>Discards the pending events without returning them.</summary>
    void ClearDomainEvents();
}
