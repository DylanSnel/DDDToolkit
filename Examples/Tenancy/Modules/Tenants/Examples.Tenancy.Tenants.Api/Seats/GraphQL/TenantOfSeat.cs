namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>The tenant one of the caller's seats is in, as the list a tenant is picked from shows it.</summary>
/// <param name="Id">The tenant.</param>
/// <param name="Slug">What a request names it by, in the <c>Tenant</c> header.</param>
/// <param name="Name">Its name, which is its organization's.</param>
/// <param name="Status">Whether it is in use.</param>
internal sealed record TenantOfSeat(TenantId Id, string Slug, string Name, TenantStatus Status);
