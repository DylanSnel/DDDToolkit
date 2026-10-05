using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Projects.Contracts.RowAccess;

namespace Examples.Tenancy.Projects.Infrastructure.Access;

// A column rule, as NameAndPlanChangeWithTheEditKey is: closing a project and reopening it ask the close key in C#,
// and the policy for changing a project would let a seat that only edits it close it with a statement of its own.

/// <summary>
/// Whether a project is open or closed changes only for a seat that holds the key to close it
/// (<see cref="ProjectKeys.Close"/>), on the project or at a unit above it: what closing and reopening ask. Asked of
/// the project as it was and as it is about to be, beside the policy that lets the seat change the row at all.
/// </summary>
[RowAccess<Project>(RowOperations.Change, To = [RowAccessRoles.User], Columns = [nameof(Project.State)])]
public static partial class StateChangesWithTheCloseKey
{
    /// <summary>Whether the calling seat holds the key to close <paramref name="project"/>.</summary>
    public static bool Allows(Project project, Caller caller) => ProjectsWhereIHold.Ids(ProjectKeys.Close).Contains(project.Id);
}
