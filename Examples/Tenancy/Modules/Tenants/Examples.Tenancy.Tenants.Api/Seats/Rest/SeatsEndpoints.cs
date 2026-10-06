using Examples.Tenancy.Tenants.Api.Organization.Rest;
using Examples.Tenancy.Tenants.Api.Roles.Rest;
using Examples.Tenancy.Tenants.Application.Seats;
using Examples.Tenancy.Tenants.Application.Seats.Commands;
using Examples.Tenancy.Tenants.Application.Seats.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.Seats.Rest;

/// <summary>
/// Seats over HTTP: who am I and which seats are mine, the tenant's seats, renaming one, and suspending,
/// reactivating and deactivating one. Each route sends one command or query of the application project's
/// <c>Seats</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="TenantsModule"/> says what all of them keep to. Internal: the module's entry maps them into the
/// groups the host hands it.
/// </remarks>
internal static class SeatsEndpoints
{
    /// <summary>
    /// <c>GET /me/seats</c>: the signed-in person's seats in every tenant, suspended ones included, for picking
    /// a tenant. It needs a token and no tenant, so it is mapped outside the group that requires a seat.
    /// </summary>
    public static IEndpointRouteBuilder MapSeatsOfMine(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/me/seats", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new SeatsOfMine(), cancellationToken)).Select(Describe)));

        return group;
    }

    /// <summary>
    /// Maps <c>GET /me</c>, <c>GET /tenancy/seats</c>, <c>GET /tenancy/seats/{seatId}/grants</c>,
    /// <c>PUT /tenancy/seats/{seatId}/name</c>, and <c>POST /tenancy/seats/{seatId}/suspend</c>, <c>/reactivate</c>
    /// and <c>/deactivate</c>, each for a seat in the tenant the request selected.
    /// </summary>
    public static IEndpointRouteBuilder MapSeatsEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Who the calling seat is, where it is placed, which roles it holds for which period, and every key it
        // holds now with where it reaches. Only a seat has a self: everything else is refused.
        group.MapGet("/me", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok(Describe(await sender.Send(new OverviewOfMine(), cancellationToken))));

        // Every seat of the tenant, by name, for the pickers: id, the name this application keeps and status, never an
        // identity.
        group.MapGet("/tenancy/seats", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new TenantSeats(), cancellationToken)).Select(Describe)));

        // Another seat's roles, where and for when: for whoever holds tenancy.seats.manage for the whole tenant.
        group.MapGet("/tenancy/seats/{seatId}/grants", async (SeatId seatId, ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new SeatGrants(seatId), cancellationToken)).Select(grant => new
            {
                grant.UnitId,
                grant.UnitPath,
                grant.RoleId,
                grant.Role,
                grant.StartsAt,
                grant.EndsAt,
                grant.AppliesNow,
            })));

        // The name a seat is shown by is this application's field, and renaming it this application's own command: a
        // seat renames itself, another seat takes tenancy.seats.manage for the whole tenant.
        group.MapPut("/tenancy/seats/{seatId}/name", async (SeatId seatId, NewName body, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new RenameSeat(seatId, body.DisplayName), cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/tenancy/seats/{seatId}/suspend", async (SeatId seatId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new SuspendTenantSeat(seatId), cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/tenancy/seats/{seatId}/reactivate", async (SeatId seatId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new ReactivateTenantSeat(seatId), cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/tenancy/seats/{seatId}/deactivate", async (SeatId seatId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new DeactivateTenantSeat(seatId), cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>
    /// A seat as every answer of this project writes it: its id, the name this application keeps on it and its status,
    /// never an identity.
    /// </summary>
    internal static object Describe(SeatListing seat) => new { seat.Id, seat.DisplayName, seat.Status };

    // One of the caller's own seats: its tenant, and the seat as every answer writes it, by the name that tenant keeps.
    private static object Describe(SeatOfMine mine) => new
    {
        tenant = new { mine.Tenant.Id, mine.Tenant.Slug, mine.Tenant.Name, mine.Tenant.Status },
        seat = Describe(mine.Seat),
    };

    // A seat's own overview shows units and roles too, each as the feature that owns it writes it.
    private static object Describe(SeatOverviewListing overview) => new
    {
        tenant = new
        {
            overview.Tenant.Id,
            overview.Tenant.Slug,
            overview.Tenant.Name,
            overview.Tenant.Shape,
            overview.Tenant.Status,
        },
        seat = Describe(overview.Seat),
        placements = overview.Placements.Select(placement => new
        {
            unit = OrganizationEndpoints.Describe(placement.Unit),
            placement.IsPrimary,
            grants = placement.Grants.Select(grant => new { grant.RoleId, grant.Role, grant.StartsAt, grant.EndsAt, grant.AppliesNow }),
        }),
        roles = overview.Roles.Select(RolesEndpoints.Describe),
        keys = overview.Keys.Select(reach => new
        {
            reach.Key,
            reach.WholeTenant,
            grantedAt = reach.GrantedAt.Select(OrganizationEndpoints.Describe),
            reaches = reach.Reaches.Select(OrganizationEndpoints.Describe),
        }),
    };

    /// <summary>The name a seat is shown by from now on, by the rule of the seat class: 1 to 200 characters.</summary>
    public sealed record NewName(string? DisplayName);
}
