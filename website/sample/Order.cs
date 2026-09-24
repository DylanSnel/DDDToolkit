using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Invariants;

namespace Shop;

[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public Order(OrderId id, Address shipTo) : base(id)
    {
        ShipTo = shipTo;
        RaiseDomainEvent(new OrderPlaced(id));
    }

    public Address ShipTo { get; private set; }

    public partial IReadOnlyList<OrderLine> Lines { get; }

    public sealed class MustHaveLines : IInvariant<Order>
    {
        public string Code => "ORDER_HAS_NO_LINES";

        public InvariantFailure? Check(Order order)
            => order.Lines.Count == 0
                ? "An order has at least one line."
                : null;
    }
}
