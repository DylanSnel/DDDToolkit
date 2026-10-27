using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// What a storage package answers about one kind of resource, for the current caller: the part of the access
/// manager that reads where the resources are kept. An application never takes it: it takes the access manager of
/// the resource, <see cref="MembershipAccessManager{TResource}"/>, which a storage package registers over this
/// (<see cref="MembershipAccessManager.Over{TResource, TResourceId}"/>).
/// <para>
/// A caller holds a permission on a resource through its members: it is a member now, and holds a role there, now,
/// that gives the permission. A role counts only inside its membership, and only for the permissions a member's role can
/// give (<see cref="MembershipRules.RolePermissions"/>). Being a member gives one permission by itself, when the rules
/// name one (<see cref="MembershipRules.EveryMemberPermission"/>). The owner of a resource holds the permissions marked
/// <c>Owner</c> (<see cref="MembershipRules.OwnerPermissions"/>), by owning it, and nothing through a role, and the
/// application's own work holds every permission: the application itself, and its work in a scope the rules name
/// (<see cref="MembershipRules.IsOwnWork"/>). Where the rules say so (<see cref="MembershipRules.ReachFromAbove"/>), a
/// permission held where the resource sits, or above it, is held on the resource as well, by a caller that is no member
/// of it. A resource the caller reaches in no way does not exist for it.
/// </para>
/// <para>
/// The storage answers, because whether a membership and a role apply now is compared where the rows are, inside
/// one statement. What is refused, and in which order, is not the storage's: the access manager decides that over
/// <see cref="HoldAsync"/>, the same for every storage.
/// </para>
/// </summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
public interface IMembershipStorage<TResource, TResourceId>
    where TResource : class, IEntity<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <summary>The rules of the resource, with the codes it refuses under.</summary>
    MembershipRules Rules { get; }

    /// <summary>
    /// Refuses a caller nothing can be answered for at all, with that caller's own refusal. Where the caller's member
    /// id is its own id or a claim there is no such caller: one that is nobody's member simply reaches nothing, so this
    /// returns for everybody. Where the application resolves who a caller is as a member
    /// (<see cref="MemberSource.Resolved"/>), a caller the application refuses outright is refused here, before
    /// anything is read (<see cref="ICallerMember{TResourceId, TMemberId}.Require"/>).
    /// </summary>
    /// <exception cref="Exceptions.RefusalException">The caller is nobody the questions can be asked about.</exception>
    void RequireCaller();

    /// <summary>
    /// The resources the caller reaches for <paramref name="permission"/>, as a set the storage puts into a statement:
    /// what <see cref="MembershipAccessManager{TResource}.Reach"/> answers.
    /// </summary>
    /// <param name="permission">A permission.</param>
    MemberReach<TResource> Reach(string permission);

    /// <summary>
    /// The resources the caller sees, as a set the storage puts into a statement: what
    /// <see cref="MembershipAccessManager{TResource}.ReachSeen"/> answers.
    /// </summary>
    MemberReach<TResource> ReachSeen();

    /// <summary>
    /// What the caller holds on one resource, in one statement: <see langword="null"/> when the caller does not
    /// see the resource, which is also the answer for a resource that does not exist; otherwise the resource,
    /// its version, and how the caller holds <paramref name="permission"/> on it and until when, if it does. It
    /// refuses nobody.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="permission">A permission.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<MemberHold<TResourceId>?> HoldAsync(TResourceId resource, string permission, CancellationToken cancellationToken);

    /// <summary>
    /// The permissions of <paramref name="permissions"/> the caller holds on each of <paramref name="resources"/> it
    /// sees, in one statement whatever their number. A resource the caller does not see, or holds none of the
    /// permissions on, is not in the answer.
    /// </summary>
    /// <param name="resources">The resources asked about, at most <see cref="MembershipAccessManager.MostResources"/> different ones.</param>
    /// <param name="permissions">Permissions.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="ArgumentException">More than <see cref="MembershipAccessManager.MostResources"/> different resources are asked about.</exception>
    Task<IReadOnlyDictionary<TResourceId, IReadOnlySet<string>>> PermissionsOnAsync(
        IReadOnlyCollection<TResourceId> resources,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken);
}
