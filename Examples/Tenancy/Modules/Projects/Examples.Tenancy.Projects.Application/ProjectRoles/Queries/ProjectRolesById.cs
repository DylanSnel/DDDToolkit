using DDDToolkit.Supporting.Tenancy.Access;
using Mediator;

namespace Examples.Tenancy.Projects.Application.ProjectRoles.Queries;

/// <summary>
/// Those of some project roles that are the tenant's, asked once for all of them: for whoever was handed ids and
/// shows what they stand for, such as a crew's roles.
/// </summary>
/// <remarks>
/// Nobody is refused for an id: a role of another tenant, and one that does not exist, are left out of the answer
/// alike. The keys of each are answered as <see cref="TenantProjectRoles"/> answers them.
/// </remarks>
/// <param name="Ids">The roles asked about, at most <see cref="MostRoles"/> different ones.</param>
public sealed record ProjectRolesById(IReadOnlyList<ProjectRoleId> Ids) : IQuery<IReadOnlyList<ProjectRoleListing>>, IProjectsRequest
{
    /// <summary>The most roles one question is about.</summary>
    public const int MostRoles = 200;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.InTenant();
}

/// <summary>
/// Answers <see cref="ProjectRolesById"/> in two statements, on a reading of this query's own, whatever the number
/// of ids; none when no id is asked about.
/// </summary>
/// <param name="reads">Where project roles are read: a context per query.</param>
/// <param name="access">Whether the caller holds the key the roles' keys are answered to.</param>
public sealed class ProjectRolesByIdHandler(IProjectReads reads, ProjectAccess access) : IQueryHandler<ProjectRolesById, IReadOnlyList<ProjectRoleListing>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>projects.too-many-ids</c>, with <c>Max</c>.</exception>
    public async ValueTask<IReadOnlyList<ProjectRoleListing>> Handle(ProjectRolesById query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query.Ids);

        var ids = query.Ids.Distinct().ToList();
        if (ids.Count > ProjectRolesById.MostRoles)
        {
            throw ProjectRefusals.Of(ProjectRefusals.TooManyIds, ("Max", ProjectRolesById.MostRoles));
        }

        if (ids.Count == 0)
        {
            return [];
        }

        // The roles, and whether the caller holds the key their keys are answered to, only when any is the tenant's.
        await using var reading = reads.Open();
        var roles = await reading.ProjectRolesAsync(ids, cancellationToken);
        return ProjectRoleListings.Answered(roles, roles.Count > 0 && await access.Questions(reading).HoldsTenantWideAsync(ProjectRoleListing.KeysKey, cancellationToken));
    }
}
