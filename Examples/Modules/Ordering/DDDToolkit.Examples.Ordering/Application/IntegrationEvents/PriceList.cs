using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Examples.Catalog.Contracts;
using DDDToolkit.Examples.SharedKernel;

namespace DDDToolkit.Examples.Ordering.Application.IntegrationEvents;

/// <summary>Adds a newly listed product to Ordering's price list.</summary>
/// <remarks>
/// It does not assume the product is new here. A price change can overtake the listing on its way
/// through a broker, and then the row is already there; the older of the two prices loses either way.
/// </remarks>
[IntegrationEventConsumer("ordering.price-list.listed")]
public sealed class RecordListedPrice(OrderingContext context) : IIntegrationEventHandler<ProductListedV1>
{
    public Task HandleAsync(ProductListedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
        => PriceList.RecordAsync(context, contract.Sku, new Money(contract.Price, contract.Currency), message.OccurredAt, cancellationToken);
}

/// <summary>Keeps Ordering's price list up to date when Catalog reprices a product.</summary>
[IntegrationEventConsumer("ordering.price-list.repriced")]
public sealed class RecordChangedPrice(OrderingContext context) : IIntegrationEventHandler<ProductPriceChangedV1>
{
    public Task HandleAsync(ProductPriceChangedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
        => PriceList.RecordAsync(context, contract.Sku, new Money(contract.Price, contract.Currency), message.OccurredAt, cancellationToken);
}

internal static class PriceList
{
    /// <summary>
    /// Inserts or reprices the row. No <c>SaveChanges</c>: the inbox saves this change together with the
    /// row that says the message was applied.
    /// </summary>
    public static async Task RecordAsync(OrderingContext context, string sku, Money price, DateTimeOffset pricedAt, CancellationToken cancellationToken)
    {
        var current = await context.CatalogPrices.FindAsync([sku], cancellationToken);
        if (current is null)
        {
            context.CatalogPrices.Add(new CatalogPrice(sku, price, pricedAt));
        }
        else
        {
            current.Reprice(price, pricedAt);
        }
    }
}
