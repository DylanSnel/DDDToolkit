using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Examples.Catalog.Contracts;

namespace DDDToolkit.Examples.Catalog.Application.Products;

/// <summary>A product was repriced: tell the others the new price.</summary>
public sealed class PublishProductPriceChanged : IOutboundIntegrationEvent<ProductPriceChanged, ProductPriceChangedV1>
{
    public ValueTask<ProductPriceChangedV1?> CreateAsync(ProductPriceChanged changed, CancellationToken cancellationToken)
        => new(new ProductPriceChangedV1(changed.Sku, changed.Price.Amount, changed.Price.Currency));
}
