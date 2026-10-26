namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>One page of the tenants' directory.</summary>
    /// <param name="Items">The tenants of the page, by slug.</param>
    /// <param name="Next">What to ask the next page with, or <see langword="null"/> when this is the last one.</param>
    public sealed record TenantDirectoryPage(IReadOnlyList<TenantListing> Items, string? Next);
}
