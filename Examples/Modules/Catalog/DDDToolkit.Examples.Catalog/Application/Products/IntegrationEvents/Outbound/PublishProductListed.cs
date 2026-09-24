using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Examples.Catalog.Contracts;

namespace DDDToolkit.Examples.Catalog.Application.Products;

/// <summary>A product was listed: tell the others its SKU, name and price.</summary>
/// <remarks>
/// The seam between Catalog's domain and everyone else. <see cref="ProductListed"/> carries a
/// <c>Money</c> and can change shape whenever Catalog wants; <see cref="ProductListedV1"/> is plain
/// fields that stay put until Catalog publishes a V2 on purpose. The outbox runs this at delivery, so the
/// stored row stays the domain event.
/// </remarks>
public sealed class PublishProductListed : IOutboundIntegrationEvent<ProductListed, ProductListedV1>
{
    public ValueTask<ProductListedV1?> CreateAsync(ProductListed listed, CancellationToken cancellationToken)
        => new(new ProductListedV1(listed.Sku, listed.Name, listed.Price.Amount, listed.Price.Currency));
}
