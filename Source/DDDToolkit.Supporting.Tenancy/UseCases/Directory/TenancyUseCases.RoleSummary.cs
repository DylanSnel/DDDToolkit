namespace DDDToolkit.Supporting.Tenancy.UseCases;

public static partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// A role, and whether it manages access: whether it is given only by a seat that holds its keys that
    /// manage access, rather than by whoever holds <c>tenancy.grants.manage</c> where the seat is placed.
    /// </summary>
    public sealed record RoleSummary(TRoleId Id, string Name, string? FromPack, RoleStatus Status, IReadOnlyList<string> Keys, bool ManagesAccess);
}
