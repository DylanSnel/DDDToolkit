namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// The names of the four set functions a database answers a resource's membership questions with, for the
/// caller of the connection. They are the resource's own names, because an application may have several kinds
/// of resource with members in one schema. Rules do not ask them by name: they ask the last two by the
/// resource's id, through a <c>[ResourceAccessContract&lt;TKey&gt;]</c>, and the export writes the policy with
/// whatever the names are. So the names are said only to keep ones a database already has; left out, the rules
/// take them from the resource's name (<see cref="For"/>).
/// </summary>
/// <param name="AsMember">Answers the ids of the resources the caller is a member of now.</param>
/// <param name="AsMemberWith">Answers, for a key, the ids of the resources where the caller is a member now and holds a role now that gives the key.</param>
/// <param name="Seen">
/// Answers the ids of the resources the caller sees: those it is a member of now, those it owns, from the moment
/// a resource's row names it as owner, and, where the rules say so, those reached from above.
/// </param>
/// <param name="HeldOn">Answers, for a key, the ids of the resources the caller holds the key on.</param>
public sealed record MembershipFunctions(string AsMember, string AsMemberWith, string Seen, string HeldOn)
{
    /// <summary>
    /// The names a resource's functions have unless its rules say otherwise: <c>documents_as_member</c>,
    /// <c>documents_as_member_with</c>, <c>documents_i_see</c> and <c>documents_where_i_hold</c> for the
    /// resource named <c>documents</c>. They follow from the name alone, so they stay what they are for as long as
    /// the resource keeps its name.
    /// </summary>
    /// <param name="name">The resource's name, as its rules have it. A dot or a dash in it is written as an underscore.</param>
    public static MembershipFunctions For(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var stem = name.Replace('.', '_').Replace('-', '_');
        return new MembershipFunctions(stem + "_as_member", stem + "_as_member_with", stem + "_i_see", stem + "_where_i_hold");
    }

    /// <summary>The four names, in the order they are declared.</summary>
    internal IReadOnlyList<string> All => [AsMember, AsMemberWith, Seen, HeldOn];
}
