using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// A role of one kind of resource, kept as a row: a name, what it is for, and the permission keys it gives
/// a member that holds it. The application declares its own class with
/// <see cref="KeptRoleAttribute{TRoleId, TResource}"/>, for a resource whose rules say its roles are kept
/// (<see cref="MembershipRules.RolesKept"/>): roles a customer makes for itself, where the roles a
/// resource's rules declare are the same for everybody.
/// <para>
/// A role is made, renamed, given keys and archived, and each of those says what happened, so the
/// application raises its own events: the role raises none. Who may do any of it is the application's to
/// decide, command by command, as it decides who may give a role.
/// </para>
/// <para>
/// Its keys are held to the resource's rules: a role gives only keys the rules let a member's role give
/// (<see cref="MembershipRules.MemberKeys"/>), and one that would give another is refused. The rules are
/// handed to everything that changes the role, as the one declaration the application has of them, and a
/// refusal carries the code those rules give it.
/// </para>
/// <para>
/// An archived role gives nothing from then on and is not given again. It stays, and stays on the members
/// that hold it, so what they held still has a name. A role made from a starter role remembers which
/// (<see cref="MadeFrom"/>), and only when it is made: that is how the owner's role is found, whatever a
/// customer has called it since.
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
public abstract partial class KeptRoleAggregate<TRoleId>
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
    /// public PlotRole(PlotRoleId id, GardenId garden, KeptRoleDraft draft) : base(id, draft, PlotMembership.Rules)
    ///     =&gt; GardenId = garden;
    /// </code>
    /// </summary>
    /// <param name="id">The role's id.</param>
    /// <param name="draft">Its name, what it is for, the keys it gives, and the starter role it is made from, if any.</param>
    /// <param name="rules">The rules of the resource the role is of.</param>
    /// <exception cref="ArgumentNullException"><paramref name="draft"/> or <paramref name="rules"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The rules do not say the resource's roles are kept, or the draft names a starter role the rules do not
    /// declare.
    /// </exception>
    /// <exception cref="RefusalException">
    /// <c>role-name-invalid</c> for a name that is blank or too long, or a description that is too long;
    /// <c>key-not-for-members</c> for a key the rules do not let a member's role give. Under the resource's codes.
    /// </exception>
    protected KeptRoleAggregate(TRoleId id, KeptRoleDraft draft, MembershipRules rules)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var codes = CodesOf(rules);

        Name = ValidName(draft.Name, codes);
        Description = ValidDescription(draft.Description, codes);
        Keys = ValidKeys(draft.Keys, rules);
        MadeFrom = StarterNamed(draft.MadeFrom, rules);
        Status = KeptRoleStatus.Active;
    }

    /// <summary>The role's name, with no white space around it.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What the role is for; empty when nobody said.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The permission keys the role holds: distinct and in ordinal order. What it gives a member is asked of <see cref="Gives"/>.</summary>
    public IReadOnlyList<string> Keys { get; private set; } = [];

    /// <summary>Whether the role is in use. An archived role gives nothing.</summary>
    public KeptRoleStatus Status { get; private set; }

    /// <summary>
    /// The name of the starter role this one was made from, as the resource's rules declare it, or
    /// <see langword="null"/> for a role a customer made. Set when the role is made and never after, so it
    /// outlasts a new name.
    /// </summary>
    public string? MadeFrom { get; private set; }

    /// <summary>
    /// Whether the role gives <paramref name="key"/> to a member that holds it: it is in use, it holds the
    /// key, and the resource's rules let a member's role give that key. The last is asked again here, since
    /// the rules may have changed since the role was given its keys.
    /// </summary>
    /// <param name="key">A permission key.</param>
    /// <param name="rules">The rules of the resource the role is of.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="rules"/> is null.</exception>
    public bool Gives(string key, MembershipRules rules)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(rules);

        return Status == KeptRoleStatus.Active && Keys.Contains(key, StringComparer.Ordinal) && rules.MemberKeys.Allows(key);
    }

    /// <summary>Renames the role and says again what it is for.</summary>
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
        return true;
    }

    /// <summary>
    /// Sets the keys the role gives, for every member that holds it. Every key is one the resource's rules
    /// let a member's role give; white space around a key is dropped, and a key said twice is one key.
    /// </summary>
    /// <param name="keys">Every key the role gives from now on.</param>
    /// <param name="rules">The rules of the resource the role is of.</param>
    /// <returns>The keys that came in and the keys that went out; neither for the keys it has already.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> or <paramref name="rules"/> is null.</exception>
    /// <exception cref="RefusalException">
    /// <c>role-archived</c>; <c>key-not-for-members</c>, naming every key the rules do not let a member's role
    /// give. Under the resource's codes.
    /// </exception>
    public RoleKeysSet SetKeys(IReadOnlyCollection<string> keys, MembershipRules rules)
    {
        ArgumentNullException.ThrowIfNull(keys);
        RequireInUse(CodesOf(rules));

        var next = ValidKeys(keys, rules);
        var before = Keys;

        // Both lists are distinct and in ordinal order, as a role's keys are, so what is left of each is too.
        var set = new RoleKeysSet([.. next.Except(before, StringComparer.Ordinal)], [.. before.Except(next, StringComparer.Ordinal)]);
        if (set.Changed)
        {
            Keys = next;
        }

        return set;
    }

    /// <summary>
    /// Archives the role: it stays, on every member that holds it, gives nothing from now on, and is not
    /// given again. The role every owner holds is not archived: nobody could be made an owner after.
    /// </summary>
    /// <param name="rules">The rules of the resource the role is of.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    /// <exception cref="RefusalException">
    /// <c>role-archived</c>: it already is; <c>owner-role-stays</c> for the role made from the owner's starter
    /// role. Under the resource's codes.
    /// </exception>
    public void Archive(MembershipRules rules)
    {
        var codes = CodesOf(rules);
        RequireInUse(codes);

        if (MadeFrom is not null && string.Equals(MadeFrom, rules.OwnerRole, StringComparison.Ordinal))
        {
            throw codes.Of(MembershipRefusals.OwnerRoleStays);
        }

        Status = KeptRoleStatus.Archived;
    }

    /// <summary>The codes of rules whose roles are kept for the resource: the only rules a role is made and changed under.</summary>
    private static MembershipCodes CodesOf(MembershipRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules.RolesKept
            ? rules.Codes
            : throw new ArgumentException(
                "The rules '" + rules.Name + "' do not say the resource's roles are kept, so no role is made or changed under them: a member would hold it and "
                + "it would give nothing. Say it in the rules, rolesKept: true, or hand over the rules of the resource this role is of.",
                nameof(rules));
    }

    private void RequireInUse(MembershipCodes codes)
    {
        if (Status == KeptRoleStatus.Archived)
        {
            throw codes.Of(MembershipRefusals.RoleIsArchived);
        }
    }

    /// <summary>A name as it is kept: with no white space around it, and refused when nothing is left or it is too long.</summary>
    private static string ValidName(string? name, MembershipCodes codes)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > MaxNameLength
            ? throw codes.Of(MembershipRefusals.RoleNameInvalid, ("Min", 1), ("Max", MaxNameLength))
            : trimmed;
    }

    /// <summary>A description as it is kept: with no white space around it, empty when there is none, and refused when it is too long.</summary>
    private static string ValidDescription(string? description, MembershipCodes codes)
    {
        var trimmed = description?.Trim() ?? string.Empty;
        return trimmed.Length > MaxDescriptionLength
            ? throw codes.Of(MembershipRefusals.RoleNameInvalid, ("Min", 0), ("Max", MaxDescriptionLength), (RefusalException.FieldArgument, "description"))
            : trimmed;
    }

    /// <summary>
    /// The keys as a role keeps them, distinct and in ordinal order, refused when one of them is blank or is
    /// not a key the rules let a member's role give.
    /// </summary>
    private static string[] ValidKeys(IReadOnlyCollection<string>? keys, MembershipRules rules)
    {
        string[] asked = [.. (keys ?? []).Select(key => key?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string[] refused = [.. asked.Where(key => key.Length == 0 || !rules.MemberKeys.Allows(key))];

        return refused.Length > 0
            ? throw rules.Codes.Of(MembershipRefusals.KeyNotForMembers, ("Keys", string.Join(", ", refused)))
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
                + "Make the starter roles from the drafts StarterRoles.Missing answers, and leave MadeFrom out of a draft for a role a customer makes.",
                "draft");
    }
}
