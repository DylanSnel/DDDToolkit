using Examples.Webshop.Catalog.Api.GraphQL;
using Examples.Webshop.Catalog.Domain.Products;
using Examples.Webshop.Ordering.Domain.Orders;
using HotChocolate;
using HotChocolate.Types;

// HotChocolate's attribute: it names what HotChocolate's generator writes for this project, AddStorefrontTypes,
// which registers the type extension below. Program.cs calls it.
[assembly: HotChocolate.Module("StorefrontTypes")]

namespace Examples.Webshop.Pgmq.Storefront.GraphQL;

/// <summary>The product a line is for: Ordering's SKU, joined to Catalog's lookup, both in this service.</summary>
/// <remarks>
/// A field this service adds to Ordering's <c>OrderLine</c>: Catalog and Ordering are one schema here, so the
/// line can answer with Catalog's own <c>Product</c>. Where each module is a source schema of its own, Ordering
/// names the product by its SKU and a gateway fetches the rest from Catalog; see <c>ProductStub</c> in the
/// Ordering module.
/// </remarks>
[ObjectType<OrderLine>]
internal static partial class OrderLineProduct
{
    public static Task<Product?> GetProductAsync(
        [Parent] OrderLine line,
        ProductBySkuDataLoader products,
        CancellationToken cancellationToken)
        => products.LoadAsync(line.Sku, cancellationToken);
}
