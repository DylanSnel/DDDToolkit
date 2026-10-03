using Examples.Tenancy.Tenants.Api.Directory.GraphQL;
using Examples.Tenancy.Tenants.Application.Seats.Commands;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// What changes whether a seat counts. A mutation sends the command its route sends, then reads what it changed
/// and answers that; a refusal is not caught here, and arrives in the payload's <c>errors</c>.
/// </summary>
internal static class SeatsMutations
{
    /// <summary>Suspends a seat: it keeps its placements and roles, and counts for nothing until it is reactivated.</summary>
    [Mutation]
    public static async Task<SampleTenancy.SeatSummary?> SeatSuspendAsync(SeatId seatId, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new SuspendTenantSeat(seatId), cancellationToken);
        return await sender.SeatNowAsync(seatId, cancellationToken);
    }

    /// <summary>Makes a suspended seat count again.</summary>
    [Mutation]
    public static async Task<SampleTenancy.SeatSummary?> SeatReactivateAsync(SeatId seatId, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivateTenantSeat(seatId), cancellationToken);
        return await sender.SeatNowAsync(seatId, cancellationToken);
    }

    /// <summary>Deactivates a seat for good.</summary>
    [Mutation]
    public static async Task<SampleTenancy.SeatSummary?> SeatDeactivateAsync(SeatId seatId, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateTenantSeat(seatId), cancellationToken);
        return await sender.SeatNowAsync(seatId, cancellationToken);
    }
}
