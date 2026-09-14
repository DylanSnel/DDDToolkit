using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;
using Mediator;

namespace DDDToolkit.Mediator.Tests.Domain;

/// <summary>
/// The marker every publishable event in this test domain carries. Deriving from both interfaces in
/// one place is the pattern <c>DispatchWithMediator</c> expects, and the same one the example uses.
/// </summary>
public interface IBasketEvent : IDomainEvent, INotification;

[DomainEventName("basket.created")]
public sealed record BasketCreated(BasketId BasketId, string Name) : DomainEvent, IBasketEvent;

[DomainEventName("basket.item-added")]
public sealed record ItemAdded(BasketId BasketId, string Item) : DomainEvent, IBasketEvent;

[DomainEventName("basket.emptied")]
public sealed record BasketEmptied(BasketId BasketId) : DomainEvent, IBasketEvent;

/// <summary>
/// Deliberately not an <see cref="INotification"/>: Mediator has no way to publish it, which is the
/// case <c>DispatchWithMediator</c> turns into an exception naming this type.
/// </summary>
[DomainEventName("basket.archived")]
public sealed record BasketArchived(BasketId BasketId) : DomainEvent;
