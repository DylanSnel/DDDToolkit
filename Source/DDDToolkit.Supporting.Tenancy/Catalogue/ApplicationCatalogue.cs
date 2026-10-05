namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// What the application adds to the catalogue, besides Tenancy's own keys and what its modules contribute: role
/// packs, keys it owns itself, and marks on keys that manage access. It is data in code, built and checked once
/// by <see cref="TenancyCatalogue.Build(ApplicationCatalogue, IEnumerable{Permission})"/>.
/// <para>
/// Every part is optional, and an application that needs none of them leaves the catalogue out: its modules
/// contribute their keys, and every tenant starts with the role of <see cref="TenancyPacks.DefaultAdministrators"/>,
/// given to its first seat. <c>new ApplicationCatalogue()</c> is that catalogue, and Tenancy builds it when
/// <c>TenancyOptions.Catalogue</c> is not set. Tenancy asks for nothing more, because it decides nothing more
/// with it: what kind of unit a unit is, a region or a site, is the application's to keep, as a field of its own
/// unit class.
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
/// there is one administrators' pack for each shape of tenant. Left out or empty: every tenant starts with the
/// default administrators' role alone.
/// </param>
/// <param name="Permissions">
/// The keys the application owns itself, besides Tenancy's and the modules' contributions. A module's keys are
/// contributed by the module, next to the code that asks for them, and are not listed here.
/// </param>
/// <param name="AccessManagingKeys">
/// Keys that manage access besides those marked where they are declared (<see cref="Permission.ManagesAccess"/>):
/// the application's own or any module's. Listing a key that is already marked, Tenancy's included, changes
/// nothing, so a module that starts marking its own key breaks no application. A listed retired key counts
/// once it is live again.
/// </param>
public sealed record ApplicationCatalogue(
    IReadOnlyList<RolePack>? Packs = null,
    IReadOnlyList<Permission>? Permissions = null,
    IReadOnlyList<string>? AccessManagingKeys = null)
{
    /// <summary>The role packs, none when none are given: what is read is never <see langword="null"/>.</summary>
    public IReadOnlyList<RolePack> Packs { get; init; } = Packs ?? [];
}
