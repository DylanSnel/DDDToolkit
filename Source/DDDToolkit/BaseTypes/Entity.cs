using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Interfaces;

namespace DDDToolkit.BaseTypes;

/// <summary>
/// Base class for entities: objects with an identity that outlives their attribute values. Two
/// entities are equal when their ids are equal. Child entities belong to exactly one aggregate; they
/// do not raise domain events themselves, the root does.
/// </summary>
public abstract class Entity<TIdObject> : IEntity<TIdObject>, IEquatable<Entity<TIdObject>>, IHasInvariants
    where TIdObject : IEntityId, IEquatable<TIdObject>
{
    public TIdObject Id { get; protected set; }

    protected Entity(TIdObject id) => Id = id;

#pragma warning disable CS8618 // Id is assigned by the persistence framework or the deriving constructor.
    protected Entity()
    {
    }
#pragma warning restore CS8618

    /// <summary>
    /// Runs this object's invariant checks and throws when one is broken. Generated code overrides
    /// this with a call to the <c>CheckInvariants()</c> seam, so implementing that partial method in
    /// your own part of the class is all you have to do.
    /// <para>
    /// Call it from a unit test or a command handler to assert consistency without a database.
    /// <c>DDDToolkit.EntityFramework</c> calls it for you on every aggregate root a
    /// <c>SaveChanges</c> writes.
    /// </para>
    /// <para>
    /// It checks this object only. The toolkit does not walk into children, because only the
    /// aggregate root knows which of its children a rule is about; a root whose rule spans its
    /// children calls <c>child.EnsureInvariants()</c> from its own <c>CheckInvariants()</c>.
    /// </para>
    /// </summary>
    /// <exception cref="InvariantViolationException">An invariant of this object does not hold.</exception>
    public virtual void EnsureInvariants()
    {
    }

    /// <summary>
    /// Builds an <see cref="InvariantViolationException"/> that already names this entity's type and
    /// id. Meant to be thrown from <c>CheckInvariants()</c>:
    /// <code>
    /// partial void CheckInvariants()
    /// {
    ///     if (Lines.Count == 0) throw InvariantViolation("An order must have at least one line.");
    /// }
    /// </code>
    /// </summary>
    /// <param name="violation">What must have been true and was not, in the domain's own words.</param>
    protected InvariantViolationException InvariantViolation(string violation)
        => new(GetType(), Id, violation);

    /// <summary>
    /// Builds an <see cref="InvariantViolationException"/> for several rules broken at once, so one
    /// check can report everything it found rather than only the first problem.
    /// </summary>
    /// <param name="violations">Every rule that was broken, in the domain's own words.</param>
    protected InvariantViolationException InvariantViolation(IEnumerable<string> violations)
        => new(GetType(), Id, violations);

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
