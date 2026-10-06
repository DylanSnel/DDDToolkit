using Examples.Tenancy.Tenants.Api.Directory.GraphQL;
using Examples.Tenancy.Tenants.Application.Placements.Commands;
using Examples.Tenancy.Tenants.Application.Seats;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Placements.GraphQL;

/// <summary>
/// What changes where a seat is placed. A mutation sends the command its route sends, then answers the seat; a
/// refusal arrives in the payload's <c>errors</c>.
/// </summary>
internal static class PlacementsMutations
{
    /// <summary>Places a seat in a unit, as its primary placement or beside it.</summary>
    [Mutation]
    public static async Task<SeatListing?> SeatPlaceAsync(SeatId seatId, OrganizationUnitId unitId, bool primary, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new MakePlacement(seatId, unitId, primary), cancellationToken);
        return await sender.SeatNowAsync(seatId, cancellationToken);
    }

    /// <summary>Withdraws a seat from a unit, with the roles it held there.</summary>
    [Mutation]
    public static async Task<SeatListing?> PlacementWithdrawAsync(SeatId seatId, OrganizationUnitId unitId, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new WithdrawPlacement(seatId, unitId), cancellationToken);
        return await sender.SeatNowAsync(seatId, cancellationToken);
    }
}
