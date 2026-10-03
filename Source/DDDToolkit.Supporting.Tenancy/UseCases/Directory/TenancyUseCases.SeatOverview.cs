namespace DDDToolkit.Supporting.Tenancy.UseCases;

public static partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>Who the calling seat is, and what it may do where.</summary>
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
}
