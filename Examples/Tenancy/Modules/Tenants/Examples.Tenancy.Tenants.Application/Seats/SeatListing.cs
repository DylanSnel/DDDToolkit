using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;

namespace Examples.Tenancy.Tenants.Application.Seats;

/// <summary>
/// A seat as this application shows it: what the Tenancy package says of it, with the name this application keeps
/// on its own seat class. Never an identity.
/// </summary>
/// <remarks>
/// A seat has no name in Tenancy: no rule of the package reads one, so what a person is shown by is the
/// application's to say. This one keeps a name per tenant on its <see cref="Seat"/>, and the queries that answer
/// seats ask the directory with a view, <see cref="Of"/>: the directory decides which seats the caller is answered,
/// and hands the view each one's summary next to the seat itself, which it read anyway. The caller's own seats in
/// every tenant, for the tenant picker, come the same way, through <see cref="SeatOfMine.Of"/>. So every way a seat
/// is answered, a route, a GraphQL field, the answer of a mutation, the overview of the caller's own seat and the
/// picker, carries the name with no read more. The view picks what leaves: the seat it is handed has the identity
/// too, and the view leaves it there.
/// </remarks>
/// <param name="Id">The seat.</param>
/// <param name="DisplayName">The name it is shown by in its tenant, this application's own field.</param>
/// <param name="Status">Whether it counts.</param>
public sealed record SeatListing(SeatId Id, string DisplayName, SeatStatus Status)
{
    /// <summary>The seat as the directory answered it, with the name its own class keeps.</summary>
    /// <param name="seat">What the package's directory says of the seat.</param>
    /// <param name="own">The seat itself, this application's class, as the directory read it.</param>
    internal static SeatListing Of(TenantsTenancy.SeatSummary seat, Seat own) => new(seat.Id, own.DisplayName, seat.Status);

    /// <summary>
    /// <paramref name="seats"/> by name, and by id where two seats are shown by one name, so a list reads the same
    /// every time: the directory answers seats in the order of their ids, as it has nothing a person would order them
    /// by.
    /// </summary>
    /// <param name="seats">The seats, as the directory answered them through <see cref="Of"/>.</param>
    internal static IReadOnlyList<SeatListing> ByName(IEnumerable<SeatListing> seats)
        => [.. seats.OrderBy(seat => seat.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(seat => seat.Id)];
}
