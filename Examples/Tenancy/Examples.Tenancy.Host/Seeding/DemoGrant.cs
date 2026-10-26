namespace Examples.Tenancy.Host.Seeding;

/// <summary>
/// A role the seeder grants a seat at a unit where it is placed. The period is relative to the moment it seeds:
/// from <paramref name="From"/> before then, or from then when left out, until <paramref name="Until"/> after
/// then, or for good when left out. A start in the past is for system work only, which the seeder is.
/// </summary>
/// <param name="Person">Whose seat is granted the role.</param>
/// <param name="Unit">Where.</param>
/// <param name="Pack">The pack the tenant's role was copied from.</param>
/// <param name="From">How long before seeding the grant starts.</param>
/// <param name="Until">How long after seeding it ends; negative for a grant that has ended already.</param>
public sealed record DemoGrant(DemoPerson Person, OrganizationUnitId Unit, string Pack, TimeSpan? From = null, TimeSpan? Until = null);
