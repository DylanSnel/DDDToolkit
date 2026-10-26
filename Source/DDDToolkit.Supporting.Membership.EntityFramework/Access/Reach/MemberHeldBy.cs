namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The ways a caller can hold one permission on a resource, as the rules say it for that permission. Several can apply to
/// one permission: an owner holds a permission the rules give an owner, a role may give it too, and it may be held from above.
/// </summary>
[Flags]
internal enum MemberHeldBy
{
    /// <summary>The caller holds the permission in no way.</summary>
    None = 0,

    /// <summary>By owning the resource: a permission of the resource, which its owner holds with no end.</summary>
    Owner = 1,

    /// <summary>By a membership that applies now, whatever roles are held in it.</summary>
    Membership = 2,

    /// <summary>By a membership that applies now, with a role held in it now that gives the permission.</summary>
    Roles = 4,

    /// <summary>From above: the permission is held where the resource sits, or above it, as the application answers.</summary>
    Above = 8,
}
