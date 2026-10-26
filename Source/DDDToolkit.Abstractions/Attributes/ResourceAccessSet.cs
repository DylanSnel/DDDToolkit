namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Which set a <see cref="ResourceAccessContractAttribute{TKey}"/> publishes: one of the two questions the access to a
/// resource answers for the caller of the connection, with the ids of the resources it answers yes for.
/// </summary>
/// <remarks>
/// The package that keeps the resource's access writes the function that answers each, under whatever name it gives
/// it, and says which set the function answers. The Membership package answers both for a resource with members.
/// </remarks>
public enum ResourceAccessSet
{
    /// <summary>
    /// The resources the caller sees, asked without arguments: <c>Ids()</c>. A resource with members is seen by its
    /// members, by its owner, and, where its rules let a permission held above reach it, by whoever holds the permission that sees
    /// it there.
    /// </summary>
    Seen,

    /// <summary>
    /// The resources the caller holds a permission on, asked with the permission: <c>Ids(string permission)</c>. On a resource with members
    /// a permission is held through a role, by being a member for the permission that being one gives, by owning the resource, or
    /// from above.
    /// </summary>
    HeldOn,
}
