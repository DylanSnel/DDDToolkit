namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>A role a seat holds at a unit, for a period, and whether that period applies now.</summary>
    public sealed record GrantSummary(TRoleId RoleId, string Role, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool AppliesNow);
}
