using DDDToolkit.Abstractions.Attributes;

namespace Shop;

[Entity<Guid>("LINE")]
public partial class OrderLine
{
    public OrderLine(OrderLineId id, string sku, int quantity) : base(id)
        => (Sku, Quantity) = (sku, quantity);

    public string Sku { get; private set; }

    public int Quantity { get; private set; }
}
