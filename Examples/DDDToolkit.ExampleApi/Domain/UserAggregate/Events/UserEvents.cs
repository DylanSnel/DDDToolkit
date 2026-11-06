using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;

namespace DDDToolkit.ExampleApi.Domain.UserAggregate.Events;

[DomainEventName("user.created")]
public sealed record UserCreated(UserId UserId) : DomainEvent;

[DomainEventName("user.order-placed")]
public sealed record OrderPlaced(UserId UserId, OrderId OrderId) : DomainEvent;
