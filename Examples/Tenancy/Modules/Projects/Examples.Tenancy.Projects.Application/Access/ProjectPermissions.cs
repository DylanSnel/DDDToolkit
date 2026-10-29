using DDDToolkit.Supporting.Tenancy.Access;

namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// Projects' permissions, each declared once, with where it is asked and what holds it: the one place the module
/// says them. Tenancy's generator puts every one into the catalogue under the module's name, with the description
/// its level says, so a role of the organization can hold it; the Membership package's generator hands those asked
/// on a project to the projects' rules (<see cref="ProjectMembership"/>), so the check in front of a handler and the
/// functions in the database answer from the same lines.
/// </summary>
/// <remarks>
/// The permissions themselves are published in the contracts (<see cref="ProjectPermissionNames"/>), since another module names one
/// when it asks Projects a question, and the host names them in its starter roles; what each one is, is said here.
/// <list type="bullet">
/// <item>Held by a project's owner by owning it: seeing it, editing it, closing it and managing its crew, the
/// permissions of a crew lead. Opening a project and naming its owner are not: the first is asked at a unit, where there
/// is no project yet, and the second is the organization's.</item>
/// <item>Given by a crew role: every permission on a project but naming its owner. A crew role takes the permissions of
/// other modules that act on a project as well, a surveyor's permission to record an inspection say, as the projects' rules
/// say (<see cref="ProjectMembership"/>).</item>
/// <item>Reached from above: a role of the organization that holds one at the unit a project hangs at, or above
/// it, holds it on the project; naming an owner is held that way and no other.</item>
/// </list>
/// The descriptions are the English texts, which the screens that put permissions into roles show;
/// <c>ProjectPermissionTexts.nl.resx</c> beside this class has the Dutch, by the permission's name, as the refusal
/// texts do.
/// </remarks>
[Permissions]
public static class ProjectPermissions
{
    [On<Project>(EveryMember = true, Description = "See a project and its crew.")]
    public const string View = ProjectPermissionNames.View;

    [AtUnit(Description = "Open a project at a unit.")]
    public const string Open = ProjectPermissionNames.Open;

    [On<Project>(Implies = [ProjectPermissionNames.View], Description = "Rename or move a project.")]
    public const string Edit = ProjectPermissionNames.Edit;

    [On<Project>(Description = "Close or reopen a project.")]
    public const string Close = ProjectPermissionNames.Close;

    [On<Project>(ManagesMembers = true, Implies = [ProjectPermissionNames.View], Description = "Add and remove crew members, give and take their project roles.")]
    public const string ManageCrew = ProjectPermissionNames.ManageCrew;

    [On<Project>(Owner = false, InRoles = false, NamesOwner = true, Description = "Name a project's owner.")]
    public const string ChangeOwner = ProjectPermissionNames.ChangeOwner;
}
