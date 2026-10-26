namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// One permission on a resource with members, as a constant marked <see cref="OnAttribute{TResource}"/> declares it:
/// its permission, and how it is held there. The rules of the resource are made from every one of them
/// (<see cref="MembershipRules.Permissions"/>).
/// </summary>
/// <param name="Name">The permission's name, such as <c>boards.edit</c>.</param>
/// <param name="Owner">Whether the owner holds it by owning the resource (<see cref="OnAttribute{TResource}.Owner"/>).</param>
/// <param name="EveryMember">Whether every member holds it by being one (<see cref="OnAttribute{TResource}.EveryMember"/>).</param>
/// <param name="InRoles">Whether a role can give it (<see cref="OnAttribute{TResource}.InRoles"/>).</param>
/// <param name="LocksMembers">Whether the database holds the member rows to it (<see cref="OnAttribute{TResource}.LocksMembers"/>).</param>
/// <param name="LocksOwner">Whether the database holds the owner column to it (<see cref="OnAttribute{TResource}.LocksOwner"/>).</param>
/// <param name="FromAbove">Whether it reaches the resource from above (<see cref="OnAttribute{TResource}.FromAbove"/>).</param>
public sealed record ResourcePermission(
    string Name,
    bool Owner = true,
    bool EveryMember = false,
    bool InRoles = true,
    bool LocksMembers = false,
    bool LocksOwner = false,
    bool FromAbove = true);
