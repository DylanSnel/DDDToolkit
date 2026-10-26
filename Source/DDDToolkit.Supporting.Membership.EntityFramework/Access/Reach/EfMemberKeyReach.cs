using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The reach for several keys the access questions of a resource stored with Entity Framework make: the
/// reach that sees, and one reach for each key asked about.
/// </summary>
internal sealed class EfMemberKeyReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> : MemberKeyReach<TResourceId>, IEfMemberKeyReach<TResource, TResourceId>
    where TResource : AggregateRoot<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> _see;
    private readonly IReadOnlyList<EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId>> _held;

    /// <summary>A reach for the keys of <paramref name="held"/>, each a reach of its own, over what <paramref name="see"/> sees.</summary>
    public EfMemberKeyReach(
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> see,
        IReadOnlyList<EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId>> held)
        : base(see, [.. held.Select(reach => reach.Key!)])
    {
        _see = see;
        _held = held;
    }

    /// <summary>Whether no key asked about can be held by the caller at all, so there is nothing to read.</summary>
    public bool HoldsNothing => _held.All(reach => reach.Nothing);

    /// <inheritdoc />
    /// <remarks>
    /// One part for each key the caller can hold, each over the resources it sees, put together where the
    /// statement runs: a part per key, since not every database can apply a set of keys to each row.
    /// </remarks>
    public IQueryable<MemberKeyOn<TResourceId>> KeysOn(IQueryable<TResource> resources, DbContext? context)
    {
        var seen = _see.Within(resources, context);
        IQueryable<MemberKeyOn<TResourceId>>? held = null;

        foreach (var reach in _held)
        {
            if (reach.Nothing)
            {
                continue;
            }

            // Seen as a member is a membership that applies now already, and whatever else lets a caller see
            // a resource holds the key that sees: a key that being a member gives needs no second look. Except
            // owning it, where the owner does not hold that key: rules whose roles are rows may leave it out of
            // the keys the owner holds, and an owner whose own membership has not begun sees what it owns
            // without it, as the function that answers where a key is held says.
            var key = reach.Key!;
            var seenHolds = reach.HeldBy.HasFlag(MemberHeldBy.Membership)
                            && _see.HeldBy.HasFlag(MemberHeldBy.Membership)
                            && (reach.HeldBy.HasFlag(MemberHeldBy.Owner) || !_see.HeldBy.HasFlag(MemberHeldBy.Owner));
            var heldBy = seenHolds ? seen : reach.Within(seen, context);
            var part = heldBy.Select(resource => new MemberKeyOn<TResourceId> { Resource = resource.Id, Key = key });
            held = held is null ? part : held.Concat(part);
        }

        return held ?? seen.Where(resource => false).Select(resource => new MemberKeyOn<TResourceId> { Resource = resource.Id, Key = string.Empty });
    }
}
