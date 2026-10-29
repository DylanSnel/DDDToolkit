namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// Tenancy's own starter role, which the catalogue adds when the application declares no administrators' starter role.
/// <para>
/// A tenant cannot start without one: the first seat is granted the administrators' role, and it is that role
/// that holds the permissions to manage access, so somebody can give the next role. Every application needs such a
/// starter role, and most need nothing more of it than every permission, so the application need not declare it. Once it
/// declares an administrators' starter role of its own, for any shape, it declares them for every shape, and this one
/// is not added.
/// </para>
/// </summary>
public static class TenancyStarterRoles
{
    /// <summary>
    /// The permission of <see cref="DefaultAdministrators"/>: the starter role its roles remember they came from, and what
    /// <c>TenantToProvision.RoleIds</c> names it by.
    /// </summary>
    public const string DefaultAdministratorsKey = "administrator";

    /// <summary>
    /// The administrators' starter role <see cref="TenancyCatalogue.Build(ApplicationCatalogue, IEnumerable{Permission})"/>
    /// adds when the application declares none: named "Administrator", seeded for every shape on provision and
    /// listed first. It lists no permissions, so the role made from it holds every live permission of the catalogue as built, as
    /// an administrators' starter role of the application's own that lists none does. A permission a module declares later is
    /// the starter role's too: a tenant provisioned after that gets it with the role, and a role made from the starter role before
    /// gets it when the host syncs the starter roles (<c>services.SyncStarterRoles()</c>).
    /// <para>
    /// Its name and description come in English and in Dutch, the languages the package ships: a tenant
    /// provisioned in Dutch gets a role called "Beheerder". An application's <see cref="IStarterRoleTexts"/> is
    /// asked first, by this permission, so it names the role in any language, Dutch included.
    /// </para>
    /// </summary>
    public static StarterRole DefaultAdministrators { get; } = new(
        DefaultAdministratorsKey,
        "Administrator",
        "Holds every permission: runs the organization, its people and their access",
        [],
        Administers: true,
        Order: 0);
}
