using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;

namespace Shop;

[DomainEventName("shop.order-placed")]
public sealed record OrderPlaced(OrderId Order) : DomainEvent;
