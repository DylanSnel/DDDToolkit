namespace Examples.Tenancy.Tenants.Application.Seats;

/// <summary>
/// A seat as this application shows it: its id, the name this application keeps on its own seat class, and its
/// status. Never an identity.
/// </summary>
/// <remarks>
/// A seat has no name in Tenancy: no rule of the package reads one, so what a person is shown by is the
/// application's to say. This one keeps a name per tenant on its <c>Seat</c>, and the package's directory answers
/// that class whole, as does its lookup of the caller's own seats in every tenant, for the tenant picker. So each query
/// that answers seats selects this from the seats the package read, with no read more: a route, a GraphQL field, the
/// answer of a mutation, a seat's overview and the picker all carry the name. The seat it selects from has the
/// identity too; the select leaves it there.
/// </remarks>
/// <param name="Id">The seat.</param>
/// <param name="DisplayName">The name it is shown by in its tenant, this application's own field.</param>
/// <param name="Status">Whether it counts.</param>
public sealed record SeatListing(SeatId Id, string DisplayName, SeatStatus Status)
{
    /// <summary>
    /// <paramref name="seats"/> by name, and by id where two seats are shown by one name, so a list reads the same
    /// every time: the directory answers seats in the order of their ids, as it has nothing a person would order them
    /// by.
    /// </summary>
    /// <param name="seats">The seats, as a query selected them.</param>
    internal static IReadOnlyList<SeatListing> ByName(IEnumerable<SeatListing> seats)
        => [.. seats.OrderBy(seat => seat.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(seat => seat.Id)];
}
