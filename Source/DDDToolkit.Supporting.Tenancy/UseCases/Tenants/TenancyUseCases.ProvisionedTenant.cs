namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>The ids a provisioned tenant was given.</summary>
    /// <param name="Tenant">The tenant, and its organization.</param>
    /// <param name="RootUnit">The root unit.</param>
    /// <param name="AdminSeat">The first administrator's seat.</param>
    /// <param name="AdministratorRole">The role copied from the administrators' pack.</param>
    /// <param name="RolesByPack">Every role copied from a pack, by pack key.</param>
    public sealed record ProvisionedTenant(
        TTenantId Tenant,
        TUnitId RootUnit,
        TSeatId AdminSeat,
        TRoleId AdministratorRole,
        IReadOnlyDictionary<string, TRoleId> RolesByPack);
}
