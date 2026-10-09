using GreenDonut;
using HotChocolate;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Webshop.Inventory.Api.GraphQL;

/// <summary>
/// A product as Inventory knows it: a SKU it keeps stock of. The name and the price are Catalog's.
/// </summary>
public sealed record InventoryProduct(string Sku);

/// <summary>
/// Inventory's part of the <c>Product</c> type, for a schema a Fusion gateway composes with Catalog's.
/// </summary>
/// <remarks>
/// Catalog owns what a product is called and what it costs; Inventory owns how many there are. Both
/// declare a <c>Product</c>, keyed on its SKU, and the gateway merges them into one: a client asks
/// <c>productBySku(sku: "COFFEE-1KG") { name price { amount } stock { available } }</c>, and the gateway
/// asks Catalog for the first two fields and Inventory, through its internal lookup below, for the third.
/// Neither module knows the other's classes.
/// <para>
/// The stock itself stays the <see cref="StockItem"/> aggregate and its own node; this type only says
/// which product it is the stock of. So <c>node(id:)</c> still finds a stock item, and Inventory's ids
/// never compete with Catalog's for the <c>Product</c> node.
/// </para>
/// </remarks>
public sealed class InventoryProductType : ObjectType<InventoryProduct>
{
    protected override void Configure(IObjectTypeDescriptor<InventoryProduct> descriptor)
    {
        descriptor.Name("Product");
        descriptor.BindFieldsExplicitly();
        descriptor.Directive(new EntityKey("sku"));

        descriptor.Field(product => product.Sku);

        descriptor
            .Field("stock")
            .Type<StockItemType>()
            .Resolve(async context => await context.DataLoader<StockItemBySkuDataLoader>()
                .LoadAsync(context.Parent<InventoryProduct>().Sku, context.RequestAborted));
    }
}

/// <summary>How the gateway fetches Inventory's part of a product. Internal: clients ask Catalog's.</summary>
/// <remarks>
/// A lookup returns a nullable type, as Fusion requires of every lookup; this one never returns null,
/// since any SKU has Inventory's part, with or without stock.
/// </remarks>
public static class ProductStockQueries
{
    [Query]
    [Lookup]
    [Internal]
    public static InventoryProduct? GetProductBySku(string sku) => new(sku);
}

/// <summary>The stock of each SKU, batched.</summary>
public sealed class StockItemBySkuDataLoader(IServiceScopeFactory scopes, IBatchScheduler batchScheduler, DataLoaderOptions options)
    : BatchDataLoader<string, StockItem>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<string, StockItem>> LoadBatchAsync(IReadOnlyList<string> skus, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<InventoryContext>().StockItems
            .Where(item => skus.Contains(item.Sku))
            .ToDictionaryAsync(item => item.Sku, cancellationToken);
    }
}
