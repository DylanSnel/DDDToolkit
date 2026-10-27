namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// Who the calling seat is, and what it may do where (<c>GET /me</c>): the seat manager's overview, each object in it as
/// the API writes it. The placements name units and roles by id, and the overview's units and roles name them.
/// </summary>
/// <param name="Tenant">The tenant the seat is in.</param>
/// <param name="TenantName">The tenant's name, which is its organization's.</param>
/// <param name="Seat">The seat.</param>
/// <param name="Placements">Where the seat is placed, with the roles held at each for which period.</param>
/// <param name="Units">Each unit the seat is placed at, by its path from the root.</param>
/// <param name="Roles">Each role the seat's grants name.</param>
/// <param name="Permissions">Every permission the seat holds at <paramref name="AsOf"/>, and where it reaches.</param>
/// <param name="AsOf">The moment the overview holds for.</param>
public sealed record WhoAmIAnswer(
    TenantInfo Tenant,
    string TenantName,
    SeatInfo Seat,
    IReadOnlyList<PlacementInfo> Placements,
    IReadOnlyList<UnitPath> Units,
    IReadOnlyList<RoleInfo> Roles,
    IReadOnlyList<PermissionOfMineInfo> Permissions,
    DateTimeOffset AsOf)
{
    /// <summary>The path of a unit the seat is placed at, or its id when the overview names no such unit.</summary>
    public string PathOf(Guid unit) => Units.FirstOrDefault(found => found.Id == unit)?.Path ?? unit.ToString();

    /// <summary>The name of a role a grant of the seat names, or nothing when the overview names no such role.</summary>
    public string NameOf(Guid role) => Roles.FirstOrDefault(found => found.Id == role)?.Name ?? string.Empty;

    /// <summary>Whether <paramref name="grant"/> applies at the moment the overview holds for.</summary>
    public bool AppliesNow(GrantInfo grant) => grant.StartsAt <= AsOf && (grant.EndsAt is not { } ends || ends > AsOf);
}
