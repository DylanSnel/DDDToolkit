using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Projects.Contracts.RowAccess;

namespace Examples.Tenancy.Projects.Infrastructure.Access;

// A column rule: one condition more, for a change of these columns alone. The policy for changing a project lets a
// seat write the row when it holds any key that changes one (SeatsChangeTheProjectsTheyWorkOn), because a row knows
// no command. Renaming a project and planning it ask the edit key in C#, and without this rule a seat that only
// manages the crew, only names owners or only opens projects at the unit would rename a project with a statement
// of its own, past the check in front of the handler. Nothing here is run by the application: the export writes a
// trigger on the columns the name and the planned days are stored in, three, as the planned days are two.

/// <summary>
/// A project's name and its planned days change only for a seat that holds the key to edit it
/// (<see cref="ProjectKeys.Edit"/>), on the project or at a unit above it, however it holds it: what renaming and
/// planning ask. Asked of the project as it was and as it is about to be, beside the policy that lets the seat change
/// the row at all.
/// </summary>
/// <remarks>
/// For a signed-in user, the one role a seat's statement runs as: the function the rule asks is granted to that role
/// alone. Any other role a caller's statement runs as, an anonymous caller or an operator's token, does not change
/// these columns, and the application's own work in the tenant is not held.
/// </remarks>
[RowAccess<Project>(RowOperations.Change, To = [RowAccessRoles.User], Columns = [nameof(Project.Name), nameof(Project.Planned)])]
public static partial class NameAndPlanChangeWithTheEditKey
{
    /// <summary>Whether the calling seat holds the key to edit <paramref name="project"/>.</summary>
    public static bool Allows(Project project, Caller caller) => ProjectsWhereIHold.Ids(ProjectKeys.Edit).Contains(project.Id);
}
