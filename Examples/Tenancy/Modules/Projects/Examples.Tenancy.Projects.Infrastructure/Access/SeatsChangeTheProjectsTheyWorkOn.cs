using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.Access;
using Examples.Tenancy.Projects.Contracts.RowAccess;

namespace Examples.Tenancy.Projects.Infrastructure.Access;

// The database's rule for changing a project is coarser than the application's, on purpose. In C# each command
// asks its own key: renaming and moving ask one, closing another, the crew a third, the owner a fourth. A row
// knows no command, so the policy asks whether the seat holds any of them, and the application's own check, which
// runs first, is the one that tells them apart. What the policy adds is the second lock: a seat that only sees a
// project, or one of another tenant, changes nothing of it, whatever statement reaches the database. The crew and
// the owner are held closer than that by the Membership package's own lock, written from the projects' rules.

/// <summary>
/// A seat changes a project, and writes its crew, when it holds a key that changes one on it
/// (<see cref="ProjectsWhereIHold"/>): through the organization, any of the four, and through the crew, or by
/// owning it, the three a crew gives. A project that is moved has to pass at the unit it arrives at as well, where
/// the key to open one is what the application asks, so that key counts at a unit too.
/// </summary>
[RowAccess<Project>(RowOperations.Change, To = [RowAccessRoles.User])]
public static partial class SeatsChangeTheProjectsTheyWorkOn
{
    /// <summary>Whether the calling seat may change <paramref name="project"/>.</summary>
    public static bool Allows(Project project, Caller caller)
        => ProjectsWhereIHold.Ids(ProjectKeys.Edit).Contains(project.Id)
            || ProjectsWhereIHold.Ids(ProjectKeys.Close).Contains(project.Id)
            || ProjectsWhereIHold.Ids(ProjectKeys.ManageCrew).Contains(project.Id)
            || ProjectsWhereIHold.Ids(ProjectKeys.ChangeOwner).Contains(project.Id)
            || TenancyRowAccess.UnitsWhereIHold<OrganizationUnitId>(ProjectKeys.Open).Contains(project.UnitId);
}
