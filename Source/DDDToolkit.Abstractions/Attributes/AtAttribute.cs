namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks the property of an aggregate root that says where it hangs: the unit of an organization a board belongs to,
/// the bay a crate stands in. A fact about what the aggregate is, said in the domain beside its <c>[Owner]</c>, and no
/// rule about who may do what with it:
/// <code>
/// [AggregateRoot&lt;BoardId&gt;]
/// public sealed partial class Board
/// {
///     [Owner] public SeatId OwnerSeatId { get; private set; }
///
///     [At] public OrganizationUnitId UnitId { get; private set; }
/// }
/// </code>
/// Whether a permission held there reaches the aggregate is the policy's to say: the rules of a resource with members say it
/// with <c>reachFromAbove</c>, and read where it hangs from this property. The name pairs with the levels of a
/// permission, <c>[AtUnit]</c> and <c>[AtTenant]</c>: a permission held at a unit reaches what hangs at it.
/// </summary>
/// <remarks>
/// One per aggregate root, on the root itself, of the id of the place: never nullable, since an aggregate that hangs
/// nowhere could not be reached from above. Two, or a nullable one, is DDD00097; rules that reach a resource from above
/// whose class marks none is DDD00098.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class AtAttribute : Attribute;
