namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// A crew member: its seat, by its id, the period its membership counts for, and the project roles it holds on the crew.
/// Being on the crew lets it see the project; anything more comes from a role.
/// </summary>
/// <remarks>
/// The API answers a member's roles to whoever manages the crew, and <c>null</c> to anybody else: who holds which
/// role on a crew is not for everyone who sees the project. So no roles here is not "holds none"
/// (<see cref="RolesAreShown"/>).
/// </remarks>
public sealed record CrewInfo(
    Guid SeatId,
    bool IsOwner,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool AppliesNow,
    IReadOnlyList<CrewRoleInfo>? Roles)
{
    /// <summary>Whether the API answered the member's roles: it does for a caller who manages the crew.</summary>
    public bool RolesAreShown => Roles is not null;

    /// <summary>The roles it holds on the crew, each for a period of its own; none for a member with no role, and none when they were not answered.</summary>
    public IReadOnlyList<CrewRoleInfo> RolesHeld => Roles ?? [];
}
