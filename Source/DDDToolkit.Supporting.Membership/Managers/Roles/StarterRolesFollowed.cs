using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Managers;

/// <summary>
/// What following the starter roles did (<see cref="CustomRoleManager{TRole, TRoleId, TResource}.FollowStarterRolesAsync"/>).
/// A role that follows its starter role already, one a customer made and one that is archived are in none of the lists.
/// </summary>
/// <typeparam name="TRole">The application's role class.</typeparam>
/// <typeparam name="TRoleId">The role class's id.</typeparam>
/// <param name="Made">The roles made, in the order the rules declare their starter roles.</param>
/// <param name="Changed">The roles whose permissions changed, each with the class's <c>OnPermissionsSet(set)</c> called.</param>
/// <param name="WithoutTheirStarterRole">
/// The roles made from a starter role the rules no longer declare. They keep their permissions, and follow the starter
/// role again if the rules declare it again under the same name.
/// </param>
public sealed record StarterRolesFollowed<TRole, TRoleId>(
    IReadOnlyList<TRole> Made,
    IReadOnlyList<TRoleId> Changed,
    IReadOnlyList<TRoleId> WithoutTheirStarterRole)
    where TRole : CustomRoleAggregate<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
