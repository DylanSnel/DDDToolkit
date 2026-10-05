using Mediator;

namespace Examples.Tenancy.Tenants.Application.Organization.Queries;

/// <summary>
/// The units the caller reads, each by its path from the root: for a seat, the units it is placed in and every
/// unit below them; for system work in the tenant, every unit.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant. Which units a seat reads is the package's to say: its directory answers.
/// </remarks>
public sealed record OrganizationUnits : IQuery<IReadOnlyList<SampleTenancy.UnitSummary>>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.InTenant();
}

/// <summary>Answers <see cref="OrganizationUnits"/> from the Tenancy package's directory.</summary>
/// <param name="reads">Where Tenancy is read: the directory, in a scope of this query's own.</param>
public sealed class OrganizationUnitsHandler(ITenancyReads reads) : IQueryHandler<OrganizationUnits, IReadOnlyList<SampleTenancy.UnitSummary>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">The caller's own refusal when it is nobody.</exception>
    public async ValueTask<IReadOnlyList<SampleTenancy.UnitSummary>> Handle(OrganizationUnits query, CancellationToken cancellationToken)
        => await reads.AskDirectoryAsync(directory => directory.ListUnitsAsync(cancellationToken));
}
