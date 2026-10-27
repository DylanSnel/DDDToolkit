using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.Access;
using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.Events;
using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects;

namespace Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles;

/// <summary>
/// A role a tenant gives on a project's crew: a name, what it is for, and the permissions it gives on the one project it
/// is held on. An aggregate of its own, beside the project, made by the tenant and kept by this module.
/// </summary>
/// <remarks>
/// There are two kinds of role in this application, each kept where it belongs. A role of the organization is
/// Tenancy's: given at a unit, it counts for every project there and below. A project role is this one: given on
/// one project's crew, it counts there and nowhere else. A tenant starts with the starter roles the application
/// declares (a crew lead's, a surveyor's, an observer's), made once when the tenant is set up, and makes, renames,
/// archives and gives other permissions to its own from then on.
/// <para>
/// Declared with the Membership package's role template, which keeps what a role is: its name and description,
/// its permissions, chosen from what the rules let a crew role give, whether it is archived, and which starter role it
/// was made from, so a starter role is made once whatever the tenant has called it since. The tenant is this
/// application's column, set here and kept to by Tenancy's own rule, like every row of a tenant. The rules are
/// handed in, never kept: a role is a row, and the rules are the module's one declaration of what a crew role may
/// give and of the codes it refuses under.
/// </para>
/// <para>
/// The package raises no events. The role calls the hooks below after a change that changed something, and they raise
/// the module's own, so a change that changes nothing raises nothing.
/// </para>
/// </remarks>
[CustomRole<ProjectRoleId, Project>]
public sealed partial class ProjectRole
{
    /// <summary>
    /// Makes a project role of a tenant: one the tenant makes for itself, or one of the starter roles, when the
    /// draft says which it was made from.
    /// </summary>
    /// <param name="id">Its id.</param>
    /// <param name="tenantId">The tenant it is a role of, for good.</param>
    /// <param name="draft">Its name, what it is for, its permissions, and the starter role it is made from, if any.</param>
    /// <param name="rules">The rules of the projects' crews, which the permissions are held to.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.role-name-invalid</c>, naming the field; <c>projects.permission-not-for-members</c>, naming the permissions
    /// no crew role may give.
    /// </exception>
    public ProjectRole(ProjectRoleId id, TenantId tenantId, CustomRoleDraft draft, MembershipRules rules) : base(id, draft, rules)
    {
        TenantId = tenantId;
        RaiseDomainEvent(new ProjectRoleMade(id, tenantId));
    }

    /// <summary>The tenant the role is of. It never changes; marked <c>[OfTenant]</c>, so the tenant filter reads it.</summary>
    [OfTenant]
    public TenantId TenantId { get; private set; }

    /// <summary>
    /// The role was renamed, or described again: the hook the role calls after a change that changed something. The
    /// project role manager, <c>ProjectRoleManager</c>, makes the change.
    /// </summary>
    partial void OnRenamed() => RaiseDomainEvent(new ProjectRoleRenamed(Id));

    /// <summary>The role gives other permissions from now on, on every crew it is held on.</summary>
    partial void OnPermissionsSet(RolePermissionsSet set) => RaiseDomainEvent(new ProjectRolePermissionsChanged(Id));

    /// <summary>The role was archived: it gives nothing from then on, and stays on the crews that hold it.</summary>
    partial void OnArchived() => RaiseDomainEvent(new ProjectRoleArchived(Id));
}
