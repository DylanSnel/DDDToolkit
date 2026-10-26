using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Access.Queries;

/// <summary>
/// Whether the caller holds a permission on a project it may see, and through what: for a screen that asks about one
/// permission the commands have no single action for.
/// </summary>
/// <remarks>
/// It asks about the caller only, and only about a project the caller sees with <see cref="RequiredPermission"/>, so it
/// refuses nobody for the permission asked about: not holding it is the answer. The permission comes from outside, so the order
/// is the caller, then the permission, then whether the caller sees the project: a project the caller cannot see is not
/// found, as everywhere, whatever the permission.
/// </remarks>
/// <param name="Project">The project.</param>
/// <param name="Permission">A permission, as the request names it.</param>
[SeenWith<ProjectId>(RequiredPermission)]
public sealed record PermissionOnProject(ProjectId Project, string Permission) : IQuery<ProjectPermissionHeld>, IProjectsRequest
{
    /// <summary>The permission that decides whether the project is there for the caller at all.</summary>
    public const string RequiredPermission = ProjectPermissionNames.View;
}

/// <summary>Whether a caller holds a permission on a project, and how it reaches the project when it does.</summary>
/// <param name="Allowed">Whether the permission asked about is the caller's on that project.</param>
/// <param name="Via">Through what, or <see langword="null"/> when it does not hold it.</param>
public sealed record ProjectPermissionHeld(bool Allowed, ProjectVia? Via);

/// <summary>Answers <see cref="PermissionOnProject"/> in one statement, the access questions' own.</summary>
/// <param name="access">How the caller holds a permission on a project it may see.</param>
/// <param name="catalogue">The permissions the application knows, for a permission that comes from outside.</param>
public sealed class PermissionOnProjectHandler(ProjectAccess access, TenancyCatalogue catalogue) : IQueryHandler<PermissionOnProject, ProjectPermissionHeld>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.unknown-permission</c> for a permission the catalogue does not know; <c>projects.not-found</c> for a
    /// project the caller may not see.
    /// </exception>
    public async ValueTask<ProjectPermissionHeld> Handle(PermissionOnProject query, CancellationToken cancellationToken)
    {
        // The questions treat an unknown permission as a bug in the code that asks. Here it is only a wrong request.
        if (!catalogue.Knows(query.Permission))
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.UnknownPermission, ("Permissions", query.Permission));
        }

        var via = await access.ViaAsync(query.Project, query.Permission, cancellationToken);

        return new ProjectPermissionHeld(via is not null, via);
    }
}
