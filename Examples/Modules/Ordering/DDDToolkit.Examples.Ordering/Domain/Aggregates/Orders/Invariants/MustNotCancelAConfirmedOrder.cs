using DDDToolkit.Invariants;

namespace DDDToolkit.Examples.Ordering.Domain.Orders;

public partial class Order
{
    /// <summary>
    /// A confirmed order has been paid for and has a van booked. Taking it back is a return, which is a
    /// different process with a refund in it, not a cancellation.
    /// </summary>
    /// <remarks>
    /// A rule about state, not about a call. <see cref="Cancel"/> does not check it first: it cancels,
    /// and this rule says whether an order that was confirmed and is now cancelled may exist. The
    /// endpoint asks the order after cancelling and turns this code into a 422; a handler that forgets to
    /// ask is stopped by the save.
    /// </remarks>
    public sealed class MustNotCancelAConfirmedOrder : IInvariant<Order>
    {
        /// <summary>What a caller branches on, so that nobody has to match on the message.</summary>
        public const string ViolationCode = "ORDER_ALREADY_CONFIRMED";

        /// <inheritdoc />
        public string Code => ViolationCode;

        /// <inheritdoc />
        public InvariantFailure? Check(Order order)
            => order.Status is OrderStatus.Cancelled && order.ConfirmedAt is not null
                ? "A confirmed order cannot be cancelled. Return it instead."
                : null;
    }
}
