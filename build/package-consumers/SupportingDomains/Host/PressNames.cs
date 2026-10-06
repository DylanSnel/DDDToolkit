using Acme.Press.Tenants;

namespace Acme.Press.Host;

/// <summary>
/// What the host asks Tenancy's directory, named through <c>PressTenancy</c>: Tenancy's use cases closed over the
/// classes, which the toolkit's generator writes into the domain project that declares them, so that no project
/// above it writes the nine types. The host sees it through the infrastructure project. This does not compile when
/// that generator did not arrive in the domain project.
/// </summary>
public static class PressNames
{
    /// <summary>What the seats with these ids are called, in the tenant the caller works in.</summary>
    /// <param name="directory">Tenancy's directory, which <c>AddTenancy</c> registered.</param>
    /// <param name="seats">The seats.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    public static async Task<IReadOnlyList<string>> OfAsync(PressTenancy.TenancyDirectory directory, IReadOnlyCollection<SeatId> seats, CancellationToken cancellationToken)
        => [.. (await directory.SeatsByIdAsync(seats, cancellationToken)).Select(seat => seat.DisplayName)];
}
