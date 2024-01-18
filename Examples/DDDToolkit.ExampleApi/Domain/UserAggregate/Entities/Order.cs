using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.ExampleApi.Domain.ProductAggregate.ValueObjects;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;

namespace DDDToolkit.ExampleApi.Domain.UserAggregate.Entities;

[Entity<OrderId>()]
public partial class Order
{

    public Order() : base(OrderId.CreateUnique())
    {
    }

    private readonly List<ProductId> _products = [];

    public ICollection<ProductId> Products { get; set; } = [];
    public DateTime PlacedAt { get; set; } = DateTime.Now;

    public User User { get; set; } = default!;
}
