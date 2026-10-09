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
    /// <param name="Id">The role.</param>
    /// <param name="Name">Its name in the tenant.</param>
    /// <param name="FromPack">The key of the catalogue's pack it was made from, or <see langword="null"/> for a role made by hand.</param>
    /// <param name="Status">Whether it is active, or archived and granting nothing.</param>
    /// <param name="Keys">The keys it grants.</param>
    /// <param name="ManagesAccess">
    /// Whether the catalogue marks one of its live keys as managing access; never for an archived role.
    /// </param>
    public sealed record RoleSummary(TRoleId Id, string Name, string? FromPack, RoleStatus Status, IReadOnlyList<string> Keys, bool ManagesAccess);
}
