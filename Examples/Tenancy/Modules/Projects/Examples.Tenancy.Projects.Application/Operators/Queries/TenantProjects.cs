using DDDToolkit.Supporting.Tenancy.Access;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Operators.Queries;

/// <summary>
/// Every project of one tenant, by number, for the application's own staff: its own data and who changed it last.
/// </summary>
/// <remarks>
/// For operators only. An operator holds no seat and no key, so nothing here is filtered by a reach: the query
/// names the tenant, and answers all of its projects. A tenant that does not exist has none, and is answered an
/// empty list. It answers no crew: who is on a project, with which role, is the tenant's own business.
/// </remarks>
/// <param name="Tenant">The tenant whose projects are read.</param>
public sealed record TenantProjects(TenantId Tenant) : IQuery<IReadOnlyList<TenantProject>>, IProjectsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.OperatorsOnly();
}

/// <summary>Answers <see cref="TenantProjects"/> in one statement, on a reading of this query's own.</summary>
/// <param name="reads">Where projects are read: a context per query.</param>
public sealed class TenantProjectsHandler(IProjectReads reads) : IQueryHandler<TenantProjects, IReadOnlyList<TenantProject>>
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<TenantProject>> Handle(TenantProjects query, CancellationToken cancellationToken)
    {
        await using var reading = reads.Open();
        return await reading.OfTenantAsync(query.Tenant, cancellationToken);
    }
}
