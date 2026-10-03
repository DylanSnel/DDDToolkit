using DDDToolkit.Supporting.Tenancy.Access;
using Examples.Tenancy.Inspections.Application.Recording;
using Examples.Tenancy.Inspections.Application.Recording.Queries;
using Mediator;

namespace Examples.Tenancy.Inspections.Application.Operators.Queries;

/// <summary>
/// Every inspection of one project of one tenant, newest first, for the application's own staff.
/// </summary>
/// <remarks>
/// For operators only. An operator holds no seat and no key, so Projects' gate has nothing to answer about it:
/// the query names the tenant and the project, and reads the inspections kept under both. A project of another
/// tenant, or one that does not exist, has none there, and is answered an empty list.
/// </remarks>
/// <param name="Tenant">The tenant the project is of.</param>
/// <param name="Project">The project.</param>
public sealed record TenantProjectInspections(TenantId Tenant, ProjectId Project) : IQuery<IReadOnlyList<InspectionOverview>>, IInspectionsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.OperatorsOnly();
}

/// <summary>Answers <see cref="TenantProjectInspections"/> in one statement, on a context of this query's own.</summary>
/// <param name="reads">Where inspections are read: a context per query.</param>
public sealed class TenantProjectInspectionsHandler(IInspectionReads reads) : IQueryHandler<TenantProjectInspections, IReadOnlyList<InspectionOverview>>
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<InspectionOverview>> Handle(TenantProjectInspections query, CancellationToken cancellationToken)
        => await reads.OfTenantProjectAsync(query.Tenant, query.Project, cancellationToken);
}
