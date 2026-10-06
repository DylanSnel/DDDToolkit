namespace Examples.Tenancy.Tenants.Application.Seats;

/// <summary>
/// One of the caller's own seats, for a tenant picker: the tenant it is in, and the seat as every answer of this
/// module shows one, <see cref="SeatListing"/>, with the name this application keeps for the person in that tenant.
/// Never an identity.
/// </summary>
/// <remarks>
/// The package finds the caller's seats across tenants, before one is picked, and answers this application's own
/// seats, each beside what is not on the seat, its tenant; the query that answers the picker selects this from them.
/// So the picker shows a seat by the name its tenant keeps, as that tenant's own answers do, with no read more.
/// </remarks>
/// <param name="Tenant">The tenant the seat is in.</param>
/// <param name="Seat">The seat, as this application shows it.</param>
public sealed record SeatOfMine(TenantOfSeat Tenant, SeatListing Seat);
