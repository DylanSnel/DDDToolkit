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
public sealed record OverviewOfMine : IQuery<TenantsTenancy.SeatOverview<SeatListing>>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.InTenant();
}

/// <summary>
/// Answers <see cref="OverviewOfMine"/> from the Tenancy package's directory, the seat with the name its own class
/// keeps (<see cref="SeatListing.Of"/>), from the seat the directory read anyway.
/// </summary>
/// <param name="reads">Where Tenancy is read: the directory, in a scope of this query's own.</param>
public sealed class OverviewOfMineHandler(ITenancyReads reads) : IQueryHandler<OverviewOfMine, TenantsTenancy.SeatOverview<SeatListing>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.not-seated</c> for every caller that is not a seat.</exception>
    public async ValueTask<TenantsTenancy.SeatOverview<SeatListing>> Handle(OverviewOfMine query, CancellationToken cancellationToken)
        => await reads.AskDirectoryAsync(directory => directory.WhoAmIAsync(SeatListing.Of, cancellationToken));
}
