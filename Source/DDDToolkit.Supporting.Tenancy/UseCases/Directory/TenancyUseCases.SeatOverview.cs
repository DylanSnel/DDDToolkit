namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// Who the calling seat is, and what it may do where, with the seat as Tenancy keeps it: its id and status
    /// (<see cref="TenancyDirectory.WhoAmIAsync(CancellationToken)"/>). An application that shows the seat by what it
    /// keeps itself, such as a name, asks with a view and is answered a <see cref="SeatOverview{TSeatView}"/>.
    /// </summary>
    /// <param name="Tenant">The tenant it is in.</param>
    /// <param name="Seat">The seat.</param>
    /// <param name="Placements">Where it is placed, the primary placement first, with the roles it holds at each.</param>
    /// <param name="Roles">The roles it holds anywhere.</param>
    /// <param name="Keys">Every key it holds now, where it is granted and every unit it reaches.</param>
    public sealed record SeatOverview(
        TenantSummary Tenant,
        SeatSummary Seat,
        IReadOnlyList<PlacementSummary> Placements,
        IReadOnlyList<RoleSummary> Roles,
        IReadOnlyList<KeyReach> Keys);

    /// <summary>
    /// Who the calling seat is, and what it may do where, with the seat as the application's view answered it
    /// (<see cref="TenancyDirectory.WhoAmIAsync{TView}"/>): the package's summary with the name the application keeps
    /// on its own seat class, say.
    /// </summary>
    /// <typeparam name="TSeatView">What the application's view made of the package's summary and its own seat.</typeparam>
    /// <param name="Tenant">The tenant it is in.</param>
    /// <param name="Seat">The seat, as the view answered it.</param>
    /// <param name="Placements">Where it is placed, the primary placement first, with the roles it holds at each.</param>
    /// <param name="Roles">The roles it holds anywhere.</param>
    /// <param name="Keys">Every key it holds now, where it is granted and every unit it reaches.</param>
    public sealed record SeatOverview<TSeatView>(
        TenantSummary Tenant,
        TSeatView Seat,
        IReadOnlyList<PlacementSummary> Placements,
        IReadOnlyList<RoleSummary> Roles,
        IReadOnlyList<KeyReach> Keys);
}
