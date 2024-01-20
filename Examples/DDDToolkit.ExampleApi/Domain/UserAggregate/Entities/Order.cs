using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.ExampleApi.Domain.ProductAggregate.ValueObjects;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;

namespace DDDToolkit.ExampleApi.Domain.UserAggregate.Entities;

[Entity<OrderId>()]
public partial class Order
{

    public Order(OrderId id, List<ProductId> products) : base()
    {
        Id = id;
        _products = products;
    }

    private readonly List<ProductId> _products = [];

    public IReadOnlyList<ProductId> Products => _products.AsReadOnly();
    public DateTime PlacedAt { get; set; } = DateTime.Now;

    public User User { get; set; } = default!;
}
