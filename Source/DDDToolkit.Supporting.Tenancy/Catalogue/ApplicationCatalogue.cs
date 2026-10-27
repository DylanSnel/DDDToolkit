namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// What the application adds to the catalogue, besides Tenancy's own permissions and what its modules contribute: role
/// starter roles, permissions it owns itself, marks on permissions that manage access, and whether those permissions stay contained. It is data
/// in code, built and checked once by <see cref="TenancyCatalogue.Build(ApplicationCatalogue, IEnumerable{Permission})"/>.
/// <para>
/// Every part is optional, and an application that needs none of them leaves the catalogue out: its modules
/// contribute their permissions, every tenant starts with the role of <see cref="TenancyStarterRoles.DefaultAdministrators"/>,
/// given to its first seat, and the permissions that manage access stay contained. <c>new ApplicationCatalogue()</c> is
/// that catalogue, and Tenancy builds it when <c>TenancyOptions.Catalogue</c> is not set. Tenancy asks for nothing
/// more, because it decides nothing more with it: what kind of unit a unit is, a region or a site, is the
/// application's to keep, as a field of its own unit class.
/// </para>
/// <code>
/// public static ApplicationCatalogue Application { get; } = new(
///     StarterRoles: [new StarterRole("viewer", "Viewer", "Looks at the orders", [ShopPermissions.OrdersView])],
///     AccessManagingPermissions: [ShopPermissions.Refund]);
/// </code>
/// </summary>
/// <param name="StarterRoles">
/// The starter roles. Either none of them is an administrators' starter role, and the catalogue adds
/// <see cref="TenancyStarterRoles.DefaultAdministrators"/>, which holds every live permission in a tenant of every shape; or
/// there is one administrators' starter role seeded for each shape of tenant (<see cref="StarterRole.SeededFor"/>). Left out
/// or empty: every tenant starts with the default administrators' role alone.
/// </param>
/// <param name="Permissions">
/// The permissions the application owns itself, besides Tenancy's and the modules' contributions. A module's permissions are
/// stated by the module, next to the code that asks for them, on a list it marks with
/// <see cref="TenancyPermissionsAttribute"/>, and are not listed here.
/// </param>
/// <param name="AccessManagingPermissions">
/// Permissions that manage access besides those marked where they are declared (<see cref="Permission.ManagesAccess"/>):
/// the application's own or any module's. Listing a permission that is already marked, Tenancy's included, changes
/// nothing, so a module that starts marking its own permission breaks no application. A listed retired permission counts
/// once it is live again.
/// </param>
/// <param name="ContainAccessManagingPermissions">
/// Whether a seat hands on a permission that manages access only where it holds that permission itself: on unless the
/// application turns it off. While it is on:
/// <list type="bullet">
/// <item>a role that holds such a permission is given and taken away only by a seat that holds each of its permissions that
/// manage access at that unit, for at least as long as the grant runs, and never by a seat to itself;</item>
/// <item>only an administrator puts such a permission into a role, takes one out, or archives a role that holds one;</item>
/// <item>a seat suspends, deactivates or reactivates another only where it could take away or give back each
/// grant of a role that manages access that the change takes away or gives back;</item>
/// <item>a move of a unit gives or takes away such a permission from nobody where the mover does not hold it.</item>
/// </list>
/// On Postgres the database holds a seat, behind every query of its own, to the part a single statement shows:
/// which roles that manage access it gives, changes and takes away at a unit, never to itself, the same for an
/// invitation, and which seats holding one it stops. How long against its own hold, the administrator, and moves
/// stay with the use cases, as they do for every rule of Tenancy's that a policy cannot see.
/// <para>
/// Off, a role or a permission that manages access goes as one that manages none: a seat that holds
/// <see cref="TenancyPermissions.GrantsManage"/> at a unit gives every role there, to anyone placed there for as long as
/// it says, and to itself for no longer than it holds that permission there; so in practice whoever holds a permission that
/// manages access administers everything that permission reaches. Roles that manage no access go as they always do, and
/// a move still gives the mover nothing. What keeps the access model sound stays either way: each use case asks
/// its permission where it acts, a tenant keeps an administrator, and a seat keeps its identity and its tenant. Leave it
/// on when the database can be reached without the application's handlers, through Supabase's Data API say.
/// </para>
/// <para>
/// It is about a seat handing permissions on, not about the application: system work in a tenant holds every permission there
/// and is never held to it. A handler that checked a condition of the application's own, a quiz passed say, gives
/// the role inside <c>TenancyWork.BeginSystemIn</c>, with it on, for the calling seat and with a role the
/// application chose, never one the request named. The export writes the setting into the access files, as it
/// writes the marks, and on Postgres a start-up check refuses a database written with the other setting.
/// </para>
/// </param>
public sealed record ApplicationCatalogue(
    IReadOnlyList<StarterRole>? StarterRoles = null,
    IReadOnlyList<Permission>? Permissions = null,
    IReadOnlyList<string>? AccessManagingPermissions = null,
    bool ContainAccessManagingPermissions = true)
{
    /// <summary>The starter roles, none when none are given: what is read is never <see langword="null"/>.</summary>
    public IReadOnlyList<StarterRole> StarterRoles { get; init; } = StarterRoles ?? [];
}
