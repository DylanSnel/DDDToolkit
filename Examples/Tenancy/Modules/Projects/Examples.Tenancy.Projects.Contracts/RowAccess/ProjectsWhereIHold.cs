using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Projects.Contracts.ValueObjects;

namespace Examples.Tenancy.Projects.Contracts.RowAccess;

/// <summary>
/// The projects on which the calling seat holds a key, for a row access rule of another module to ask: at the
/// project's unit or above it, or through a role on its crew.
/// </summary>
/// <remarks>
/// The database's side of <see cref="Gate.IProjectGate"/> for a key of any module, the asking module's own
/// included: Inspections asks <c>ProjectsWhereIHold.Ids(InspectionKeys.Record)</c> for the key that records.
/// Opening a project and naming its owner are never held through a crew, and the answer leaves the crew out for
/// those two, as the gate does.
/// <para>
/// It names no function, as <see cref="ProjectsISee"/> names none: it is asked by the project's id and the key,
/// and the export writes the policy with the function the Membership package writes for the projects. Only a
/// database answers it: called in C#, <c>Ids(key)</c> throws.
/// </para>
/// </remarks>
[ModuleContract]
[ResourceAccessContract<ProjectId>(ResourceAccessSet.HeldOn)]
public static partial class ProjectsWhereIHold;
