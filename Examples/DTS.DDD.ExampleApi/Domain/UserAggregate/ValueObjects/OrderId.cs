using DTS.DDD.Abstractions.Attributes;

namespace DTS.DDD.ExampleApi.Domain.UserAggregate.ValueObjects;

[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;
