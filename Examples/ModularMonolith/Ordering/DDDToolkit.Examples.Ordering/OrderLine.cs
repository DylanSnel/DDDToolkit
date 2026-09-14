using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Examples.Ordering;

/// <summary>
/// A line of an order. A child entity, so <c>[Entity]</c> rather than <c>[AggregateRoot]</c>: it has an
/// identity, it is loaded and saved with its order, and it raises no events of its own.
/// </summary>
/// <remarks>
/// The identifier is generated from the raw value on the attribute, so there is no <c>OrderLineId.cs</c>
/// anywhere. That is the right call here and the wrong one for <c>OrderId</c>: nothing outside this
/// aggregate ever names an order line, so the type needs no file to navigate to. Compare
/// <c>OrderingContracts.cs</c>, where the opposite decision is written out.
/// <para>
/// A child entity has invariants of its own, and two callers run them.
/// <c>Invariants/MustNameASku.cs</c> is this line's rule, stated on the line because it is about the
/// line alone; <see cref="Order"/> reports it because asking a root asks the aggregate, and the save
/// asks this line directly when this line is what changed. The rule that reaches across the lines
/// stays on <see cref="Order"/>, in the seam at the bottom of <c>Order.cs</c>.
/// </para>
/// </remarks>
[Entity<Guid>("LINE")]
public partial class OrderLine
{
    public OrderLine(OrderLineId id, string sku, int quantity) : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);

        Sku = sku;
        Quantity = quantity;
    }

    public string Sku { get; private set; } = string.Empty;

    public int Quantity { get; private set; }
}
