namespace Examples.Tenancy.Tenants.Application.Operators;

/// <summary>
/// A tenant as the application's operators see it listed: what this application selects of the package's tenant, with
/// its name and how many of its seats are in use beside it. No person, and no row of the tenant's.
/// </summary>
/// <param name="Id">The tenant.</param>
/// <param name="Slug">The slug it is selected by.</param>
/// <param name="Name">Its name, which is its organization's.</param>
/// <param name="Status">Whether it is in use.</param>
/// <param name="ActiveSeats">How many of its seats are active now.</param>
public sealed record ListedTenant(TenantId Id, string Slug, string Name, TenantStatus Status, int ActiveSeats);
