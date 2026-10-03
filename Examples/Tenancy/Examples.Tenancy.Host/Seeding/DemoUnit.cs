namespace Examples.Tenancy.Host.Seeding;

/// <summary>A unit the seeder adds below the root, or below another seeded unit.</summary>
/// <param name="Id">Its fixed id.</param>
/// <param name="Parent">The unit it hangs below.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind">One of the catalogue's unit kinds.</param>
public sealed record DemoUnit(OrganizationUnitId Id, OrganizationUnitId Parent, string Name, string Kind);
