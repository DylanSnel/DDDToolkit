using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Interfaces;
using DDDToolkit.Invariants;

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
    /// Runs the invariant checks of the aggregate this object holds and throws when one is broken.
    /// That means this object's own rules and <c>CheckInvariants()</c> seam first, and then every
    /// child entity it holds in a generated collection, each of which answers the same way, so a
    /// grandchild is reached without anyone naming it. Generated code overrides this; implementing
    /// the <c>CheckInvariants()</c> partial method in your own part of the class is all you have to
    /// do.
    /// <para>
    /// Call it from a unit test or a command handler to assert that an aggregate in memory is
    /// consistent, with no <c>DbContext</c> anywhere near it. The aggregate root is the consistency
    /// boundary, so answering for the boundary means answering for what is inside it.
    /// </para>
    /// <para>
    /// This is a stricter question than the one the save asks, and deliberately so. It asks every
    /// child the aggregate holds; <c>DDDToolkit.EntityFramework</c> asks only the objects a
    /// <c>SaveChanges</c> adds or modifies, read from the change tracker, so that a child which was
    /// never loaded is never asked and never quietly fetched. Stricter here means a clean answer here
    /// is never contradicted by the save.
    /// </para>
    /// <para>
    /// A caller that walks the graph itself, as that interceptor does, wants
    /// <see cref="EnsureOwnInvariants"/> instead: asking this one from inside such a walk reports
    /// every child twice.
    /// </para>
    /// </summary>
    /// <exception cref="InvariantViolationException">An invariant of this object or of one of its children does not hold.</exception>
    public virtual void EnsureInvariants() => EnsureOwnInvariants();

    /// <summary>
    /// Runs the invariants of the aggregate this object holds, children included, and returns every
    /// one that is broken without throwing. Empty means consistent. Generated code overrides this to
    /// run the nested <see cref="IInvariant{TEntity}"/> rules, to fold in whatever the
    /// <c>CheckInvariants()</c> seam threw and to add what the children report, so one call reports
    /// all of it at once.
    /// <para>
    /// Every violation names the entity that reported it, so a handler that asks the root "did that
    /// break anything" can say which child is the problem without parsing a message.
    /// </para>
    /// <para>
    /// Ask this before you save, when "not yet consistent" is an answer you want to handle rather
    /// than an exception you want to catch. The Entity Framework interceptor calls
    /// <see cref="EnsureInvariants"/> instead, because at the save it is no longer an answer, and it
    /// calls the self-only pair below because it is already walking the graph itself.
    /// </para>
    /// </summary>
    public virtual IReadOnlyList<InvariantViolation> GetInvariantViolations() => GetOwnInvariantViolations();

    /// <summary>
    /// Runs the invariant checks of this object alone, without asking the children it holds, and
    /// throws when one is broken.
    /// <para>
    /// Wanted by exactly one kind of caller: one that already enumerates the graph and asks every
    /// object in it separately, which is what <c>DDDToolkit.EntityFramework</c> does from the change
    /// tracker. Everything else wants <see cref="EnsureInvariants"/>, which answers for the whole
    /// aggregate.
    /// </para>
    /// </summary>
    /// <exception cref="InvariantViolationException">An invariant of this object does not hold.</exception>
    public virtual void EnsureOwnInvariants()
    {
    }

    /// <summary>
    /// Runs the invariants of this object alone and returns every one that is broken, without
    /// throwing and without asking the children it holds. Empty says this object is consistent and
    /// says nothing about what is inside it.
    /// <para>
    /// The non-throwing half of <see cref="EnsureOwnInvariants"/>, for the same one caller. Ask
    /// <see cref="GetInvariantViolations"/> to find out whether an aggregate is consistent.
    /// </para>
    /// </summary>
    public virtual IReadOnlyList<InvariantViolation> GetOwnInvariantViolations() => [];

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
