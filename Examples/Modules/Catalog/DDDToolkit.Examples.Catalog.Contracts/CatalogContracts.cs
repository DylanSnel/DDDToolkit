using DDDToolkit.Abstractions.Attributes;

[assembly: Module("Catalog")]

namespace DDDToolkit.Examples.Catalog.Contracts;

/// <summary>A product can be ordered, at this price.</summary>
/// <remarks>
/// The price travels as an amount and a currency rather than as the shared kernel's <c>Money</c>. A
/// message outlives the code that wrote it, and a consumer that deployed against these two fields
/// keeps reading them whatever happens to <c>Money</c> later.
/// </remarks>
[IntegrationEvent("catalog.product-listed", Version = 1)]
public sealed record ProductListedV1(string Sku, string Name, decimal Price, string Currency);

/// <summary>A product's price changed. Orders placed from now on are priced at <see cref="Price"/>.</summary>
[IntegrationEvent("catalog.product-price-changed", Version = 1)]
public sealed record ProductPriceChangedV1(string Sku, decimal Price, string Currency);
