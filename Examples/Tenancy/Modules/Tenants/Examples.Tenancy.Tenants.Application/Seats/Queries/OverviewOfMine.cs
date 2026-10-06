using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Queries;

/// <summary>
/// Who the calling seat is: its tenant, the seat with the name this application keeps on it, where it is placed and
/// with which roles for which period, and every key it holds now with where that key reaches.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant. Only a seat has a self, so the package's directory refuses
/// everyone else with <c>tenancy.not-seated</c>, system work included.
/// </remarks>
public sealed record OverviewOfMine : IQuery<SeatOverviewListing>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.InTenant();
}

/// <summary>
/// Answers <see cref="OverviewOfMine"/> from the Tenancy package's directory: the module's own seat, whole, with the
/// paths, role names and keys the package puts beside it, selected into what this module shows of a seat.
/// </summary>
/// <param name="reads">Where Tenancy is read: the directory, in a scope of this query's own.</param>
public sealed class OverviewOfMineHandler(ITenancyReads reads) : IQueryHandler<OverviewOfMine, SeatOverviewListing>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.not-seated</c> for every caller that is not a seat.</exception>
    public async ValueTask<SeatOverviewListing> Handle(OverviewOfMine query, CancellationToken cancellationToken)
    {
        var overview = await reads.AskDirectoryAsync(directory => directory.WhoAmIAsync(cancellationToken));
        var seat = overview.Seat;

        // The placements and grants are the seat's own; the path of each unit and the name of each role are what the
        // package read beside it, and whether a grant applies is asked at the moment the overview holds for. RoleOf
        // answers no role only for one the store did not read, as a filter on the role class would hide one; the grant
        // is the seat's all the same, so it is shown, with no name.
        return new SeatOverviewListing(
            overview.Tenant,
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
            overview.Roles,
            overview.Keys);
    }
}
