namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The ways a caller can hold one key on a resource, as the rules say it for that key. Several can apply to
/// one key: an owner holds every key of the resource, a role may give it too, and it may be held from above.
/// </summary>
[Flags]
internal enum MemberHeldBy
{
    /// <summary>The caller holds the key in no way.</summary>
    None = 0,

    /// <summary>By owning the resource: a key of the resource, which its owner holds with no end.</summary>
    Owner = 1,

    /// <summary>By a membership that applies now, whatever roles are held in it.</summary>
    Membership = 2,

    /// <summary>By a membership that applies now, with a role held in it now that gives the key.</summary>
    Roles = 4,

    /// <summary>From above: the key is held where the resource sits, or above it, as the application answers.</summary>
    Above = 8,
}
