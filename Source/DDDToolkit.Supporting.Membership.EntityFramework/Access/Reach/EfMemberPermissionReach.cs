using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The reach for several permissions the access manager of a resource stored with Entity Framework makes: the
/// reach that sees, and one reach for each permission asked about. A permission on a resource the caller may not see
/// is never answered, so asking about a resource says nothing about whether it exists.
/// </summary>
internal sealed class EfMemberPermissionReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId>
    where TResource : AggregateRoot<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> _see;
    private readonly IReadOnlyList<EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId>> _held;

    /// <summary>A reach for the permissions of <paramref name="held"/>, each a reach of its own, over what <paramref name="see"/> sees.</summary>
    public EfMemberPermissionReach(
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> see,
        IReadOnlyList<EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId>> held)
    {
        _see = see;
        _held = held;
    }

    /// <summary>Whether no permission asked about can be held by the caller at all, so there is nothing to read.</summary>
    public bool HoldsNothing => _held.All(reach => reach.Nothing);

    /// <summary>
    /// The permissions held on those of <paramref name="resources"/> the caller sees, one row for each permission held
    /// on a resource, by the resource's id: in a statement that runs on <paramref name="context"/>, or on the request's own.
    /// </summary>
    /// <remarks>
    /// One part for each permission the caller can hold, each over the resources it sees, put together where the
    /// statement runs: a part per permission, since not every database can apply a set of permissions to each row.
    /// </remarks>
    public IQueryable<MemberPermissionOn<TResourceId>> PermissionsOn(IQueryable<TResource> resources, DbContext? context)
    {
        var seen = _see.Within(resources, context);
        IQueryable<MemberPermissionOn<TResourceId>>? held = null;

        foreach (var reach in _held)
        {
            if (reach.Nothing)
            {
                continue;
            }

            // Seen as a member is a membership that applies now already, and whatever else lets a caller see
            // a resource holds the permission that sees: a permission that being a member gives needs no second look. Except
            // owning it, where the owner does not hold that permission: rules whose roles are rows may leave it out of
            // the permissions the owner holds, and an owner whose own membership has not begun sees what it owns
            // without it, as the function that answers where a permission is held says.
            var permission = reach.Permission!;
            var seenHolds = reach.HeldBy.HasFlag(MemberHeldBy.Membership)
                            && _see.HeldBy.HasFlag(MemberHeldBy.Membership)
                            && (reach.HeldBy.HasFlag(MemberHeldBy.Owner) || !_see.HeldBy.HasFlag(MemberHeldBy.Owner));
            var heldBy = seenHolds ? seen : reach.Within(seen, context);
            var part = heldBy.Select(resource => new MemberPermissionOn<TResourceId> { Resource = resource.Id, Permission = permission });
            held = held is null ? part : held.Concat(part);
        }

        return held ?? seen.Where(resource => false).Select(resource => new MemberPermissionOn<TResourceId> { Resource = resource.Id, Permission = string.Empty });
    }

    /// <summary>
    /// The pairs of resource and permission <see cref="PermissionsOn"/> answers, with the resource itself in each: the
    /// permissions put together first, by id, and each joined to its resource of <paramref name="resources"/>.
    /// </summary>
    public IQueryable<PermissionOn<TResource>> Pairs(IQueryable<TResource> resources, DbContext? context)
        => PermissionsOn(resources, context).Join(
            resources,
            held => held.Resource,
            resource => resource.Id,
            (held, resource) => new PermissionOn<TResource> { Resource = resource, Permission = held.Permission });
}
