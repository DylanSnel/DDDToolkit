namespace Examples.Tenancy.Host.Seeding;

/// <summary>A unit the seeder adds below the root, or below another seeded unit.</summary>
/// <param name="Id">Its fixed id.</param>
/// <param name="Parent">The unit it hangs below.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind">What kind of unit it is: the application's own field, set by the callback of the use case that adds it.</param>
public sealed record DemoUnit(OrganizationUnitId Id, OrganizationUnitId Parent, string Name, UnitKind Kind);
