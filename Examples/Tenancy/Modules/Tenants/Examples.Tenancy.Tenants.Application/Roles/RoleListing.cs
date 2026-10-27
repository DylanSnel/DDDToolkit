using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Domain;

namespace Examples.Tenancy.Tenants.Application.Roles;

/// <summary>
/// A role of the tenant as the role manager read it, with two facts the role does not hold: whether it manages access,
/// and whether the caller reads its permissions.
/// </summary>
/// <remarks>
/// Anybody who works in the tenant reads what a role is called. What it lets its holder do, its permissions, is for
/// those who decide that: a caller who holds <see cref="ManageRolesPermission"/> for the whole tenant. The role holds
/// its permissions all the same, so whatever shows a listing writes them only when <see cref="PermissionsShown"/> is
/// true. A route writes them as <see langword="null"/> otherwise. A GraphQL field asks the same permission itself.
/// The permissions a seat holds itself are in its own overview, with where each reaches.
/// </remarks>
/// <param name="Role">The role, read for the question and tracked by nobody.</param>
/// <param name="ManagesAccess">
/// Whether one of its permissions manages access, as the role manager says. While containment is on, as it is in the
/// sample, such a role is given only by a seat that holds those permissions.
/// </param>
/// <param name="PermissionsShown">
/// Whether the caller holds <see cref="ManageRolesPermission"/> for the whole tenant, and so reads the role's permissions.
/// </param>
public sealed record RoleListing(Role Role, bool ManagesAccess, bool PermissionsShown)
{
    /// <summary>The permission a caller holds for the whole tenant to read a role's permissions: the one that manages the tenant's roles.</summary>
    public const string ManageRolesPermission = TenancyPermissions.RolesManage;

    /// <summary>
    /// <paramref name="roles"/>, in the order the role manager answered them, each with
    /// <paramref name="permissionsShown"/>.
    /// </summary>
    /// <param name="roles">The roles, each with whether it manages access, as the role manager said.</param>
    /// <param name="permissionsShown">Whether the caller holds <see cref="ManageRolesPermission"/> for the whole tenant.</param>
    internal static IReadOnlyList<RoleListing> Listed(IReadOnlyList<(Role Role, bool ManagesAccess)> roles, bool permissionsShown)
        => [.. roles.Select(each => new RoleListing(each.Role, each.ManagesAccess, permissionsShown))];

    /// <summary>The roles, each with whether it manages access, as the role manager <paramref name="manager"/> says.</summary>
    /// <param name="roles">The roles the role manager answered.</param>
    /// <param name="manager">The role manager that answered them. It reads nothing for this.</param>
    internal static IReadOnlyList<(Role Role, bool ManagesAccess)> Judged(IReadOnlyList<Role> roles, RoleManager manager)
        => [.. roles.Select(role => (role, manager.ManagesAccess(role)))];
}
