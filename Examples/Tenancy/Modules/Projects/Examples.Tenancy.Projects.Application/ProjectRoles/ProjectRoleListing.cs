using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Examples.Tenancy.Projects.Application.ProjectRoles;

/// <summary>
/// A project role as this module lists it: what it is called and what it is for, which starter role it was made
/// from, whether it can still be given, and, for whoever decides that, the permissions it gives on a crew.
/// </summary>
/// <remarks>
/// Anybody who works in the tenant reads what a project role is called: a crew names its members' roles by id, and
/// a screen that shows a crew shows their names. What a role lets its holders do, its permissions, is for those who decide
/// that: a caller who holds <see cref="ManageRolesPermission"/> for the whole tenant, as for the tenant's roles of the
/// organization. The queries that list project roles hold that themselves, so a route, a field and whatever asks
/// next keep to it.
/// </remarks>
/// <param name="Id">The role.</param>
/// <param name="Name">Its name.</param>
/// <param name="Description">What it is for; empty when nobody said.</param>
/// <param name="MadeFrom">The starter role it was made from, such as <c>crew-lead</c>, or <see langword="null"/> for one the tenant made.</param>
/// <param name="Status">Whether it can still be given.</param>
/// <param name="Permissions">
/// The permissions it gives on a crew, in the order of their text, or <see langword="null"/> for a caller who does not
/// hold <see cref="ManageRolesPermission"/>: not an empty list, which would say the role gives none.
/// </param>
public sealed record ProjectRoleListing(
    ProjectRoleId Id,
    string Name,
    string Description,
    string? MadeFrom,
    CustomRoleStatus Status,
    IReadOnlyList<string>? Permissions)
{
    /// <summary>
    /// The permission a caller holds for the whole tenant to be answered <see cref="Permissions"/>, and to make, rename, change the permissions of
    /// and archive project roles: the one that manages the tenant's roles.
    /// </summary>
    public const string ManageRolesPermission = TenancyPermissions.RolesManage;
}
