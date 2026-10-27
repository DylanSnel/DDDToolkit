namespace Examples.Tenancy.Tenants.Application.Seats;

/// <summary>
/// A seat and what it may do where, as this application shows it: the tenant, the seat as every answer shows one,
/// where it is placed with the roles held at each for which period, the roles it holds anywhere, and every permission it
/// holds now with where that permission reaches. Never an identity.
/// </summary>
/// <remarks>
/// The package's seat manager answers the module's own <c>Seat</c>, whole, with only what is not on it beside it: the
/// tenant, the paths of the units it is placed at, the roles its grants name and the permissions it holds. The query selects
/// this from that, the seat's own placements and grants with the paths and role names put beside them, so nothing is
/// read twice and the seat's identity stays where it was. The tenant and the roles are what this application selects
/// of the package's, and the permissions are the package's records as they are: nothing of them is the seat a second
/// time.
/// </remarks>
/// <param name="Tenant">The tenant the seat is in.</param>
/// <param name="Seat">The seat, as this application shows it.</param>
/// <param name="Placements">Where it is placed, the primary placement first and then by path, with the roles held at each.</param>
/// <param name="Roles">The roles it holds anywhere, by name.</param>
/// <param name="Permissions">Every permission it holds now, where it is granted and every unit it reaches.</param>
public sealed record SeatOverviewListing(
    TenantOfMine Tenant,
    SeatListing Seat,
    IReadOnlyList<PlacementListing> Placements,
    IReadOnlyList<RoleOfMine> Roles,
    IReadOnlyList<SeatManager.PermissionReach> Permissions);
