using DTS.DDD.Abstractions.Attributes;
using DTS.DDD.BaseTypes;

namespace Shop;

[DomainEventName("shop.order-placed")]
public sealed record OrderPlaced(OrderId Order) : DomainEvent;
