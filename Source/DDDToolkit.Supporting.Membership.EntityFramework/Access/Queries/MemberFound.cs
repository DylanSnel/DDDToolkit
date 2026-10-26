namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// A resource with how a reach reaches it: a step of a statement, never read as it is. A query goes on from
/// it to what it answers, the resource's own columns next to how the caller holds the key:
/// <code>
/// .Reached(act).Select(found =&gt; new DocumentAnswer(found.Resource.Id, found.Resource.Archived, found.AsMember))
/// </code>
/// It is made by setting its members, which Entity Framework reads back as columns, so each fact is a
/// subquery of the one statement, and one the query does not go on to is not asked.
/// </summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
public sealed class MemberFound<TResource>
    where TResource : class
{
    /// <summary>The resource.</summary>
    public required TResource Resource { get; init; }

    /// <summary>
    /// Whether the caller holds the key on the resource as one of its members: it is a member now, with what
    /// the reach asks of a membership, a role that gives the key, held now, or nothing more for a key that
    /// being a member gives; or it owns the resource, whose owner holds every key of it.
    /// </summary>
    public required bool AsMember { get; init; }

    /// <summary>
    /// The first moment the caller no longer holds the key on the resource that way, as its membership and
    /// its roles stand now, or <see langword="null"/> when that has no end, as for the resource's owner, and
    /// when <see cref="AsMember"/> is <see langword="false"/>. The end of the membership for a key that being
    /// a member gives; for a key a role gives, the end of the last role that gives it now, or of the
    /// membership if that ends sooner. It says nothing of a hold from above (<see cref="FromAbove"/>), which
    /// has no end the package knows: a caller that holds the key that way as well keeps it past this moment,
    /// for as long as it sees the resource. The access questions say that in the hold they answer
    /// (<see cref="Access.MemberHold{TResourceId}.Until"/>).
    /// </summary>
    public required DateTimeOffset? Until { get; init; }

    /// <summary>
    /// Whether the key is held where the resource sits, or above it, as the application answers it. Always
    /// <see langword="false"/> for a resource whose rules do not let it be reached from above.
    /// </summary>
    public required bool FromAbove { get; init; }
}
