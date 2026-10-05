using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Examples.Tenancy.Inspections.Application.Access;

/// <summary>
/// Inspections' part of the permission catalogue: its key, declared next to the code that asks for it, once, on a
/// list marked with <see cref="TenancyPermissionsAttribute"/>, which what composes the modules finds by itself.
/// </summary>
/// <remarks>
/// Like Projects' keys, it reaches a project in two ways: held at a unit, on every project there and below it;
/// held on a project's crew, on that project alone. Projects answers both, although the key is not Projects' own:
/// its gate answers for any key the catalogue knows. Recording on a project means seeing it, so the key brings
/// <see cref="ProjectKeys.View"/> with it.
/// </remarks>
public static class InspectionCatalogue
{
    /// <summary>The module the key is listed under.</summary>
    public const string Module = "Inspections";

    /// <summary>
    /// The keys, as the catalogue lists them: the one place the module states them. The host registers them, and
    /// the export builds the catalogue with them, through the list Tenancy's generator writes into each,
    /// <c>TenancyPermissionsOfModules.All</c>.
    /// </summary>
    [TenancyPermissions]
    public static IReadOnlyList<Permission> Permissions { get; } =
    [
        new(InspectionKeys.Record, Module, "Record an inspection on a project", Implies: [ProjectKeys.View], Order: 10),
    ];
}
