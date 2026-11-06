using DTS.DDD.Abstractions.Attributes;
using DTS.DDD.ExampleApi.Domain.ProductAggregate.ValueObjects;

namespace DTS.DDD.ExampleApi.Domain.ProductAggregate;

[AggregateRoot<ProductId>]
public partial class Product
{
    public required string Name { get; init; }
    public int Price { get; init; }

    public Product(ProductId id) : base(id)
    {

    }
}
