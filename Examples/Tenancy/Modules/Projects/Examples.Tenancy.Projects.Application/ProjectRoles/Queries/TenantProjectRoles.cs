using DDDToolkit.Supporting.Tenancy.Access;
using Mediator;

namespace Examples.Tenancy.Projects.Application.ProjectRoles.Queries;

/// <summary>
/// The tenant's project roles, archived ones included, by name: what a screen offers to give on a crew, names a
/// crew's roles by, and manages the roles from.
/// </summary>
/// <remarks>
/// Whoever works in the tenant reads them, and the keys of each for a caller who holds
/// <see cref="ProjectRoleListing.KeysKey"/> for the whole tenant (<see cref="ProjectRoleListing"/>).
/// </remarks>
public sealed record TenantProjectRoles : IQuery<IReadOnlyList<ProjectRoleListing>>, IProjectsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.InTenant();
}

/// <summary>Answers <see cref="TenantProjectRoles"/> in two statements, on a reading of this query's own.</summary>
/// <param name="reads">Where project roles are read: a context per query.</param>
/// <param name="access">Whether the caller holds the key the roles' keys are answered to.</param>
public sealed class TenantProjectRolesHandler(IProjectReads reads, ProjectAccess access) : IQueryHandler<TenantProjectRoles, IReadOnlyList<ProjectRoleListing>>
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ProjectRoleListing>> Handle(TenantProjectRoles query, CancellationToken cancellationToken)
    {
        // The roles, and whether the caller holds the key their keys are answered to, only when there are any.
        await using var reading = reads.Open();
        var roles = await reading.ProjectRolesAsync(ids: null, cancellationToken);
        return ProjectRoleListings.Answered(roles, roles.Count > 0 && await access.Questions(reading).HoldsTenantWideAsync(ProjectRoleListing.KeysKey, cancellationToken));
    }
}
