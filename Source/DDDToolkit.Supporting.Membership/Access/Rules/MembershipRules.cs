using System.ComponentModel;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// The rules of access through the members of one kind of resource, as one declaration: the resource's permissions,
/// where the roles come from, who the caller is as a member, whether something above the resource reaches it, and
/// whose system work holds everything on it.
/// <code>
/// public static MembershipRules Rules { get; } = MembershipRules.For&lt;Board&gt;(
///     Roles.Custom(starters: [new("editor", [BoardPermissions.View, BoardPermissions.Edit, BoardPermissions.Comment])]));
/// </code>
/// <para>
/// Made through the resource's type, which says its name and its codes (<see cref="IMembershipResource"/>), and from
/// the permissions declared on it, the constants of a <c>[Permissions]</c> class marked
/// <see cref="OnAttribute{TResource}"/>: the owner holds those marked <c>Owner</c>, every member the one marked
/// <c>EveryMember</c>, and a role those marked <c>InRoles</c>. So nothing of the resource is said twice.
/// </para>
/// <para>
/// They say who holds a permission: the owner by owning the resource, a member by being one and through its roles.
/// They do not say who may add a member, give or take a role, or hand the resource to another owner. That is the
/// application's to decide, command by command: each of its commands requires what the application chooses,
/// usually one of the resource's permissions held on it, and whatever further rule it has it writes itself.
/// </para>
/// <para>
/// It is data, checked once when it is made, so everything that decides access reads the same thing: the questions
/// asked in C# before a handler runs, the functions that answer the same questions in the database, and whatever
/// writes those functions. Declare one per kind of resource. Nothing here is shared between two resources, so an
/// application with documents and folders declares two, and neither touches the other.
/// </para>
/// <para>
/// Three things are said apart from each other, and any of the one goes with any of the others. Who a member is:
/// what follows from the member id, the caller's seat for the seat id of a package that keeps an organization's seats
/// and the caller's user id for one over a Guid, or a claim of its token, or an id the application resolves for the caller (<see cref="Members"/>). Where the
/// roles come from: in code, made by a customer, or kept elsewhere (<see cref="Roles"/>). And whether a permission
/// held where the resource sits, or above it, reaches the resource (<see cref="ReachFromAbove"/>). So a resource beside an
/// organization can have the organization's people as members with roles of its own, in code or made by a customer,
/// or with the organization's roles, with reach from above or without.
/// </para>
/// </summary>
public sealed class MembershipRules
{
    private readonly Dictionary<string, DeclaredRole> _roles;
    private readonly HashSet<string> _ownerKeys;
    private readonly HashSet<string> _notFromAbove;

    private MembershipRules(
        Type resource,
        string name,
        MembershipCodes? codes,
        string? scope,
        IReadOnlyList<ResourcePermission> permissions,
        IReadOnlyList<string> elsewhere,
        Roles roles,
        MemberSource? members,
        ReachFromAbove? reachFromAbove,
        IReadOnlyList<string>? systemScopes)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var what = resource.Name;
        if (!MembershipCodes.IsPrefix(name))
        {
            throw new ArgumentException(
                "'" + name + "' is not the name of a resource with members. A name is lower case letters, digits and dashes, in parts joined by dots, "
                + "each part starting with a letter, such as 'documents'.",
                nameof(name));
        }

        Resource = resource;
        Name = name;
        Codes = codes ?? MembershipCodes.Under(name);
        Functions = MembershipFunctions.For(name);
        Members = members ?? MemberSource.Inferred;
        Roles = roles;
        ReachFromAbove = reachFromAbove;
        Permissions = Checked(permissions, what);

        OwnerKeys = [.. Permissions.Where(permission => permission.Owner).Select(permission => permission.Key)];
        _ownerKeys = new HashSet<string>(OwnerKeys, StringComparer.Ordinal);
        EveryMemberKey = OneMarked(permission => permission.EveryMember, "EveryMember", "every member holds", what)?.Key;
        MembersLockKey = OneMarked(permission => permission.LocksMembers, "LocksMembers", "locks the members", what)?.Key;
        OwnerLockKey = OneMarked(permission => permission.LocksOwner, "LocksOwner", "locks the owner", what)?.Key;
        _notFromAbove = new HashSet<string>(Permissions.Where(permission => !permission.FromAbove).Select(permission => permission.Key), StringComparer.Ordinal);

        // What a role can give: the permissions marked InRoles, or, where the roles take keys of other modules, every
        // key but the resource's own that no role gives, the keys its module declares at another level, and those named.
        RoleKeys = roles.OtherKeys is { } other
            ? new RoleKeys(excepts: true, [.. Permissions.Where(permission => !permission.InRoles).Select(permission => permission.Key), .. elsewhere, .. other.Excepted])
            : new RoleKeys(excepts: false, Permissions.Where(permission => permission.InRoles).Select(permission => permission.Key));

        if (reachFromAbove is not null && EveryMemberKey is null)
        {
            // A resource that is not seen does not exist for a caller, so a key held above would reach nobody but its
            // members: whoever holds the key that sees, there, sees it.
            throw new ArgumentException(
                "The rules of " + what + " let it be reached from above, and no permission on it is held by every member: whoever holds that permission where a "
                + what + " sits, or above it, sees it, and without one nobody above would. Mark the permission that sees it [On<" + what + ">(EveryMember = true)].",
                nameof(reachFromAbove));
        }

        if (reachFromAbove is not null && !ReachesFromAbove(EveryMemberKey!))
        {
            throw new ArgumentException(
                "'" + EveryMemberKey + "', the permission every member of " + what + " holds, is the one that sees it from above, and is marked FromAbove = false. "
                + "Let it reach from above, or leave reachFromAbove out.",
                nameof(reachFromAbove));
        }

        CheckRoles(what);
        _roles = roles.Declared.ToDictionary(role => role.Name, StringComparer.Ordinal);

        MembersLock(MembersLockKey, "LocksMembers", what);
        MembersLock(OwnerLockKey, "LocksOwner", what);
        SystemScopes = Scopes(systemScopes, scope);
    }

    /// <summary>The rules of the resource <typeparamref name="TResource"/>.</summary>
    /// <param name="roles">
    /// Where the roles a member holds come from: <see cref="Access.Roles.Fixed"/> in code,
    /// <see cref="Access.Roles.Custom"/> made by a customer, or <see cref="Access.Roles.External"/> kept elsewhere.
    /// </param>
    /// <param name="members">
    /// Who the caller is as a member; left out, it follows from what a member is known by
    /// (<see cref="MemberSource.Inferred"/>): the caller's seat for the seat id of a package that keeps an organization's
    /// seats, the caller's user id for an id over a Guid.
    /// </param>
    /// <param name="reachFromAbove">
    /// Says that a permission held where the resource hangs, or above it, reaches the resource
    /// (<see cref="Access.ReachFromAbove"/>): <c>true</c> beside a package that keeps an organization's units, or the function of the application's own
    /// that answers where the caller holds a key. Left out, a resource is reached through its members and by its owner
    /// alone. Said, the resource's class marks the place it hangs at <c>[At]</c>, and one permission is held by every
    /// member (<see cref="OnAttribute{TResource}.EveryMember"/>): what is held above sees a resource by that permission.
    /// </param>
    /// <param name="systemScopes">
    /// The scopes whose system work holds every permission on every resource of this kind
    /// (<see cref="SystemScopes"/>). Left out, the scope of the module that declares the resource
    /// (<see cref="IMembershipResource.MembershipScope"/>): <c>boards</c> for the module <c>Boards</c>, so work begun
    /// as <c>Caller.SystemIn("boards")</c> does what it needs on boards. Said, the scopes named and no
    /// other: name the module's own among them to keep it, and say <c>[]</c> for no system work in a scope at all.
    /// </param>
    /// <typeparam name="TResource">The resource: the aggregate root a member class names, which the generator makes one.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="roles"/> is null.</exception>
    /// <exception cref="ArgumentException">The rules do not hold together; the message says which entry, and what to write.</exception>
    public static MembershipRules For<TResource>(
        Roles roles,
        MemberSource? members = null,
        ReachFromAbove? reachFromAbove = null,
        IReadOnlyList<string>? systemScopes = null)
        where TResource : class, IMembershipResource
    {
        ArgumentNullException.ThrowIfNull(roles);

        // The resource's own members first: reading them runs its assembly's module initializer, where the generator
        // declares the permissions of the [Permissions] classes that assembly holds.
        var name = TResource.MembershipName;
        var codes = TResource.MembershipCodes ?? throw new InvalidOperationException(typeof(TResource).Name + " answers no codes: the generator writes them, or the class declares a static MembershipCodes of its own.");
        var scope = TResource.MembershipScope;
        var (permissions, elsewhere) = MembershipPermissions.Of<TResource>();
        return new MembershipRules(
            typeof(TResource),
            name,
            codes,
            scope,
            permissions,
            elsewhere,
            roles,
            members,
            reachFromAbove,
            systemScopes);
    }

    /// <summary>The resource's class.</summary>
    public Type Resource { get; }

    /// <summary>The resource's name: what its codes and its functions are named after.</summary>
    public string Name { get; }

    /// <summary>The codes the resource refuses with.</summary>
    public MembershipCodes Codes { get; }

    /// <summary>The names of the resource's set functions in the database, which follow from its name.</summary>
    public MembershipFunctions Functions { get; }

    /// <summary>Who the caller is as a member.</summary>
    public MemberSource Members { get; }

    /// <summary>Where the roles a member holds come from, and the roles declared in code.</summary>
    public Roles Roles { get; }

    /// <summary>
    /// Says that a permission held where the resource hangs, or above it, reaches the resource, or
    /// <see langword="null"/> when a resource is reached through its members and by its owner alone.
    /// </summary>
    public ReachFromAbove? ReachFromAbove { get; }

    /// <summary>The permissions on the resource, each once, as they are declared: every key asked about on a resource of this kind.</summary>
    public IReadOnlyList<ResourcePermission> Permissions { get; }

    /// <summary>
    /// The permissions the owner holds by owning the resource, those marked <c>Owner</c>, in the order they are
    /// declared. The owner holds no role, so this is all it holds by owning it.
    /// </summary>
    public IReadOnlyList<string> OwnerKeys { get; }

    /// <summary>
    /// The permission every member holds by being one, whatever roles it holds, or <see langword="null"/>: usually
    /// the one that sees the resource. Without one a resource is still seen by its members, and by its owner, and
    /// that is all membership alone gives. Where a resource is reached from above (<see cref="ReachFromAbove"/>) it is the
    /// permission that sees it from there as well.
    /// </summary>
    public string? EveryMemberKey { get; }

    /// <summary>Which keys a member's role can give on the resource, wherever the roles come from.</summary>
    public RoleKeys RoleKeys { get; }

    /// <summary>
    /// The permission the application's own commands require to change the members of a resource, the one marked
    /// <c>LocksMembers</c>, or <see langword="null"/>. It is here for a database that checks every row itself, and
    /// for nothing else: whatever writes the database's policies holds both member tables to it, for every role the
    /// functions are granted to, and a row is written only by a caller that holds it on the resource, or the
    /// permission that locks the owner. No code of the package gates a command by it.
    /// <para>
    /// The database asks what the caller holds when each statement runs. Mark it <c>Owner</c>, as the default is,
    /// where a caller opens a resource as itself: the owner's own first rows are then its to write.
    /// </para>
    /// </summary>
    public string? MembersLockKey { get; }

    /// <summary>
    /// The permission the application's own command requires to hand a resource to another owner, the one marked
    /// <c>LocksOwner</c>, or <see langword="null"/>. As <see cref="MembersLockKey"/>, it is here for a database that
    /// checks every row itself: a change of a resource leaves its owner as it was unless the caller holds it there.
    /// <para>
    /// Whoever holds it by owning the resource alone holds it no longer once the owner has changed, and what a
    /// hand-over writes after that is then not its to write. A hand-over by the owner itself is saved as the
    /// application's own work; one by somebody who holds it through a role, or from above, as the caller.
    /// </para>
    /// </summary>
    public string? OwnerLockKey { get; }

    /// <summary>
    /// The scopes whose work is the application's own for this resource: work that runs as
    /// <see cref="Caller.SystemIn"/> in one of them holds every permission on every resource of this kind, as
    /// <see cref="Caller.System"/> does. The module's own scope unless the rules say otherwise; work in any other
    /// scope holds nothing on a resource, since a scope is a module's own and another module's work is not above
    /// this resource's rules.
    /// </summary>
    public IReadOnlyList<string> SystemScopes { get; }

    /// <summary>
    /// Whether being a member gives <paramref name="key"/> by itself, whatever roles the member holds: so for
    /// <see cref="EveryMemberKey"/>, and for nothing else.
    /// </summary>
    /// <param name="key">A permission key.</param>
    public bool MembershipGives(string key) => EveryMemberKey is not null && string.Equals(key, EveryMemberKey, StringComparison.Ordinal);

    /// <summary>
    /// Whether a resource's owner holds <paramref name="key"/> on it by being its owner: so for every one of
    /// <see cref="OwnerKeys"/>, and for no other key, with no end.
    /// </summary>
    /// <param name="key">A permission key.</param>
    public bool OwnerHolds(string key) => _ownerKeys.Contains(key);

    /// <summary>
    /// Whether a member can hold <paramref name="key"/> on the resource at all, by being a member or through a
    /// role: <see cref="EveryMemberKey"/>, and every key within <see cref="RoleKeys"/>.
    /// </summary>
    /// <param name="key">A permission key.</param>
    public bool MembersHold(string key) => MembershipGives(key) || RoleKeys.Allows(key);

    /// <summary>
    /// Whether <paramref name="key"/> held where the resource sits, or above it, reaches the resource: where the
    /// rules let a resource be reached from above, for every key but the permissions marked <c>FromAbove = false</c>.
    /// </summary>
    /// <param name="key">A permission key.</param>
    public bool ReachesFromAbove(string key) => ReachFromAbove is not null && !_notFromAbove.Contains(key);

    /// <summary>
    /// Whether <paramref name="caller"/> is the application's own work for this resource, which holds every
    /// permission on every resource of this kind: <see cref="Caller.System"/>, always, and
    /// <see cref="Caller.SystemIn"/> in one of <see cref="SystemScopes"/>. Work in any other scope is not, and holds
    /// nothing.
    /// </summary>
    /// <param name="caller">The caller a question is asked for.</param>
    /// <exception cref="ArgumentNullException"><paramref name="caller"/> is null.</exception>
    public bool IsOwnWork(Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        return caller.IsSystem || (caller.IsSystemIn && caller.Scope is { } scope && SystemScopes.Contains(scope, StringComparer.Ordinal));
    }

    /// <summary>
    /// The fixed roles that give <paramref name="key"/> on the resource: those that list it, when a member's role can
    /// give it at all, in the order they are declared. None where the roles are rows, a customer's or kept elsewhere,
    /// since which of those give a key is read where they are kept.
    /// </summary>
    /// <param name="key">A permission key.</param>
    public IReadOnlyList<NamedRole> RolesWith(string key)
        => Roles.Kind == RolesKind.Fixed && RoleKeys.Allows(key)
            ? [.. Roles.Declared.Where(role => role.Keys.Contains(key, StringComparer.Ordinal)).Select(role => new NamedRole(role.Name))]
            : [];

    /// <summary>
    /// The keys <paramref name="role"/> gives a member on the resource: the keys it lists that a member's role can
    /// give. None for a role the rules do not declare. For a customer's roles, the keys the starter role of that name
    /// is made with.
    /// </summary>
    /// <param name="role">A role.</param>
    public IReadOnlyList<string> KeysOf(NamedRole role)
        => role.Value is { } name && _roles.TryGetValue(name, out var declared)
            ? [.. declared.Keys.Where(RoleKeys.Allows)]
            : [];

    /// <summary>Whether <paramref name="role"/> is one of the roles declared in code: a fixed role, or a starter role a customer's roles are made from.</summary>
    /// <param name="role">A role.</param>
    public bool Knows(NamedRole role) => role.Value is { } name && _roles.ContainsKey(name);

    /// <summary>
    /// The rules of <paramref name="resource"/>, made from what is said here rather than read off its type: for a
    /// test, which has a resource of its own to try many permissions on. An application says
    /// <see cref="For{TResource}"/>, which reads the same off the resource and the generator's declaration of its
    /// permissions.
    /// </summary>
    /// <param name="resource">The resource's class, which the rules are about.</param>
    /// <param name="name">The resource's name.</param>
    /// <param name="permissions">The permissions on it.</param>
    /// <param name="roles">Where the roles come from.</param>
    /// <param name="members">Who the caller is as a member.</param>
    /// <param name="reachFromAbove">Whether it is reached from above.</param>
    /// <param name="systemScopes">The scopes whose system work holds everything on it; left out, <paramref name="scope"/>.</param>
    /// <param name="scope">The scope of its module's system work, or <see langword="null"/>.</param>
    /// <param name="codes">The codes it refuses under; <see cref="MembershipCodes.Under"/> the name when left out.</param>
    /// <param name="elsewhere">The keys its module declares at another level.</param>
    /// <exception cref="ArgumentException">The rules do not hold together.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static MembershipRules Declare(
        Type resource,
        string name,
        IReadOnlyList<ResourcePermission> permissions,
        Roles roles,
        MemberSource? members = null,
        ReachFromAbove? reachFromAbove = null,
        IReadOnlyList<string>? systemScopes = null,
        string? scope = null,
        MembershipCodes? codes = null,
        IReadOnlyList<string>? elsewhere = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(permissions);

        return new MembershipRules(resource, name, codes, scope, permissions, elsewhere ?? [], roles, members, reachFromAbove, systemScopes);
    }

    private static IReadOnlyList<ResourcePermission> Checked(IReadOnlyList<ResourcePermission> permissions, string what)
    {
        if (permissions.Count == 0)
        {
            throw new ArgumentException(
                "No permission is declared on " + what + ". Mark the keys asked on it [On<" + what + ">] in a [Permissions] class of the project that makes "
                + "the rules, or of one it references: the package's generator declares them for MembershipRules.For<" + what + ">.",
                nameof(permissions));
        }

        if (permissions.Any(permission => permission is null || string.IsNullOrWhiteSpace(permission.Key)))
        {
            throw new ArgumentException("The permissions of " + what + " are permission keys, and a key is not blank.", nameof(permissions));
        }

        if (permissions.GroupBy(permission => permission.Key, StringComparer.Ordinal).FirstOrDefault(key => key.Count() > 1) is { } twice)
        {
            throw new ArgumentException("'" + twice.Key + "' is declared on " + what + " twice. A permission is declared once.", nameof(permissions));
        }

        return [.. permissions];
    }

    private ResourcePermission? OneMarked(Func<ResourcePermission, bool> marked, string setting, string what, string resource)
    {
        var found = Permissions.Where(marked).ToList();
        return found.Count <= 1
            ? found.FirstOrDefault()
            : throw new ArgumentException(
                "Two permissions on " + resource + " are marked " + setting + ": '" + found[0].Key + "' and '" + found[1].Key + "'. One permission " + what + "; mark the one.",
                "permissions");
    }

    private void CheckRoles(string what)
    {
        if (Roles.Kind == RolesKind.Fixed)
        {
            foreach (var role in Roles.Declared)
            {
                foreach (var key in role.Keys)
                {
                    if (!Permissions.Any(permission => permission.Key == key))
                    {
                        // A key the resource never declares: a typo, or a permission that was never marked [On<TResource>].
                        throw new ArgumentException(
                            "The role '" + role.Name + "' lists '" + key + "', which is no permission on " + what + ". Mark it [On<" + what + ">], or take it out of the role.",
                            "roles");
                    }

                    if (!RoleKeys.Allows(key))
                    {
                        throw new ArgumentException(
                            "The role '" + role.Name + "' lists '" + key + "', which no role gives on " + what + ": it is marked InRoles = false. Take it out of the role.",
                            "roles");
                    }
                }
            }

            return;
        }

        if (Roles.Kind == RolesKind.Custom)
        {
            if (RoleKeys is { Excepts: false, Keys.Count: 0 })
            {
                // A role's keys are chosen from what a role can give, so a customer could make roles and none of them could
                // ever give anything: every key would be refused, and nothing would say why.
                throw new ArgumentException(
                    "No permission on " + what + " can be given by a role: every one is marked InRoles = false, so no role a customer makes could give anything. "
                    + "Let a role give one, or take the keys of other modules, Roles.Custom(otherKeys: OtherKeys.All).",
                    "roles");
            }

            // A starter role is a row in the end, and a row's keys are held to what a role can give.
            foreach (var role in Roles.Declared)
            {
                if (role.Keys.FirstOrDefault(key => !RoleKeys.Allows(key)) is { } refused)
                {
                    throw new ArgumentException(
                        "The starter role '" + role.Name + "' lists '" + refused + "', which a role cannot give on " + what + ". Take it out of the role, "
                        + "or let a role give it: InRoles on a permission of " + what + ", or otherKeys for a key of another module.",
                        "roles");
                }
            }
        }
    }

    /// <summary>
    /// Refuses a lock nobody could pass: a permission the database's lock asks for that the owner does not hold,
    /// every member does not hold, no role can give and nothing above reaches. The lock would keep everybody out.
    /// </summary>
    private void MembersLock(string? key, string setting, string what)
    {
        if (key is null || OwnerHolds(key) || MembershipGives(key) || RoleKeys.Allows(key) || ReachesFromAbove(key))
        {
            return;
        }

        throw new ArgumentException(
            "'" + key + "' is marked " + setting + ", and nobody would hold it on " + what + ": not the owner (Owner = false), no role (InRoles = false), not every member, "
            + "and nothing above. The database's lock would keep everybody out. Let the owner hold it, or a role give it.",
            "permissions");
    }

    private static IReadOnlyList<string> Scopes(IReadOnlyList<string>? scopes, string? own)
    {
        if (scopes is null)
        {
            return own is null ? [] : [own];
        }

        foreach (var scope in scopes)
        {
            if (string.IsNullOrEmpty(scope) || !scope.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-'))
            {
                throw new ArgumentException(
                    "'" + scope + "' is not a scope. A scope is lower case letters, digits, '_' and '-', such as a module's name: what Caller.SystemIn is begun with.",
                    "systemScopes");
            }
        }

        return scopes.Distinct(StringComparer.Ordinal).Count() == scopes.Count
            ? [.. scopes]
            : throw new ArgumentException("The scopes whose work is the application's own are each named once.", "systemScopes");
    }
}
