using DDDToolkit.Supporting.Tenancy;

namespace Examples.Tenancy.Host.Seeding;

/// <summary>A tenant of the demonstration, with every fixed id the seeder gives it.</summary>
/// <param name="Id">The tenant's id, which is also its organization's.</param>
/// <param name="Slug">What the <c>Tenant</c> header names it by.</param>
/// <param name="Name">The tenant's name, which is also its root's.</param>
/// <param name="Shape">Flat or hierarchical.</param>
/// <param name="Root">The root unit's id.</param>
/// <param name="Administrator">The first administrator's seat, which provisioning places at the root and grants the role of the administrators' pack seeded for the tenant's shape.</param>
/// <param name="Roles">The ids of the roles of the organization copied from the packs seeded for the tenant's shape, by pack key.</param>
/// <param name="ProjectRoles">The ids of the project roles made from the starter roles when the tenant is set up, by the starter role's key.</param>
/// <param name="Units">The units below the root, parents before children.</param>
/// <param name="Seats">Every other seat, each with its primary placement.</param>
/// <param name="Grants">The roles granted at units, besides the administrators' role provisioning grants the first administrator.</param>
/// <param name="Suspended">The people whose seat is suspended once they are placed and granted.</param>
/// <param name="Projects">The projects, opened last.</param>
public sealed record DemoTenant(
    TenantId Id,
    string Slug,
    string Name,
    TenantShape Shape,
    OrganizationUnitId Root,
    DemoSeat Administrator,
    IReadOnlyDictionary<string, RoleId> Roles,
    IReadOnlyDictionary<string, ProjectRoleId> ProjectRoles,
    IReadOnlyList<DemoUnit> Units,
    IReadOnlyList<DemoSeat> Seats,
    IReadOnlyList<DemoGrant> Grants,
    IReadOnlyList<DemoPerson> Suspended,
    IReadOnlyList<DemoProject> Projects)
{
    /// <summary>The kind of the root unit, which provisioning sets through its callback for the root.</summary>
    public const UnitKind RootKind = UnitKind.Company;

    /// <summary>
    /// The key of the administrators' pack seeded for this tenant's shape: the pack whose role provisioning grants the
    /// first administrator. A flat tenant's holds every key; a hierarchical tenant's runs access.
    /// </summary>
    public string AdministratorsPack => Shape == TenantShape.Flat ? SampleCatalogue.TenantAdmin : SampleCatalogue.AccessAdmin;

    /// <summary>The tenant's administrators' role: the one made from <see cref="AdministratorsPack"/>.</summary>
    public RoleId AdministratorsRole => Roles[AdministratorsPack];

    /// <summary>The unit with this name: the root by the tenant's name, or one of <see cref="Units"/>.</summary>
    /// <exception cref="ArgumentException">The tenant has no unit of that name.</exception>
    public OrganizationUnitId UnitNamed(string name)
        => name == Name ? Root
            : Units.FirstOrDefault(unit => unit.Name == name)?.Id
              ?? throw new ArgumentException(Name + " has no unit named " + name + ".", nameof(name));

    /// <summary>The project with this name.</summary>
    /// <exception cref="ArgumentException">The tenant has no project of that name.</exception>
    public DemoProject ProjectNamed(string name)
        => Projects.FirstOrDefault(project => project.Name == name)
           ?? throw new ArgumentException(Name + " has no project named " + name + ".", nameof(name));

    /// <summary>The seat of <paramref name="person"/> in this tenant.</summary>
    /// <exception cref="ArgumentException">They have no seat here.</exception>
    public SeatId SeatOf(DemoPerson person)
        => Administrator.Person == person ? Administrator.Id
            : Seats.FirstOrDefault(seat => seat.Person == person)?.Id
              ?? throw new ArgumentException(person.Name + " has no seat in " + Name + ".", nameof(person));

    /// <summary>
    /// What of this tenant a database lacks that has <paramref name="seats"/>, <paramref name="roles"/>,
    /// <paramref name="projectRoles"/> and <paramref name="projects"/> in it, each named for a person to read, such
    /// as <c>seat Hana</c>, <c>role people-office</c>, <c>project role surveyor</c> or <c>project M-001</c>; empty
    /// when all of it is there.
    /// </summary>
    /// <remarks>
    /// A database an earlier version of the sample seeded has every seat, role and project, and no project role:
    /// its tenants were never set up for projects, so none of them can open one. The project roles are what says so.
    /// </remarks>
    /// <param name="seats">The tenant's seats the database has.</param>
    /// <param name="roles">The tenant's roles of the organization the database has.</param>
    /// <param name="projectRoles">The tenant's project roles the database has.</param>
    /// <param name="projects">The tenant's projects the database has.</param>
    public IReadOnlyList<string> MissingFrom(
        IReadOnlySet<SeatId> seats,
        IReadOnlySet<RoleId> roles,
        IReadOnlySet<ProjectRoleId> projectRoles,
        IReadOnlySet<ProjectId> projects)
    {
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(projectRoles);
        ArgumentNullException.ThrowIfNull(projects);

        return
        [
            .. Seats.Prepend(Administrator).Where(seat => !seats.Contains(seat.Id)).Select(seat => "seat " + seat.Person.Name),
            .. Roles.Where(role => !roles.Contains(role.Value)).Select(role => "role " + role.Key),
            .. ProjectRoles.Where(role => !projectRoles.Contains(role.Value)).Select(role => "project role " + role.Key),
            .. Projects.Where(project => !projects.Contains(project.Id)).Select(project => "project " + project.Number),
        ];
    }
}
