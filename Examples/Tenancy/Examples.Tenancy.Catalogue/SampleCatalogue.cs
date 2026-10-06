using Examples.Tenancy.Inspections.Application.Access;
using Examples.Tenancy.Projects.Application.Access;
using Examples.Tenancy.Projects.Contracts.Keys;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects.Entities;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Examples.Tenancy.Catalogue;

/// <summary>
/// The application's part of the permission catalogue: the role packs a new tenant's roles of the organization are
/// copied from, and the keys of a module it marks as managing access; and next to it the starter project roles every
/// tenant's crews begin with.
/// </summary>
/// <remarks>
/// This is the application's data, not a module's, so it lives in a project of its own that every program of the
/// application references: the host, which runs with it, and the program that exports the database's policies,
/// which are written from it. Tenancy and Membership on Postgres write their SQL into the exported files because the
/// modules that store them on Postgres reference them; what that SQL depends on is marked here, where it is
/// declared: the catalogue with <c>[TenancyCatalogue]</c> (<see cref="Application"/>) and the projects' rules with
/// <c>[MembershipRules&lt;CrewMember&gt;]</c> (<see cref="ProjectRules"/>). The keys are not declared here: Tenancy
/// brings its own (<see cref="TenancyKeys"/>), and every other module states its keys next to the code that asks for
/// them, once, on the list it marks with <c>[TenancyPermissions]</c>. Tenancy's generator collects those lists into
/// the host, as <c>TenancyPermissionsOfModules</c>, and the export finds the same marked lists in the modules it
/// references, so neither names a module's keys, and a module that is added changes nothing here. The packs only
/// name them. The catalogue is built and checked once, when the host starts, and a catalogue that does not hold
/// together stops the start.
/// <para>
/// There are two administrators' packs, each seeded for one shape of tenant (<see cref="RolePack.SeededFor"/>),
/// and a tenant's first seat is granted the one seeded for its shape. A flat tenant has one unit and usually a
/// handful of people, so its administrator does everything: <see cref="TenantAdmin"/> lists no keys, and once
/// built holds every live key of the catalogue, the modules' included. A hierarchical tenant separates running
/// access from doing the work: <see cref="AccessAdmin"/> lists its keys, Tenancy's own and the ones that manage
/// access, so its holder gives every role, names owners, manages crews and sees every project, and renames,
/// closes and records on none. The catalogue refuses a listing administrators' pack that leaves out a key that
/// manages access, so marking another key stops the host at start-up until this pack lists it. Either way nobody
/// but system work is needed to appoint, say, an area manager.
/// </para>
/// <para>
/// There are two kinds of role, each kept where it belongs. A role of the organization is Tenancy's, copied from a
/// pack here: granted at a unit, it reaches every project at that unit and below it. A project role is the
/// Projects module's, made from a starter role here (<see cref="ProjectRoles"/>): given on a project's crew, it
/// counts on that project only, and gives only the keys that act on a project. The crew lead's holds every one of
/// them, and a project's owner holds them by owning the project, so the owner may do everything that acts on it.
/// Opening projects, and so moving one to another unit, naming an owner and managing the organization take a role
/// at a unit.
/// </para>
/// <para>
/// An area manager is appointed at an area of the organization. A crew lead, a surveyor and an observer are
/// usually project roles, given on a crew, and are packs of the organization as well, for a tenant that grants
/// one at a unit: a surveyor at a site records on every project there. The area manager's and the access admin's
/// packs are seeded for a hierarchical tenant only: a flat one has nothing but its root to manage, and gets them
/// when it changes shape. The other packs name no shape, so every tenant gets them.
/// </para>
/// <para>
/// Tenant admin, Access admin, Area manager, Crew lead and People office manage access. Two of Projects' keys are marked as
/// managing access: naming a project's owner, because the owner leads the crew until the organization names
/// another, and managing a crew, because whoever holds it at a unit may put anyone on every crew there, in any
/// role, themselves included. So a role at a unit that holds either is given only by someone who holds it there
/// for at least as long, and never to oneself; whoever manages grants and holds neither gives Surveyor and
/// Observer. The marks rule the organization's grants only. On a project's crew a role reaches that project
/// alone (<see cref="ProjectCatalogue.CrewGives"/>), and whoever manages the crew gives any project role, the crew
/// lead's included, as the owner rule allows.
/// </para>
/// </remarks>
public static class SampleCatalogue
{
    /// <summary>
    /// The key of the administrators' pack of a flat tenant, which its first seat is granted at the root: every
    /// key there is.
    /// </summary>
    public const string TenantAdmin = "tenant-admin";

    /// <summary>
    /// The key of the administrators' pack of a hierarchical tenant, which its first seat is granted at the root:
    /// Tenancy's keys and the keys that manage access, and of the application's work only seeing projects.
    /// </summary>
    public const string AccessAdmin = "access-admin";

    /// <summary>The key of the pack for running an area of the organization: its projects, units, seats and grants.</summary>
    public const string AreaManager = "area-manager";

    /// <summary>
    /// The key of the pack for leading a project's crew, and of the starter project role of the same name, the
    /// role a project's owner holds on it. It is the key Projects finds the owner's role by, so it is Projects'
    /// constant.
    /// </summary>
    public const string CrewLead = ProjectMembership.CrewLead;

    /// <summary>The key of the pack for working on a project's site and recording what is found there, and of the starter project role of the same name.</summary>
    public const string Surveyor = "surveyor";

    /// <summary>The key of the pack for looking at a project and nothing more, and of the starter project role of the same name.</summary>
    public const string Observer = "observer";

    /// <summary>
    /// The key of the pack for giving people their roles, and nothing else: whoever holds it at a unit gives any
    /// role that manages no access to anyone placed there or below, without holding the role's keys.
    /// </summary>
    public const string PeopleOffice = "people-office";

    /// <summary>
    /// The project roles every tenant's crews start with, made once when a tenant is set up: the crew lead's, which
    /// every project's owner holds, a surveyor's, who records inspections, and an observer's. The tenant's own to
    /// rename, re-key and archive afterwards, and to add to. Each gives the keys that act on a project, of whichever
    /// module asks them, which is why the application says them and not the Projects module.
    /// </summary>
    public static IReadOnlyList<StarterProjectRole> ProjectRoles { get; } =
    [
        new(CrewLead, "Crew lead", "Leads a project crew", [ProjectKeys.View, ProjectKeys.Edit, ProjectKeys.Close, ProjectKeys.ManageCrew, InspectionKeys.Record]),
        new(Surveyor, "Surveyor", "Records inspections", [ProjectKeys.View, InspectionKeys.Record]),
        new(Observer, "Observer", "Looks", [ProjectKeys.View]),
    ];

    /// <summary>
    /// The projects' membership, made from <see cref="ProjectRoles"/> as the Projects module makes it from the same
    /// starter roles when the host adds it.
    /// </summary>
    public static ProjectMembership Projects { get; } = new(ProjectRoles);

    /// <summary>
    /// The projects' rules, marked as those of a project's crew members: the Membership package writes the projects'
    /// functions and the lock on their crews into the exported files from them.
    /// </summary>
    /// <remarks>
    /// The host registers the projects with rules the Projects module makes, at run time, from the starter roles the
    /// host hands it, and the build cannot read a value the registration is called with. So the rules the export
    /// writes from are these, made from the same starter roles; the host's start-up check holds the database to the
    /// rules it runs with.
    /// </remarks>
    [MembershipRules<CrewMember>]
    public static MembershipRules ProjectRules => Projects.Rules;

    /// <summary>
    /// The application's packs, and the keys of a module it marks as managing access: naming a project's owner and
    /// managing a crew, which Projects declares. What kind of unit a unit is, a company, a region, an area or a site,
    /// is not the catalogue's: no access rule reads it, so it is a field of the application's own unit class.
    /// </summary>
    /// <remarks>
    /// Marked, so the export writes Tenancy's policies from the catalogue the host runs with: this part, which the host
    /// hands Tenancy's registration, and every list of keys the modules mark, which Tenancy's generator collects into
    /// the host and the export finds in the modules it references. On Postgres the host's start-up check compares the
    /// database's functions with the catalogue the host runs with: policies written from another catalogue would
    /// contain other grants than the application gives.
    /// </remarks>
    [TenancyCatalogue]
    public static ApplicationCatalogue Application { get; } = new(
        Packs:
        [
            new(TenantAdmin, "Tenant admin", "Runs the tenant", [], SeededFor: TenantShape.Flat, Administers: true, Order: 10),
            new(
                AccessAdmin,
                "Access admin",
                "Runs access across the organization",
                [
                    .. TenancyKeys.Permissions.Select(permission => permission.Key),
                    ProjectKeys.ChangeOwner, ProjectKeys.ManageCrew, ProjectKeys.View,
                ],
                SeededFor: TenantShape.Hierarchical,
                Administers: true,
                Order: 10),
            new(
                AreaManager,
                "Area manager",
                "Runs an area of the organization",
                [
                    ProjectKeys.View, ProjectKeys.Open, ProjectKeys.Edit, ProjectKeys.Close, ProjectKeys.ManageCrew, ProjectKeys.ChangeOwner,
                    InspectionKeys.Record,
                    TenancyKeys.UnitsManage, TenancyKeys.SeatsManage, TenancyKeys.GrantsManage,
                ],
                SeededFor: TenantShape.Hierarchical,
                Order: 20),
            new(
                CrewLead,
                "Crew lead",
                "Leads a project crew",
                [ProjectKeys.View, ProjectKeys.Edit, ProjectKeys.Close, ProjectKeys.ManageCrew, InspectionKeys.Record],
                Order: 30),
            new(Surveyor, "Surveyor", "Records inspections", [ProjectKeys.View, InspectionKeys.Record], Order: 40),
            new(Observer, "Observer", "Looks", [ProjectKeys.View], Order: 50),
            new(PeopleOffice, "People office", "Gives people their roles", [TenancyKeys.GrantsManage], Order: 60),
        ],
        AccessManagingKeys: [ProjectKeys.ChangeOwner, ProjectKeys.ManageCrew]);
}
