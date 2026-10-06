using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;

namespace Examples.Tenancy.Tenants.Application.Seats;

/// <summary>
/// One of the caller's own seats, for a tenant picker: the tenant it is in, as the Tenancy package found it, and the
/// seat as every answer of this module shows one, <see cref="SeatListing"/>, with the name this application keeps for
/// the person in that tenant. Never an identity.
/// </summary>
/// <remarks>
/// The package finds the caller's seats across tenants, before one is picked, and hands its view each one beside this
/// application's own seat, which it read in the same statement: <see cref="Of"/> takes the name from there. So the
/// picker shows a seat by the name its tenant keeps, as that tenant's own answers do, with no read more.
/// </remarks>
/// <param name="Found">What the package found: the tenant, by what a picker shows of it, and the seat's id and status.</param>
/// <param name="Seat">The seat, as this application shows it.</param>
public sealed record SeatOfMine(SeatOfCaller<TenantId, SeatId> Found, SeatListing Seat)
{
    /// <summary>The seat as the package found it, with the name its own class keeps in that tenant.</summary>
    /// <param name="found">What the package found of the seat and its tenant.</param>
    /// <param name="own">The seat itself, this application's class, as the package read it.</param>
    internal static SeatOfMine Of(SeatOfCaller<TenantId, SeatId> found, Seat own)
        => new(found, new SeatListing(found.Seat, own.DisplayName, found.SeatStatus));
}
