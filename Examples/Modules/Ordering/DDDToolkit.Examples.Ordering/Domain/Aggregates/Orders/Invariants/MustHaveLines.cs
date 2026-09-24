using DDDToolkit.Invariants;

namespace DDDToolkit.Examples.Ordering.Domain.Orders;

public partial class Order
{
    /// <summary>
    /// An order with no lines is not an order. This was a line in the <c>CheckInvariants</c> seam
    /// until the HTTP layer had to answer for it: <c>Api/OrderingEndpoints.cs</c> turns a broken order into a 422
    /// that names which rule broke, and naming a rule means having a code to name it by. A seam has
    /// no code to give.
    /// </summary>
    /// <remarks>
    /// Nesting is required rather than tidy. The generator finds rules among the entity's own nested
    /// types, which is what lets this one read <c>_lines</c> without the field becoming anybody
    /// else's business, and what keeps discovery from having to scan the compilation. A copy of this
    /// class outside <see cref="Order"/> would compile, read the same, pass its own unit test and
    /// never run; the analyzer reports that as DDD00024 rather than letting it ship.
    /// </remarks>
    public sealed class MustHaveLines : IInvariant<Order>
    {
        /// <summary>What a caller branches on, so that nobody has to match on the message.</summary>
        public const string ViolationCode = "ORDER_HAS_NO_LINES";

        /// <inheritdoc />
        public string Code => ViolationCode;

        /// <inheritdoc />
        public InvariantFailure? Check(Order order)
            => order._lines.Count == 0 ? "An order must have at least one line." : null;
    }
}
