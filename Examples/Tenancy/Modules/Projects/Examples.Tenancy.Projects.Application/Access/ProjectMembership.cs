using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Projects.Contracts.RowAccess;

namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// Who holds which key on a project through its crew, as one declaration: the rules of the Membership package
/// for this module's projects, which the access checks in C#, the database's functions and the export all read,
/// with the starter roles every tenant's project roles are made from.
/// </summary>
/// <remarks>
/// A project's crew are seats of the tenant, so who the caller is as a member is the seat Tenancy resolved for
/// the request, and the database asks Tenancy's function for it. The roles a crew member holds are this module's
/// own, <c>ProjectRole</c>s a tenant makes for itself, kept by the package. And a key held at the unit a project
/// hangs at, or above it, reaches the project: an organization role at North sees and acts on every project
/// there. So the rules say each of the three, and name the functions of Tenancy's that answer them.
/// <list type="bullet">
/// <item><b>The keys of a project.</b> Its owner holds them by owning it: seeing it, editing it, closing it and
/// managing its crew, the keys of a crew lead. Opening a project and naming its owner are not among them: the
/// first is asked at a unit, where there is no project yet, and the second is the organization's.</item>
/// <item><b>What a crew role may give.</b> Every key that acts on a project, whichever module asks it
/// (<see cref="ProjectCatalogue.CrewGives"/>): never opening a project, naming an owner or managing the
/// organization. A project role that holds such a key gives nothing with it, so no crew role reaches past its
/// project.</item>
/// <item><b>The keys the database holds the crew and the owner to.</b> A row knows no command, so the rules
/// name the keys the commands require: managing the crew, for whoever writes a crew member's row or a role
/// held on it, and naming the owner, for whoever changes the owner column. The package writes the lock from
/// them; the commands still decide who may.</item>
/// <item><b>The application's own work.</b> System work in a tenant, Tenancy's own and this module's, holds every
/// key on every project of it: the seeder opens projects and puts seats on their crews, and a seat that gives up
/// its own place on a crew is saved as this module's work for it. Work in any other scope holds nothing on a
/// project.</item>
/// </list>
/// Row access rules ask the projects a seat sees and those it holds a key on by the project's id, through
/// <see cref="ProjectsISee"/> and <see cref="ProjectsWhereIHold"/>, this module's rules and other modules' alike,
/// and the export writes their policies with whatever names these rules give the functions. The rules keep the
/// names this sample's database already has (<see cref="Functions"/>).
/// <para>
/// One instance for the application, made where the module is added, from the starter roles the host declares,
/// and registered as it is: the handlers read the rules from it. The program that exports the database's
/// functions makes the rules from the same starter roles, and the host's start-up check holds the database to the
/// rules it runs with.
/// </para>
/// </remarks>
public sealed class ProjectMembership
{
    /// <summary>The rules' name: what the codes are prefixed with, and the owner of the functions' logical names.</summary>
    public const string Name = "projects";

    /// <summary>
    /// The starter role a project's owner holds on its crew: the crew lead's. The tenant's role made from it is
    /// the owner's, whatever the tenant has called it since, and it is not archived.
    /// </summary>
    public const string CrewLead = "crew-lead";

    /// <summary>
    /// The scope of this module's own system work, <c>Caller.SystemIn("projects")</c>: the save in which a seat gives
    /// up its own place on a crew, which the command has checked already.
    /// </summary>
    public const string Scope = "projects";

    /// <summary>
    /// The four functions under the names this sample's database has had since before the Membership package took
    /// them from the resource's name: the projects whose crew the caller is on, those where a role it holds there
    /// gives a key, those it sees, and those it holds a key on.
    /// </summary>
    /// <remarks>
    /// Kept on purpose, and said here alone: the access files of Projects and Inspections create and ask these four,
    /// and a new name would leave the old functions in a database until Projects' next access file. No rule names
    /// them, since rules ask by the project's id. An application without such a database leaves <c>functions:</c> out
    /// of its rules, and its functions are <c>projects_as_member</c>, <c>projects_as_member_with</c>,
    /// <c>projects_i_see</c> and <c>projects_where_i_hold</c>.
    /// </remarks>
    public static MembershipFunctions Functions { get; } = new("crew_member_project_ids", "crew_project_ids", "project_ids_i_see", "project_ids_where_i_hold");

    /// <summary>The projects' rules, with <paramref name="starterRoles"/> as the roles every tenant starts with.</summary>
    /// <param name="starterRoles">
    /// The starter roles the application declares: one of them <see cref="CrewLead"/>, every key of each one a
    /// crew role may give.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The starter roles name no <see cref="CrewLead"/>, have one key or one name twice, or one of them lists a key
    /// twice or a key a crew role may not give.
    /// </exception>
    public ProjectMembership(IReadOnlyList<StarterProjectRole> starterRoles)
    {
        ArgumentNullException.ThrowIfNull(starterRoles);

        if (starterRoles.SelectMany(role => role.Keys).FirstOrDefault(key => !ProjectCatalogue.CrewGives(key)) is { } beyond)
        {
            throw new ArgumentException(
                "A starter project role lists '" + beyond + "', which no crew role gives: opening a project, naming its owner and managing the organization are held at a unit.",
                nameof(starterRoles));
        }

        // A name is used once among a tenant's project roles, which an index holds where they are stored. Two
        // starter roles of one name would be refused there, when the first tenant is set up, and not here, where
        // the application declares them.
        if (starterRoles.GroupBy(role => role.Name, StringComparer.Ordinal).FirstOrDefault(named => named.Count() > 1) is { } twice)
        {
            throw new ArgumentException(
                "Two starter project roles are called '" + twice.Key + "'. A tenant's project roles each have a name of their own, and the starter roles are its first ones.",
                nameof(starterRoles));
        }

        StarterRoles = [.. starterRoles];
        Rules = new MembershipRules(
            Name,
            keys: ProjectCatalogue.LeadKeys,
            roles: [.. starterRoles.Select(role => new DeclaredRole(role.Key, role.Keys))],
            members: MemberSource.Resolved(TenancyRowAccess.Owner + "/caller_seat"),
            seeKey: ProjectKeys.View,
            ownerRole: CrewLead,
            memberKeys: MemberKeys.AllBut([.. ProjectCatalogue.OrganizationKeys, .. TenancyKeys.Permissions.Select(permission => permission.Key)]),
            codes: ProjectRefusals.Membership,
            functions: Functions,
            above: new ReachFromAbove(TenancyRowAccess.Owner + "/units_where_i_hold"),
            rolesKept: true,
            changeMembersKey: ProjectKeys.ManageCrew,
            changeOwnerKey: ProjectKeys.ChangeOwner,
            systemScopes: [TenancyWork.SystemScope, Scope]);
    }

    /// <summary>The rules the projects are registered with.</summary>
    public MembershipRules Rules { get; }

    /// <summary>The starter roles, with the names and descriptions a tenant's roles made from them first have.</summary>
    public IReadOnlyList<StarterProjectRole> StarterRoles { get; }
}
