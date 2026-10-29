using DDDToolkit.Supporting.Tenancy.Access;

namespace Examples.Tenancy.Inspections.Application.Access;

/// <summary>
/// Inspections' permission, declared once, next to the code that asks for it: Tenancy's generator puts it into the
/// catalogue under the module's name, with the description its level says, so a role of the organization can hold it.
/// </summary>
/// <remarks>
/// Like Projects' permissions, it reaches a project in two ways: held at a unit, on every project there and below it;
/// held on a project's crew, on that project alone, since the projects' crew roles take permissions of other modules.
/// Projects answers both, although the permission is not Projects' own: its gate answers for any permission the catalogue knows.
/// Recording on a project means seeing it, so the permission brings <see cref="ProjectPermissionNames.View"/> with it. The permission itself
/// is published in the contracts (<see cref="InspectionPermissionNames"/>), where the host names it in its starter roles.
/// </remarks>
[Permissions]
public static class InspectionPermissions
{
    [AtUnit(Implies = [ProjectPermissionNames.View], Description = "Record an inspection on a project.")]
    public const string Record = InspectionPermissionNames.Record;
}
