namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// One of the questions the access to a resource answers for the caller of the connection, with the ids of the resources
/// it answers yes for, or, for <see cref="FellowMembers"/>, with the ids of the members it finds. A row access rule asks
/// each through <see cref="Access.ResourceQuestions"/>, by the resource's id.
/// </summary>
/// <remarks>
/// The package that keeps the resource's access writes the function that answers each, under whatever name it gives
/// it, and says which set the function answers. The Membership package answers all three for a resource with members.
/// </remarks>
public enum ResourceAccessSet
{
    /// <summary>
    /// The resources the caller sees, asked without arguments: <c>user.Sees&lt;TKey&gt;()</c>. A resource with members is
    /// seen by its members, by its owner, and, where its rules let a permission held above reach it, by whoever holds the
    /// permission that sees it there.
    /// </summary>
    Seen,

    /// <summary>
    /// The resources the caller holds a permission on, asked with the permission: <c>user.Holds&lt;TKey&gt;(permission)</c>.
    /// On a resource with members a permission is held through a role, by being a member for the permission that being one
    /// gives, by owning the resource, or from above.
    /// </summary>
    HeldOn,

    /// <summary>
    /// The members of every resource the caller is a member of now, the caller among them, asked without arguments:
    /// <c>user.FellowMembers&lt;TKey, TMemberId&gt;()</c>. Its ids are the members', not the resources': the people on the
    /// projects a seat works on, say, which a read rule on the application's seat class asks to let a seat read who it
    /// works with.
    /// </summary>
    FellowMembers,
}
