using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// A role of one kind of resource, kept as a row: a name, what it is for, and the permissions it gives
/// a member that holds it. The application declares its own class with
/// <see cref="CustomRoleAttribute{TRoleId, TResource}"/>, for a resource whose rules say a customer makes its
/// roles (<see cref="Access.Roles.Custom"/>): roles a customer makes for itself, where fixed roles are the same for
/// everybody.
/// <para>
/// A role is made, renamed, given permissions and archived, and the role says each change that changed something
/// to a hook of the application's class: the partial methods <c>OnRenamed()</c>, <c>OnPermissionsSet(set)</c> and
/// <c>OnArchived()</c>, which the package's generator declares on the class. The application raises its own events
/// there, under its own names: the role raises none. The role manager the generator writes for the class,
/// <c>BoardRoleManager</c>, makes each change, and asks first whether the caller may: the permission the rules name for
/// it (<see cref="Access.Roles.ManagedWith"/>).
/// </para>
/// <para>
/// Its permissions are held to the resource's rules: a role gives only permissions the rules let a member's role give
/// (<see cref="MembershipRules.RolePermissions"/>), the permissions marked <c>InRoles</c>, and one that would give another
/// is refused. The rules are
/// handed to everything that changes the role, as the one declaration the application has of them, and a
/// refusal carries the code those rules give it.
/// </para>
/// <para>
/// An archived role gives nothing from then on and is not given again. It stays, and stays on the members
/// that hold it, so what they held still has a name. A role made from a starter role remembers which
/// (<see cref="MadeFrom"/>), and only when it is made: that is how the starter roles a customer has not made yet
/// are found, whatever it has called the ones it has. A role called "owner" is a role like any other: the owner of a
/// resource holds no role.
/// </para>
/// <para>
/// The class is the application's. Whose a role is, a customer's say, is a property the application adds
/// and sets in its own constructor, and keeping one customer's roles from another's is its own rule on its
/// own rows, as for any aggregate. That a name is not used twice is an index it declares over that
/// property and <see cref="Name"/>.
/// </para>
/// </summary>
/// <typeparam name="TRoleId">The application's id of the role: what a member holds it by.</typeparam>
[AggregateRootBase]
public abstract partial class CustomRoleAggregate<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The longest name a role may have.</summary>
    public const int MaxNameLength = 120;

    /// <summary>The longest description a role may have.</summary>
    public const int MaxDescriptionLength = 1000;

    /// <summary>
    /// Makes a role from <paramref name="draft"/>, in use. The application's class calls it from a
    /// constructor of its own, which sets what it added:
    /// <code>
    /// public PlotRole(PlotRoleId id, GardenId garden, CustomRoleDraft draft) : base(id, draft, PlotMembership.Rules)
    ///     =&gt; GardenId = garden;
    /// </code>
    /// </summary>
    /// <param name="id">The role's id.</param>
    /// <param name="draft">Its name, what it is for, the permissions it gives, and the starter role it is made from, if any.</param>
    /// <param name="rules">The rules of the resource the role is of.</param>
    /// <exception cref="ArgumentNullException"><paramref name="draft"/> or <paramref name="rules"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The rules do not say the resource's roles are kept, or the draft names a starter role the rules do not
    /// declare.
    /// </exception>
    /// <exception cref="RefusalException">
    /// <c>role-name-invalid</c> for a name that is blank or too long, or a description that is too long;
    /// <c>permission-not-for-members</c> for a permission the rules do not let a member's role give. Under the resource's codes.
    /// </exception>
    protected CustomRoleAggregate(TRoleId id, CustomRoleDraft draft, MembershipRules rules)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var codes = CodesOf(rules);

        Name = ValidName(draft.Name, codes);
        Description = ValidDescription(draft.Description, codes);
        Permissions = ValidKeys(draft.Permissions, rules);
        MadeFrom = StarterNamed(draft.MadeFrom, rules);
        Status = CustomRoleStatus.Active;
    }

    /// <summary>The role's name, with no white space around it.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What the role is for; empty when nobody said.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The permissions the role holds: distinct and in ordinal order. What it gives a member is asked of <see cref="Gives"/>.</summary>
    public IReadOnlyList<string> Permissions { get; private set; } = [];

    /// <summary>Whether the role is in use. An archived role gives nothing.</summary>
    public CustomRoleStatus Status { get; private set; }

    /// <summary>
    /// The name of the starter role this one was made from, as the resource's rules declare it, or
    /// <see langword="null"/> for a role a customer made. Set when the role is made and never after, so it
    /// outlasts a new name.
    /// </summary>
    public string? MadeFrom { get; private set; }

    /// <summary>
    /// Whether the role gives <paramref name="permission"/> to a member that holds it: it is in use, it holds the
    /// permission, and the resource's rules let a member's role give that permission. The last is asked again here, since
    /// the rules may have changed since the role was given its permissions.
    /// </summary>
    /// <param name="permission">A permission.</param>
    /// <param name="rules">The rules of the resource the role is of.</param>
    /// <exception cref="ArgumentNullException"><paramref name="permission"/> or <paramref name="rules"/> is null.</exception>
    public bool Gives(string permission, MembershipRules rules)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(rules);

        return Status == CustomRoleStatus.Active && Permissions.Contains(permission, StringComparer.Ordinal) && rules.RolePermissions.Allows(permission);
    }

    /// <summary>Renames the role and says again what it is for, and calls the class's <c>OnRenamed()</c> when that changed anything.</summary>
    /// <param name="name">Its new name; white space around it is dropped.</param>
    /// <param name="description">What it is for, or nothing.</param>
    /// <param name="rules">The rules of the resource the role is of.</param>
    /// <returns>Whether anything changed; <see langword="false"/> for the name and the description it has already.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    /// <exception cref="RefusalException"><c>role-archived</c>, <c>role-name-invalid</c>, under the resource's codes.</exception>
    public bool Rename(string name, string? description, MembershipRules rules)
    {
        var codes = CodesOf(rules);
        RequireInUse(codes);

        var validName = ValidName(name, codes);
        var validDescription = ValidDescription(description, codes);
        if (validName == Name && validDescription == Description)
        {
            return false;
        }

        Name = validName;
        Description = validDescription;
        (this as ICustomRoleHooks)?.Renamed();
        return true;
    }

    /// <summary>
    /// Sets the permissions the role gives, for every member that holds it. Every permission is one the resource's rules
    /// let a member's role give; white space around a permission is dropped, and a permission said twice is one permission.
    /// The class's <c>OnPermissionsSet(set)</c> is called when that changed anything.
    /// </summary>
    /// <param name="permissions">Every permission the role gives from now on.</param>
    /// <param name="rules">The rules of the resource the role is of.</param>
    /// <returns>The permissions that came in and the permissions that went out; neither for the permissions it has already.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="permissions"/> or <paramref name="rules"/> is null.</exception>
    /// <exception cref="RefusalException">
    /// <c>role-archived</c>; <c>permission-not-for-members</c>, naming every permission the rules do not let a member's role
    /// give. Under the resource's codes.
    /// </exception>
    public RolePermissionsSet SetPermissions(IReadOnlyCollection<string> permissions, MembershipRules rules)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        RequireInUse(CodesOf(rules));

        var next = ValidKeys(permissions, rules);
        var before = Permissions;

        // Both lists are distinct and in ordinal order, as a role's permissions are, so what is left of each is too.
        var set = new RolePermissionsSet([.. next.Except(before, StringComparer.Ordinal)], [.. before.Except(next, StringComparer.Ordinal)]);
        if (set.Changed)
        {
            Permissions = next;
            (this as ICustomRoleHooks)?.PermissionsSet(set);
        }

        return set;
    }

    /// <summary>
    /// Archives the role: it stays, on every member that holds it, gives nothing from now on, and is not
    /// given again. The class's <c>OnArchived()</c> is called after.
    /// </summary>
    /// <param name="rules">The rules of the resource the role is of.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    /// <exception cref="RefusalException"><c>role-archived</c>: it already is. Under the resource's codes.</exception>
    public void Archive(MembershipRules rules)
    {
        var codes = CodesOf(rules);
        RequireInUse(codes);

        Status = CustomRoleStatus.Archived;
        (this as ICustomRoleHooks)?.Archived();
    }

    /// <summary>The codes of rules whose roles a customer makes: the only rules a role is made and changed under.</summary>
    private static MembershipCodes CodesOf(MembershipRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules.Roles.Kind == RolesKind.Custom
            ? rules.Codes
            : throw new ArgumentException(
                "The rules '" + rules.Name + "' do not say a customer makes the resource's roles, so no role is made or changed under them: a member would hold it and "
                + "it would give nothing. Say it in the rules, Roles.Custom(), or hand over the rules of the resource this role is of.",
                nameof(rules));
    }

    private void RequireInUse(MembershipCodes codes)
    {
        if (Status == CustomRoleStatus.Archived)
        {
            throw codes.Refuse(MembershipRefusals.RoleIsArchived);
        }
    }

    /// <summary>A name as it is kept: with no white space around it, and refused when nothing is left or it is too long.</summary>
    private static string ValidName(string? name, MembershipCodes codes)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > MaxNameLength
            ? throw codes.Refuse(MembershipRefusals.RoleNameInvalid, ("Min", 1), ("Max", MaxNameLength))
            : trimmed;
    }

    /// <summary>A description as it is kept: with no white space around it, empty when there is none, and refused when it is too long.</summary>
    private static string ValidDescription(string? description, MembershipCodes codes)
    {
        var trimmed = description?.Trim() ?? string.Empty;
        return trimmed.Length > MaxDescriptionLength
            ? throw codes.Refuse(MembershipRefusals.RoleNameInvalid, ("Min", 0), ("Max", MaxDescriptionLength), (RefusalException.FieldArgument, "description"))
            : trimmed;
    }

    /// <summary>
    /// The permissions as a role keeps them, distinct and in ordinal order, refused when one of them is blank or is
    /// not a permission the rules let a member's role give.
    /// </summary>
    private static string[] ValidKeys(IReadOnlyCollection<string>? permissions, MembershipRules rules)
    {
        string[] asked = [.. (permissions ?? []).Select(permission => permission?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string[] refused = [.. asked.Where(permission => permission.Length == 0 || !rules.RolePermissions.Allows(permission))];

        return refused.Length > 0
            ? throw rules.Codes.Refuse(MembershipRefusals.PermissionNotForMembers, ("Permissions", string.Join(", ", refused)))
            : asked;
    }

    /// <summary>The starter role a draft names, which is one the rules declare, or <see langword="null"/> when it names none.</summary>
    private static string? StarterNamed(string? madeFrom, MembershipRules rules)
    {
        if (string.IsNullOrWhiteSpace(madeFrom))
        {
            return null;
        }

        return rules.Knows(new NamedRole(madeFrom))
            ? madeFrom
            : throw new ArgumentException(
                "The draft says the role is made from the starter role '" + madeFrom + "', and the rules '" + rules.Name + "' declare no role of that name. "
                + "Make the starter roles with the role manager's CreateStarterRolesAsync, and leave MadeFrom out of a draft for a role a customer makes.",
                "draft");
    }
}
