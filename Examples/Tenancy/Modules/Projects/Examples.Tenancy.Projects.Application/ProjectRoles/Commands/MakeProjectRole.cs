using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Tenancy;
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

/// <summary>
/// Handles <see cref="MakeProjectRole"/>: refuses a permission the catalogue does not know, as Tenancy refuses it, then has
/// the project role manager add the role, made in the caller's tenant with the module's constructor, and save it.
/// </summary>
/// <param name="roles">The project role manager, which hands the role the projects' rules.</param>
/// <param name="catalogue">The permissions the application knows.</param>
/// <param name="tenancy">Tenancy, asked about the current caller: the tenant the role is made in.</param>
public sealed class MakeProjectRoleHandler(ProjectRoleManager roles, TenancyCatalogue catalogue, TenancyAccessManager tenancy)
    : ICommandHandler<MakeProjectRole, ProjectRoleId>
{
    /// <inheritdoc />
    /// <returns>The new role's id.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.unknown-permission</c>; <c>tenancy.not-permitted</c>, naming the permission, for a caller that does not
    /// hold it for the whole tenant; <c>projects.role-name-invalid</c>, <c>projects.permission-not-for-members</c>,
    /// <c>projects.role-name-taken</c>.
    /// </exception>
    public async ValueTask<ProjectRoleId> Handle(MakeProjectRole command, CancellationToken cancellationToken)
    {
        var permissions = MakeProjectRole.Known(command.Permissions, catalogue);
        var role = await roles.CreateAsync(
            new ProjectRole(ProjectRoleId.Create(), tenancy.CurrentTenant.Require().Id, new CustomRoleDraft(command.Name, command.Description, permissions), roles.Rules),
            cancellationToken);
        return role.Id;
    }
}
