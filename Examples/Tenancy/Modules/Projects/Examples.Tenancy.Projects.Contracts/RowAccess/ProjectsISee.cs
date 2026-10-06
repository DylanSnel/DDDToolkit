using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Projects.Contracts.ValueObjects;

namespace Examples.Tenancy.Projects.Contracts.RowAccess;

/// <summary>
/// The projects the calling seat sees, for a row access rule of another module to ask: on a database that
/// checks every row, what belongs to a project is read by whoever reads the project.
/// </summary>
/// <remarks>
/// The database's side of <see cref="Gate.IProjectGate"/>: the gate answers a use case in C#, and this answers a
/// policy, as the ids of every project the seat reaches, asked once per statement. Projects publishes it; a rule
/// of another module asks <c>ProjectsISee.Ids().Contains(row.ProjectId)</c>, and never reads Projects' tables.
/// <para>
/// It names no function. It is asked by the project's id, and the export writes the policy with the function that
/// answers the projects seen: the one the Membership package writes from the projects' rules, under whatever name
/// those rules give it. So a module that asks depends on this declaration alone, and not on how Projects answers.
/// Only a database answers it: called in C#, <c>Ids()</c> throws.
/// </para>
/// </remarks>
[ModuleContract]
[ResourceAccessContract<ProjectId>(ResourceAccessSet.Seen)]
public static partial class ProjectsISee;
