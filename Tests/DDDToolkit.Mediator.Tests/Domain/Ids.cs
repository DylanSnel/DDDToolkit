using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Mediator.Tests.Domain;

/// <summary>Struct id with a prefix, primary key of <see cref="Basket"/>.</summary>
[EntityId<Guid>("BASKET")]
public readonly partial record struct BasketId;
