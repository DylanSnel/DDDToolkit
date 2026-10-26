using System.Collections.ObjectModel;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// The codes one resource refuses with: for each rule <see cref="MembershipRefusals"/> names, the code a
/// caller of that resource branches on.
/// <code>
/// public static MembershipCodes Membership { get; } = MembershipCodes.Under("documents");
/// </code>
/// gives <c>documents.not-found</c>, <c>documents.already-member</c> and so on: the rule's own name under the
/// resource's prefix. A resource that calls a rule otherwise says so:
/// <code>
/// MembershipCodes.Under("folders").With(MembershipRefusals.AlreadyMember, "already-on-folder");
/// </code>
/// And a resource that has a word of its own for a member has a client read the member from an argument of
/// that name, <c>Staff</c> instead of <c>Member</c>:
/// <code>
/// MembershipCodes.Under("folders").WithMemberArgument("Staff");
/// </code>
/// <para>
/// A resource has its own codes because an application may have several kinds of resource with members, and
/// a client should read from a code which one refused it. The member list of a resource refuses with them
/// (<see cref="MemberList{TMember, TId, TMemberId, TRoleId}"/>), and so do the access questions and the
/// admission of that resource, which read them from its rules. Declare them once, where the aggregate can
/// reach them, and hand the same instance to both: a static property of the aggregate, which the member list
/// the package's generator writes there is made with, and which the rules take as their <c>codes</c>.
/// </para>
/// <para>
/// The kind and the English text of a refusal are the package's whatever its code. For the reader's language
/// the package ships its texts in English and Dutch, and whatever registers the resource offers them under
/// this resource's codes (<see cref="TextKeys"/>), so an application that localizes its failures adds no
/// line for them. A resource file of the application's own for these codes, in its own words, is asked first.
/// </para>
/// <para>
/// It never changes: <see cref="With"/> and <see cref="WithMemberArgument"/> answer another map.
/// </para>
/// </summary>
public sealed class MembershipCodes
{
    /// <summary>
    /// The name of the argument a refusal carries the member in, unless the resource has a word of its own
    /// for it (<see cref="WithMemberArgument"/>): <c>Member</c>.
    /// </summary>
    public const string DefaultMemberArgument = "Member";

    /// <summary>
    /// The rules that are reported under the package's own name, whichever resource they are about: the one a
    /// member checks by itself, and the two a role kept for a resource checks by itself.
    /// </summary>
    private static readonly string[] OwnNames = [MembershipRefusals.RoleHeld, MembershipRefusals.RoleNameInvalid, MembershipRefusals.RoleKeysNotNormalized];

    /// <summary>The names of the other arguments a refusal of Membership carries, which the member's cannot take.</summary>
    private static readonly string[] OtherArguments = ["Role", "Key", "Keys", "Min", "Max", RefusalException.FieldArgument];

    private readonly IReadOnlyDictionary<string, string> _codes;

    private MembershipCodes(string prefix, Dictionary<string, string> codes, string memberArgument)
    {
        Prefix = prefix;
        MemberArgument = memberArgument;
        _codes = new ReadOnlyDictionary<string, string>(codes);
        All = [.. MembershipRefusals.Codes.Select(code => codes[code])];

        var texts = new Dictionary<string, string>(codes.Count + 1, StringComparer.Ordinal);
        foreach (var (rule, code) in codes)
        {
            texts[code] = rule;
        }

        // What a member, or a role, checks by itself it reports under the package's own name for the rule:
        // neither knows whose it is. Those texts belong to every resource that has members.
        foreach (var own in OwnNames)
        {
            texts.TryAdd(own, own);
        }

        TextKeys = new ReadOnlyDictionary<string, string>(texts);
    }

    /// <summary>
    /// The codes of a resource whose codes start with <paramref name="prefix"/>: each rule's own name, under
    /// that prefix.
    /// </summary>
    /// <param name="prefix">
    /// What the resource's codes start with, usually the name of its module: lower case letters, digits and
    /// dashes, in parts joined by dots, each part starting with a letter, such as <c>documents</c>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> is not a prefix a code can have.</exception>
    public static MembershipCodes Under(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (!IsPrefix(prefix))
        {
            throw new ArgumentException(
                "'" + prefix + "' cannot start a refusal code. A prefix is lower case letters, digits and dashes, in parts joined by dots, "
                + "each part starting with a letter, such as 'documents'.",
                nameof(prefix));
        }

        return new MembershipCodes(
            prefix,
            MembershipRefusals.Codes.ToDictionary(code => code, code => prefix + "." + SuffixOf(code), StringComparer.Ordinal),
            DefaultMemberArgument);
    }

    /// <summary>What every code of the resource starts with.</summary>
    public string Prefix { get; }

    /// <summary>
    /// The name of the argument a refusal of this resource carries the member in:
    /// <see cref="DefaultMemberArgument"/> unless the resource has a word of its own
    /// (<see cref="WithMemberArgument"/>).
    /// </summary>
    public string MemberArgument { get; }

    /// <summary>Every code of the resource, one per rule, in the order <see cref="MembershipRefusals.Codes"/> lists the rules.</summary>
    public IReadOnlyList<string> All { get; }

    /// <summary>
    /// For each code a failure about this resource's members carries, the entry of
    /// <c>MembershipFailures.resx</c> that holds its text: the resource's own codes, and the names of the rules
    /// a member, or a role, checks by itself, which it reports as they stand.
    /// </summary>
    public IReadOnlyDictionary<string, string> TextKeys { get; }

    /// <summary>The code this resource gives the rule <paramref name="code"/>.</summary>
    /// <param name="code">One of the constants of <see cref="MembershipRefusals"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not one of Membership's.</exception>
    public string this[string code]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(code);
            return _codes.TryGetValue(code, out var own) ? own : throw NotOurs(code);
        }
    }

    /// <summary>
    /// The same codes, but for the rule <paramref name="code"/>, which this resource calls
    /// <paramref name="suffix"/>: for a resource whose codes were there before, or that has a word of its own
    /// for its members.
    /// </summary>
    /// <param name="code">One of the constants of <see cref="MembershipRefusals"/>.</param>
    /// <param name="suffix">What comes after the prefix: lower case letters, digits and dashes, starting with a letter.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> is not one of Membership's, <paramref name="suffix"/> is not one a code can end
    /// in, or another rule of the resource has that code already: two rules a client could not tell apart.
    /// </exception>
    public MembershipCodes With(string code, string suffix)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(suffix);
        if (!_codes.ContainsKey(code))
        {
            throw NotOurs(code);
        }

        if (!IsPart(suffix))
        {
            throw new ArgumentException(
                "'" + suffix + "' cannot end a refusal code. A suffix is lower case letters, digits and dashes, starting with a letter, such as 'already-member'.",
                nameof(suffix));
        }

        var renamed = Prefix + "." + suffix;
        if (_codes.FirstOrDefault(pair => pair.Value == renamed && pair.Key != code) is { Key: { } taken })
        {
            throw new ArgumentException(
                "'" + renamed + "' is this resource's code for " + taken + " already. Two rules with one code cannot be told apart by a client.",
                nameof(suffix));
        }

        return new MembershipCodes(Prefix, new Dictionary<string, string>(_codes, StringComparer.Ordinal) { [code] = renamed }, MemberArgument);
    }

    /// <summary>
    /// The same codes, for a resource whose refusals carry the member in an argument called
    /// <paramref name="name"/> instead of <see cref="DefaultMemberArgument"/>: a resource that has
    /// a word of its own for a member, or whose clients read that argument already. What a client reads from
    /// a refusal is its code and its arguments, so a resource's own words go for both.
    /// <para>
    /// Every refusal of this resource then carries the member under that name, whoever makes it: the member
    /// list, the admission, the two checks of the whole list. The one rule a member checks by itself keeps
    /// <see cref="DefaultMemberArgument"/>, as it keeps the package's name for the rule: a member
    /// does not know whose it is.
    /// </para>
    /// </summary>
    /// <param name="name">The argument's name: letters and digits, starting with a letter, such as <c>Staff</c>.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> cannot name an argument, or is the name of another argument a refusal of
    /// Membership carries: two values under one name.
    /// </exception>
    public MembershipCodes WithMemberArgument(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]) || !name.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException(
                "'" + name + "' cannot name an argument of a refusal. A name is letters and digits, starting with a letter, such as 'Staff'.",
                nameof(name));
        }

        if (OtherArguments.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "'" + name + "' is the name of another argument a refusal carries. The member needs a name of its own, such as 'Staff'.",
                nameof(name));
        }

        return new MembershipCodes(Prefix, new Dictionary<string, string>(_codes, StringComparer.Ordinal), name);
    }

    /// <summary>
    /// The refusal for the rule <paramref name="code"/>, under this resource's code for it, with the rule's
    /// kind, its English message filled from <paramref name="arguments"/>, and the arguments themselves for a
    /// translation to use. A rule that is about one input also carries that input's name, as the argument
    /// <see cref="RefusalException.FieldArgument"/>, unless <paramref name="arguments"/> name one themselves.
    /// The member, given as <see cref="DefaultMemberArgument"/>, is carried as this resource's
    /// <see cref="MemberArgument"/>.
    /// </summary>
    /// <param name="code">One of the constants of <see cref="MembershipRefusals"/>.</param>
    /// <param name="arguments">The values the message names, by name.</param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not one of Membership's: a refusal nobody can look up is a bug.</exception>
    public RefusalException Refuse(string code, params (string Name, object? Value)[] arguments)
    {
        var own = this[code];
        var (kind, template) = MembershipRefusals.Row(code);
        var named = Arguments(code, arguments);

        return new RefusalException(own, kind, MembershipRefusals.Fill(template, named), named);
    }

    /// <summary>
    /// What a rule about the whole of this resource's member list reports when it does not hold after the
    /// fact: the same English text as the refusal, and the same arguments under the same names, so one
    /// translation serves both and a client reads both alike.
    /// </summary>
    internal InvariantFailure FailureOf(string code, params (string Name, object? Value)[] arguments)
    {
        var named = Arguments(code, arguments);
        return new InvariantFailure(MembershipRefusals.Fill(MembershipRefusals.TemplateOf(code), named), named);
    }

    /// <summary>
    /// What a rule a member, or a role, checks by itself reports when it does not hold: the refusal's English
    /// text and arguments, under the package's own names, because neither knows whose it is.
    /// </summary>
    internal static InvariantFailure Failure(string code, params (string Name, object? Value)[] arguments)
    {
        var named = MembershipRefusals.Named(code, arguments);
        return new InvariantFailure(MembershipRefusals.Fill(MembershipRefusals.TemplateOf(code), named), named);
    }

    /// <summary>The arguments of a refusal or a failure of this resource, with the member under this resource's name for it.</summary>
    private Dictionary<string, object?> Arguments(string code, (string Name, object? Value)[] arguments)
    {
        var named = MembershipRefusals.Named(code, arguments);
        if (!string.Equals(MemberArgument, DefaultMemberArgument, StringComparison.Ordinal)
            && named.Remove(DefaultMemberArgument, out var member))
        {
            named[MemberArgument] = member;
        }

        return named;
    }

    /// <summary>Whether <paramref name="prefix"/> can start a code: parts joined by dots, each a letter and then letters, digits and dashes.</summary>
    internal static bool IsPrefix(string prefix) => prefix.Split('.').All(IsPart);

    private static string SuffixOf(string code) => code[(MembershipRefusals.Prefix.Length + 1)..];

    /// <summary>Whether <paramref name="part"/> is one part of a code: a letter, then letters, digits and dashes.</summary>
    private static bool IsPart(string part)
        => part.Length > 0
           && part[0] is >= 'a' and <= 'z'
           && part.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static ArgumentException NotOurs(string code)
        => new("'" + code + "' is not one of Membership's refusals. Name a rule by its constant of MembershipRefusals.", nameof(code));
}
