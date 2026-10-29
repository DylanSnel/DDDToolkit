using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// The roles there are for the members of one kind of resource: whether a role exists, which the member manager asks
/// before it gives one.
/// <para>
/// Where the roles are fixed (<see cref="Roles.Fixed"/>), the package answers it from the rules. Where a customer makes
/// them (<see cref="Roles.Custom"/>), the package that stores them answers it from their rows: a role in use. Where they
/// are kept elsewhere (<see cref="Roles.External"/>), the roles an organization makes for itself say, the application
/// answers it from there, registered with the resource, or the join with that organization's package answers it.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The id of the kind of resource the members are of.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public interface IMemberRoles<TResourceId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>Whether <paramref name="role"/> is a role in use that a member can be given.</summary>
    /// <param name="role">The role.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    ValueTask<bool> ExistsAsync(TRoleId role, CancellationToken cancellationToken);
}
