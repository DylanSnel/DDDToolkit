using Examples.Tenancy.Inspections.Application.Access;
using Examples.Tenancy.Projects.Application.Access;
using Examples.Tenancy.Projects.Contracts.Permissions;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects.Entities;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Examples.Tenancy.Catalogue;

/// <summary>
/// The application's part of the permission catalogue: the starter roles a new tenant's roles of the organization are
/// copied from, and the permissions of a module it marks as managing access; and next to it the starter project roles every
/// tenant's crews begin with.
/// </summary>
/// <remarks>
/// This is the application's data, not a module's, so it lives in a project of its own that every program of the
/// application references: the host, which runs with it, and the program that exports the database's policies,
/// which are written from it. Tenancy and Membership on Postgres write their SQL into the exported files because the
/// modules that store them on Postgres reference them; what that SQL depends on is marked here, where it is
/// declared: the catalogue with <c>[TenancyCatalogue]</c> (<see cref="Application"/>) and the projects' rules with
/// <c>[MembershipRules&lt;CrewMember&gt;]</c> (<see cref="ProjectRules"/>). The permissions are not declared here: Tenancy
/// brings its own (<see cref="TenancyPermissions"/>), and every other module states its permissions next to the code that asks for
/// them, once, on the list it marks with <c>[TenancyPermissions]</c>. Tenancy's generator collects those lists into
/// the host, as <c>TenancyPermissionsOfModules</c>, and the export finds the same marked lists in the modules it
/// references, so neither names a module's permissions, and a module that is added changes nothing here. The starter roles only
/// name them. The catalogue is built and checked once, when the host starts, and a catalogue that does not hold
/// together stops the start.
/// <para>
/// There are two administrators' starter roles, each seeded for one shape of tenant (<see cref="StarterRole.SeededFor"/>),
/// and a tenant's first seat is granted the one seeded for its shape. A flat tenant has one unit and usually a
/// handful of people, so its administrator does everything: <see cref="TenantAdmin"/> lists no permissions, and once
/// built holds every live permission of the catalogue, the modules' included. A hierarchical tenant separates running
/// access from doing the work: <see cref="AccessAdmin"/> lists its permissions, Tenancy's own and the ones that manage
/// access, so its holder gives every role, names owners, manages crews and sees every project, and renames,
/// closes and records on none. The catalogue refuses a listing administrators' starter role that leaves out a permission that
/// manages access, so marking another permission stops the host at start-up until this starter role lists it. Either way nobody
/// but system work is needed to appoint, say, an area manager.
/// </para>
/// <para>
/// There are two kinds of role, each kept where it belongs. A role of the organization is Tenancy's, copied from a
/// starter role here: granted at a unit, it reaches every project at that unit and below it. A project role is the
/// Projects module's, made from a starter role here (<see cref="ProjectRoles"/>): given on a project's crew, it
/// counts on that project only, and gives only the permissions that act on a project. The crew lead's holds every one of
/// them, and a project's owner holds them by owning the project, so the owner may do everything that acts on it.
/// Opening projects, and so moving one to another unit, naming an owner and managing the organization take a role
/// at a unit.
/// </para>
/// <para>
/// An area manager is appointed at an area of the organization. A crew lead, a surveyor and an observer are
/// usually project roles, given on a crew, and are starter roles of the organization as well, for a tenant that grants
/// one at a unit: a surveyor at a site records on every project there. The area manager's and the access admin's
/// starter roles are seeded for a hierarchical tenant only: a flat one has nothing but its root to manage, and gets them
/// when it changes shape. The other starter roles name no shape, so every tenant gets them.
/// </para>
/// <para>
/// Tenant admin, Access admin, Area manager, Crew lead and People office manage access. Two of Projects' permissions are marked as
/// managing access: naming a project's owner, because the owner leads the crew until the organization names
/// another, and managing a crew, because whoever holds it at a unit may put anyone on every crew there, in any
/// role, themselves included. So a role at a unit that holds either is given only by someone who holds it there
/// for at least as long, and never to oneself; whoever manages grants and holds neither gives Surveyor and
/// Observer. The marks rule the organization's grants only. On a project's crew a role reaches that project
/// alone (<see cref="ProjectMembership"/>), and whoever manages the crew gives any project role, the crew lead's
/// included; the owner holds none, since it holds what an owner does by owning the project.
/// </para>
/// <para>
/// That a role which manages access is given only by someone who holds its permissions is containment, which a catalogue
/// keeps unless the application turns <see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/> off. The sample
/// leaves it on, and so writes nothing for it: who may give which role is no rule of its own product. Its database,
/// which only its handlers reach, holds the rule again as a second lock behind every query of the sample's: it
/// refuses a seat a role that manages access at a unit where the seat lacks that role's permissions, any such role to
/// itself, such an invitation, and stopping a seat whose such grants it could not take away; how long a grant runs,
/// a change of such a permission in a role, and moves stay with the use cases. An application that lets somebody
/// earn a role that manages access, by passing a course say, keeps it on as well: its own handler checks the
/// course, and gives the role as Tenancy's system work in the tenant, which containment does not hold, for the
/// calling seat and with the role the course names, never one the request does.
/// </para>
/// </remarks>
public static class SampleCatalogue
{
    /// <summary>
    /// The key of the administrators' starter role of a flat tenant, which its first seat is granted at the root: every
    /// permission there is.
    /// </summary>
    public const string TenantAdmin = "tenant-admin";

    /// <summary>
    /// The key of the administrators' starter role of a hierarchical tenant, which its first seat is granted at the root:
    /// Tenancy's permissions and the permissions that manage access, and of the application's work only seeing projects.
    /// </summary>
    public const string AccessAdmin = "access-admin";

    /// <summary>The key of the starter role for running an area of the organization: its projects, units, seats and grants.</summary>
    public const string AreaManager = "area-manager";

    /// <summary>
    /// The key of the starter role for leading a project's crew, and of the starter project role of the same name: a crew
    /// member in it holds what a project's owner holds by owning it, and records inspections besides.
    /// </summary>
    public const string CrewLead = "crew-lead";

    /// <summary>The key of the starter role for working on a project's site and recording what is found there, and of the starter project role of the same name.</summary>
    public const string Surveyor = "surveyor";

    /// <summary>The key of the starter role for looking at a project and nothing more, and of the starter project role of the same name.</summary>
    public const string Observer = "observer";

    /// <summary>
    /// The key of the starter role for giving people their roles, and nothing else: whoever holds it at a unit gives any
    /// role that manages no access to anyone placed there or below, without holding the role's permissions.
    /// </summary>
    public const string PeopleOffice = "people-office";

    /// <summary>
    /// The project roles every tenant's crews start with, made once when a tenant is set up: the crew lead's, which
    /// a former owner usually stays on the crew in, a surveyor's, who records inspections, and an observer's. The tenant's own to
    /// rename, give other permissions and archive afterwards, and to add to. Each gives the permissions that act on a project, of whichever
    /// module asks them, which is why the application says them and not the Projects module.
    /// </summary>
    public static IReadOnlyList<StarterProjectRole> ProjectRoles { get; } =
    [
        new(CrewLead, "Crew lead", "Leads a project crew", [ProjectPermissionNames.View, ProjectPermissionNames.Edit, ProjectPermissionNames.Close, ProjectPermissionNames.ManageCrew, InspectionPermissionNames.Record]),
        new(Surveyor, "Surveyor", "Records inspections", [ProjectPermissionNames.View, InspectionPermissionNames.Record]),
        new(Observer, "Observer", "Looks", [ProjectPermissionNames.View]),
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
    /// The application's starter roles, and the permissions of a module it marks as managing access: naming a project's owner and
    /// managing a crew, which Projects declares. What kind of unit a unit is, a company, a region, an area or a site,
    /// is not the catalogue's: no access rule reads it, so it is a field of the application's own unit class.
    /// </summary>
    /// <remarks>
    /// Marked, so the export writes Tenancy's policies from the catalogue the host runs with: this part, which the host
    /// hands Tenancy's registration, and every list of permissions the modules mark, which Tenancy's generator collects into
    /// the host and the export finds in the modules it references. On Postgres the host's start-up check compares the
    /// database's functions with the catalogue the host runs with: policies written from another catalogue would
    /// contain other grants than the application gives.
    /// </remarks>
    [TenancyCatalogue]
    public static ApplicationCatalogue Application { get; } = new(
        StarterRoles:
        [
            new(TenantAdmin, "Tenant admin", "Runs the tenant", [], SeededFor: TenantShape.Flat, Administers: true, Order: 10),
            new(
                AccessAdmin,
                "Access admin",
                "Runs access across the organization",
                [
                    .. TenancyPermissions.All.Select(permission => permission.Name),
                    ProjectPermissionNames.ChangeOwner, ProjectPermissionNames.ManageCrew, ProjectPermissionNames.View,
                ],
                SeededFor: TenantShape.Hierarchical,
                Administers: true,
                Order: 10),
            new(
                AreaManager,
                "Area manager",
                "Runs an area of the organization",
                [
                    ProjectPermissionNames.View, ProjectPermissionNames.Open, ProjectPermissionNames.Edit, ProjectPermissionNames.Close, ProjectPermissionNames.ManageCrew, ProjectPermissionNames.ChangeOwner,
                    InspectionPermissionNames.Record,
                    TenancyPermissions.UnitsManage, TenancyPermissions.SeatsManage, TenancyPermissions.GrantsManage,
                ],
                SeededFor: TenantShape.Hierarchical,
                Order: 20),
            new(
                CrewLead,
                "Crew lead",
                "Leads a project crew",
                [ProjectPermissionNames.View, ProjectPermissionNames.Edit, ProjectPermissionNames.Close, ProjectPermissionNames.ManageCrew, InspectionPermissionNames.Record],
                Order: 30),
            new(Surveyor, "Surveyor", "Records inspections", [ProjectPermissionNames.View, InspectionPermissionNames.Record], Order: 40),
            new(Observer, "Observer", "Looks", [ProjectPermissionNames.View], Order: 50),
            new(PeopleOffice, "People office", "Gives people their roles", [TenancyPermissions.GrantsManage], Order: 60),
        ],
        AccessManagingPermissions: [ProjectPermissionNames.ChangeOwner, ProjectPermissionNames.ManageCrew]);
}
