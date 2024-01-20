using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Interfaces;

namespace DDDToolkit.BaseTypes;

public abstract partial class Entity<TIdObject> : IEquatable<Entity<TIdObject>>, IHasDomainEvents
    where TIdObject : IEntityId
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public TIdObject Id { get; protected set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();


    protected Entity(TIdObject id)
        => Id = id;

#pragma warning disable CS8618
    protected Entity()
    {
    }
#pragma warning restore CS8618

    public override bool Equals(object? obj)
        => obj is Entity<TIdObject> entity && Id.Equals(entity.Id);

    public bool Equals(Entity<TIdObject>? other)
        => Equals((object?)other);

    public static bool operator ==(Entity<TIdObject> left, Entity<TIdObject> right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(Entity<TIdObject> left, Entity<TIdObject> right)
    {
        return !Equals(left, right);
    }

    public override int GetHashCode() => Id.GetHashCode();

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
