using Examples.Tenancy.Tenants.Api.Directory.GraphQL;
using Examples.Tenancy.Tenants.Application.Seats;
using Examples.Tenancy.Tenants.Application.Seats.Commands;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// What changes a seat: whether it counts, which the package decides, and the name it is shown by, which is this
/// application's own. A mutation sends the command its route sends, then reads what it changed and answers that; a
/// refusal is not caught here, and arrives in the payload's <c>errors</c>.
/// </summary>
internal static class SeatsMutations
{
    /// <summary>
    /// Gives a seat another name to be shown by: this application's own command, under its own rule. A seat renames
    /// itself; another seat takes <c>tenancy.seats.manage</c> for the whole tenant.
    /// </summary>
    [Mutation]
    public static async Task<SeatListing?> SeatRenameAsync(SeatId seatId, string displayName, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new RenameSeat(seatId, displayName), cancellationToken);
        return await sender.SeatNowAsync(seatId, cancellationToken);
    }

    /// <summary>Suspends a seat: it keeps its placements and roles, and counts for nothing until it is reactivated.</summary>
    [Mutation]
    public static async Task<SeatListing?> SeatSuspendAsync(SeatId seatId, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new SuspendTenantSeat(seatId), cancellationToken);
        return await sender.SeatNowAsync(seatId, cancellationToken);
    }

    /// <summary>Makes a suspended seat count again.</summary>
    [Mutation]
    public static async Task<SeatListing?> SeatReactivateAsync(SeatId seatId, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivateTenantSeat(seatId), cancellationToken);
        return await sender.SeatNowAsync(seatId, cancellationToken);
    }

    /// <summary>Deactivates a seat for good.</summary>
    [Mutation]
    public static async Task<SeatListing?> SeatDeactivateAsync(SeatId seatId, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateTenantSeat(seatId), cancellationToken);
        return await sender.SeatNowAsync(seatId, cancellationToken);
    }
}
