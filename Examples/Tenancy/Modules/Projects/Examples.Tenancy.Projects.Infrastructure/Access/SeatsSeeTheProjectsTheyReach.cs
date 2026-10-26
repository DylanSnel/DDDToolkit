using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Projects.Contracts.RowAccess;

namespace Examples.Tenancy.Projects.Infrastructure.Access;

// What the database itself lets a seat read of the projects: the same two ways in that the access checks ask in
// C#, the organization and the crew. Nothing here is run by the application: a rule is only translated to SQL,
// and the export writes each as a policy of the projects' table, which the database then asks of every row,
// beside the application's own check.
//
// A policy on the projects' table cannot read the crew's tables itself: their policies follow the project's, so
// it would be asking itself. The functions the Membership package writes from the projects' rules read them
// without their policies, and answer with the ids of the projects they allow, once per statement. This module
// publishes two of them as contracts, by the project's id and with no function's name, and its own rules ask them
// as other modules' rules do.

/// <summary>
/// A seat reads the projects it reaches: those whose crew it is on, and those at a unit where it holds the key to
/// see them, there or above it (<see cref="ProjectsISee"/>). The set is asked once per statement, however many
/// projects are read.
/// </summary>
[RowAccess<Project>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class SeatsSeeTheProjectsTheyReach
{
    /// <summary>Whether the calling seat reaches <paramref name="project"/>, through the organization or through its crew.</summary>
    public static bool Allows(Project project, Caller caller) => ProjectsISee.Ids().Contains(project.Id);
}
