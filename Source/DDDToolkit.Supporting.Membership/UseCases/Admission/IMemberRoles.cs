using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership.UseCases;

/// <summary>
/// The roles there are for the members of one kind of resource, as the admission asks about them: whether a
/// role exists.
/// <para>
/// Where the roles are fixed (<see cref="Roles.Fixed"/>), the package answers it from the rules
/// (<see cref="NamedRoles{TResourceId}"/>). Where a customer makes them (<see cref="Roles.Custom"/>), the package
/// that stores them answers it from their rows: a role in use. Where they are kept elsewhere
/// (<see cref="Roles.External"/>), the roles an organization makes for itself say, the application answers it from
/// there, registered with the resource.
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
