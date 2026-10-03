using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.Access;
using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.Events;
using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects;

namespace Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles;

/// <summary>
/// A role a tenant gives on a project's crew: a name, what it is for, and the keys it gives on the one project it
/// is held on. An aggregate of its own, beside the project, made by the tenant and kept by this module.
/// </summary>
/// <remarks>
/// There are two kinds of role in this application, each kept where it belongs. A role of the organization is
/// Tenancy's: given at a unit, it counts for every project there and below. A project role is this one: given on
/// one project's crew, it counts there and nowhere else. A tenant starts with the starter roles the application
/// declares (a crew lead's, a surveyor's, an observer's), made once when the tenant is set up, and makes, renames,
/// re-keys and archives its own from then on.
/// <para>
/// Declared with the Membership package's role template, which keeps what a role is: its name and description,
/// its keys, chosen from what the rules let a crew role give, whether it is archived, and which starter role it
/// was made from, so the crew lead's role is found whatever the tenant has called it since. The tenant is this
/// application's column, set here and kept to by Tenancy's own rule, like every row of a tenant. The rules are
/// handed in, never kept: a role is a row, and the rules are the module's one declaration of what a crew role may
/// give and of the codes it refuses under.
/// </para>
/// <para>
/// The package raises no events, and answers what each change did instead; the methods here raise the module's
/// own from that, so a change that changes nothing raises nothing.
/// </para>
/// </remarks>
[KeptRole<ProjectRoleId, Project>]
public sealed partial class ProjectRole
{
    /// <summary>
    /// Makes a project role of a tenant: one the tenant makes for itself, or one of the starter roles, when the
    /// draft says which it was made from.
    /// </summary>
    /// <param name="id">Its id.</param>
    /// <param name="tenantId">The tenant it is a role of, for good.</param>
    /// <param name="draft">Its name, what it is for, its keys, and the starter role it is made from, if any.</param>
    /// <param name="rules">The rules of the projects' crews, which the keys are held to.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.role-name-invalid</c>, naming the field; <c>projects.key-not-for-members</c>, naming the keys
    /// no crew role may give.
    /// </exception>
    public ProjectRole(ProjectRoleId id, TenantId tenantId, KeptRoleDraft draft, MembershipRules rules) : base(id, draft, rules)
    {
        TenantId = tenantId;
        RaiseDomainEvent(new ProjectRoleMade(id, tenantId));
    }

    /// <summary>The tenant the role is of. It never changes; the tenant filter reads it.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>Gives the role another name, or another description, or both. The same again changes nothing.</summary>
    /// <param name="name">Its name, as it is to be.</param>
    /// <param name="description">What it is for, as it is to be; <see langword="null"/> clears it.</param>
    /// <param name="rules">The rules of the projects' crews.</param>
    /// <exception cref="Exceptions.RefusalException"><c>projects.role-archived</c>, <c>projects.role-name-invalid</c>.</exception>
    public void RenameRole(string name, string? description, MembershipRules rules)
    {
        if (Rename(name, description, rules))
        {
            RaiseDomainEvent(new ProjectRoleRenamed(Id));
        }
    }

    /// <summary>
    /// Has the role give these keys from now on, on every crew it is held on. The same keys again change nothing.
    /// </summary>
    /// <param name="keys">Every key it is to give.</param>
    /// <param name="rules">The rules of the projects' crews, which say what a crew role may give.</param>
    /// <exception cref="Exceptions.RefusalException"><c>projects.role-archived</c>, <c>projects.key-not-for-members</c>.</exception>
    public void SetRoleKeys(IReadOnlyCollection<string> keys, MembershipRules rules)
    {
        if (SetKeys(keys, rules).Changed)
        {
            RaiseDomainEvent(new ProjectRoleKeysChanged(Id));
        }
    }

    /// <summary>
    /// Archives the role: it gives nothing from then on and is given to nobody, and stays on the crews that hold
    /// it, so what they held still has a name. The crew lead's role, which every owner holds, is not archived.
    /// </summary>
    /// <param name="rules">The rules of the projects' crews, which name the owner's role.</param>
    /// <exception cref="Exceptions.RefusalException"><c>projects.role-archived</c>, <c>projects.owner-role-stays</c>.</exception>
    public void TakeOutOfUse(MembershipRules rules)
    {
        Archive(rules);
        RaiseDomainEvent(new ProjectRoleArchived(Id));
    }
}
