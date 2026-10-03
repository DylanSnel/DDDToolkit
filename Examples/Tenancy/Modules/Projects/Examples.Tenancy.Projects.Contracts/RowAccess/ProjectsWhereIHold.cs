using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Projects.Contracts.ValueObjects;

namespace Examples.Tenancy.Projects.Contracts.RowAccess;

/// <summary>
/// The projects on which the calling seat holds a key, for a row access rule of another module to ask: at the
/// project's unit or above it, or through a role on its crew.
/// </summary>
/// <remarks>
/// The database's side of <see cref="Gate.IProjectGate"/> for a key of any module, the asking module's own
/// included: Inspections asks it for the key that records. Opening a project and naming its owner are never
/// held through a crew, and the definition leaves the crew out for those two, as the gate does. Only a database
/// answers it: called in C#, it throws.
/// </remarks>
[ModuleContract]
[AccessFunctionContract<ProjectId>("project_ids_where_i_hold")]
public static partial class ProjectsWhereIHold
{
    /// <summary>The ids of the projects the calling seat holds <paramref name="key"/> on.</summary>
    /// <param name="key">A key of the catalogue.</param>
    public static partial AccessSet<ProjectId> Ids(string key);
}
