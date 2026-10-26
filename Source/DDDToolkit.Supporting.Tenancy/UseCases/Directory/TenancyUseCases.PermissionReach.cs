namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// A permission a seat holds now: the units it is granted at, every unit it reaches (those and every unit below
    /// them), and whether it holds for the whole tenant, which is being granted at the root.
    /// </summary>
    /// <param name="Permission">The permission.</param>
    /// <param name="AtTenant">Whether the seat holds it for the whole tenant: one of its grants is at the root.</param>
    /// <param name="GrantedAt">The units a grant of the seat gives it at, each once, by its path from the root.</param>
    /// <param name="Reaches">Every unit it holds the permission at: those, and every unit below them, each once, by path.</param>
    public sealed record PermissionReach(string Permission, bool AtTenant, IReadOnlyList<UnitRef> GrantedAt, IReadOnlyList<UnitRef> Reaches);
}
