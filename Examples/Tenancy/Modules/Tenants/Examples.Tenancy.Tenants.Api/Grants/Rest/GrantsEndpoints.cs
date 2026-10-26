using System.Text.Json.Serialization;
using Examples.Tenancy.Tenants.Application.Grants.Commands;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.Grants.Rest;

/// <summary>
/// The roles a seat holds at a unit, over HTTP: granting one and revoking one. Each route sends one command of
/// the application project's <c>Grants</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="TenantsModule"/> says what all of them keep to. Internal: the module's entry maps them into the
/// group the host hands it.
/// </remarks>
internal static class GrantsEndpoints
{
    /// <summary>
    /// Maps <c>POST /tenancy/seats/{seatId}/grants</c> and
    /// <c>DELETE /tenancy/seats/{seatId}/grants/{unitId}/{roleId}</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapGrantsEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // A grant made over HTTP starts now: the package refuses a start from anyone but system work, so neither
        // the body nor the command has a field for one.
        group.MapPost("/tenancy/seats/{seatId}/grants", async (SeatId seatId, GrantToMake body, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new MakeGrant(seatId, body.UnitId, body.RoleId, body.EndsAt, body.Reason), cancellationToken);
            return Results.NoContent();
        });

        group.MapDelete("/tenancy/seats/{seatId}/grants/{unitId}/{roleId}", async (SeatId seatId, OrganizationUnitId unitId, RoleId roleId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new RevokeGrant(seatId, unitId, roleId), cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>
    /// A grant to make, from now until <paramref name="EndsAt"/> (<c>until</c>), or for good. The unit and
    /// the role are required: left out, either would bind as an empty id and come back as a refusal about a unit
    /// or a role nobody named, where a 400 invalid-request says what is wrong.
    /// </summary>
    public sealed record GrantToMake(
        [property: JsonRequired] OrganizationUnitId UnitId,
        [property: JsonRequired] RoleId RoleId,
        [property: JsonPropertyName("until")] DateTimeOffset? EndsAt,
        string? Reason);
}
