using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;

[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;
