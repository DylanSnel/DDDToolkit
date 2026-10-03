namespace DDDToolkit.Supporting.Tenancy.UseCases;

public static partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>A seat: never its identity.</summary>
    public sealed record SeatSummary(TSeatId Id, string DisplayName, SeatStatus Status);
}
