using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;

namespace DDDToolkit.Tests.Domain;

/// <summary>Raised by <see cref="Basket"/>'s constructor. Carries a stable wire name.</summary>
[DomainEventName("test.basket-opened")]
public sealed record BasketOpened(BasketId BasketId) : DomainEvent;

/// <summary>Raised by <see cref="Basket.AddLine"/>. Deliberately has no <c>[DomainEventName]</c>.</summary>
public sealed record LineAdded(BasketId BasketId, BasketLineId LineId) : DomainEvent;

/// <summary>Raised by <see cref="Basket.RemoveLine"/>.</summary>
public sealed record LineRemoved(BasketId BasketId, BasketLineId LineId) : DomainEvent;

/// <summary>A named event with a derived type, to pin that the name is not inherited.</summary>
[DomainEventName("test.named-base")]
public record NamedBaseEvent : DomainEvent;

/// <summary>Derives from a named event but carries no attribute of its own.</summary>
public record DerivedFromNamedEvent : NamedBaseEvent;

/// <summary>
/// An event that does not derive from <c>DomainEvent</c>, to prove the toolkit only requires
/// <see cref="IDomainEvent"/> and that an aggregate will happily raise a hand-rolled implementation.
/// </summary>
public sealed record HandRolledEvent(Guid EventId, DateTimeOffset OccurredAt) : IDomainEvent;
