using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.ExampleApi.Domain.ProductAggregate.ValueObjects;

namespace DDDToolkit.ExampleApi.Domain.ProductAggregate;

[AggregateRoot<ProductId>]
public partial class Product
{
    public required string Name { get; init; }
    public int Price { get; init; }

    public Product(ProductId id) : base(id)
    {

    }
}
