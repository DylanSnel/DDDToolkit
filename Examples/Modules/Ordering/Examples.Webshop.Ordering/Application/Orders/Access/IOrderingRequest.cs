using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;

namespace Examples.Webshop.Ordering.Application.Orders;

/// <summary>
/// What a request of Ordering's implements to say what it requires of its caller: the toolkit's generator writes
/// <see cref="IRequireAccess.RequiredAccess"/> into this interface, one line per request, from the request's attributes.
/// </summary>
/// <remarks>
/// Ordering sends no request through a mediator: its endpoints are its handlers. So an endpoint asks the checks
/// itself, <c>AccessChecks&lt;IOrderingRequest&gt;.RequireAsync(request)</c>, before it does anything, in the same
/// method that then does the work, and <see cref="OrderingModule"/> registers the checks with
/// <c>AddAccessChecks&lt;IOrderingRequest&gt;()</c>. Most of Ordering's endpoints require nothing of their caller: a
/// guest places and follows an order, and the rules in <c>Domain/Aggregates/Orders/Access</c> say what a caller reads.
/// Only the request about a customer's own order goes through the door.
/// </remarks>
[AccessRequests]
public partial interface IOrderingRequest : IRequireAccess;
