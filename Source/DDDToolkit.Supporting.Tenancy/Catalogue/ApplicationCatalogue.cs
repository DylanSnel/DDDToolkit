namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// What the application adds to the catalogue, besides Tenancy's own keys and what its modules contribute: role
/// packs, keys it owns itself, marks on keys that manage access, and whether those keys stay contained. It is data
/// in code, built and checked once by <see cref="TenancyCatalogue.Build(ApplicationCatalogue, IEnumerable{Permission})"/>.
/// <para>
/// Every part is optional, and an application that needs none of them leaves the catalogue out: its modules
/// contribute their keys, every tenant starts with the role of <see cref="TenancyPacks.DefaultAdministrators"/>,
/// given to its first seat, and the keys that manage access stay contained. <c>new ApplicationCatalogue()</c> is
/// that catalogue, and Tenancy builds it when <c>TenancyOptions.Catalogue</c> is not set. Tenancy asks for nothing
/// more, because it decides nothing more with it: what kind of unit a unit is, a region or a site, is the
/// application's to keep, as a field of its own unit class.
/// </para>
/// <code>
/// public static ApplicationCatalogue Application { get; } = new(
///     Packs: [new RolePack("viewer", "Viewer", "Looks at the orders", [ShopKeys.OrdersView])],
///     AccessManagingKeys: [ShopKeys.Refund]);
/// </code>
/// </summary>
/// <param name="Packs">
/// The role packs. Either none of them is an administrators' pack, and the catalogue adds
/// <see cref="TenancyPacks.DefaultAdministrators"/>, which holds every live key in a tenant of every shape; or
/// there is one administrators' pack seeded for each shape of tenant (<see cref="RolePack.SeededFor"/>). Left out
/// or empty: every tenant starts with the default administrators' role alone.
/// </param>
/// <param name="Permissions">
/// The keys the application owns itself, besides Tenancy's and the modules' contributions. A module's keys are
/// stated by the module, next to the code that asks for them, on a list it marks with
/// <see cref="TenancyPermissionsAttribute"/>, and are not listed here.
/// </param>
/// <param name="AccessManagingKeys">
/// Keys that manage access besides those marked where they are declared (<see cref="Permission.ManagesAccess"/>):
/// the application's own or any module's. Listing a key that is already marked, Tenancy's included, changes
/// nothing, so a module that starts marking its own key breaks no application. A listed retired key counts
/// once it is live again.
/// </param>
/// <param name="ContainAccessManagingKeys">
/// Whether a seat hands on a key that manages access only where it holds that key itself: on unless the
/// application turns it off. While it is on:
/// <list type="bullet">
/// <item>a role that holds such a key is given and taken away only by a seat that holds each of its keys that
/// manage access at that unit, for at least as long as the grant runs, and never by a seat to itself;</item>
/// <item>only an administrator puts such a key into a role, takes one out, or archives a role that holds one;</item>
/// <item>a seat suspends, deactivates or reactivates another only where it could take away or give back each
/// grant of a role that manages access that the change takes away or gives back;</item>
/// <item>a move of a unit gives or takes away such a key from nobody where the mover does not hold it.</item>
/// </list>
/// On Postgres the database holds a seat, behind every query of its own, to the part a single statement shows:
/// which roles that manage access it gives, changes and takes away at a unit, never to itself, the same for an
/// invitation, and which seats holding one it stops. How long against its own hold, the administrator, and moves
/// stay with the use cases, as they do for every rule of Tenancy's that a policy cannot see.
/// <para>
/// Off, a role or a key that manages access goes as one that manages none: a seat that holds
/// <see cref="TenancyKeys.GrantsManage"/> at a unit gives every role there, to anyone placed there for as long as
/// it says, and to itself for no longer than it holds that key there; so in practice whoever holds a key that
/// manages access administers everything that key reaches. Roles that manage no access go as they always do, and
/// a move still gives the mover nothing. What keeps the access model sound stays either way: each use case asks
/// its key where it acts, a tenant keeps an administrator, and a seat keeps its identity and its tenant. Leave it
/// on when the database can be reached without the application's handlers, through Supabase's Data API say.
/// </para>
/// <para>
/// It is about a seat handing keys on, not about the application: system work in a tenant holds every key there
/// and is never held to it. A handler that checked a condition of the application's own, a quiz passed say, gives
/// the role inside <c>TenancyWork.BeginSystemIn</c>, with it on, for the calling seat and with a role the
/// application chose, never one the request named. The export writes the setting into the access files, as it
/// writes the marks, and on Postgres a start-up check refuses a database written with the other setting.
/// </para>
/// </param>
public sealed record ApplicationCatalogue(
    IReadOnlyList<RolePack>? Packs = null,
    IReadOnlyList<Permission>? Permissions = null,
    IReadOnlyList<string>? AccessManagingKeys = null,
    bool ContainAccessManagingKeys = true)
{
    /// <summary>The role packs, none when none are given: what is read is never <see langword="null"/>.</summary>
    public IReadOnlyList<RolePack> Packs { get; init; } = Packs ?? [];
}
