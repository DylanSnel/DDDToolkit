using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Projects.Application.ProjectRoles.Commands;

/// <summary>
/// Has one of the tenant's project roles give these permissions from now on, on every crew it is held on: every permission it is
/// to give, chosen from the permissions a crew role may give.
/// </summary>
/// <remarks>
/// The crew lead's role is given other permissions like any other. What a project's owner may do does not hang on it: the owner
/// holds no role, and what it holds by owning the project the projects' permissions say.
/// </remarks>
/// <param name="Id">The role.</param>
/// <param name="Permissions">Every permission it is to give.</param>
[RequiresPermission(RequiredPermission)]
public sealed record SetProjectRolePermissions(ProjectRoleId Id, IReadOnlyCollection<string> Permissions) : ICommand, IProjectsRequest
{
    /// <summary>The permission the caller holds for the whole tenant.</summary>
    public const string RequiredPermission = ProjectRoleListing.ManageRolesPermission;
}

/// <summary>
/// Handles <see cref="SetProjectRolePermissions"/>: refuses a permission the catalogue does not know, as Tenancy refuses
/// it, then has the project role manager set the role's permissions and save.
/// </summary>
/// <param name="roles">The project role manager, which holds a role's permissions to what the projects' rules let a crew role give.</param>
/// <param name="catalogue">The permissions the application knows.</param>
public sealed class SetProjectRolePermissionsHandler(ProjectRoleManager roles, TenancyCatalogue catalogue) : ICommandHandler<SetProjectRolePermissions>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.unknown-permission</c>; <c>tenancy.not-permitted</c>, naming the permission, for a caller that does not
    /// hold it for the whole tenant; <c>projects.role-not-found</c>, for a role of another tenant too;
    /// <c>projects.role-archived</c>, <c>projects.permission-not-for-members</c>.
    /// </exception>
    public async ValueTask<Unit> Handle(SetProjectRolePermissions command, CancellationToken cancellationToken)
    {
        await roles.SetPermissionsAsync(command.Id, MakeProjectRole.Known(command.Permissions, catalogue), cancellationToken);
        return Unit.Value;
    }
}
