using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Projects.Contracts.RowAccess;

namespace Examples.Tenancy.Inspections.Infrastructure.Access;

// What the database itself lets a seat do with inspections: the two questions the use cases put to Projects'
// gate in C#, put to Projects' published sets as row access rules. Nothing here is run by the application: the
// export writes each as a policy, which the database then asks of every row, beside the application's own check.
// That a closed project takes no inspection stays the application's to say: the access check refuses it, and so
// does the handler, which asks Projects' gate again. A project closed between that question and the insert still
// gets the inspection.

/// <summary>
/// A seat reads the inspections of the projects it sees. Which those are is Projects' answer, asked once per
/// statement; this module reads no table of Projects'.
/// </summary>
[RowAccess<Inspection>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class SeatsSeeTheInspectionsOfProjectsTheySee
{
    /// <summary>Whether the calling seat sees the project <paramref name="inspection"/> was recorded on.</summary>
    public static bool Allows(Inspection inspection, Caller caller)
        => ProjectsISee.Ids().Contains(inspection.ProjectId);
}
