using DTS.DDD.Abstractions.Attributes;
using DTS.DDD.BaseTypes;
using DTS.DDD.Interfaces;

namespace DTS.DDD.Mediator.Tests.Domain;

/// <summary>
/// The interface every event of the basket carries besides <see cref="IDomainEvent"/>. A handler declared for it
/// runs for each of them, because the publisher finds handlers by the runtime type and its interfaces.
/// </summary>
public interface IBasketEvent : IDomainEvent
{
    /// <summary>The basket the event is about.</summary>
    BasketId BasketId { get; }
}

[DomainEventName("basket.created")]
public sealed record BasketCreated(BasketId BasketId, string Name) : DomainEvent, IBasketEvent;

[DomainEventName("basket.item-added")]
public sealed record ItemAdded(BasketId BasketId, string Item) : DomainEvent, IBasketEvent;

[DomainEventName("basket.emptied")]
public sealed record BasketEmptied(BasketId BasketId) : DomainEvent, IBasketEvent;

/// <summary>An event without a handler of its own, and no <see cref="IBasketEvent"/>: it is published to nobody.</summary>
[DomainEventName("basket.archived")]
public sealed record BasketArchived(BasketId BasketId) : DomainEvent;
