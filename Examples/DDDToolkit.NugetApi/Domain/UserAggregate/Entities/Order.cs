using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.NugetApi.Domain.ProductAggregate.ValueObjects;
using DDDToolkit.NugetApi.Domain.UserAggregate.ValueObjects;

namespace DDDToolkit.NugetApi.Domain.UserAggregate.Entities;

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
    public DateTime PlacedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = default!;
}
