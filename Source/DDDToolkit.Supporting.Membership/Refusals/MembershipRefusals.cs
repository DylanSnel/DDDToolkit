using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// Every way Membership refuses, named once, with its kind and its English text: the package's own word for
/// each rule.
/// <para>
/// A caller is never refused with one of these as it stands. Members belong to a resource of the application,
/// and an application may have several kinds of resource with members, each with its own codes: a refusal
/// carries the code that resource's <see cref="MembershipCodes"/> give the rule, <c>documents.already-member</c>
/// for the rule named <see cref="AlreadyMember"/> here. The names below are what a code map is written over,
/// what the entries of <c>MembershipFailures.resx</c> and its Dutch sibling are called, and what the one rule a
/// member checks by itself reports (<see cref="RoleHeld"/>), since a member does not know whose it is. A role
/// kept for a resource reports the two rules it checks by itself the same way (<see cref="RoleNameInvalid"/>,
/// <see cref="RoleKeysNotNormalized"/>).
/// </para>
/// <para>
/// An argument a message shows is a value, such as a key or an id, never a word: the English and the Dutch
/// texts are whole sentences of their own. A refusal of kind <see cref="RefusalKind.Invalid"/> names the input
/// it is about in the argument <see cref="RefusalException.FieldArgument"/>, in the word the member list calls
/// that input by, <c>role</c> or <c>until</c>, or the word a role calls it by, <c>name</c>, <c>description</c>
/// or <c>keys</c>, so a form puts the text under the input; an edge whose input is called otherwise maps the
/// word.
/// </para>
/// <para>
/// Where a rule below says its argument is <c>Member</c>, a resource with a word of its own for a member
/// carries it under that word (<see cref="MembershipCodes.WithMemberArgument"/>).
/// </para>
/// <para>
/// The texts say "member" and "role" and never what the resource is: a resource that wants its own words,
/// "staff" for its members say, registers a resource file of its own for its own codes in front of the
/// package's. The Dutch texts have no word for the reader: which of the familiar and the formal one fits is
/// the application's to say.
/// </para>
/// </summary>
public static class MembershipRefusals
{
    /// <summary>What every name here starts with.</summary>
    internal const string Prefix = "membership";

    // ---------------------------------------------------------------- who is asking

    /// <summary>
    /// The resource is not one the caller may see: it does not exist, or is out of the caller's reach. One
    /// answer for both, so a refusal never tells anyone a resource exists.
    /// </summary>
    public const string NotFound = "membership.not-found";

    /// <summary>The caller may see the resource but does not hold the key the request needs on it. Argument: <c>Key</c>.</summary>
    public const string NotPermitted = "membership.not-permitted";

    // ---------------------------------------------------------------- who may be a member, in which role

    /// <summary>The one to be made a member, or owner, is not active, or is not known where members come from. Argument: <c>Member</c>.</summary>
    public const string MemberNotActive = "membership.member-not-active";

    /// <summary>
    /// The role is not one that goes to a member of this resource: its rules do not know it, or what decides
    /// which roles go on a member list rules it out. Arguments: <c>Role</c>, and <c>Field</c>, which is <c>role</c>.
    /// </summary>
    public const string RoleNotForMembers = "membership.role-not-for-members";

    /// <summary>There is no role in use to give an owner, so nobody can be named one.</summary>
    public const string NoOwnerRole = "membership.no-owner-role";

    // ---------------------------------------------------------------- the member list

    /// <summary>
    /// The member has a membership already, now or from a later start: a member is on the list once, and holds
    /// its roles there. A membership that has ended is not in the way. Argument: <c>Member</c>.
    /// </summary>
    public const string AlreadyMember = "membership.already-member";

    /// <summary>
    /// The one named is not a member, or its membership has ended, which a role would not bring back.
    /// Argument: <c>Member</c>.
    /// </summary>
    public const string MemberNotFound = "membership.member-not-found";

    /// <summary>The member holds the role already, now or from a later start. Arguments: <c>Member</c>, <c>Role</c>.</summary>
    public const string RoleHeld = "membership.role-held";

    /// <summary>The member does not hold the role. Arguments: <c>Member</c>, <c>Role</c>.</summary>
    public const string RoleNotHeld = "membership.role-not-held";

    /// <summary>The change would take the owner off the member list, or take the owner's role.</summary>
    public const string OwnerProtected = "membership.owner-protected";

    /// <summary>The member named owner is the owner already.</summary>
    public const string AlreadyOwner = "membership.already-owner";

    /// <summary>A membership, or a role, would end at or before its start. Argument: <c>Field</c>, which is <c>until</c>.</summary>
    public const string InvalidPeriod = "membership.invalid-period";

    // ---------------------------------------------------------------- the roles kept for a resource

    /// <summary>The role is archived: it gives nothing, and it is not renamed, given keys or archived again.</summary>
    public const string RoleIsArchived = "membership.role-archived";

    /// <summary>
    /// A role's name is blank or too long, or its description is too long. Arguments: <c>Min</c>, <c>Max</c>,
    /// and <c>Field</c>, which is <c>name</c> or <c>description</c>.
    /// </summary>
    public const string RoleNameInvalid = "membership.role-name-invalid";

    /// <summary>
    /// A role would give a key the resource's rules do not let a member's role give. Arguments: <c>Keys</c>,
    /// the keys refused, and <c>Field</c>, which is <c>keys</c>.
    /// </summary>
    public const string KeyNotForMembers = "membership.key-not-for-members";

    /// <summary>The role is the one every owner holds, and that role is not archived: without it nobody could be an owner.</summary>
    public const string OwnerRoleStays = "membership.owner-role-stays";

    /// <summary>A role's keys are not distinct, not blank and in ordinal order: what a role checks of itself after the fact.</summary>
    public const string RoleKeysNotNormalized = "membership.role-keys-not-normalized";

    /// <summary>
    /// The kind and English text of every rule. The text is a template: <c>{Name}</c> is filled from the
    /// argument of that name, exactly as a translation in <c>MembershipFailures.nl.resx</c> is.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (RefusalKind Kind, string Template)> Table =
        new ReadOnlyDictionary<string, (RefusalKind Kind, string Template)>(new Dictionary<string, (RefusalKind Kind, string Template)>(StringComparer.Ordinal)
        {
            [NotFound] = (RefusalKind.NotFound, "That was not found, or it is not one you can see."),
            [NotPermitted] = (RefusalKind.NotPermitted, "Doing this needs the key {Key}."),

            [MemberNotActive] = (RefusalKind.Conflict, "That one cannot be a member: not active, or not known here."),
            [RoleNotForMembers] = (RefusalKind.Invalid, "That role cannot be given to a member here."),
            [NoOwnerRole] = (RefusalKind.Conflict, "There is no role in use to give an owner."),

            [AlreadyMember] = (RefusalKind.Conflict, "That one is a member already, or is due to become one. Give the member a role instead."),
            [MemberNotFound] = (RefusalKind.NotFound, "That one is not a member, or the membership has ended."),
            [RoleHeld] = (RefusalKind.Conflict, "That member already has this role."),
            [RoleNotHeld] = (RefusalKind.NotFound, "That member does not have this role."),
            [OwnerProtected] = (RefusalKind.Conflict, "The owner stays a member, in the owner's role, until somebody else is named owner."),
            [AlreadyOwner] = (RefusalKind.Conflict, "That member is the owner already."),
            [InvalidPeriod] = (RefusalKind.Invalid, "A period's end must come after its start."),

            [RoleIsArchived] = (RefusalKind.Conflict, "That role is archived."),
            [RoleNameInvalid] = (RefusalKind.Invalid, "Enter {Min} to {Max} characters."),
            [KeyNotForMembers] = (RefusalKind.Invalid, "A role cannot give these keys here: {Keys}."),
            [OwnerRoleStays] = (RefusalKind.Conflict, "The role every owner holds cannot be archived."),
            [RoleKeysNotNormalized] = (RefusalKind.Conflict, "A role's keys must be distinct, not blank and in ordinal order."),
        });

    /// <summary>
    /// The input each rule of kind <see cref="RefusalKind.Invalid"/> is about, in the word the member list, or a
    /// role, calls it by. A refusal carries it as the argument <see cref="RefusalException.FieldArgument"/>, so
    /// the place that refuses does not have to remember to; one that is about another input says which.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Fields =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RoleNotForMembers] = "role",
            [InvalidPeriod] = "until",
            [RoleNameInvalid] = "name",
            [KeyNotForMembers] = "keys",
        });

    /// <summary>Every name, in the order the table lists them.</summary>
    public static IReadOnlyCollection<string> Codes { get; } = Table.Keys.ToArray();

    /// <summary>The kind of refusal a rule is.</summary>
    /// <param name="code">One of the names above.</param>
    /// <exception cref="ArgumentException">The name is not one of Membership's.</exception>
    public static RefusalKind KindOf(string code) => Row(code).Kind;

    /// <summary>
    /// The English text of a rule, as a template: a placeholder such as <c>{Key}</c> is filled from the argument
    /// of that name.
    /// </summary>
    /// <param name="code">One of the names above.</param>
    /// <exception cref="ArgumentException">The name is not one of Membership's.</exception>
    public static string TemplateOf(string code) => Row(code).Template;

    /// <summary>The kind and English template of <paramref name="code"/>.</summary>
    internal static (RefusalKind Kind, string Template) Row(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return Table.TryGetValue(code, out var row)
            ? row
            : throw new ArgumentException("'" + code + "' is not one of Membership's refusals. They are the constants of MembershipRefusals.", nameof(code));
    }

    /// <summary>
    /// The arguments a refusal or a failure of <paramref name="code"/> carries: what it was given, and the
    /// input the rule is about, unless <paramref name="arguments"/> name one themselves.
    /// </summary>
    internal static Dictionary<string, object?> Named(string code, (string Name, object? Value)[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var named = new Dictionary<string, object?>(arguments.Length + 1, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in arguments)
        {
            named[name] = value;
        }

        if (Fields.TryGetValue(code, out var field))
        {
            named.TryAdd(RefusalException.FieldArgument, field);
        }

        return named;
    }

    /// <summary>
    /// Fills <c>{Name}</c> placeholders, in the invariant culture because the English text is the domain's
    /// own and must read the same wherever it runs. A placeholder with no argument is left as written.
    /// </summary>
    internal static string Fill(string template, IReadOnlyDictionary<string, object?> arguments)
    {
        if (template.IndexOf('{') < 0)
        {
            return template;
        }

        var result = new StringBuilder(template.Length + 16);
        var index = 0;
        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            var close = open < 0 ? -1 : template.IndexOf('}', open + 1);
            if (close < 0)
            {
                result.Append(template, index, template.Length - index);
                break;
            }

            result.Append(template, index, open - index);
            var name = template.Substring(open + 1, close - open - 1);
            if (arguments.TryGetValue(name, out var value))
            {
                result.Append(value switch
                {
                    null => string.Empty,
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString(),
                });
            }
            else
            {
                result.Append(template, open, close - open + 1);
            }

            index = close + 1;
        }

        return result.ToString();
    }
}
