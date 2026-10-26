using System.Text;
using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// The set a <see cref="ContributedFunction"/> answers for one resource: what a rule asks through a
/// <c>[ResourceAccessContract&lt;TKey&gt;]</c>, by the resource's id and the set, with no function's name. A script
/// writes such a rule with this function, whatever the contribution calls it.
/// <code>
/// new ContributedFunction("documents_i_see", "", "SETOF uuid", body, SecurityDefiner: true, GrantTo: [RowAccessRoles.User],
///     Answers: new ResourceAccessAnswer(typeof(DocumentId), ResourceAccessSet.Seen))
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// One function answers a set for a resource, in every context a script is written with: a second one is refused,
/// naming both. The function answers with the resource's ids, <c>SETOF</c> their type, and takes what the set is
/// asked with: nothing for <see cref="ResourceAccessSet.Seen"/>, the key as <c>text</c> for
/// <see cref="ResourceAccessSet.HeldOn"/>. A rule that asks a set no contribution answers is refused when its
/// script is written, naming the rule and the resource.
/// </para>
/// <para>
/// The Membership package says this of the two functions it writes for a resource with members, so a host that
/// lists that package's contribution has nothing more to say.
/// </para>
/// </remarks>
/// <param name="Key">The type of the resource's id, as the model maps it: what the rule's contract is declared with.</param>
/// <param name="Set">The set the function answers.</param>
public sealed record ResourceAccessAnswer(Type Key, ResourceAccessSet Set)
{
    /// <summary>The name a rule asks the set by: what <see cref="NameOf"/> answers for <see cref="Key"/> and <see cref="Set"/>.</summary>
    /// <exception cref="ArgumentNullException"><see cref="Key"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="Set"/> is no set <see cref="ResourceAccessSet"/> has.</exception>
    public string Name => NameOf(Key, Set);

    /// <summary>
    /// What the record prints: its <see cref="Key"/> and <see cref="Set"/>, and not <see cref="Name"/>, which throws for
    /// the very answers a message, a log line or a debugger should show as they are: one with no key, or with a set
    /// the enum lacks, which a script refuses, naming the function.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Key = ").Append(Key).Append(", Set = ").Append(Set);
        return true;
    }

    /// <summary>
    /// The name a rule asks a resource access set by, in its SQL as <c>{fn:...}</c>: <c>@</c>, the full name of the
    /// resource's id, and the set, <c>@Projects.Contracts.ProjectId/seen</c> or <c>.../held_on</c>. It is what a
    /// contract's generated <c>Name</c> says, so SQL a contribution writes asks the set the same way.
    /// </summary>
    /// <param name="key">The type of the resource's id.</param>
    /// <param name="set">The set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="set"/> is no set <see cref="ResourceAccessSet"/> has.</exception>
    public static string NameOf(Type key, ResourceAccessSet set)
    {
        ArgumentNullException.ThrowIfNull(key);

        return "@" + key.FullName + "/" + set switch
        {
            ResourceAccessSet.Seen => "seen",
            ResourceAccessSet.HeldOn => "held_on",
            _ => throw new ArgumentOutOfRangeException(nameof(set), set, "A resource access set is Seen or HeldOn."),
        };
    }

    /// <summary>
    /// Whether <paramref name="name"/>, as SQL asks a function, is the name of a resource access set: no logical
    /// name of a function starts with <c>@</c>.
    /// </summary>
    /// <param name="name">A name from <c>{fn:...}</c>.</param>
    public static bool IsName(string name) => !string.IsNullOrEmpty(name) && name[0] == '@';

    /// <summary>
    /// A resource access set's name in words, for a message that names it: <c>the resources the caller sees, by the
    /// id Projects.Contracts.ProjectId</c>.
    /// </summary>
    /// <param name="name">The set's name, as <see cref="NameOf"/> spells it.</param>
    public static string Described(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var slash = name.LastIndexOf('/');
        var key = slash > 1 ? name[1..slash] : name.TrimStart('@');
        var set = slash > 1 ? name[(slash + 1)..] : "";
        return $"the resources {(set == "held_on" ? "the caller holds a key on" : "the caller sees")}, by the id {key}";
    }
}
