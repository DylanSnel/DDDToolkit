namespace Examples.Tenancy.Host.Seeding;

/// <summary>A person's seat in a tenant, and the unit it is placed in, as its primary placement.</summary>
/// <param name="Id">Its fixed id.</param>
/// <param name="Person">Whose seat it is.</param>
/// <param name="PlacedIn">Where it is placed.</param>
public sealed record DemoSeat(SeatId Id, DemoPerson Person, OrganizationUnitId PlacedIn);
