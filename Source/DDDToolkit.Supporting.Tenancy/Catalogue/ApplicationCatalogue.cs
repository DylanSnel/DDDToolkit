namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// What the application brings to the catalogue: its role packs, its unit kinds, and the permission keys it
/// owns itself. Modules that own keys add theirs by contribution. It is data in code, built and checked once
/// by <see cref="TenancyCatalogue.Build"/>.
/// </summary>
/// <param name="Packs">
/// The role packs. Either none of them is an administrators' pack, and the catalogue adds
/// <see cref="TenancyPacks.DefaultAdministrators"/>, which holds every live key in a tenant of every shape; or
/// there is one administrators' pack for each shape of tenant. Empty is allowed: every tenant then starts with
/// the default administrators' role alone.
/// </param>
/// <param name="UnitKinds">The kinds of unit, at least one.</param>
/// <param name="Permissions">The application's own keys, besides Tenancy's and the modules' contributions.</param>
/// <param name="AccessManagingKeys">
/// Keys that manage access besides those marked where they are declared (<see cref="Permission.ManagesAccess"/>):
/// the application's own or any module's. Listing a key that is already marked, Tenancy's included, changes
/// nothing, so a module that starts marking its own key breaks no application. A listed retired key counts
/// once it is live again.
/// </param>
public sealed record ApplicationCatalogue(
    IReadOnlyList<RolePack> Packs,
    IReadOnlyList<UnitKind> UnitKinds,
    IReadOnlyList<Permission>? Permissions = null,
    IReadOnlyList<string>? AccessManagingKeys = null)
{
    /// <summary>
    /// The part of an application that declares no role packs: every tenant starts with the role of
    /// <see cref="TenancyPacks.DefaultAdministrators"/> alone, given to its first seat. The kinds of unit and
    /// the keys are all it names:
    /// <code>
    /// public static ApplicationCatalogue Application { get; } = new(
    ///     UnitKinds: [new UnitKind("site", "Site")],
    ///     Permissions: ShopKeys.All);
    /// </code>
    /// <para>
    /// It has no <c>Packs</c> parameter, so a call that leaves the packs out finds it, and one that names them,
    /// <c>Packs: []</c> included, finds the other, as before. A call that passes two empty lists without names,
    /// <c>new([], [])</c>, fits both and does not compile: it names no kind of unit either, which
    /// <see cref="TenancyCatalogue.Build"/> refuses.
    /// </para>
    /// </summary>
    /// <param name="UnitKinds">The kinds of unit, at least one.</param>
    /// <param name="Permissions">The application's own keys, besides Tenancy's and the modules' contributions.</param>
    /// <param name="AccessManagingKeys">
    /// Keys that manage access besides those marked where they are declared (<see cref="Permission.ManagesAccess"/>),
    /// as for the other constructor.
    /// </param>
    public ApplicationCatalogue(
        IReadOnlyList<UnitKind> UnitKinds,
        IReadOnlyList<Permission>? Permissions = null,
        IReadOnlyList<string>? AccessManagingKeys = null)
        : this([], UnitKinds, Permissions, AccessManagingKeys)
    {
    }
}
