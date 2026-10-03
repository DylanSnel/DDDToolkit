using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Queries;

/// <summary>
/// Who the calling seat is: its tenant, where it is placed and with which roles for which period, and every key
/// it holds now with where that key reaches.
/// </summary>
/// <remarks>
/// The package decides who may ask: only a seat has a self, so its directory refuses everyone else with
/// <c>tenancy.not-seated</c>, system work included.
/// </remarks>
public sealed record OverviewOfMine : IQuery<SampleTenancy.SeatOverview>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.DecidedByThePackage();
}

/// <summary>Answers <see cref="OverviewOfMine"/> from the Tenancy package's directory.</summary>
/// <param name="reads">Where Tenancy is read: the directory, in a scope of this query's own.</param>
public sealed class OverviewOfMineHandler(ITenancyReads reads) : IQueryHandler<OverviewOfMine, SampleTenancy.SeatOverview>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.not-seated</c> for every caller that is not a seat.</exception>
    public async ValueTask<SampleTenancy.SeatOverview> Handle(OverviewOfMine query, CancellationToken cancellationToken)
        => await reads.AskDirectoryAsync(directory => directory.WhoAmIAsync(cancellationToken));
}
