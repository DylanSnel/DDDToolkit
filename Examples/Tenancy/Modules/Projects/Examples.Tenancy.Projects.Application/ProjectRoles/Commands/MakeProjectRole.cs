using DDDToolkit.Access;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Projects.Application.ProjectRoles.Commands;

/// <summary>
/// Makes a project role of the tenant's own: a name, what it is for, and the permissions it gives on whatever crew it is
/// given on, chosen from the permissions a crew role may give.
/// </summary>
/// <remarks>
/// Who makes, renames and archives project roles, and sets their permissions, is this application's rule: whoever manages the tenant's
/// roles, <see cref="RequiredPermission"/> held for the whole tenant, the permission the roles of the organization are managed
/// with as well. A project role's permissions decide what everybody who holds it may do on a crew, so it is decided where
/// the tenant's roles are.
/// </remarks>
/// <param name="Name">Its name, unique among the tenant's project roles.</param>
/// <param name="Description">What it is for, or <see langword="null"/>.</param>
/// <param name="Permissions">The permissions it gives: permissions of the catalogue that a crew role may give.</param>
[RequiresPermission(RequiredPermission)]
public sealed record MakeProjectRole(string Name, string? Description, IReadOnlyCollection<string> Permissions) : ICommand<ProjectRoleId>, IProjectsRequest
{
    /// <summary>The permission the caller holds for the whole tenant.</summary>
    public const string RequiredPermission = ProjectRoleListing.ManageRolesPermission;

    /// <summary>
    /// <paramref name="permissions"/>, when the catalogue knows every one of them: a permission it does not know is a wrong
    /// request, refused as Tenancy refuses it. Whether a crew role may give each is the projects' rules' to say.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.unknown-permission</c>, with the <c>Permissions</c> that are unknown.</exception>
    internal static IReadOnlyCollection<string> Known(IReadOnlyCollection<string>? permissions, TenancyCatalogue catalogue)
    {
        var asked = permissions ?? [];
        var unknown = asked.Where(permission => !catalogue.Knows(permission)).Distinct(StringComparer.Ordinal).ToList();
        return unknown.Count > 0
            ? throw TenancyRefusals.Refuse(TenancyRefusals.UnknownPermission, ("Permissions", string.Join(", ", unknown)))
            : asked;
    }
}

/// <summary>Handles <see cref="MakeProjectRole"/>: makes the role, in the caller's tenant, and saves it.</summary>
/// <remarks>
/// Nothing of a package's stands between this command and the role, and nothing else asks who may send it, so the
/// handler asks again what its request declared, of the very checks the pipeline asks. It then decides the same
/// wherever it runs, in a program that left the module's access behavior out of the pipeline as much as in the
/// host, for the check's statement a second time on a command that is sent rarely.
/// </remarks>
/// <param name="store">Where project roles are added and saved.</param>
/// <param name="membership">The projects' rules, which a role's permissions are held to.</param>
/// <param name="catalogue">The permissions the application knows.</param>
/// <param name="answers">Tenancy's answers about the current caller: the tenant the role is made in.</param>
/// <param name="checks">The module's access checks, which hold a request to what it declared.</param>
public sealed class MakeProjectRoleHandler(IProjectStore store, ProjectMembership membership, TenancyCatalogue catalogue, SampleAnswers answers, AccessChecks<IProjectsRequest> checks)
    : ICommandHandler<MakeProjectRole, ProjectRoleId>
{
    /// <inheritdoc />
    /// <returns>The new role's id.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c>, naming the permission, for a caller that does not hold it for the whole tenant;
    /// <c>tenancy.unknown-permission</c>, <c>projects.role-name-invalid</c>, <c>projects.permission-not-for-members</c>,
    /// <c>projects.role-name-taken</c>.
    /// </exception>
    public async ValueTask<ProjectRoleId> Handle(MakeProjectRole command, CancellationToken cancellationToken)
    {
        await checks.RequireAsync(command, cancellationToken);

        var permissions = MakeProjectRole.Known(command.Permissions, catalogue);
        var role = new ProjectRole(ProjectRoleId.Create(), answers.RequireTenant().Tenant, new CustomRoleDraft(command.Name, command.Description, permissions), membership.Rules);

        store.Add(role);
        await store.SaveAsync(cancellationToken);
        return role.Id;
    }
}
