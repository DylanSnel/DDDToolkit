namespace Examples.Tenancy.Tenants.Application.Seats;

/// <summary>
/// The tenant the calling seat is in, as its own overview shows it: what this application selects of the package's
/// tenant, with the name the package read beside it.
/// </summary>
/// <param name="Id">The tenant.</param>
/// <param name="Slug">What a request names it by, in the <c>Tenant</c> header.</param>
/// <param name="Name">Its name, which is its organization's.</param>
/// <param name="Shape">Its shape: its root alone, or a tree of units below it.</param>
/// <param name="Status">Where it is in its life: being set up, active, suspended or closed.</param>
public sealed record TenantOfMine(TenantId Id, string Slug, string Name, TenantShape Shape, TenantStatus Status);
