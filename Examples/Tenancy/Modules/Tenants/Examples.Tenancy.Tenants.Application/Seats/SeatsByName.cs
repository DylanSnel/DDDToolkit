using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;

namespace Examples.Tenancy.Tenants.Application.Seats;

/// <summary>The order the module answers a list of seats in: by the name this application keeps on each.</summary>
internal static class SeatsByName
{
    /// <summary>
    /// <paramref name="seats"/> by name, and by id where two seats are shown by one name, so a list reads the same every
    /// time. The seat manager answers seats in the order of their ids, as it has nothing a person would order them by.
    /// </summary>
    /// <param name="seats">The seats, as the seat manager read them.</param>
    public static IReadOnlyList<Seat> Ordered(IEnumerable<Seat> seats)
        => [.. seats.OrderBy(seat => seat.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(seat => seat.Id)];
}
