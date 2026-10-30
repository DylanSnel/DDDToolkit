using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Inspections.Application.Access;

namespace Examples.Tenancy.Inspections.Infrastructure.Access;

/// <summary>
/// A seat records an inspection on a project where it holds the permission to record, at the project's unit or through
/// its crew, and only in its own name: the seat an inspection says recorded it is the calling one. A caller
/// without a seat matches no seat, so it records nothing.
/// </summary>
[RowAccess<Inspection>(RowOperations.Create, To = [RowAccessRoles.User])]
public static partial class SeatsRecordWhereTheyMay
{
    /// <summary>Whether the calling seat may record <paramref name="inspection"/>.</summary>
    public static bool Allows(Inspection inspection, IRowUser user)
        => user.Holds<ProjectId>(InspectionPermissionNames.Record).Contains(inspection.ProjectId)
            && inspection.RecordedBy == user.CurrentSeatId();
}
