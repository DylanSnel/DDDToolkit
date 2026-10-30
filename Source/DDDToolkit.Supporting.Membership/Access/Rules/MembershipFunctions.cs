namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// The names of the five set functions a database answers a resource's membership questions with, for the
/// caller of the connection. They are the resource's own names, because an application may have several kinds
/// of resource with members in one schema. They follow from the resource's name (<see cref="For"/>), so a resource
/// that keeps its name keeps them; one that is renamed with <c>[Members("...")]</c> gets new ones. Rules do not ask
/// them by name: they ask the last three by the resource's id, <c>user.Sees&lt;TKey&gt;()</c>,
/// <c>user.Holds&lt;TKey&gt;(permission)</c> and <c>user.FellowMembers&lt;TKey, TMemberId&gt;()</c>,
/// and the export writes the policy with whatever the names are.
/// </summary>
/// <param name="AsMember">Answers the ids of the resources the caller is a member of now.</param>
/// <param name="AsMemberWith">Answers, for a permission, the ids of the resources where the caller is a member now and holds a role now that gives the permission.</param>
/// <param name="Seen">
/// Answers the ids of the resources the caller sees: those it is a member of now, those it owns, from the moment
/// a resource's row names it as owner, and, where the rules say so, those reached from above.
/// </param>
/// <param name="HeldOn">Answers, for a permission, the ids of the resources the caller holds the permission on.</param>
/// <param name="FellowMembers">
/// Answers the ids of the members, now, of every resource the caller is a member of now, the caller among them: the
/// people on the projects a seat works on, which a read rule on the application's seat class may ask.
/// </param>
public sealed record MembershipFunctions(string AsMember, string AsMemberWith, string Seen, string HeldOn, string FellowMembers)
{
    /// <summary>
    /// The names a resource's functions have: <c>documents_as_member</c>,
    /// <c>documents_as_member_with</c>, <c>documents_i_see</c>, <c>documents_where_i_hold</c> and
    /// <c>documents_fellow_members</c> for the resource named <c>documents</c>. They follow from the name alone, so they stay what they are for as long as
    /// the resource keeps its name.
    /// </summary>
    /// <param name="name">The resource's name, as its rules have it. A dot or a dash in it is written as an underscore.</param>
    public static MembershipFunctions For(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var stem = name.Replace('.', '_').Replace('-', '_');
        return new MembershipFunctions(stem + "_as_member", stem + "_as_member_with", stem + "_i_see", stem + "_where_i_hold", stem + "_fellow_members");
    }

    /// <summary>The five names, in the order they are declared.</summary>
    internal IReadOnlyList<string> All => [AsMember, AsMemberWith, Seen, HeldOn, FellowMembers];
}
