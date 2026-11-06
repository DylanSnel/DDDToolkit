using DTS.DDD.Abstractions.Attributes;
using DTS.DDD.BaseTypes;
using DTS.DDD.ExampleApi.Domain.UserAggregate.ValueObjects;
using DTS.DDD.ExampleLibrary.Common;

namespace DTS.DDD.ExampleApi.Domain.UserAggregate.Events;

[DomainEventName("user.created")]
public sealed record UserCreated(UserId UserId) : DomainEvent, IBaseDomainEvent;

[DomainEventName("user.order-placed")]
public sealed record OrderPlaced(UserId UserId, OrderId OrderId) : DomainEvent, IBaseDomainEvent;
