namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// A seat, as Tenancy keeps it: its id and whether it counts. Never its identity, and no name: a seat has none in
    /// Tenancy. An application that shows its seats by a name, or by anything else it keeps, asks the directory with a
    /// view of the seat (<see cref="TenancyDirectory.ListSeatsAsync{TView}"/>,
    /// <see cref="TenancyDirectory.SeatsByIdAsync{TView}"/>, <see cref="TenancyDirectory.WhoAmIAsync{TView}"/>),
    /// which hands it this summary and its own seat side by side.
    /// </summary>
    public sealed record SeatSummary(TSeatId Id, SeatStatus Status);
}
