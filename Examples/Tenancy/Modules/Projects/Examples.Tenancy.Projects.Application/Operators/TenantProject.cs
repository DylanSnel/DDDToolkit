namespace Examples.Tenancy.Projects.Application.Operators;

/// <summary>
/// A project of one tenant as the application's own staff read it: its own data and who changed it last, with
/// nothing about a caller's reach, since an operator has none.
/// </summary>
/// <param name="Id">The project.</param>
/// <param name="Number">Its number.</param>
/// <param name="Name">Its name.</param>
/// <param name="UnitId">The unit it hangs at.</param>
/// <param name="State">Open or closed.</param>
/// <param name="OwnerSeat">The seat that owns it.</param>
/// <param name="Planned">The days it is planned for, or <see langword="null"/>.</param>
/// <param name="ChangedBy">Who changed it last.</param>
public sealed record TenantProject(
    ProjectId Id,
    string Number,
    string Name,
    OrganizationUnitId UnitId,
    ProjectState State,
    SeatId OwnerSeat,
    DateRange? Planned,
    ChangedBy ChangedBy);
