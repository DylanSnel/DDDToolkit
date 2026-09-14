using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.ExampleApi.Domain.ProductAggregate.ValueObjects;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;

namespace DDDToolkit.ExampleApi.Domain.UserAggregate.Entities;

/// <summary>
/// A child entity of the User aggregate. [Entity] makes it an EF Core owned type; the partial
/// collection property gets a generated <c>_products</c> backing field that EF maps directly.
/// </summary>
[Entity<OrderId>]
public partial class Order
{
    public Order(OrderId id, IEnumerable<ProductId> products) : base(id)
    {
        _products.AddRange(products);
    }

    /// <summary>The products in this order. Read-only outside the entity; backed by the generated <c>_products</c> list.</summary>
    public partial IReadOnlyList<ProductId> Products { get; }

    public DateTime PlacedAt { get; private set; } = DateTime.UtcNow;

    public void AddProduct(ProductId productId) => _products.Add(productId);
}
