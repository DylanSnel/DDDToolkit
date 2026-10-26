namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// One permission of the catalogue: what a role grants, and what a use case asks for.
/// <para>
/// A permission is retired with <paramref name="Retired"/> and never removed. A retired permission grants nothing and is
/// not added to a role again, but a role that holds it keeps it, so retiring a permission breaks no role and no
/// stored right.
/// </para>
/// </summary>
/// <param name="Name">
/// The permission, such as <c>projects.edit</c>: lowercase, dotted, with at least one dot, and at most
/// <see cref="MaxNameLength"/> characters.
/// </param>
/// <param name="Module">The module that owns it, such as <c>Projects</c>.</param>
/// <param name="Description">What holding it lets a seat do, for the screens that assign roles.</param>
/// <param name="Implies">
/// Permissions that come with this one, such as the permission to view with the permission to edit. One hop only: a permission that
/// another implies implies nothing itself.
/// </param>
/// <param name="Retired">Whether the permission is retired.</param>
/// <param name="Order">Where it is listed within its module; lower first.</param>
/// <param name="ManagesAccess">
/// Whether holding the permission gives power over other people's access. A role holding such a permission is given and
/// taken away only by a seat that holds the permission there itself, for at least as long, and never by a seat to
/// itself, as long as the application keeps such permissions contained, which it does unless it turns
/// <see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/> off. Any other role is given by whoever holds
/// <see cref="TenancyPermissions.GrantsManage"/> where the seat is placed.
/// <para>
/// Mark a permission when it should be given only by someone who holds it: power over roles, placements or the
/// tree, or a decision the organization keeps for itself, such as naming a project's owner. A permission whose power
/// reaches no further than the permissions of roles that manage no access may stay unmarked. Managing a project's
/// crew is such a permission: a crew gives only permissions that act on its project.
/// </para>
/// <para>
/// A permission nobody marks is given by every grants manager, to anyone placed where they hold
/// <see cref="TenancyPermissions.GrantsManage"/>, themselves included. The application marks permissions it does not declare, a module's among them, with
/// <see cref="ApplicationCatalogue.AccessManagingPermissions"/>.
/// </para>
/// </param>
public sealed record Permission(
    string Name,
    string Module,
    string Description,
    IReadOnlyList<string>? Implies = null,
    bool Retired = false,
    int Order = 100,
    bool ManagesAccess = false)
{
    /// <summary>The longest permission: what a storage keeps a stored right's permission in.</summary>
    public const int MaxNameLength = TenancyNames.MaxPermissionLength;
}
