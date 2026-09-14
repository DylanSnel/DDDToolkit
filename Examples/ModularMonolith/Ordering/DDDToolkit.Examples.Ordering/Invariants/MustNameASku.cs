using DDDToolkit.Invariants;

namespace DDDToolkit.Examples.Ordering;

public partial class OrderLine
{
    /// <summary>
    /// A rule of the child, and the one this example was built around: the handler in
    /// <c>Host/Endpoints.cs</c> asks the <see cref="Order"/> whether what it just did is allowed, and
    /// hears about this rule. The order answers for the lines it holds, so the rule reaches the
    /// handler without <see cref="Order"/> knowing it exists and without anybody writing a loop over
    /// the lines. The violation carries this line's type and id, which is what lets the endpoint say
    /// which line is at fault rather than only that one is.
    /// </summary>
    /// <remarks>
    /// Nothing validates a SKU on the way in: the endpoint checks the address and the number of
    /// lines, and takes the rest of the body as it comes. That is the argument for stating the rule
    /// here rather than there. The endpoint is one door, the aggregate has several, and this one is
    /// the door everything has to leave through. The save is still the guarantee: an
    /// <c>InvariantInterceptor</c> asks this line before every <c>SaveChanges</c> that writes it,
    /// whether or not a handler thought to ask first.
    /// <para>
    /// Compare the quantity, which the constructor refuses outright with an
    /// <see cref="ArgumentOutOfRangeException"/>. That is a different job: a guard answers the caller
    /// who passed nonsense, an invariant answers the save. Entity Framework materialises a row
    /// without going anywhere near that constructor.
    /// </para>
    /// </remarks>
    public sealed class MustNameASku : IInvariant<OrderLine>
    {
        /// <summary>What a caller branches on, so that nobody has to match on the message.</summary>
        public const string ViolationCode = "LINE_HAS_NO_SKU";

        /// <inheritdoc />
        public string Code => ViolationCode;

        /// <inheritdoc />
        public string? Check(OrderLine line)
            => string.IsNullOrWhiteSpace(line.Sku) ? "A line must name the thing it is ordering." : null;
    }
}
