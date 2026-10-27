using Mediator;

namespace Examples.Tenancy.Tenants.Application.Organization.Queries;

/// <summary>
/// The units the caller reads, each with its path from the root and its depth: for a seat, the units it is placed in
/// and every unit below them; for system work in the tenant, every unit.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant. Which units a seat reads is the package's to say: its organization
/// manager answers. Each unit is the module's own class, with the kind it keeps, in the organization manager's record
/// beside its path and depth, which the unit does not hold.
/// </remarks>
[InTenant]
public sealed record OrganizationUnits : IQuery<IReadOnlyList<OrganizationManager.UnitInTree>>, ITenantsRequest;

/// <summary>Answers <see cref="OrganizationUnits"/> from the Tenancy package's organization manager.</summary>
/// <param name="reads">Where Tenancy is read: the organization manager, in a scope of this query's own.</param>
public sealed class OrganizationUnitsHandler(ITenancyReads reads) : IQueryHandler<OrganizationUnits, IReadOnlyList<OrganizationManager.UnitInTree>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">The caller's own refusal when it is nobody.</exception>
    public async ValueTask<IReadOnlyList<OrganizationManager.UnitInTree>> Handle(OrganizationUnits query, CancellationToken cancellationToken)
        => await reads.AskAsync((OrganizationManager manager) => manager.ListUnitsAsync(cancellationToken));
}
