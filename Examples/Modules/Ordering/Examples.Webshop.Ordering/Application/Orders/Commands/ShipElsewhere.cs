using DDDToolkit.Access;
using Examples.Webshop.Ordering.Contracts;

namespace Examples.Webshop.Ordering.Application.Orders;

/// <summary>
/// A customer sends their own order to another address, while it is placed.
/// </summary>
/// <remarks>
/// The policy is the request's, and the fact the order's: <c>[OwnOnly]</c> says the request is about the caller's own,
/// and <c>[Own]</c> on the order's id makes that the order's owner, which <c>Order.PlacedBy</c> is marked to be. The
/// generator writes <c>AccessRequirement.OwnerOf(request.Order)</c> for it, and the check reads who placed the order, one
/// column of one row, as the caller. So only the customer who placed it sends it elsewhere: a guest's order is nobody's,
/// and goes nowhere but where it was placed for, whoever has its id, since a link to it may be shared; another
/// customer's order is somebody else's, or not found at all where row level security keeps it from the caller.
/// </remarks>
/// <param name="Order">The order.</param>
/// <param name="Street">The new address's street.</param>
/// <param name="City">Its city.</param>
/// <param name="PostalCode">Its postal code.</param>
[OwnOnly]
public sealed record ShipElsewhere([Own] OrderId Order, string Street, string City, string PostalCode) : IOrderingRequest;
