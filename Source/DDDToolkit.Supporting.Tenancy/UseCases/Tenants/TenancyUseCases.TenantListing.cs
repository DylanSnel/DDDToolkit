namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>A tenant as the tenants' directory lists it for an operator: no person, and no row of it.</summary>
    /// <param name="Id">The tenant.</param>
    /// <param name="Slug">The slug it is selected by.</param>
    /// <param name="Name">Its name, which is its organization's.</param>
    /// <param name="Status">Whether it is in use.</param>
    /// <param name="ActiveSeats">How many of its seats are active now.</param>
    public sealed record TenantListing(TTenantId Id, string Slug, string Name, TenantStatus Status, int ActiveSeats);
}
