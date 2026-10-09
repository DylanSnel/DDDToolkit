using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.Access;
using Examples.Tenancy.Tenants.Application.Seats.Commands;

namespace Examples.Tenancy.Tenants.Infrastructure.Access;

// A column rule on a field of this application's own, on the package's table. Tenancy's policy on the seats lets a seat
// write its own row, and a seat that manages seats or grants anywhere in the tenant write any seat's row, because
// every save of a seat writes its version: a placement and a grant as much as a rename. The package holds its own
// columns of the row, the id, the identity, the tenant and the status, and decides nothing about this module's, so
// without this rule a seat that only gives roles at one unit would rename every seat of the tenant with a statement of
// its own, past the check in RenameSeat. Nothing here is run by the application: the export writes a trigger on the
// column the name is stored in. The job title has no command, and no rule: it is as writable as the row.

/// <summary>
/// A seat's name changes only for the seat itself, or for a seat that manages seats for the whole tenant
/// (<see cref="RenameSeat.RequiredKey"/>): what <see cref="RenameSeat"/> asks. Asked of the seat as it was and as it
/// is about to be, beside the policy that lets the caller change the row at all.
/// </summary>
/// <remarks>
/// For a signed-in user, the one role a seat's statement runs as. Any other role a caller's statement runs as, an
/// anonymous caller or an operator's token, does not change the column, and the application's own work in the tenant
/// is not held. A seat is named as it is made, in the callbacks of the package's use cases, which is an insert: the
/// rule is about a change.
/// </remarks>
[RowAccess<Seat>(RowOperations.Change, To = [RowAccessRoles.User], Columns = [nameof(Seat.DisplayName)])]
public static partial class NameChangesByTheSeatOrWithTheSeatsKey
{
    /// <summary>Whether <paramref name="seat"/> is the calling seat, or the calling seat manages seats for the whole tenant.</summary>
    public static bool Allows(Seat seat, Caller caller)
        => seat.Id == TenancyRowAccess.CallerSeat<SeatId>() || TenancyRowAccess.HoldsTenantWide(RenameSeat.RequiredKey);
}
