namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>A tenant.</summary>
    /// <param name="Id">The tenant.</param>
    /// <param name="Slug">The slug it is known by.</param>
    /// <param name="Name">Its name: its organization's.</param>
    /// <param name="Shape">Its shape: its root alone, or a tree of units below it.</param>
    /// <param name="Status">Where it is in its life: being set up, active, suspended or closed.</param>
    public sealed record TenantSummary(TTenantId Id, string Slug, string Name, TenantShape Shape, TenantStatus Status);
}
