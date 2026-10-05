namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// Tenancy's own role pack, which the catalogue adds when the application declares no administrators' pack.
/// <para>
/// A tenant cannot start without one: the first seat is granted the administrators' role, and it is that role
/// that holds the keys to manage access, so somebody can give the next role. Every application needs such a
/// pack, and most need nothing more of it than every key, so the application need not declare it. Once it
/// declares an administrators' pack of its own, for any shape, it declares them for every shape, and this one
/// is not added.
/// </para>
/// </summary>
public static class TenancyPacks
{
    /// <summary>
    /// The key of <see cref="DefaultAdministrators"/>: the pack its roles remember they came from, and what
    /// <c>TenantToProvision.RoleIds</c> and <c>ProvisionedTenant.RolesByPack</c> name it by.
    /// </summary>
    public const string DefaultAdministratorsKey = "administrator";

    /// <summary>
    /// The administrators' pack <see cref="TenancyCatalogue.Build(ApplicationCatalogue, IEnumerable{Permission})"/>
    /// adds when the application declares none: named "Administrator", for every shape, seeded on provision and
    /// listed first. It lists no keys, so the role made from it holds every live key of the catalogue as built, a
    /// key a module declares later included, as an administrators' pack of the application's own that lists none
    /// does.
    /// <para>
    /// Its name and description come in English and in Dutch, the languages the package ships: a tenant
    /// provisioned in Dutch gets a role called "Beheerder". An application's <see cref="IRolePackTexts"/> is
    /// asked first, by this key, so it names the role in any language, Dutch included.
    /// </para>
    /// </summary>
    public static RolePack DefaultAdministrators { get; } = new(
        DefaultAdministratorsKey,
        "Administrator",
        "Holds every key: runs the organization, its people and their access",
        [],
        Administers: true,
        Order: 0);
}
