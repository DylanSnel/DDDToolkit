using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// Who holds which permission on a project through its crew, as one declaration: the rules of the Membership package
/// for this module's projects, which the access checks in C#, the database's functions and the export all read,
/// with the starter roles every tenant's project roles are made from.
/// </summary>
/// <remarks>
/// The rules are made through the project's type, <c>MembershipRules.For&lt;Project&gt;</c>, which says the rest:
/// the name <c>projects</c>, which the codes and the functions in the database follow, the codes the project
/// declares (<c>ProjectRefusals.Membership</c>), and the permissions on a project (<see cref="ProjectPermissions"/>).
/// What is left to say here is two things, and one that is said by nothing.
/// <list type="bullet">
/// <item><b>Where the roles come from.</b> A tenant makes its own project roles, <c>ProjectRole</c>s, from the
/// starter roles the host declares (<see cref="Roles.Custom"/>). A crew role gives the permissions on a project
/// that a role can, and permissions of other modules that act on a project, a surveyor's permission to record an inspection say:
/// never one of Tenancy's, which manage the organization, nor one Projects asks at a unit. A project role that holds
/// such a permission gives nothing with it, so no crew role reaches past its project.</item>
/// <item><b>Reach from above.</b> A permission held at the unit a project hangs at, its <c>[At]</c> property, or above it,
/// reaches the project: an organization role at North sees and acts on every project there, and the
/// organization names a project's owner that way alone. Tenancy's function answers where the caller holds a
/// permission.</item>
/// <item><b>Who the caller is as a member</b>, which is said by nothing: a crew member is a seat of the tenant,
/// so it is the seat Tenancy resolved for the request, and the database asks Tenancy's function for it.</item>
/// </list>
/// System work of this module, in the scope <see cref="Scope"/>, holds every permission on every project of its tenant,
/// which is what the rules say when they name no scope: the seeder opens projects and puts seats on their crews
/// in it, and a seat that gives up its own place on a crew is saved as this module's work for it. Work in any
/// other scope, Tenancy's own included, holds nothing on a project.
/// <para>
/// One instance for the application, made where the module is added, from the starter roles the host declares,
/// and registered as it is: the handlers read the rules from it. The program that exports the database's
/// functions makes the rules from the same starter roles, and the host's start-up check holds the database to the
/// rules it runs with.
/// </para>
/// </remarks>
public sealed class ProjectMembership
{
    /// <summary>
    /// The scope of this module's own system work, <c>Caller.SystemIn("projects")</c>: the name of the module as a
    /// scope is written, which is the scope the projects' rules let through when they name none. The seeder works in
    /// it, and so does the save in which a seat gives up its own place on a crew, which the command has checked
    /// already.
    /// </summary>
    public const string Scope = "projects";

    /// <summary>The projects' rules, with <paramref name="starterRoles"/> as the roles every tenant starts with.</summary>
    /// <param name="starterRoles">
    /// The starter roles the application declares: the permissions of each one are permissions a crew role can give, or permissions
    /// of other modules that act on a project.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The starter roles have one permission or one name twice, or one of them lists a permission twice or a permission a crew role may
    /// not give.
    /// </exception>
    public ProjectMembership(IReadOnlyList<StarterProjectRole> starterRoles)
    {
        ArgumentNullException.ThrowIfNull(starterRoles);

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
        Rules = MembershipRules.For<Project>(
            Roles.Custom(
                starters: [.. starterRoles.Select(role => new DeclaredRole(role.Key, role.Permissions))],
                otherPermissions: OtherPermissions.AllBut([.. TenancyPermissions.All.Select(permission => permission.Name)])),
            reachFromAbove: true);
    }

    /// <summary>The rules the projects are registered with.</summary>
    public MembershipRules Rules { get; }

    /// <summary>The starter roles, with the names and descriptions a tenant's roles made from them first have.</summary>
    public IReadOnlyList<StarterProjectRole> StarterRoles { get; }
}
