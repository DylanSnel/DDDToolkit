namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// How a caller holds a key on a resource. An application that shows it has its own word for each, "staff" for
/// its members say, and maps these at its edge.
/// </summary>
public enum MemberVia
{
    /// <summary>
    /// Through the resource's members: the caller is a member now and holds a role there, now, that gives the
    /// key; or the key is one that being a member gives by itself; or the caller is the owner, who holds every
    /// key of the resource.
    /// </summary>
    Members,

    /// <summary>
    /// From above the resource: the caller holds the key where the resource sits, or above it, and does not
    /// hold it as one of the resource's members, which comes first. Only a resource whose rules say it is
    /// reached from above (<see cref="MembershipRules.Above"/>) is reached this way.
    /// </summary>
    Above,

    /// <summary>The application's own work, which holds every key.</summary>
    System,
}
