using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Interfaces;

namespace DDDToolkit.BaseTypes;

/// <summary>
/// Base class for aggregate roots: the consistency boundary of a cluster of entities and value
/// objects. The root is the only object that raises domain events and the only one that carries an
/// optimistic concurrency <see cref="Version"/>.
/// <para>
/// Being the consistency boundary is also what makes the root the place for invariants. Implement
/// the generated <c>CheckInvariants()</c> seam to say what must be true of the whole cluster after
/// every change; <see cref="Entity{TIdObject}.EnsureInvariants"/> runs it, and the Entity Framework
/// integration runs it for you before each save.
/// </para>
/// </summary>
public abstract class AggregateRoot<TIdObject> : Entity<TIdObject>, IAggregateRoot, IHasDomainEvents
    where TIdObject : IEntityId, IEquatable<TIdObject>
{
    [Internal]
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TIdObject id) : base(id)
    {
    }

    protected AggregateRoot()
    {
    }

    /// <inheritdoc />
    /// <remarks>
    /// Mapped as a concurrency token by DDDToolkit.EntityFramework, which also increments it on every
    /// save that touches this aggregate. A stale version surfaces as a <c>ConcurrencyConflictException</c>.
    /// </remarks>
    public long Version { get; private set; }

    /// <summary>The events raised since the aggregate was loaded or last saved, in order.</summary>
    [Internal]
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Records a domain event. Only the aggregate itself can raise events.</summary>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    IReadOnlyList<IDomainEvent> IHasDomainEvents.DequeueDomainEvents()
    {
        if (_domainEvents.Count == 0)
        {
            return [];
        }

        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }

    void IHasDomainEvents.ClearDomainEvents() => _domainEvents.Clear();
}
