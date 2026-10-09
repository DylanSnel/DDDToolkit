using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace Examples.Tenancy.Projects.Infrastructure.Access;

/// <summary>
/// A seat adds a project at a unit where it holds the key to open one, there or above it, and owns the project
/// itself unless it may name owners there too: what <c>OpenProject</c> asks. A crew gives no such key: a project
/// that is being opened has none yet. The crew the project is opened with follows this rule, since an aggregate's
/// entities are written under the rules of the aggregate.
/// </summary>
/// <remarks>
/// The Membership package's lock holds an owner that changes, and a new row had no owner before: whom a new
/// project names is this rule's to hold. Without its second half, a seat that may open projects would make any
/// seat the owner of one by a statement of its own, and that seat would hold every key an owner holds.
/// </remarks>
[RowAccess<Project>(RowOperations.Create, To = [RowAccessRoles.User])]
public static partial class SeatsOpenProjectsWhereTheyMay
{
    /// <summary>Whether the calling seat may open <paramref name="project"/> at the unit it names, for the owner it names.</summary>
    public static bool Allows(Project project, Caller caller)
        => TenancyRowAccess.UnitsWhereIHold<OrganizationUnitId>(ProjectKeys.Open).Contains(project.UnitId)
           && (project.OwnerSeatId == TenancyRowAccess.CallerSeat<SeatId>()
               || TenancyRowAccess.UnitsWhereIHold<OrganizationUnitId>(ProjectKeys.ChangeOwner).Contains(project.UnitId));
}
