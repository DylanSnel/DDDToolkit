using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Examples.SharedKernel;

namespace DDDToolkit.Examples.Ordering.Domain.Orders;

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
/// <para>
/// The unit price is the price the line was placed at, copied from the catalog, and it never changes
/// afterwards. A repriced product does not reprice an order somebody already paid for.
/// </para>
/// <para>
/// It is stored as two plain columns and read back as <see cref="Money"/>, where <c>Order.Total</c> is a
/// <see cref="Money"/> stored inline. The difference is Entity Framework's, not the domain's: a line is an
/// owned type, and Entity Framework cannot yet write a migration for a complex type inside an owned one.
/// The model builds and the database works, but the generated snapshot calls a method that does not
/// exist and does not compile. Two columns and a property that assembles them keep the domain talking
/// in <see cref="Money"/> and the migrations compiling.
/// </para>
/// </remarks>
[Entity<Guid>("LINE")]
public partial class OrderLine
{
    public OrderLine(OrderLineId id, string sku, int quantity, Money unitPrice) : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
        ArgumentNullException.ThrowIfNull(unitPrice);

        Sku = sku;
        Quantity = quantity;
        UnitPriceAmount = unitPrice.Amount;
        UnitPriceCurrency = unitPrice.Currency;
    }

    public string Sku { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public decimal UnitPriceAmount { get; private set; }

    public string UnitPriceCurrency { get; private set; } = Money.Euro;

    /// <summary>What one of these cost when the order was placed.</summary>
    public Money UnitPrice => new(UnitPriceAmount, UnitPriceCurrency);

    /// <summary>What this line costs: the unit price, <see cref="Quantity"/> times.</summary>
    public Money Subtotal => UnitPrice.Times(Quantity);
}
