namespace DTS.DDD.Abstractions.Attributes;

/// <summary>
/// Marks the property of an aggregate root that names its owner. A fact about what the aggregate is, said in the
/// domain, and no rule about who may do what with it:
/// <code>
/// [AggregateRoot&lt;BoardId&gt;]
/// public sealed partial class Board
/// {
///     [Owner] public UserId Owner { get; private set; }
/// }
/// </code>
/// The request states who may act on the owner's own, with the attributes for the caller's own. The access check reads
/// them. <c>[OwnOnly]</c> with <c>[Own] BoardId Board</c> lets only the board's owner through. <c>[OrOwn] BoardId Board</c>
/// beside the request's only permission lets through the owner, or whoever holds the permission.
/// </summary>
/// <remarks>
/// The property is of a type the caller has an own of: a <see cref="Guid"/>, the caller's verified identity;
/// Tenancy's seat id; or a kind of the application's own that its <c>IOwnedByCurrentUser</c> says the caller's own of. It may
/// be nullable, and a row whose owner is null is nobody's: nobody comes through as its owner. One per aggregate root,
/// on the root itself, and never on an entity inside it, which a request does not name by its own id; on another
/// type, or twice, it is DDD00087, and on a type nothing could say the caller's own of DDD00088.
/// <para>
/// The check reads that one column of that one row, as the caller, so row level security still decides whether the
/// row is there for the caller at all: a row it does not see is not found. Several owners of one aggregate are
/// members, which Membership keeps.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class OwnerAttribute : Attribute;
