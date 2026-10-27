using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Queries;

/// <summary>
/// Who the calling seat is: the seat manager's overview of it. The overview holds the tenant, the seat with its
/// placements and grants, the path of each unit it is placed at, the roles its grants name, and every permission it
/// holds now with where that permission reaches.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant. Only a seat has a self, so the seat manager refuses everyone else
/// with <c>tenancy.not-seated</c>, system work included. The seat holds its identity, so whatever shows the overview
/// writes what it chooses of the seat, and never the identity.
/// </remarks>
[InTenant]
public sealed record OverviewOfMine : IQuery<SeatManager.SeatOverview>, ITenantsRequest;

/// <summary>Answers <see cref="OverviewOfMine"/> from the Tenancy package's seat manager.</summary>
/// <param name="reads">Where Tenancy is read: the seat manager, in a scope of this query's own.</param>
public sealed class OverviewOfMineHandler(ITenancyReads reads) : IQueryHandler<OverviewOfMine, SeatManager.SeatOverview>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.not-seated</c> for every caller that is not a seat.</exception>
    public async ValueTask<SeatManager.SeatOverview> Handle(OverviewOfMine query, CancellationToken cancellationToken)
        => await reads.AskAsync((SeatManager seats) => seats.WhoAmIAsync(cancellationToken));
}
