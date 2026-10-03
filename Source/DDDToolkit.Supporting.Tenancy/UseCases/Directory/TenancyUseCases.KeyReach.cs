namespace DDDToolkit.Supporting.Tenancy.UseCases;

public static partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// A key a seat holds now: the units it is granted at, every unit it reaches (those and every unit below
    /// them), and whether it holds for the whole tenant, which is being granted at the root.
    /// </summary>
    public sealed record KeyReach(string Key, bool WholeTenant, IReadOnlyList<UnitRef> GrantedAt, IReadOnlyList<UnitRef> Reaches);
}
