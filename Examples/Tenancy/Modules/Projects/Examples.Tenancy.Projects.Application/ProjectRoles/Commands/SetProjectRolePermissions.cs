using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
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

/// <summary>Handles <see cref="SetProjectRolePermissions"/>: loads the role, sets its permissions and saves.</summary>
/// <remarks>
/// Nothing of a package's stands between this command and the role, and nothing else asks who may send it, so the
/// handler asks again what its request declared, of the very checks the pipeline asks. It then decides the same
/// wherever it runs, in a program that left the module's access behavior out of the pipeline as much as in the
/// host, for the check's statement a second time on a command that is sent rarely.
/// </remarks>
/// <param name="store">Where project roles are loaded and saved.</param>
/// <param name="membership">The projects' rules, which say what a crew role may give.</param>
/// <param name="catalogue">The permissions the application knows.</param>
/// <param name="checks">The module's access checks, which hold a request to what it declared.</param>
public sealed class SetProjectRolePermissionsHandler(IProjectStore store, ProjectMembership membership, TenancyCatalogue catalogue, AccessChecks<IProjectsRequest> checks) : ICommandHandler<SetProjectRolePermissions>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c>, naming the permission, for a caller that does not hold it for the whole tenant;
    /// <c>tenancy.unknown-permission</c>; <c>projects.role-not-found</c>, for a role of another tenant too;
    /// <c>projects.role-archived</c>, <c>projects.permission-not-for-members</c>.
    /// </exception>
    public async ValueTask<Unit> Handle(SetProjectRolePermissions command, CancellationToken cancellationToken)
    {
        await checks.RequireAsync(command, cancellationToken);

        var permissions = MakeProjectRole.Known(command.Permissions, catalogue);
        var role = await store.LoadRoleAsync(command.Id, cancellationToken) ?? throw ProjectRefusals.Refuse(ProjectRefusals.RoleNotFound);

        role.SetRolePermissions(permissions, membership.Rules);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
