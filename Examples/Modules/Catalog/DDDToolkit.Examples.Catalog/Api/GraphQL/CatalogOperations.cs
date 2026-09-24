using DDDToolkit.Exceptions;
using DDDToolkit.Examples.SharedKernel;
using HotChocolate;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Examples.Catalog.Api.GraphQL;

/// <summary>Catalog's queries: the range, and one product by SKU.</summary>
[ExtendObjectType(OperationTypeNames.Query)]
public sealed class CatalogQueries
{
    public async Task<IReadOnlyList<Product>> GetProductsAsync(CatalogContext catalog, CancellationToken cancellationToken)
        => await catalog.Products.OrderBy(product => product.Sku).ToListAsync(cancellationToken);

    /// <summary>
    /// The lookup, as a query field. A Fusion gateway resolves a <c>Product</c> another module holds only
    /// the SKU of, such as Ordering's order lines, through it.
    /// </summary>
    [Lookup]
    public Task<Product?> GetProductBySkuAsync(string sku, ProductBySkuDataLoader products, CancellationToken cancellationToken)
        => products.LoadAsync(sku, cancellationToken);
}

/// <summary>Catalog's mutations: list a product, reprice one.</summary>
/// <remarks>
/// Invalid input is not checked here. <c>ToValid()</c> throws <c>InvalidValueObjectException</c>, and
/// <c>AddDDDToolkitErrors()</c> turns it into one GraphQL error per failure, with the code and the field
/// in <c>extensions</c>. The REST endpoints ask <c>TryToValid</c> instead, because a 400 wants the
/// failures as a value; GraphQL wants them as errors.
/// </remarks>
[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class CatalogMutations
{
    public async Task<Product> ListProductAsync(string sku, string name, decimal price, string currency, CatalogContext catalog, CancellationToken cancellationToken)
    {
        var product = new Product(ProductId.CreateSequential(), sku, name, new Money(price, currency).ToValid());
        catalog.Products.Add(product);
        await catalog.SaveChangesAsync(cancellationToken);
        return product;
    }

    /// <summary>
    /// Reprices a product. The id is the product's node id; HotChocolate hands the resolver the
    /// <see cref="ProductId"/> inside it.
    /// </summary>
    public async Task<Product> RepriceProductAsync([ID<Product>] ProductId id, decimal price, string currency, CatalogContext catalog, CancellationToken cancellationToken)
    {
        var product = await catalog.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new GraphQLException(ErrorBuilder.New().SetMessage("No such product.").SetCode("NOT_FOUND").Build());

        product.ChangePrice(new Money(price, currency).ToValid());

        try
        {
            await catalog.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException conflict)
        {
            throw new GraphQLException(ErrorBuilder.New().SetMessage(conflict.Message).SetCode("CONCURRENCY_CONFLICT").Build());
        }

        return product;
    }
}
