namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// One permission key of the catalogue: what a role grants, and what a use case asks for.
/// <para>
/// A key is retired with <paramref name="Retired"/> and never removed. A retired key grants nothing and is
/// not added to a role again, but a role that holds it keeps it, so retiring a key breaks no role and no
/// stored right.
/// </para>
/// </summary>
/// <param name="Key">
/// The key, such as <c>projects.edit</c>: lowercase, dotted, with at least one dot, and at most
/// <see cref="MaxKeyLength"/> characters.
/// </param>
/// <param name="Module">The module that owns it, such as <c>Projects</c>.</param>
/// <param name="Description">What holding it lets a seat do, for the screens that assign roles.</param>
/// <param name="Implies">
/// Keys that come with this one, such as the key to view with the key to edit. One hop only: a key that
/// another implies implies nothing itself.
/// </param>
/// <param name="Retired">Whether the key is retired.</param>
/// <param name="Order">Where it is listed within its module; lower first.</param>
/// <param name="ManagesAccess">
/// Whether holding the key gives power over other people's access. A role holding such a key is given and
/// taken away only by a seat that holds the key there itself, for at least as long, and never by a seat to
/// itself. Any other role is given by whoever holds <see cref="TenancyKeys.GrantsManage"/> where the seat is
/// placed.
/// <para>
/// Mark a key when it should be given only by someone who holds it: power over roles, placements or the
/// tree, or a decision the organization keeps for itself, such as naming a project's owner. A key whose power
/// reaches no further than the keys of roles that manage no access may stay unmarked. Managing a project's
/// crew is such a key: a crew gives only keys that act on its project.
/// </para>
/// <para>
/// A key nobody marks is given by every grants manager, to anyone placed where they hold
/// <see cref="TenancyKeys.GrantsManage"/>, themselves included. The application marks keys it does not declare, a module's among them, with
/// <see cref="ApplicationCatalogue.AccessManagingKeys"/>.
/// </para>
/// </param>
public sealed record Permission(
    string Key,
    string Module,
    string Description,
    IReadOnlyList<string>? Implies = null,
    bool Retired = false,
    int Order = 100,
    bool ManagesAccess = false)
{
    /// <summary>The longest key: what a storage keeps a stored right's key in.</summary>
    public const int MaxKeyLength = TenancyNames.MaxPermissionKeyLength;
}
