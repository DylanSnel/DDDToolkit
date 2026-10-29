using Acme.Press.Tenants;
using DDDToolkit.Supporting.Tenancy;

namespace Acme.Press.Host;

/// <summary>
/// What the host asks Tenancy's seat manager, named <c>SeatManager</c>: the package's manager closed over the classes,
/// which the toolkit's generator writes into the domain project that declares them, so that no project above it writes
/// the nine types. The host sees it through the infrastructure project. This does not compile when that generator did
/// not arrive in the domain project.
/// </summary>
public static class PressSeats
{
    /// <summary>
    /// Whether the seats with these ids count, in the tenant the caller works in. The manager answers the house's own
    /// seats, whole; a seat has no name in Tenancy, and the house keeps none on its seat class, so it selects its id and
    /// status alone.
    /// </summary>
    /// <param name="manager">Tenancy's seat manager, which <c>AddTenancy</c> registered.</param>
    /// <param name="seats">The seats.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    public static async Task<IReadOnlyList<(SeatId Id, SeatStatus Status)>> OfAsync(SeatManager manager, IReadOnlyCollection<SeatId> seats, CancellationToken cancellationToken)
        => [.. (await manager.ByIdAsync(seats, cancellationToken)).Select(seat => (seat.Id, seat.Status))];
}
