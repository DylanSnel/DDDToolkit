using DDDToolkit.Supporting.Tenancy.Access;

namespace Examples.Tenancy.Inspections.Application.Access;

/// <summary>
/// Inspections' permission, declared once, next to the code that asks for it: Tenancy's generator puts it into the
/// catalogue under the module's name, with the description of its summary, so a role of the organization can hold it.
/// </summary>
/// <remarks>
/// Like Projects' keys, it reaches a project in two ways: held at a unit, on every project there and below it;
/// held on a project's crew, on that project alone, since the projects' crew roles take keys of other modules.
/// Projects answers both, although the key is not Projects' own: its gate answers for any key the catalogue knows.
/// Recording on a project means seeing it, so the key brings <see cref="ProjectKeys.View"/> with it. The key itself
/// is published in the contracts (<see cref="InspectionKeys"/>), where the host names it in its role packs.
/// </remarks>
[Permissions]
public static class InspectionPermissions
{
    /// <summary>Record an inspection on a project.</summary>
    [AtUnit(Implies = [ProjectKeys.View])]
    public const string Record = InspectionKeys.Record;
}
