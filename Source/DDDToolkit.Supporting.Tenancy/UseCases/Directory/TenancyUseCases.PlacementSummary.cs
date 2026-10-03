namespace DDDToolkit.Supporting.Tenancy.UseCases;

public static partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>One of a seat's placements: the unit, whether it is the primary one, and the grants made with it.</summary>
    public sealed record PlacementSummary(UnitRef Unit, bool IsPrimary, IReadOnlyList<GrantSummary> Grants);
}
