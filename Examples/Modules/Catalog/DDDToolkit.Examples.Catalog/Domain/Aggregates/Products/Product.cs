using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Examples.SharedKernel;

namespace DDDToolkit.Examples.Catalog.Domain.Products;

/// <summary>Something the shop sells. Catalog's only aggregate.</summary>
/// <remarks>
/// The price is a <see cref="Money"/> from the shared kernel, stored inline as a complex type, and both
/// the constructor and <see cref="ChangePrice"/> take the always-valid twin: a price nobody validated
/// cannot reach this class. Compare <c>Order</c>, which takes <c>ValidAddress</c> for the same reason.
/// <para>
/// Changing a price is the edit two people in the back office make at the same time, and the version
/// column is what turns the second one into a 409 instead of a silent overwrite. See
/// <c>Api/CatalogEndpoints.cs</c>.
/// </para>
/// </remarks>
[AggregateRoot<Guid>("PRD")]
public partial class Product
{
    public Product(ProductId id, string sku, string name, ValidMoney price) : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);

        Sku = sku;
        Name = name;
        Price = price;

        RaiseDomainEvent(new ProductListed(id, sku, name, price));
    }

    /// <summary>What the shop prints on the box, and how every other module names this product.</summary>
    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Money Price { get; private set; }

    /// <summary>Reprices the product. Setting the price it already has changes nothing and says nothing.</summary>
    public void ChangePrice(ValidMoney price)
    {
        ArgumentNullException.ThrowIfNull(price);

        if (Price == price)
        {
            return;
        }

        Price = price;
        RaiseDomainEvent(new ProductPriceChanged(Id, Sku, price));
    }
}
