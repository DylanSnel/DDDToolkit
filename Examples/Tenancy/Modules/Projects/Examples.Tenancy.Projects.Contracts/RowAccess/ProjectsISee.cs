using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Projects.Contracts.ValueObjects;

namespace Examples.Tenancy.Projects.Contracts.RowAccess;

/// <summary>
/// The projects the calling seat sees, for a row access rule of another module to ask: on a database that
/// checks every row, what belongs to a project is read by whoever reads the project.
/// </summary>
/// <remarks>
/// The database's side of <see cref="Gate.IProjectGate"/>: the gate answers a use case in C#, and this answers a
/// policy, as the ids of every project the seat reaches, asked once per statement. Projects defines it; a rule
/// of another module asks <c>ProjectsISee.Ids().Contains(row.ProjectId)</c>, and never reads Projects' tables.
/// Only a database answers it: called in C#, it throws.
/// </remarks>
[ModuleContract]
[AccessFunctionContract<ProjectId>("project_ids_i_see", Shape = AccessFunctionShape.Set)]
public static partial class ProjectsISee;
