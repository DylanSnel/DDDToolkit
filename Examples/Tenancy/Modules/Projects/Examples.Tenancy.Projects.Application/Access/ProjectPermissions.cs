using DDDToolkit.Supporting.Tenancy.Access;

namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// Projects' permissions, each declared once, with where it is asked and what holds it: the one place the module
/// says them. Tenancy's generator puts every one into the catalogue under the module's name, with the description
/// of its summary, so a role of the organization can hold it; the Membership package's generator hands those asked
/// on a project to the projects' rules (<see cref="ProjectMembership"/>), so the check in front of a handler and the
/// functions in the database answer from the same lines.
/// </summary>
/// <remarks>
/// The keys themselves are published in the contracts (<see cref="ProjectKeys"/>), since another module names one
/// when it asks Projects a question, and the host names them in its role packs; what each one is, is said here.
/// <list type="bullet">
/// <item>Held by a project's owner by owning it: seeing it, editing it, closing it and managing its crew, the
/// keys of a crew lead. Opening a project and naming its owner are not: the first is asked at a unit, where there
/// is no project yet, and the second is the organization's.</item>
/// <item>Given by a crew role: every permission on a project but naming its owner. A crew role takes the keys of
/// other modules that act on a project as well, a surveyor's key to record an inspection say, as the projects' rules
/// say (<see cref="ProjectMembership"/>).</item>
/// <item>Reached from above: a role of the organization that holds one at the unit a project hangs at, or above
/// it, holds it on the project; naming an owner is held that way and no other.</item>
/// </list>
/// The descriptions are the English texts; <c>ProjectPermissions.nl.resx</c> beside this class has the Dutch, by the
/// permission's key, as the refusal texts do.
/// </remarks>
[Permissions]
public static class ProjectPermissions
{
    /// <summary>See a project and its crew.</summary>
    [On<Project>(EveryMember = true)]
    public const string View = ProjectKeys.View;

    /// <summary>Open a project at a unit.</summary>
    [AtUnit]
    public const string Open = ProjectKeys.Open;

    /// <summary>Rename or move a project.</summary>
    [On<Project>(Implies = [ProjectKeys.View])]
    public const string Edit = ProjectKeys.Edit;

    /// <summary>Close or reopen a project.</summary>
    [On<Project>]
    public const string Close = ProjectKeys.Close;

    /// <summary>Add and remove crew members, give and take their project roles.</summary>
    [On<Project>(LocksMembers = true, Implies = [ProjectKeys.View])]
    public const string ManageCrew = ProjectKeys.ManageCrew;

    /// <summary>Name a project's owner.</summary>
    [On<Project>(Owner = false, InRoles = false, LocksOwner = true)]
    public const string ChangeOwner = ProjectKeys.ChangeOwner;
}
