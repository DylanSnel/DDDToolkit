using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Queries;

/// <summary>
/// Who the calling seat is: its tenant, the seat with the name this application keeps on it, where it is placed and
/// with which roles for which period, and every permission it holds now with where that permission reaches.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant. Only a seat has a self, so the package's directory refuses
/// everyone else with <c>tenancy.not-seated</c>, system work included.
/// </remarks>
[InTenant]
public sealed record OverviewOfMine : IQuery<SeatOverviewListing>, ITenantsRequest;

/// <summary>
/// Answers <see cref="OverviewOfMine"/> from the Tenancy package's seat manager: the module's own seat, whole, with the
/// paths, roles and permissions the package puts beside it, selected into what this module shows of a seat.
/// </summary>
/// <param name="reads">Where Tenancy is read: the seat manager, in a scope of this query's own.</param>
/// <param name="roles">The role manager, which says whether a role manages access. It reads nothing for that.</param>
public sealed class OverviewOfMineHandler(ITenancyReads reads, RoleManager roles) : IQueryHandler<OverviewOfMine, SeatOverviewListing>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.not-seated</c> for every caller that is not a seat.</exception>
    public async ValueTask<SeatOverviewListing> Handle(OverviewOfMine query, CancellationToken cancellationToken)
    {
        var overview = await reads.AskAsync((SeatManager seats) => seats.WhoAmIAsync(cancellationToken));
        var seat = overview.Seat;

        // The placements and grants are the seat's own; the path of each unit and the name of each role are what the
        // package read beside it, and whether a grant applies is asked at the moment the overview holds for. RoleOf
        // answers no role only for one the store did not read, as a filter on the role class would hide one; the grant
        // is the seat's all the same, so it is shown, with no name.
        return new SeatOverviewListing(
            new TenantOfMine(overview.Tenant.Id, overview.Tenant.Slug.Value, overview.TenantName, overview.Tenant.Shape, overview.Tenant.Status),
            new SeatListing(seat.Id, seat.DisplayName, seat.Status),
            [
                .. seat.Placements
                    .Select(placement => new PlacementListing(
                        overview.UnitOf(placement.UnitId),
                        placement.IsPrimary,
                        [
                            .. placement.Grants
                                .Select(grant => new GrantListing(grant.RoleId, overview.RoleOf(grant.RoleId)?.Name ?? string.Empty, grant.StartsAt, grant.EndsAt, grant.AppliesAt(overview.AsOf)))
                                .OrderBy(grant => grant.Role, StringComparer.OrdinalIgnoreCase),
                        ]))
                    .OrderByDescending(placement => placement.IsPrimary)
                    .ThenBy(placement => placement.Unit.Path, StringComparer.OrdinalIgnoreCase),
            ],
            [.. overview.Roles.Select(role => new RoleOfMine(role.Id, role.Name, role.FromStarterRole, role.Status, role.Permissions, roles.ManagesAccess(role)))],
            overview.Permissions);
    }
}
