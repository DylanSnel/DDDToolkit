using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// The rules of access through the members of one kind of resource, as one declaration: who the caller is as
/// a member, the keys of the resource, what being a member gives, what each role gives, whether something
/// above the resource reaches it, and the names the resource answers and refuses under.
/// <code>
/// public static MembershipRules Rules { get; } = new(
///     "documents",
///     keys: [DocumentKeys.View, DocumentKeys.Edit, DocumentKeys.Share],
///     roles: [new("contributor", [DocumentKeys.View, DocumentKeys.Edit]), new("onlooker", [DocumentKeys.View])]);
/// </code>
/// <para>
/// They say who holds a key: the owner of a resource holds every key of it, and a member the keys its roles
/// give. They do not say who may add a member, give or take a role, or hand the resource to another owner.
/// That is the application's to decide, command by command: each of its commands requires what the
/// application chooses, usually one of these keys held on the resource, and whatever further rule it has it
/// writes itself.
/// </para>
/// <para>
/// It is data, checked once when it is made, so everything that decides access reads the same thing: the
/// questions asked in C# before a handler runs, the functions that answer the same questions in the database,
/// and whatever writes those functions. Declare one per kind of resource. Nothing here is shared between two
/// resources, so an application with documents and folders declares two, and neither touches the other.
/// </para>
/// <para>
/// Only the name, the keys and the roles are needed for a resource whose members are users. The rest has a
/// default, each said at its property.
/// </para>
/// <para>
/// Two entries are for a database that checks every row itself, and for nothing else: the key that changes the
/// members (<see cref="ChangeMembersKey"/>) and the key that changes the owner (<see cref="ChangeOwnerKey"/>).
/// They are the keys the application's own commands require for that. Named here, the database holds a caller
/// that reaches it past the application to the same keys. No code of the package gates a command by them.
/// </para>
/// <para>
/// Three things are said apart from each other, and any of the one goes with any of the others. Who a member
/// is: the caller's id, a claim of its token, or an id the application resolves for the caller
/// (<see cref="Members"/>). Where the roles come from: declared here, by name; kept for the resource, as
/// rows of the application's role class, which a customer makes and changes; or kept elsewhere and asked
/// there (<see cref="Roles"/>, <see cref="RolesKept"/>, <see cref="RolesKeptElsewhere"/>). And whether a key
/// held where the resource sits, or above it, reaches the resource (<see cref="Above"/>). So a resource
/// beside an organization can have the organization's people as members with roles of its own, declared
/// here or made by a customer, or with the organization's roles, with reach from above or without.
/// </para>
/// </summary>
public sealed class MembershipRules
{
    /// <summary>The name of the owner's role when the rules name none: <c>owner</c>.</summary>
    public const string DefaultOwnerRole = "owner";

    private readonly Dictionary<string, DeclaredRole> _roles;
    private readonly HashSet<string> _keys;

    /// <summary>The rules of one kind of resource.</summary>
    /// <param name="name">
    /// The resource's name, such as <c>documents</c>: what its codes and its functions are named after unless
    /// they are given. Lower case letters, digits and dashes, in parts joined by dots, each part starting with a
    /// letter.
    /// </param>
    /// <param name="keys">
    /// The keys of the resource: every permission key that is asked about on a resource of this kind, each once.
    /// The owner of a resource holds every one of them on it. At least one where the rules declare the roles,
    /// and every key a role lists is one of them; where the roles are rows, kept for the resource
    /// (<paramref name="rolesKept"/>) or elsewhere (<paramref name="rolesKeptElsewhere"/>), the ones an owner
    /// holds by being the owner, which may be none.
    /// </param>
    /// <param name="roles">
    /// The roles a member can hold, with the keys each gives: at least one. Where the roles are kept for the
    /// resource (<paramref name="rolesKept"/>), the starter roles instead: what the first roles are made
    /// from, which may be none. Left out where the roles are kept elsewhere
    /// (<paramref name="rolesKeptElsewhere"/>).
    /// </param>
    /// <param name="members">Where the caller's member id comes from; <see cref="MemberSource.CallerId"/> when left out.</param>
    /// <param name="seeKey">The key that being a member gives by itself, whatever roles the member holds, or none.</param>
    /// <param name="ownerRole">
    /// The role every owner holds: the name of one of <paramref name="roles"/>, and
    /// <see cref="DefaultOwnerRole"/> when left out; or, where the roles are kept elsewhere, what the
    /// application finds the owner's role by there, which is then said.
    /// </param>
    /// <param name="memberKeys">
    /// Which keys a member's role can give; every key the roles list when left out. Said where the roles are
    /// kept elsewhere, since the rules do not know what those roles hold. Where the roles are kept for the
    /// resource it is also what the keys of a role are chosen from.
    /// </param>
    /// <param name="codes">The codes the resource refuses with; <see cref="MembershipCodes.Under"/> the name when left out.</param>
    /// <param name="functions">The names of the resource's set functions; <see cref="MembershipFunctions.For"/> the name when left out.</param>
    /// <param name="grantTo">The database roles that may ask the resource's functions; the signed-in user's when left out.</param>
    /// <param name="rolesKeptElsewhere">
    /// Says that the roles a member holds are kept elsewhere, by the application, and not declared in
    /// <paramref name="roles"/>. Left out, the roles are the ones the rules declare.
    /// </param>
    /// <param name="above">
    /// Says that a key held where the resource sits, or above it, reaches the resource. Left out, a resource
    /// is reached through its members and by its owner alone. Said, the rules name a
    /// <paramref name="seeKey"/> as well: what is held above sees a resource by that key.
    /// </param>
    /// <param name="rolesKept">
    /// Says that the roles a member holds are kept for the resource: rows of the application's role class
    /// (<see cref="KeptRoleAttribute{TRoleId, TResource}"/>), which a customer makes, renames, gives keys
    /// and archives. <paramref name="roles"/> are then the starter roles those rows begin from. Left out, the
    /// roles are the ones the rules declare.
    /// </param>
    /// <param name="changeMembersKey">
    /// The key the application's commands require to add or remove a member and to give or take a role, or none:
    /// said for the database's lock on the two member tables (<see cref="ChangeMembersKey"/>).
    /// </param>
    /// <param name="changeOwnerKey">
    /// The key the application's command requires to hand the resource to another owner, or none: said for the
    /// database's lock on the owner column (<see cref="ChangeOwnerKey"/>).
    /// </param>
    /// <param name="systemScopes">
    /// The scopes whose work is the application's own for this resource (<see cref="SystemScopes"/>); none when
    /// left out, and work in a scope then holds nothing on a resource.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="keys"/> is null.</exception>
    /// <exception cref="ArgumentException">The rules do not hold together; the message says which entry, and what to write.</exception>
    public MembershipRules(
        string name,
        IReadOnlyList<string> keys,
        IReadOnlyList<DeclaredRole>? roles = null,
        MemberSource? members = null,
        string? seeKey = null,
        string? ownerRole = null,
        MemberKeys? memberKeys = null,
        MembershipCodes? codes = null,
        MembershipFunctions? functions = null,
        IReadOnlyList<string>? grantTo = null,
        RolesKeptElsewhere? rolesKeptElsewhere = null,
        ReachFromAbove? above = null,
        bool rolesKept = false,
        string? changeMembersKey = null,
        string? changeOwnerKey = null,
        IReadOnlyList<string>? systemScopes = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(keys);
        if (!MembershipCodes.IsPrefix(name))
        {
            throw new ArgumentException(
                "'" + name + "' is not a name for a resource's rules. A name is lower case letters, digits and dashes, in parts joined by dots, "
                + "each part starting with a letter, such as 'documents'.",
                nameof(name));
        }

        Name = name;
        Members = members ?? MemberSource.CallerId;
        Codes = codes ?? MembershipCodes.Under(name);

        if (rolesKept && rolesKeptElsewhere is not null)
        {
            throw new ArgumentException(
                "The rules say the roles are kept for the resource, and that they are kept elsewhere. Roles are the one or the other: rolesKept: true "
                + "for rows of the application's role class, which a customer makes, or rolesKeptElsewhere: new() for roles that are asked where they are kept.",
                nameof(rolesKept));
        }

        RolesKept = rolesKept;
        RolesKeptElsewhere = rolesKeptElsewhere;
        Above = above;
        if (above is not null && seeKey is null)
        {
            // A resource that is not seen does not exist for a caller, so a key held above would reach nobody
            // but its members: whoever holds the key that sees, there, sees it.
            throw new ArgumentException(
                "Rules that let a resource be reached from above name the key that sees it: whoever holds that key where the resource sits, or above it, "
                + "sees the resource, and without one nobody above would. Say it as the key that being a member gives: seeKey: \"documents.view\".",
                nameof(seeKey));
        }

        var elsewhere = rolesKeptElsewhere is not null;

        // Roles that are rows, wherever they are kept, may hold keys the rules never listed. The keys stated
        // here are then the ones an owner holds by owning, and neither a role nor the key that sees is held to them.
        var rows = elsewhere || rolesKept;

        // Copied, so the keys of the resource are the ones the rules were made with, whatever happens to the list they were given.
        Keys = DeclaredKeys(keys, rows);
        _keys = new HashSet<string>(Keys, StringComparer.Ordinal);

        SeeKey = seeKey is null ? null : Key(seeKey, nameof(seeKey), "The key that being a member gives");
        if (SeeKey is not null && !rows && !_keys.Contains(SeeKey))
        {
            throw new ArgumentException(
                "'" + SeeKey + "', the key that being a member gives, is not one of the resource's keys. Add it to the keys.",
                nameof(seeKey));
        }

        var declared = DeclaredRoles(roles, elsewhere, rolesKept, _keys);
        if (elsewhere)
        {
            // The rules cannot add an owner's role to roles they do not keep, and do not know what those roles hold.
            OwnerRole = string.IsNullOrWhiteSpace(ownerRole)
                ? throw new ArgumentException(
                    "Where the roles are kept elsewhere the rules add no owner's role: say what the owner's role is found by there, "
                    + "a name or a key of the application's own, as the owner's role.",
                    nameof(ownerRole))
                : ownerRole;
            MemberKeys = memberKeys ?? throw new ArgumentException(
                "Roles that are kept elsewhere may hold keys of every module: say which of them a member's role gives on this resource, "
                + "with MemberKeys.Only or MemberKeys.AllBut.",
                nameof(memberKeys));
        }
        else
        {
            OwnerRole = ownerRole ?? DefaultOwnerRole;
            if (!declared.Any(role => role.Name == OwnerRole))
            {
                if (ownerRole is not null)
                {
                    throw new ArgumentException(
                        "The owner's role '" + ownerRole + "' is not one of the roles. Declare a role of that name, or leave the owner's role out "
                        + "and the rules add '" + DefaultOwnerRole + "', which gives every key of the resource.",
                        nameof(ownerRole));
                }

                // Nobody declared an owner's role, so one is added that gives every key of the resource. The
                // owner holds those by being the owner already; whoever else is given the role holds them through it.
                declared.Add(new DeclaredRole(DefaultOwnerRole, [.. Keys]));
            }

            MemberKeys = memberKeys ?? MemberKeys.Only([.. declared.SelectMany(role => role.Keys).Distinct(StringComparer.Ordinal)]);
        }

        if (rolesKept && MemberKeys is { Excepts: false, Keys.Count: 0 })
        {
            // A role's keys are chosen from what a member's role can give, so a customer could make roles and
            // none of them could ever give anything: every key would be refused, and nothing would say why.
            throw new ArgumentException(
                "Under the rules '" + name + "' a member's role can give no key at all, so no role a customer makes could give anything. Say which keys the roles of the "
                + "resource are chosen from, memberKeys: MemberKeys.Only(\"documents.view\", \"documents.edit\"), or give a starter role its keys.",
                nameof(memberKeys));
        }

        Roles = declared.AsReadOnly();
        _roles = declared.ToDictionary(role => role.Name, StringComparer.Ordinal);

        ChangeMembersKey = LockKey(changeMembersKey, nameof(changeMembersKey), "The key that changes the members");
        ChangeOwnerKey = LockKey(changeOwnerKey, nameof(changeOwnerKey), "The key that changes the owner");
        SystemScopes = Scopes(systemScopes);

        Functions = functions ?? MembershipFunctions.For(name);
        if (Functions.All.FirstOrDefault(function => !IsFunctionName(function)) is { } misnamed)
        {
            throw new ArgumentException(
                "'" + misnamed + "' is not a name for a function. A name is lower case letters, digits and underscores, not starting with a digit, "
                + "of at most 63 characters, such as 'documents_i_see'.",
                nameof(functions));
        }

        if (Functions.All.Distinct(StringComparer.Ordinal).Count() != Functions.All.Count)
        {
            throw new ArgumentException("The four functions of a resource each answer another question, so each has a name of its own.", nameof(functions));
        }

        GrantTo = grantTo is null ? [RowAccessRoles.User] : [.. grantTo];
        if (GrantTo.Count == 0 || GrantTo.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "The functions of a resource are asked by at least one database role, and each has a name: RowAccessRoles.User unless another is said.",
                nameof(grantTo));
        }
    }

    /// <summary>The resource's name: what its codes and functions are named after by default.</summary>
    public string Name { get; }

    /// <summary>Where the caller's member id comes from.</summary>
    public MemberSource Members { get; }

    /// <summary>
    /// Whether the roles a member holds are kept for the resource: rows of the application's role class,
    /// declared with <see cref="KeptRoleAttribute{TRoleId, TResource}"/>, which a customer makes, renames,
    /// gives keys and archives, and which a member holds by their id. What such a role gives is read from its
    /// row, cut by <see cref="MemberKeys"/>, and <see cref="Roles"/> are the starter roles the rows begin from
    /// (<see cref="UseCases.StarterRoles"/>).
    /// </summary>
    public bool RolesKept { get; }

    /// <summary>
    /// Says that the roles a member holds are kept elsewhere, by the application, or <see langword="null"/>
    /// when they are the ones the rules declare (<see cref="Roles"/>) or rows kept for the resource
    /// (<see cref="RolesKept"/>). Which roles give a key is then asked where they are kept, and what such a
    /// role gives is still cut by <see cref="MemberKeys"/>.
    /// </summary>
    public RolesKeptElsewhere? RolesKeptElsewhere { get; }

    /// <summary>
    /// Says that a key held where the resource sits, or above it, reaches the resource, or
    /// <see langword="null"/> when a resource is reached through its members and by its owner alone.
    /// </summary>
    public ReachFromAbove? Above { get; }

    /// <summary>
    /// The keys of the resource, each once, in the order they were stated: every permission key that is asked
    /// about on a resource of this kind. The owner of a resource holds every one of them on it
    /// (<see cref="OwnerHolds"/>). Which of them a command requires, to add a member or to give a role say, is
    /// the application's to choose.
    /// </summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>
    /// The key that being a member gives by itself, whatever roles the member holds, or <see langword="null"/>:
    /// usually the key that sees the resource. Without one a resource is still seen by its members, and by its
    /// owner, and that is all membership alone gives. Where a resource is reached from above (<see cref="Above"/>) it is the
    /// key that sees it from there as well: a caller that holds it where the resource sits sees the resource.
    /// </summary>
    public string? SeeKey { get; }

    /// <summary>
    /// The role every owner holds, with no end: the name of one of <see cref="Roles"/>, or, where the roles
    /// are kept elsewhere, what the application finds the owner's role by there
    /// (<see cref="UseCases.IMemberRoles{TResourceId, TRoleId}.FindOwnerRoleAsync"/>). Where the roles are kept
    /// for the resource (<see cref="RolesKept"/>) it names the starter role the owner's role is made from, and
    /// that is what the owner's role is found by, whatever a customer has called it since. It is the owner's
    /// place on the member list. What the owner may do does not hang on it: the owner holds every key of the
    /// resource by owning it.
    /// </summary>
    public string OwnerRole { get; }

    /// <summary>
    /// Which keys a member's role can give on the resource, wherever the roles come from: a role that holds
    /// a key outside it gives nothing with that key here.
    /// </summary>
    public MemberKeys MemberKeys { get; }

    /// <summary>
    /// The roles the rules declare, with the owner's role last when the rules added it: a role named
    /// <see cref="DefaultOwnerRole"/> that gives every key of the resource, for rules that name no owner's role
    /// and declare none of that name. Where the roles are kept for the resource (<see cref="RolesKept"/>) they
    /// are the starter roles: no member holds one by its name, and the first roles are made from them. Empty
    /// where the roles are kept elsewhere (<see cref="RolesKeptElsewhere"/>).
    /// </summary>
    public IReadOnlyList<DeclaredRole> Roles { get; }

    /// <summary>The codes the resource refuses with.</summary>
    public MembershipCodes Codes { get; }

    /// <summary>The names of the resource's set functions in the database.</summary>
    public MembershipFunctions Functions { get; }

    /// <summary>
    /// The database roles that may ask the resource's functions, as a row access rule names roles:
    /// <see cref="RowAccessRoles.User"/> unless the rules say otherwise.
    /// </summary>
    public IReadOnlyList<string> GrantTo { get; }

    /// <summary>
    /// The key the application's own commands require to change the members of a resource, to add or remove
    /// one and to give or take a role, or <see langword="null"/> when the rules name none.
    /// <para>
    /// It is here for a database that checks every row itself, and for nothing else. Such a database lets a
    /// caller write the tables of an aggregate's entities whenever the application's rule lets it change the
    /// aggregate, and the two member tables are such tables: without this entry, whoever may change a resource
    /// in any way writes its member rows, a role for itself included, by a statement that never went through
    /// a command. Named, whatever writes the database's policies adds a lock to both tables for the database
    /// roles of <see cref="GrantTo"/>: a row is written only by a caller that holds this key on the resource,
    /// or the key that changes the owner (<see cref="ChangeOwnerKey"/>), since naming an owner writes member
    /// rows as well. The application still decides who may, by requiring the key on its commands; the database
    /// then holds to the same key. No code of the package gates a command by it.
    /// </para>
    /// <para>
    /// The database asks what the caller holds when each statement runs. An owner holds every key the rules
    /// state by owning the resource, so state this key among them where a caller opens a resource as itself:
    /// the owner's own first rows are then its to write.
    /// </para>
    /// </summary>
    public string? ChangeMembersKey { get; }

    /// <summary>
    /// The key the application's own command requires to hand a resource to another owner, or
    /// <see langword="null"/> when the rules name none.
    /// <para>
    /// As <see cref="ChangeMembersKey"/>, it is here for a database that checks every row itself. An owner
    /// holds every key of a resource, so whoever may write the owner column makes itself everything. Named,
    /// whatever writes the database's policies pins that column for the database roles of
    /// <see cref="GrantTo"/>: a change of a resource leaves its owner as it was, unless the caller holds this
    /// key on the resource. No code of the package gates a command by it.
    /// </para>
    /// <para>
    /// Whoever holds it by owning the resource alone holds it no longer once the owner has changed, and what
    /// a hand-over writes after that is then not its to write. A hand-over by the owner itself is saved as the
    /// application's own work; one by somebody who holds the key through a role, or from above, as the caller.
    /// </para>
    /// </summary>
    public string? ChangeOwnerKey { get; }

    /// <summary>
    /// The scopes whose work is the application's own for this resource: work that runs as
    /// <see cref="Caller.SystemIn"/> in one of them holds every key on every resource of this kind, as
    /// <see cref="Caller.System"/> does. Empty unless the rules say otherwise, and work in a scope then holds
    /// nothing on a resource: a scope is a module's own, and another module's work is not above this
    /// resource's rules. A module names its own scope here for the work it does on its own resources.
    /// </summary>
    public IReadOnlyList<string> SystemScopes { get; }

    /// <summary>
    /// Whether being a member gives <paramref name="key"/> by itself, whatever roles the member holds: so for
    /// <see cref="SeeKey"/>, and for nothing else.
    /// </summary>
    /// <param name="key">A permission key.</param>
    public bool MembershipGives(string key) => SeeKey is not null && string.Equals(key, SeeKey, StringComparison.Ordinal);

    /// <summary>
    /// Whether a resource's owner holds <paramref name="key"/> on it by being its owner: so for every one of
    /// <see cref="Keys"/>, and for no other key. It is held by owning the resource, with no end, whatever roles
    /// the owner holds and whatever a member's role can give.
    /// </summary>
    /// <param name="key">A permission key.</param>
    public bool OwnerHolds(string key) => _keys.Contains(key);

    /// <summary>
    /// Whether a member can hold <paramref name="key"/> on the resource at all, by being a member or through a
    /// role: <see cref="SeeKey"/>, and every key within <see cref="MemberKeys"/>.
    /// </summary>
    /// <param name="key">A permission key.</param>
    public bool MembersHold(string key) => MembershipGives(key) || MemberKeys.Allows(key);

    /// <summary>
    /// Whether <paramref name="caller"/> is the application's own work for this resource, which holds every
    /// key on every resource of this kind: <see cref="Caller.System"/>, always, and
    /// <see cref="Caller.SystemIn"/> in one of <see cref="SystemScopes"/>. Work in any other scope is not, and
    /// holds nothing.
    /// </summary>
    /// <param name="caller">The caller a question is asked for.</param>
    /// <exception cref="ArgumentNullException"><paramref name="caller"/> is null.</exception>
    public bool IsOwnWork(Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        return caller.IsSystem || (caller.IsSystemIn && caller.Scope is { } scope && SystemScopes.Contains(scope, StringComparer.Ordinal));
    }

    /// <summary>
    /// The declared roles that give <paramref name="key"/> on the resource: those that list it, when a member's
    /// role can give it at all. In the order the roles are declared; none where the roles are rows, kept for
    /// the resource or elsewhere, since which of those give a key is read where they are kept.
    /// </summary>
    /// <param name="key">A permission key.</param>
    public IReadOnlyList<NamedRole> RolesWith(string key)
        => !RolesKept && MemberKeys.Allows(key)
            ? [.. Roles.Where(role => role.Keys.Contains(key, StringComparer.Ordinal)).Select(role => new NamedRole(role.Name))]
            : [];

    /// <summary>
    /// The keys <paramref name="role"/> gives a member on the resource: the keys it lists that a member's role
    /// can give. None for a role the rules do not declare. Where the roles are kept for the resource, the keys
    /// the starter role of that name is made with.
    /// </summary>
    /// <param name="role">A role.</param>
    public IReadOnlyList<string> KeysOf(NamedRole role)
        => role.Value is { } name && _roles.TryGetValue(name, out var declared)
            ? [.. declared.Keys.Where(MemberKeys.Allows)]
            : [];

    /// <summary>Whether <paramref name="role"/> is one of <see cref="Roles"/>: a role the rules declare, or a starter role where the roles are kept for the resource.</summary>
    /// <param name="role">A role.</param>
    public bool Knows(NamedRole role) => role.Value is { } name && _roles.ContainsKey(name);

    private static IReadOnlyList<string> DeclaredKeys(IReadOnlyList<string> keys, bool rows)
    {
        if (keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("The keys of a resource are permission keys, and a key is not blank.", nameof(keys));
        }

        if (keys.GroupBy(key => key, StringComparer.Ordinal).FirstOrDefault(key => key.Count() > 1) is { } twice)
        {
            throw new ArgumentException("The keys of the resource list '" + twice.Key + "' twice. A key is stated once.", nameof(keys));
        }

        if (keys.Count == 0 && !rows)
        {
            throw new ArgumentException(
                "A resource whose rules declare its roles states the keys it is asked about, at least one: keys: [DocumentKeys.View, DocumentKeys.Edit]. "
                + "Its owner holds every one of them.",
                nameof(keys));
        }

        return [.. keys];
    }

    private static List<DeclaredRole> DeclaredRoles(IReadOnlyList<DeclaredRole>? roles, bool elsewhere, bool kept, HashSet<string> keys)
    {
        if (elsewhere)
        {
            return roles is null or { Count: 0 }
                ? []
                : throw new ArgumentException(
                    "The rules say the roles are kept elsewhere, and declare roles as well. Roles are the one or the other: leave the roles out, "
                    + "and what a role gives is asked where they are kept; or leave rolesKeptElsewhere out, and the roles are the ones declared here.",
                    nameof(roles));
        }

        if (roles is null or { Count: 0 })
        {
            if (kept)
            {
                // A customer makes the roles, so nothing has to be there to start from but the owner's role, which the rules add.
                return [];
            }

            throw new ArgumentException(
                "A resource needs at least one role a member can hold, with the keys it gives: roles: [new(\"onlooker\", [DocumentKeys.View])]. "
                + "Where a customer makes the roles, say so instead: rolesKept: true. Where they are rows the application keeps elsewhere: rolesKeptElsewhere: new().",
                nameof(roles));
        }

        var declared = new List<DeclaredRole>(roles.Count + 1);
        foreach (var role in roles)
        {
            if (role is null || string.IsNullOrWhiteSpace(role.Name) || role.Name != role.Name.Trim() || role.Name.Length > NamedRole.MaxLength)
            {
                throw new ArgumentException(
                    "A role's name is 1 to " + NamedRole.MaxLength + " characters, with no white space around it: '" + role?.Name + "' is not.",
                    nameof(roles));
            }

            if (declared.Any(other => other.Name == role.Name))
            {
                throw new ArgumentException("The role '" + role.Name + "' is declared twice. A role's name is what a member's row stores, so it is declared once.", nameof(roles));
            }

            if (role.Keys is null || role.Keys.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("The role '" + role.Name + "' lists a blank key. A role lists the keys it gives, and none is blank.", nameof(roles));
            }

            if (role.Keys.Distinct(StringComparer.Ordinal).Count() != role.Keys.Count)
            {
                throw new ArgumentException("The role '" + role.Name + "' lists a key twice.", nameof(roles));
            }

            // A starter role is a row in the end, and a row's keys are held to what a member's role can give, not to the keys stated here.
            if (!kept && role.Keys.FirstOrDefault(key => !keys.Contains(key)) is { } unknown)
            {
                // A key the owner would not hold while a member did: a typo, or a key that was never stated.
                throw new ArgumentException(
                    "The role '" + role.Name + "' lists '" + unknown + "', which is not one of the resource's keys. Add it to the keys, or take it out of the role.",
                    nameof(roles));
            }

            // Copied, so the keys a role gives are the ones it was declared with, whatever happens to the list it was given.
            declared.Add(role with { Keys = [.. role.Keys] });
        }

        return declared;
    }

    /// <summary>
    /// A key the database's lock asks for, or <see langword="null"/> when the rules name none. A key somebody
    /// can hold is one of the resource's keys, which its owner holds and which the roles the rules declare
    /// list theirs from; where the roles are rows, also one a member's role can give
    /// (<see cref="MembersHold"/>), since a row is cut by that and by nothing else; and any key where a
    /// resource is reached from above, since the rules do not know what is held there. Any other key is held
    /// by nobody, so the lock would keep everybody out: refused as the slip it is.
    /// </summary>
    private string? LockKey(string? key, string parameter, string what)
    {
        if (key is null)
        {
            return null;
        }

        var named = Key(key, parameter, what);
        var rows = RolesKept || RolesKeptElsewhere is not null;
        if (Above is not null || OwnerHolds(named) || (rows && MembersHold(named)))
        {
            return named;
        }

        var said = "'" + named + "', " + char.ToLowerInvariant(what[0]) + what[1..] + ", ";
        throw new ArgumentException(
            rows
                ? said + "is a key nobody would hold: it is none of the resource's keys, which its owner holds, and none a member's role can give. "
                  + "Add it to the keys, or to what a member's role can give (memberKeys)."
                : said + "is not one of the resource's keys, so nobody would hold it. Add it to the keys.",
            parameter);
    }

    private static IReadOnlyList<string> Scopes(IReadOnlyList<string>? scopes)
    {
        if (scopes is null)
        {
            return [];
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

    private static string Key(string key, string parameter, string what)
        => string.IsNullOrWhiteSpace(key)
            ? throw new ArgumentException(what + " is a permission key, and a key is not blank.", parameter)
            : key;

    private static bool IsFunctionName(string? name)
        => name is { Length: > 0 and <= 63 }
           && name[0] is (>= 'a' and <= 'z') or '_'
           && name.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');
}
