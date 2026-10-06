namespace Examples.Tenancy.Tenants.Application.Seats;

/// <summary>
/// A seat and what it may do where, as this application shows it: the tenant, the seat as every answer shows one,
/// where it is placed with the roles held at each for which period, the roles it holds anywhere, and every key it
/// holds now with where that key reaches. Never an identity.
/// </summary>
/// <remarks>
/// The package's directory answers the module's own <c>Seat</c>, whole, with only what is not on it beside it: the
/// tenant, the paths of the units it is placed at, the roles its grants name and the keys it holds. The query selects
/// this from that, the seat's own placements and grants with the paths and role names put beside them, so nothing is
/// read twice and the seat's identity stays where it was. The tenant, the roles and the keys are the package's
/// records as they are: nothing of them is the seat a second time.
/// </remarks>
/// <param name="Tenant">The tenant the seat is in.</param>
/// <param name="Seat">The seat, as this application shows it.</param>
/// <param name="Placements">Where it is placed, the primary placement first and then by path, with the roles held at each.</param>
/// <param name="Roles">The roles it holds anywhere, by name.</param>
/// <param name="Keys">Every key it holds now, where it is granted and every unit it reaches.</param>
public sealed record SeatOverviewListing(
    TenantsTenancy.TenantSummary Tenant,
    SeatListing Seat,
    IReadOnlyList<PlacementListing> Placements,
    IReadOnlyList<TenantsTenancy.RoleSummary> Roles,
    IReadOnlyList<TenantsTenancy.KeyReach> Keys);
