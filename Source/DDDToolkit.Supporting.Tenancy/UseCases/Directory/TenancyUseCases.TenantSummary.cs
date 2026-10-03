namespace DDDToolkit.Supporting.Tenancy.UseCases;

public static partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>A tenant.</summary>
    public sealed record TenantSummary(TTenantId Id, string Slug, string Name, TenantShape Shape, TenantStatus Status);
}
