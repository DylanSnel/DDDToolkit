using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Domain;

namespace Examples.Tenancy.Tenants.Application.Roles;

/// <summary>
/// A role as this application lists it: what the Tenancy package says of it, with its permissions only for whoever
/// decides them.
/// </summary>
/// <remarks>
/// Anybody who works in the tenant reads what a role is called. What it lets its holder do, its permissions, is for
/// those who decide that: a caller who holds <see cref="ManageRolesPermission"/> for the whole tenant.
/// The queries that list roles hold that themselves, so every way a role is answered keeps to it: a route, a
/// GraphQL field, the answer of a mutation. The record is the application's own for that reason: it has no way
/// to carry the permissions of a role to a caller they are not for. The roles a seat holds itself are another answer,
/// its own overview, and those it may always read.
/// </remarks>
/// <param name="Id">The role.</param>
/// <param name="Name">Its name.</param>
/// <param name="FromPack">The pack it was copied from, or <see langword="null"/> for a role the tenant made.</param>
/// <param name="Status">Whether it can still be given.</param>
/// <param name="Permissions">
/// The permissions it brings, or <see langword="null"/> for a caller who does not hold <see cref="ManageRolesPermission"/>:
/// not an empty list, which would say the role brings none.
/// </param>
/// <param name="ManagesAccess">
/// Whether one of its permissions manages access, and so, while containment is on, as it is in the sample, it is given only
/// by a seat that holds those permissions.
/// </param>
public sealed record RoleListing(
    RoleId Id,
    string Name,
    string? FromPack,
    RoleStatus Status,
    IReadOnlyList<string>? Permissions,
    bool ManagesAccess)
{
    /// <summary>The permission a caller holds for the whole tenant to be answered <see cref="Keys"/>: the one that manages the tenant's roles.</summary>
    public const string ManageRolesPermission = TenancyPermissions.RolesManage;

    /// <summary>
    /// <paramref name="roles"/>, in the order the role manager answered them, each with its permissions only when
    /// <paramref name="withKeys"/>.
    /// </summary>
    /// <param name="roles">The roles, as the package's role manager answered them, each with whether it manages access.</param>
    /// <param name="withKeys">Whether the caller holds <see cref="ManageRolesPermission"/> for the whole tenant.</param>
    internal static IReadOnlyList<RoleListing> From(IReadOnlyList<(Role Role, bool ManagesAccess)> roles, bool withKeys)
        => [.. roles.Select(each => new RoleListing(
            each.Role.Id,
            each.Role.Name,
            each.Role.FromPack,
            each.Role.Status,
            withKeys ? each.Role.Permissions : null,
            each.ManagesAccess))];

    /// <summary>The roles, each with whether it manages access, as the role manager <paramref name="manager"/> says.</summary>
    /// <param name="roles">The roles the role manager answered.</param>
    /// <param name="manager">The role manager that answered them.</param>
    internal static IReadOnlyList<(Role Role, bool ManagesAccess)> Judged(IReadOnlyList<Role> roles, RoleManager manager)
        => [.. roles.Select(role => (role, manager.ManagesAccess(role)))];
}
