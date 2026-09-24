using GreenDonut;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.Catalog.Api.GraphQL;

/// <summary>
/// Products by SKU, batched: a hundred order lines asking for their product are one query.
/// </summary>
/// <remarks>
/// <b>This is Catalog's lookup, and the only way anything outside Catalog gets a product.</b> Ordering
/// knows a line's SKU and nothing else about the product. The field that turns one into the other,
/// <c>OrderLine.product</c>, is added by whatever composes the modules into one schema: the monolith's
/// host, or a Fusion gateway over the services. Either way it asks this lookup, and Catalog stays the
/// only one who reads its tables.
/// <para>
/// Each batch gets a scope, and so a <see cref="CatalogContext"/>, of its own. HotChocolate resolves
/// fields in parallel, and one context shared between two resolvers would be used by both at once.
/// </para>
/// </remarks>
public sealed class ProductBySkuDataLoader(IServiceScopeFactory scopes, IBatchScheduler batchScheduler, DataLoaderOptions options)
    : BatchDataLoader<string, Product>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<string, Product>> LoadBatchAsync(IReadOnlyList<string> skus, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        return await catalog.Products
            .Where(product => skus.Contains(product.Sku))
            .ToDictionaryAsync(product => product.Sku, cancellationToken);
    }
}

/// <summary>Products by id, batched. What <c>node(id:)</c> asks when the id is a product's.</summary>
public sealed class ProductByIdDataLoader(IServiceScopeFactory scopes, IBatchScheduler batchScheduler, DataLoaderOptions options)
    : BatchDataLoader<ProductId, Product>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<ProductId, Product>> LoadBatchAsync(IReadOnlyList<ProductId> ids, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        return await catalog.Products
            .Where(product => ids.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);
    }
}
