namespace Examples.Tenancy.Host.Seeding;

/// <summary>
/// A seat the seeder puts on a project's crew, for good, holding the tenant's project role made from the starter
/// role <paramref name="Role"/> there, for good too.
/// </summary>
/// <param name="Person">Whose seat.</param>
/// <param name="Role">The starter project role the crew role was made from.</param>
public sealed record DemoCrewMember(DemoPerson Person, string Role);
