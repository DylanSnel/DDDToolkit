using DTS.DDD.Abstractions.Attributes;
using DTS.DDD.BaseTypes;
using DTS.DDD.ExampleApi.Domain.UserAggregate.ValueObjects;

namespace DTS.DDD.ExampleApi.Domain.UserAggregate.Events;

[DomainEventName("user.created")]
public sealed record UserCreated(UserId UserId) : DomainEvent;

[DomainEventName("user.order-placed")]
public sealed record OrderPlaced(UserId UserId, OrderId OrderId) : DomainEvent;
