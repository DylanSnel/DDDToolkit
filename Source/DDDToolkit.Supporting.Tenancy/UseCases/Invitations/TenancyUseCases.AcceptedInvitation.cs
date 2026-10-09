namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>What accepting an invitation gives the person who accepted.</summary>
    /// <param name="Tenant">The tenant they now have a seat in.</param>
    /// <param name="Slug">The slug that tenant is selected by, for the next request.</param>
    /// <param name="Seat">Their seat.</param>
    public sealed record AcceptedInvitation(TTenantId Tenant, string Slug, TSeatId Seat);
}
