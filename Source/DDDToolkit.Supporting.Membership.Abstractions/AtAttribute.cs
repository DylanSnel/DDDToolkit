namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// Marks the property of an aggregate root that says where it hangs: the unit of an organization a board belongs to,
/// the bay a crate stands in. It is a fact about what the aggregate is, said in the domain beside its <c>[Owner]</c>. It
/// is no rule about who may do what with it:
/// <code>
/// [AggregateRoot&lt;BoardId&gt;]
/// public sealed partial class Board
/// {
///     [Owner] public SeatId OwnerSeatId { get; private set; }
///
///     [At] public OrganizationUnitId UnitId { get; private set; }
/// }
/// </code>
/// Whether a permission held there reaches the aggregate is the policy's to say. The rules of a resource with members say
/// it with <c>reachFromAbove</c>, and read where it hangs from this property. The name pairs with the level a permission
/// is held at, <c>[AtUnit]</c>. A permission held at a unit reaches what hangs at it.
/// </summary>
/// <remarks>
/// One per aggregate root, on the root itself, of the id of the place. It is never nullable, since an aggregate that hangs
/// nowhere could not be reached from above. Two, or a nullable one, is DDD00097. Rules that reach a resource from above
/// whose class marks none are DDD00098. Membership's generator reports both, and ships in Membership's package.
/// <para>
/// The marker is in a package of its own, with no Entity Framework and nothing else of Membership's. Membership's own
/// package references it, so a project with Membership has the marker as well.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class AtAttribute : Attribute;
