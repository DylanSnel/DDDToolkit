using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// One kind of resource with members, as its registration added it: which context maps it, which
/// class its members are of, and the rules it runs with. There is one in the container for each kind of
/// resource, so whatever checks an application at start-up can ask for them all and hold each database to the
/// rules its resource was registered with.
/// </summary>
/// <param name="Context">The context that maps the resource and its members.</param>
/// <param name="Resource">The resource's aggregate.</param>
/// <param name="ResourceIdType">The type of the resource's id, which its access questions are asked for by.</param>
/// <param name="Member">The application's member class.</param>
/// <param name="Rules">The rules the resource was registered with.</param>
public sealed record MembershipRegistration(Type Context, Type Resource, Type ResourceIdType, Type Member, MembershipRules Rules);
