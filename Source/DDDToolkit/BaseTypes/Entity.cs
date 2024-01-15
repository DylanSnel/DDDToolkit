using DDDToolkit.Interfaces;

namespace DDDToolkit.BaseTypes;

public abstract partial class Entity<TId> : IEquatable<Entity<TId>>, IHasDomainEvents
    where TId : ValueObject
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public TId Id { get; protected set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public DateTime CreatedDateTime { get; private set; }
    public DateTime UpdatedDateTime { get; private set; }

    protected Entity(TId id)
        => Id = id;

#pragma warning disable CS8618
    protected Entity()
    {
    }
#pragma warning restore CS8618

    public override bool Equals(object? obj)
        => obj is Entity<TId> entity && Id.Equals(entity.Id);

    public bool Equals(Entity<TId>? other)
        => Equals((object?)other);

    public static bool operator ==(Entity<TId> left, Entity<TId> right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(Entity<TId> left, Entity<TId> right)
    {
        return !Equals(left, right);
    }

    public override int GetHashCode() => Id.GetHashCode();

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
