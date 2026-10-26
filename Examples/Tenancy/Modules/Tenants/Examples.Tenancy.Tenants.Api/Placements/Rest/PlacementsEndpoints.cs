using System.Text.Json.Serialization;
using Examples.Tenancy.Tenants.Application.Placements.Commands;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.Placements.Rest;

/// <summary>
/// Where a seat is placed, over HTTP: placing it at a unit and withdrawing it from one. Each route sends one
/// command of the application project's <c>Placements</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="TenantsModule"/> says what all of them keep to. Internal: the module's entry maps them into the
/// group the host hands it.
/// </remarks>
internal static class PlacementsEndpoints
{
    /// <summary>
    /// Maps <c>POST /tenancy/seats/{seatId}/placements</c> and
    /// <c>DELETE /tenancy/seats/{seatId}/placements/{unitId}</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapPlacementsEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/tenancy/seats/{seatId}/placements", async (SeatId seatId, PlacementToMake body, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new MakePlacement(seatId, body.UnitId, body.Primary), cancellationToken);
            return Results.NoContent();
        });

        group.MapDelete("/tenancy/seats/{seatId}/placements/{unitId}", async (SeatId seatId, OrganizationUnitId unitId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new WithdrawPlacement(seatId, unitId), cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>
    /// A placement to make. The unit is required: left out, it would bind as an empty id and come back as a
    /// refusal about a unit nobody named, where a 400 invalid-request says what is wrong.
    /// </summary>
    public sealed record PlacementToMake([property: JsonRequired] OrganizationUnitId UnitId, bool Primary);
}
