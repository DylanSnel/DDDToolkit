using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Examples.SharedKernel;

namespace DDDToolkit.Examples.Catalog.Domain.Products;

/// <summary>A product was added to the catalog. Published as <c>ProductListedV1</c>.</summary>
[DomainEventName("catalog.product-listed")]
public sealed record ProductListed(ProductId ProductId, string Sku, string Name, Money Price) : DomainEvent;

/// <summary>A product was repriced. Published as <c>ProductPriceChangedV1</c>.</summary>
[DomainEventName("catalog.product-price-changed")]
public sealed record ProductPriceChanged(ProductId ProductId, string Sku, Money Price) : DomainEvent;
