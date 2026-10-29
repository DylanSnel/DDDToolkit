using DDDToolkit.Supporting.Tenancy.Access;
using Examples.Tenancy.Tenants.Api.Organization.Rest;
using Examples.Tenancy.Tenants.Api.Roles.Rest;
using Examples.Tenancy.Tenants.Api.Tenant.Rest;
using Examples.Tenancy.Tenants.Application.Seats.Commands;
using Examples.Tenancy.Tenants.Application.Seats.Queries;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
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
/// <see cref="TenantsModule"/> says what all of them keep to. An answer writes each object with the fields the
/// object's GraphQL type shows, under the object's own names: a seat is its id, its name and its status, and never
/// its identity. Internal: the module's entry maps them into the groups the host hands it.
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

        // Who the calling seat is: the seat manager's overview, with the tenant, the seat, where it is placed with which
        // roles for which period, the path of each unit, the roles its grants name, and every permission it holds now with
        // where it reaches. Only a seat has a self: everything else is refused.
        group.MapGet("/me", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok(Describe(await sender.Send(new OverviewOfMine(), cancellationToken))));

        // Every seat of the tenant, by name, for the pickers.
        group.MapGet("/tenancy/seats", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new TenantSeats(), cancellationToken)).Select(Describe)));

        // Another seat's roles, where and for when, from the package's overview of that seat: for whoever holds
        // tenancy.seats.manage for the whole tenant, which is this application's choice, and with ?unit= the roles held
        // at that unit, for whoever holds the permission there. A seat of another tenant, or of nobody, is not found: 404.
        group.MapGet("/tenancy/seats/{seatId}/grants", async (SeatId seatId, OrganizationUnitId? unit, ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new SeatGrants(seatId, unit), cancellationToken)).Select(grant => new
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
        // seat renames itself, another seat takes tenancy.seats.manage for the whole tenant. Each change answers the
        // seat as its command answers it.
        group.MapPut("/tenancy/seats/{seatId}/name", async (SeatId seatId, NewName body, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(Describe(await sender.Send(new RenameSeat(seatId, body.DisplayName), cancellationToken))));

        group.MapPost("/tenancy/seats/{seatId}/suspend", async (SeatId seatId, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(Describe(await sender.Send(new SuspendTenantSeat(seatId), cancellationToken))));

        group.MapPost("/tenancy/seats/{seatId}/reactivate", async (SeatId seatId, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(Describe(await sender.Send(new ReactivateTenantSeat(seatId), cancellationToken))));

        group.MapPost("/tenancy/seats/{seatId}/deactivate", async (SeatId seatId, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(Describe(await sender.Send(new DeactivateTenantSeat(seatId), cancellationToken))));

        return group;
    }

    /// <summary>
    /// A seat as every answer of this project writes it: its id, the name this application keeps on it and its status,
    /// the fields of its GraphQL type. Never the identity.
    /// </summary>
    internal static object Describe(Seat seat) => new { seat.Id, seat.DisplayName, seat.Status };

    // One of the caller's own seats: the seat as every answer writes it, beside its tenant's slug, name and status.
    private static object Describe(SeatInTenant<Seat> mine) => new
    {
        seat = Describe(mine.Seat),
        mine.Slug,
        mine.OrganizationName,
        mine.TenantStatus,
    };

    // The seat manager's overview, as its GraphQL type shows it: the tenant, the seat and its own placements with their
    // grants, each object written as the feature that owns it writes it, and what the manager read beside them.
    private static object Describe(SeatManager.SeatOverview overview) => new
    {
        tenant = TenantEndpoints.Describe(overview.Tenant),
        overview.TenantName,
        seat = Describe(overview.Seat),
        placements = overview.Seat.Placements.Select(placement => new
        {
            placement.UnitId,
            placement.IsPrimary,
            grants = placement.Grants.Select(grant => new { grant.RoleId, grant.StartsAt, grant.EndsAt }),
        }),
        units = overview.Units.Select(OrganizationEndpoints.Describe),
        roles = overview.Roles.Select(RolesEndpoints.Describe),
        permissions = overview.Permissions.Select(reach => new
        {
            reach.Permission,
            reach.AtTenant,
            grantedAt = reach.GrantedAt.Select(OrganizationEndpoints.Describe),
            reaches = reach.Reaches.Select(OrganizationEndpoints.Describe),
        }),
        overview.AsOf,
    };

    /// <summary>The name a seat is shown by from now on, by the rule of the seat class: 1 to 200 characters.</summary>
    public sealed record NewName(string? DisplayName);
}
