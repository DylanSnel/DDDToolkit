using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Queries;

/// <summary>
/// Every seat of the caller's tenant, by name, for the pickers: its id, its name and its status, never an
/// identity.
/// </summary>
/// <remarks>The package decides who may ask: its directory answers anyone who works in the tenant.</remarks>
public sealed record TenantSeats : IQuery<IReadOnlyList<SampleTenancy.SeatSummary>>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.DecidedByThePackage();
}

/// <summary>Answers <see cref="TenantSeats"/> from the Tenancy package's directory.</summary>
/// <param name="reads">Where Tenancy is read: the directory, in a scope of this query's own.</param>
public sealed class TenantSeatsHandler(ITenancyReads reads) : IQueryHandler<TenantSeats, IReadOnlyList<SampleTenancy.SeatSummary>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">The caller's own refusal when it is nobody.</exception>
    public async ValueTask<IReadOnlyList<SampleTenancy.SeatSummary>> Handle(TenantSeats query, CancellationToken cancellationToken)
        => await reads.AskDirectoryAsync(directory => directory.ListSeatsAsync(cancellationToken));
}
