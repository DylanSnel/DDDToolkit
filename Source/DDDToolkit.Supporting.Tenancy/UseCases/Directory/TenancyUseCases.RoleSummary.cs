namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// A role, and whether it manages access: whether the catalogue marks one of its live keys as managing access,
    /// which an archived role never does.
    /// While containment is on (<see cref="Catalogue.ApplicationCatalogue.ContainAccessManagingKeys"/>), such a role
    /// is given only by a seat that holds those keys, rather than by whoever holds <c>tenancy.grants.manage</c>
    /// where the seat is placed; once the application turns it off, it is given as any other role, and is still
    /// marked here.
    /// </summary>
    public sealed record RoleSummary(TRoleId Id, string Name, string? FromPack, RoleStatus Status, IReadOnlyList<string> Keys, bool ManagesAccess);
}
