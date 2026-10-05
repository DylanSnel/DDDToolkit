using DDDToolkit.Supporting.Tenancy;

namespace Examples.Tenancy.Host.Seeding;

/// <summary>
/// The demonstration's tenants, units, seats, roles, grants and projects, with fixed ids, so the seeder, the
/// tests and the try-it presets all name the same rows.
/// </summary>
/// <remarks>
/// An id says what it is: its first digit gives the kind (<c>a</c> a tenant, <c>b</c> a unit, <c>c</c> a
/// seat, <c>e</c> a role of the organization, <c>9</c> a project role, <c>f</c> a project, and <c>d</c> a person,
/// in <see cref="DemoPeople"/>), and its last three the tenant (1 harbor, 2 meadow) and the number. A seat's
/// number is its person's, so <c>c0000000-…-000000000207</c> is Tove's seat in meadow. A role's number is its
/// pack's in both tenants: 1 Tenant admin, 2 Area manager, 3 Crew lead, 4 Surveyor, 5 Observer, 6 People office,
/// 7 Access admin. A tenant has the roles of its shape, so harbor has no role 1, and meadow no role 2 or 7 until
/// it turns hierarchical and is given an area manager's and an access admin's. A project role's number is the
/// number of the pack of the same name: 3 Crew lead, 4 Surveyor, 5 Observer, the starter roles every tenant has.
/// </remarks>
public static class DemoData
{
    /// <summary>Harbor Works: hierarchical, with two regions and three areas below its root.</summary>
    public static DemoTenant Harbor { get; } = CreateHarbor();

    /// <summary>Meadow Gardens: flat, so its root is its only unit.</summary>
    public static DemoTenant Meadow { get; } = CreateMeadow();

    /// <summary>Both tenants, in the order they are seeded.</summary>
    public static IReadOnlyList<DemoTenant> Tenants { get; } = [Harbor, Meadow];

    private static DemoTenant CreateHarbor()
    {
        var root = Unit(1, 1);
        var north = Unit(1, 2);
        var northCoast = Unit(1, 3);
        var northInland = Unit(1, 4);
        var south = Unit(1, 5);
        var southBay = Unit(1, 6);

        return new DemoTenant(
            new TenantId(new Guid("a0000000-0000-4000-8000-000000000001")),
            "harbor",
            "Harbor Works",
            TenantShape.Hierarchical,
            root,
            Seat(1, DemoPeople.Ada, root),
            new Dictionary<string, RoleId>(StringComparer.Ordinal)
            {
                [SampleCatalogue.AreaManager] = Role(1, 2),
                [SampleCatalogue.CrewLead] = Role(1, 3),
                [SampleCatalogue.Surveyor] = Role(1, 4),
                [SampleCatalogue.Observer] = Role(1, 5),
                [SampleCatalogue.PeopleOffice] = Role(1, 6),
                [SampleCatalogue.AccessAdmin] = Role(1, 7),
            },
            ProjectRolesOf(1),
            [
                new(north, root, "North", UnitKind.Region),
                new(northCoast, north, "North Coast", UnitKind.Area),
                new(northInland, north, "North Inland", UnitKind.Area),
                new(south, root, "South", UnitKind.Region),
                new(southBay, south, "South Bay", UnitKind.Area),
            ],
            [
                Seat(1, DemoPeople.Rhea, north),
                Seat(1, DemoPeople.Leo, northCoast),
                Seat(1, DemoPeople.Juno, northCoast),
                Seat(1, DemoPeople.Vic, northInland),
                Seat(1, DemoPeople.Seth, southBay),
                Seat(1, DemoPeople.Tove, southBay),
                Seat(1, DemoPeople.Hana, root),
                Seat(1, DemoPeople.Maud, root),
            ],
            [
                // Provisioning made Ada an access admin, which runs access and none of the work. She runs the work
                // too: an area manager for the whole of harbor. Only system work gives a seat a role that manages
                // access for itself, and seeding is system work acting for her.
                new(DemoPeople.Ada, root, SampleCatalogue.AreaManager),

                new(DemoPeople.Rhea, north, SampleCatalogue.AreaManager),

                // Grant management for the whole tenant, and nothing else: she gives roles, she does not hold them.
                new(DemoPeople.Hana, root, SampleCatalogue.PeopleOffice),

                // The administrators' role of a hierarchical tenant, and nothing else: a second administrator, who
                // runs access and does none of the work.
                new(DemoPeople.Maud, root, SampleCatalogue.AccessAdmin),

                // Four weeks that ended yesterday: the grant is there, and it gives nothing.
                new(DemoPeople.Vic, northInland, SampleCatalogue.Observer, From: TimeSpan.FromDays(30), Until: TimeSpan.FromDays(-1)),

                // Granted before the seat is suspended, so what the suspension takes away is real.
                new(DemoPeople.Seth, southBay, SampleCatalogue.Observer),
            ],
            [DemoPeople.Seth],
            [
                new("P-001", "Pier 7", Project(1, 1), northCoast, DemoPeople.Leo,
                [
                    new(DemoPeople.Juno, SampleCatalogue.Surveyor),
                    new(DemoPeople.Vic, SampleCatalogue.Observer),
                ]),
                // The one planned project: from a month before the day it is seeded to two months after. An
                // inspection on it covers days within that, which the try-it page shows.
                new("P-002", "Inland depot", Project(1, 2), northInland, DemoPeople.Rhea, [], PlannedDays: (30, 60)),
                new("P-003", "Bay bridge", Project(1, 3), southBay, DemoPeople.Ada,
                [
                    new(DemoPeople.Tove, SampleCatalogue.Surveyor),
                ]),
                new("P-004", "HQ refit", Project(1, 4), root, DemoPeople.Ada, []),
            ]);
    }

    private static DemoTenant CreateMeadow()
    {
        var root = Unit(2, 1);

        return new DemoTenant(
            new TenantId(new Guid("a0000000-0000-4000-8000-000000000002")),
            "meadow",
            "Meadow Gardens",
            TenantShape.Flat,
            root,
            Seat(2, DemoPeople.Tove, root),
            new Dictionary<string, RoleId>(StringComparer.Ordinal)
            {
                [SampleCatalogue.TenantAdmin] = Role(2, 1),
                [SampleCatalogue.CrewLead] = Role(2, 3),
                [SampleCatalogue.Surveyor] = Role(2, 4),
                [SampleCatalogue.Observer] = Role(2, 5),
                [SampleCatalogue.PeopleOffice] = Role(2, 6),
            },
            ProjectRolesOf(2),
            [],
            [],
            [],
            [],
            [
                new("M-001", "Garden shed", Project(2, 1), root, DemoPeople.Tove, []),
            ]);
    }

    /// <summary>A unit of the demonstration, by its tenant and number: harbor's root is <c>b0000000-…-000000000101</c>.</summary>
    public static OrganizationUnitId Unit(int tenant, int number) => new(Fixed('b', tenant, number));

    /// <summary>A role of the demonstration, by its tenant and number.</summary>
    public static RoleId Role(int tenant, int number) => new(Fixed('e', tenant, number));

    /// <summary>A project role of the demonstration, by its tenant and number.</summary>
    public static ProjectRoleId ProjectRole(int tenant, int number) => new(Fixed('9', tenant, number));

    /// <summary>A project of the demonstration, by its tenant and number.</summary>
    public static ProjectId Project(int tenant, int number) => new(Fixed('f', tenant, number));

    /// <summary>The ids of a tenant's starter project roles, by the starter role's key.</summary>
    private static Dictionary<string, ProjectRoleId> ProjectRolesOf(int tenant) => new(StringComparer.Ordinal)
    {
        [SampleCatalogue.CrewLead] = ProjectRole(tenant, 3),
        [SampleCatalogue.Surveyor] = ProjectRole(tenant, 4),
        [SampleCatalogue.Observer] = ProjectRole(tenant, 5),
    };

    private static DemoSeat Seat(int tenant, DemoPerson person, OrganizationUnitId placedIn)
        => new(new SeatId(Fixed('c', tenant, DemoPeople.All.ToList().IndexOf(person) + 1)), person, placedIn);

    private static Guid Fixed(char kind, int tenant, int number)
        => new($"{kind}0000000-0000-4000-8000-000000000{tenant}{number:00}");
}
