using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.BaseTypes;

/// <summary>
/// Base class for entities: objects with an identity that outlives their attribute values. Two
/// entities are equal when their ids are equal. Child entities belong to exactly one aggregate; they
/// do not raise domain events themselves, the root does.
/// </summary>
public abstract class Entity<TIdObject> : IEntity<TIdObject>, IEquatable<Entity<TIdObject>>
    where TIdObject : IEntityId, IEquatable<TIdObject>
{
    public TIdObject Id { get; protected set; }

    protected Entity(TIdObject id) => Id = id;

#pragma warning disable CS8618 // Id is assigned by the persistence framework or the deriving constructor.
    protected Entity()
    {
    }
#pragma warning restore CS8618

    public override bool Equals(object? obj) => obj is Entity<TIdObject> other && Equals(other);

    public bool Equals(Entity<TIdObject>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return EqualityComparer<TIdObject>.Default.Equals(Id, other.Id);
    }

    public static bool operator ==(Entity<TIdObject>? left, Entity<TIdObject>? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TIdObject>? left, Entity<TIdObject>? right) => !(left == right);

    public override int GetHashCode() => Id is null ? 0 : EqualityComparer<TIdObject>.Default.GetHashCode(Id);
}
