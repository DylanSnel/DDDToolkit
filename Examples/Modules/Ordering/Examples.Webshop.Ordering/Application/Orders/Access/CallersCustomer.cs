using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;

namespace Examples.Webshop.Ordering.Application.Orders;

/// <summary>
/// What the caller's own customer id is: the one over the id its token names, as <see cref="CustomerId.From"/> says, or
/// none for a guest and for the shop's own work.
/// </summary>
/// <remarks>
/// An order marks who placed it <c>[Owner]</c>, a <see cref="CustomerId"/>, so a request about the customer's own order
/// is compared with this at the door. Declared beside the requests, where the generator sees it, and registered by
/// <see cref="OrderingModule"/> with <c>AddCallersOwn&lt;CustomerId, CallersCustomer&gt;()</c>. It reads nothing.
/// </remarks>
public sealed class CallersCustomer : ICallersOwn<CustomerId>
{
    /// <inheritdoc />
    public CustomerId? Find(Caller caller) => CustomerId.From(caller);
}
