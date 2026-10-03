namespace DDDToolkit.Supporting.Tenancy.UseCases;

public static partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>A unit, with its path from the root and how deep it is, the root being 1.</summary>
    public sealed record UnitSummary(TUnitId Id, TUnitId? ParentId, string Name, string Kind, UnitStatus Status, string Path, int Depth);
}
