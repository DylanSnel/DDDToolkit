using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// A tenant's role: a name, and the permissions it grants wherever it is granted. The application
/// declares its own class with <see cref="RoleAggregateAttribute{TRoleId}"/>.
/// <para>
/// Its permissions come from the catalogue only, with the permissions they imply added, and are kept distinct and sorted.
/// A permission the catalogue later retires, or no longer knows at all, may stay on the role; it grants nothing,
/// and is only refused when someone adds it anew. A role made from a starter role remembers which one in
/// <see cref="FromStarterRole"/>, and only when it is made.
/// </para>
/// <para>
/// A role made from a starter role follows it (<see cref="FollowStarterRole{TSeatId}"/>): it remembers what the starter role gave it
/// (<see cref="PermissionsFromStarterRole"/>), so a permission the starter role gains later is added to it, a permission the starter role loses is taken out,
/// and what the tenant changed itself stays as the tenant left it. The application runs that for every tenant
/// with <c>services.SyncStarterRoles()</c>; nothing else changes a role behind the tenant's back.
/// </para>
/// <para>
/// Whoever holds <see cref="TenancyPermissions.RolesManage"/> may put any permission into any role. That is on purpose:
/// that permission is the administrator's permission, and an administrator can give themselves anything already. A permission
/// added to a role reaches every seat that holds the role, seats that gave themselves the role included, so
/// the use case lets an administrator alone add or take out a permission that manages access, or archive a role that
/// holds one.
/// </para>
/// <para>
/// Every method that changes the role takes who makes the change, <c>by</c>, and puts it on the event it
/// raises. The use cases pass the actor of their caller. A role is declared without the seat class and does not
/// know the seat id's type, so the methods take it: it follows from a caller's actor
/// (<c>role.Archive(caller.Actor)</c>), and code that names nobody, an import or a test, names the type instead
/// (<c>role.Archive&lt;SeatId&gt;()</c>).
/// </para>
/// </summary>
/// <typeparam name="TRoleId">The application's role id.</typeparam>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
[AggregateRootBase]
public abstract partial class RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
{
    /// <summary>The longest name a role may have.</summary>
    public const int MaxNameLength = TenancyNames.MaxRoleNameLength;

    /// <summary>The longest description a role may have.</summary>
    public const int MaxDescriptionLength = TenancyNames.MaxRoleDescriptionLength;

    /// <summary>The tenant the role belongs to.</summary>
    public TTenantId TenantId { get; private set; }

    /// <summary>The role's name, unique in its tenant.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What the role is for.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The key of the starter role the role was copied from, or <see langword="null"/> for a role made by hand. Set when it is made.</summary>
    public string? FromStarterRole { get; private set; }

    /// <summary>Whether the role is in use. An archived role grants nothing.</summary>
    public RoleStatus Status { get; private set; }

    /// <summary>The permissions the role grants: expanded, distinct and in ordinal order.</summary>
    public IReadOnlyList<string> Permissions { get; private set; } = [];

    /// <summary>
    /// The permissions its starter role gave the role, as the starter role held them when the role was made from it or last followed it:
    /// what the next <see cref="FollowStarterRole{TSeatId}"/> compares the starter role with, to tell a permission the starter role gained or
    /// lost from one the tenant added or took out itself. Expanded, distinct and in ordinal order: the starter role's permissions,
    /// whatever permissions the role was made with. <see langword="null"/> for a role made by hand, and for one made before
    /// roles remembered it or from a starter role the catalogue did not have then, which follows its starter role as if the starter role had
    /// given it nothing yet.
    /// </summary>
    public IReadOnlyList<string>? PermissionsFromStarterRole { get; private set; }

    /// <summary>This role's status and permissions as a snapshot, for a seat's rules.</summary>
    public RoleFacts Facts => new(Status == RoleStatus.Active, Permissions);

    /// <summary>
    /// What a constructor would do: gives a new instance its id, tenant, name, description and permissions, and
    /// remembers the starter role it came from, with that starter role's permissions as the catalogue builds it as what the starter role gave it;
    /// starts it active, and raises <see cref="RoleCreated{TTenantId, TRoleId, TSeatId}"/>. Called once, by
    /// <see cref="TenancyInstances"/>, right after the instance is made.
    /// <para>
    /// The record is the starter role's, not the draft's: provisioning drafts exactly the starter role, but an import or a seeding
    /// may make a role from a starter role with permissions of its own, and those are the tenant's, which a later sync leaves as
    /// they are. A draft that names a starter role the catalogue does not have is remembered with no record, as a role
    /// stored before roles remembered their starter role's permissions is.
    /// </para>
    /// </summary>
    internal void InitializeNew<TSeatId>(TRoleId id, TTenantId tenantId, RoleDraft draft, TenancyCatalogue catalogue, TenancyActor<TSeatId>? by)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(catalogue);

        var name = TenancyNames.Required(draft.Name, TenancyNames.RoleNameToken, MaxNameLength);
        var description = TenancyNames.Optional(draft.Description, TenancyNames.RoleDescriptionToken, MaxDescriptionLength);
        var permissions = catalogue.Expand(draft.Permissions ?? []);

        Id = id;
        TenantId = tenantId;
        Name = name;
        Description = description;
        Permissions = permissions;
        FromStarterRole = string.IsNullOrWhiteSpace(draft.FromStarterRole) ? null : draft.FromStarterRole.Trim();
        PermissionsFromStarterRole = StarterRolePermissions(FromStarterRole, catalogue);
        Status = RoleStatus.Active;

        RaiseDomainEvent(new RoleCreated<TTenantId, TRoleId, TSeatId>(tenantId, id, FromStarterRole, by));
    }

    /// <summary>Renames the role and describes it again. The same name and description change nothing and raise nothing.</summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: a role does not know it by itself.</typeparam>
    /// <param name="name">Its new name.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.role-archived</c>, <c>tenancy.name-invalid</c>.</exception>
    public void Rename<TSeatId>(string name, string description, TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        RequireActive();

        var validName = TenancyNames.Required(name, TenancyNames.RoleNameToken, MaxNameLength);
        var validDescription = TenancyNames.Optional(description, TenancyNames.RoleDescriptionToken, MaxDescriptionLength);
        if (validName == Name && validDescription == Description)
        {
            return;
        }

        Name = validName;
        Description = validDescription;
        RaiseDomainEvent(new RoleRenamed<TTenantId, TRoleId, TSeatId>(TenantId, Id, by));
    }

    /// <summary>
    /// Sets the permissions the role grants. A permission the role already holds that the catalogue has since retired, or
    /// no longer knows, may stay as it is; every other permission must be a live permission of the catalogue, and the permissions
    /// it implies are added. The same set again changes nothing and raises nothing. A change raises
    /// <see cref="RolePermissionsChanged{TTenantId, TRoleId, TSeatId}"/> with the permissions that came in and the permissions that
    /// went out, the implied ones included.
    /// </summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: a role does not know it by itself.</typeparam>
    /// <param name="permissions">Every permission the role grants from now on.</param>
    /// <param name="catalogue">The catalogue the permissions come from.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException">
    /// <c>tenancy.role-archived</c>, <c>tenancy.unknown-permission</c> for a permission that is new to the role and
    /// not live.
    /// </exception>
    public void SetPermissions<TSeatId>(IReadOnlyCollection<string> permissions, TenancyCatalogue catalogue, TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        var next = PermissionsAfterSetting(permissions, catalogue);
        var before = Permissions;
        if (next.SequenceEqual(before, StringComparer.Ordinal))
        {
            return;
        }

        Permissions = next;

        // Both lists are distinct and in ordinal order, as the role's permissions are, so what is left of each is too.
        RaiseDomainEvent(new RolePermissionsChanged<TTenantId, TRoleId, TSeatId>(
            TenantId,
            Id,
            next.Except(before, StringComparer.Ordinal).ToArray(),
            before.Except(next, StringComparer.Ordinal).ToArray(),
            by));
    }

    /// <summary>
    /// The permissions <see cref="SetPermissions{TSeatId}"/> would leave the role with, refused the same way, and nothing changed:
    /// a use case decides on the role's future before it changes it.
    /// </summary>
    internal IReadOnlyList<string> PermissionsAfterSetting(IReadOnlyCollection<string> permissions, TenancyCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(catalogue);
        RequireActive();

        var kept = new List<string>();
        var added = new List<string>();
        foreach (var permission in permissions)
        {
            var trimmed = permission?.Trim() ?? string.Empty;
            if (!catalogue.IsLive(trimmed) && Permissions.Contains(trimmed, StringComparer.Ordinal))
            {
                kept.Add(trimmed);
            }
            else
            {
                added.Add(trimmed);
            }
        }

        return kept
            .Concat(catalogue.Expand(added))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Follows the starter role the role was made from, as <paramref name="catalogue"/> builds it now. What the starter role gave
    /// the role the last time (<see cref="PermissionsFromStarterRole"/>) is compared with what the starter role holds now: a permission the starter role
    /// gained is added to the role, and a permission it lost is taken out. What the tenant changed itself stays: a permission it
    /// took out of the role stays out while the starter role still holds it, and a permission it added stays while the starter role never
    /// held it. The starter role's permissions are remembered for the next time.
    /// <para>
    /// Four things the comparison cannot tell apart are settled this way. A permission the tenant added by hand that the
    /// starter role gains later is the starter role's from then on, and goes when the starter role loses it. A permission the tenant took out that
    /// the starter role loses, and gains again later, comes back as a permission the starter role gained: once the starter role lost it, the record
    /// no longer holds it, and the event names it, among the permissions that manage access when it manages access. A permission
    /// that is not live, retired or no longer known, is never taken out: it grants nothing, and retiring a permission
    /// changes no role. And a role that has no record yet, made before roles remembered their starter role's permissions, follows
    /// as if the starter role had given it nothing: it gets every permission the starter role holds that it lacks, and loses none. A live
    /// permission the role keeps brings the permissions it implies, as everywhere a role's permissions are set.
    /// </para>
    /// <para>
    /// A permission that manages access follows the same rule, whoever runs this: only the application's code puts a permission
    /// in a starter role. The event says which of the permissions that came in or went out manage access, so whoever is told
    /// about the change, the tenant's administrators say, can see it.
    /// </para>
    /// </summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: a role does not know it by itself.</typeparam>
    /// <param name="catalogue">The catalogue the starter role is read from, as the application runs with it now.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <returns>
    /// Whether the role changed: its permissions, which raises <see cref="RoleFollowedItsStarterRole{TTenantId, TRoleId, TSeatId}"/>,
    /// or only what it remembers of its starter role, or the order its permissions were stored in, which raises nothing.
    /// <see langword="false"/> for a role made by hand, one whose starter role the catalogue no longer has, and one that
    /// follows its starter role already.
    /// </returns>
    /// <exception cref="RefusalException"><c>tenancy.role-archived</c>: an archived role follows nothing.</exception>
    public bool FollowStarterRole<TSeatId>(TenancyCatalogue catalogue, TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        if (PermissionsAfterFollowing(catalogue) is not { } following)
        {
            return false;
        }

        var remembered = PermissionsFromStarterRole is { } record && record.SequenceEqual(following.Remembered, StringComparer.Ordinal);
        PermissionsFromStarterRole = following.Remembered;

        var before = Permissions;
        var sorted = following.Permissions.SequenceEqual(before, StringComparer.Ordinal);
        Permissions = following.Permissions;

        // The permissions are compared as sets: a row may hold them in another order than the role keeps them, a seat's copy
        // of a starter role for one, and only a permission that came in or went out is a change the event tells.
        var added = following.Permissions.Except(before, StringComparer.Ordinal).ToArray();
        var removed = before.Except(following.Permissions, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (added.Length == 0 && removed.Length == 0)
        {
            return !remembered || !sorted;
        }

        RaiseDomainEvent(new RoleFollowedItsStarterRole<TTenantId, TRoleId, TSeatId>(
            TenantId,
            Id,
            FromStarterRole!,
            added,
            removed,
            added.Concat(removed).Where(catalogue.ManagesAccess).Order(StringComparer.Ordinal).ToArray(),
            by));
        return true;
    }

    /// <summary>
    /// The permissions <see cref="FollowStarterRole{TSeatId}"/> would leave the role with, and what it would remember of its starter role,
    /// refused the same way and nothing changed: a use case decides on the role's future before it changes it.
    /// <see langword="null"/> for a role with no starter role to follow: made by hand, or made from a starter role the catalogue no
    /// longer has.
    /// </summary>
    internal (IReadOnlyList<string> Permissions, IReadOnlyList<string> Remembered)? PermissionsAfterFollowing(TenancyCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        RequireActive();

        if (StarterRolePermissions(FromStarterRole, catalogue) is not { } now)
        {
            return null;
        }

        var given = PermissionsFromStarterRole ?? [];

        var held = new HashSet<string>(Permissions, StringComparer.Ordinal);
        held.UnionWith(now.Except(given, StringComparer.Ordinal));
        held.ExceptWith(given.Except(now, StringComparer.Ordinal).Where(catalogue.IsLive));

        var permissions = held
            .Where(permission => !catalogue.IsLive(permission))
            .Concat(catalogue.Expand(held.Where(catalogue.IsLive)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return (permissions, now);
    }

    /// <summary>
    /// The permissions of the starter role <paramref name="starterRole"/> as <paramref name="catalogue"/> builds it, distinct and in ordinal
    /// order: what a role made from it remembers, whatever permissions it was made with. The starter role as built holds live permissions
    /// only, expanded; an administrators' starter role that lists none, every live permission. <see langword="null"/> for no starter role,
    /// or one the catalogue does not have.
    /// </summary>
    private static string[]? StarterRolePermissions(string? starterRole, TenancyCatalogue catalogue)
        => starterRole is null
            ? null
            : catalogue.StarterRoles.FirstOrDefault(candidate => string.Equals(candidate.Key, starterRole, StringComparison.Ordinal)) is { } built
                ? built.Permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                : null;

    /// <summary>Archives the role. It stays, granted wherever it was, and grants nothing from now on.</summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: a role does not know it by itself.</typeparam>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.role-archived</c>: it already is.</exception>
    public void Archive<TSeatId>(TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        RequireActive();

        Status = RoleStatus.Archived;
        RaiseDomainEvent(new RoleArchived<TTenantId, TRoleId, TSeatId>(TenantId, Id, by));
    }

    /// <summary>Whether the role holds <paramref name="permission"/>, live or not. Whether it grants it is another question: see <see cref="Status"/> and the catalogue.</summary>
    public bool Holds(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);

    private void RequireActive()
    {
        if (Status == RoleStatus.Archived)
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.RoleArchived);
        }
    }
}
