using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Access.Queries;

/// <summary>
/// Which of some permissions the caller holds on each of some projects, and which of them it holds for the whole tenant:
/// for a client that shows many projects and what its user may do with each, asked once for all of them.
/// </summary>
/// <remarks>
/// It asks about the caller only, so it refuses nobody for a permission: holding none is the answer. A project the caller
/// may not see with <see cref="RequiredPermission"/> is left out of the answer exactly as one that does not exist, so
/// asking about an id tells nothing about it. What is answered is for showing: whether a command may run is asked
/// again when it runs.
/// </remarks>
/// <param name="Projects">The projects asked about, at most <see cref="MostProjects"/> different ones.</param>
/// <param name="Permissions">Permissions, as the request names them.</param>
[SeenWith<ProjectId>(RequiredPermission)]
public sealed record PermissionsOnProjects(IReadOnlyCollection<ProjectId> Projects, IReadOnlyCollection<string> Permissions) : IQuery<ProjectPermissionSets>, IProjectsRequest
{
    /// <summary>The permission that decides whether a project is there for the caller at all.</summary>
    public const string RequiredPermission = ProjectPermissionNames.View;

    /// <summary>The most projects one question is about.</summary>
    public const int MostProjects = 200;
}

/// <summary>The permissions a caller holds: for the whole tenant, and on each project asked about.</summary>
/// <param name="HeldAtRoot">Those of the permissions asked about that the caller holds at the tenant's root, and so everywhere.</param>
/// <param name="OnProjects">
/// For each project asked about that the caller sees and holds at least one of the permissions on: those permissions, through its
/// crew or through the organization, the projects by their ids' value. A project that is missing here is one the
/// caller holds none of them on, or does not see.
/// </param>
public sealed record ProjectPermissionSets(IReadOnlyList<string> HeldAtRoot, IReadOnlyList<ProjectPermissionSet> OnProjects);

/// <summary>What a caller holds on a single project, of the permissions it asked about.</summary>
/// <param name="Project">The project.</param>
/// <param name="Permissions">The permissions held on it, of those asked about, in the order of their text.</param>
public sealed record ProjectPermissionSet(ProjectId Project, IReadOnlyList<string> Permissions);

/// <summary>
/// Answers <see cref="PermissionsOnProjects"/> in two statements, on a reading of this query's own: the permissions held at the
/// root, and the permissions held on the projects, whatever their number.
/// </summary>
/// <param name="reads">Where projects are read: a context per query.</param>
/// <param name="access">Which permissions the caller holds where.</param>
/// <param name="catalogue">The permissions the application knows, for permissions that come from outside.</param>
public sealed class PermissionsOnProjectsHandler(IProjectReads reads, ProjectAccess access, TenancyCatalogue catalogue) : IQueryHandler<PermissionsOnProjects, ProjectPermissionSets>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.too-many-ids</c>, with <c>Max</c>; <c>tenancy.unknown-permission</c>, with the <c>Permissions</c> the
    /// catalogue does not know.
    /// </exception>
    public async ValueTask<ProjectPermissionSets> Handle(PermissionsOnProjects query, CancellationToken cancellationToken)
    {
        var projects = query.Projects.Distinct().ToList();
        if (projects.Count > PermissionsOnProjects.MostProjects)
        {
            throw ProjectRefusals.Refuse(ProjectRefusals.TooManyIds, ("Max", PermissionsOnProjects.MostProjects));
        }

        var permissions = PermissionsHeldAtRoot.Known(query.Permissions, catalogue);

        await using var reading = reads.Open();
        var atRoot = await access.HeldAtTenantAsync(reading, permissions, cancellationToken);

        if (projects.Count == 0 || permissions.Count == 0)
        {
            return new ProjectPermissionSets(atRoot, []);
        }

        // One order, the same every time: the projects by their ids' value, each one's permissions by their text.
        var held = await reading.PermissionsOnAsync(projects, access.PermissionsReachFor(permissions), cancellationToken);
        return new ProjectPermissionSets(
            atRoot,
            [.. held.OrderBy(project => project.Key.Value).Select(project => new ProjectPermissionSet(project.Key, [.. project.Value.Order(StringComparer.Ordinal)]))]);
    }
}
