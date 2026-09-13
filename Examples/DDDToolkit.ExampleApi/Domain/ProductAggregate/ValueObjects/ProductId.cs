using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.ExampleApi.Domain.ProductAggregate.ValueObjects;

/// <summary>Struct id with a prefix: ToString() gives "PRDCT_&lt;guid&gt;", Parse accepts it with or without the prefix.</summary>
[EntityId<Guid>("PRDCT")]
public readonly partial record struct ProductId;
