using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Managers;

/// <summary>
/// Says whether a role that exists may go to a member of one kind of resource: a rule of the application's
/// own on top of the roles there are, such as roles an organization keeps for itself and never gives on a
/// resource. The member manager of that kind of resource asks it when a role is given; what was given once can
/// always be taken back, so it is not asked when a role is taken.
/// <para>
/// Optional. Without one every role there is may go to a member. It is asked about the role alone: whether this
/// caller may give it is the manager's to check by the permission that manages the members, and a rule of the
/// application's own beyond that is asked in its handler, before the manager is called.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The id of the kind of resource the members are of.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public interface IMemberRolePolicy<TResourceId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>Whether <paramref name="role"/> may be given to a member of the resource.</summary>
    /// <param name="role">A role that exists.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    ValueTask<bool> MayHoldAsync(TRoleId role, CancellationToken cancellationToken);
}
