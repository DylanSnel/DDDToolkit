using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Examples.Tenancy.Inspections.Application.Access;

/// <summary>
/// Inspections' part of the permission catalogue: its key, declared next to the code that asks for it, and added
/// to the application's catalogue by the module's registration.
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

    /// <summary>The keys, as the catalogue lists them.</summary>
    public static IReadOnlyList<Permission> Permissions { get; } =
    [
        new(InspectionKeys.Record, Module, "Record an inspection on a project", Implies: [ProjectKeys.View], Order: 10),
    ];
}
