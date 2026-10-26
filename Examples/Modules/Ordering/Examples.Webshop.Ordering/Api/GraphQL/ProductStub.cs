using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Webshop.Ordering.Api.GraphQL;

/// <summary>
/// A product as Ordering knows it: its SKU, and nothing else. The name and the price are Catalog's.
/// </summary>
public sealed record ProductStub(string Sku);

/// <summary>The product a line is for, as the SKU the line holds.</summary>
public sealed class OrderLineProductStub
{
    public ProductStub? GetProduct([Parent] OrderLine line) => new(line.Sku);
}

/// <summary>
/// Ordering's part of the <c>Product</c> type, for a schema a Fusion gateway composes with Catalog's.
/// </summary>
/// <remarks>
/// A line knows the SKU it was ordered by. Ordering publishes that as a <c>Product</c> keyed on its
/// <c>sku</c>, and the gateway fetches everything else about the product from Catalog through its
/// <c>productBySku</c> lookup. Ordering does not read Catalog's tables and does not know Catalog's
/// <c>Product</c> class; it names the type, and the key the two schemas agree on.
/// <para>
/// Not for a schema that also has Catalog's <c>Product</c>: two types of one name. The storefront
/// service, which runs both modules, joins the two in-process instead, with an <c>[ObjectType&lt;OrderLine&gt;]</c>
/// class of its own. So this part is asked for, and described here by hand: what HotChocolate's generator
/// finds in a project, a type class or a static partial class with <c>[ObjectType&lt;T&gt;]</c>, it registers
/// in every schema the project is part of.
/// </para>
/// </remarks>
public static class ProductStubGraphQL
{
    /// <summary>
    /// Adds <c>OrderLine.product</c> to a source schema that a Fusion gateway composes with Catalog's.
    /// </summary>
    public static IRequestExecutorBuilder AddOrderingProductStub(this IRequestExecutorBuilder graphql)
    {
        ArgumentNullException.ThrowIfNull(graphql);
        return graphql
            .AddObjectType<ProductStub>(product =>
            {
                product.Name("Product");
                product.BindFieldsExplicitly();
                product.Directive(new EntityKey("sku"));
                product.Field(stub => stub.Sku);
            })
            .AddTypeExtension(new ObjectTypeExtension<OrderLine>(line =>
            {
                line.BindFieldsExplicitly();
                line.Field("product").ResolveWith<OrderLineProductStub>(stub => stub.GetProduct(default!));
            }));
    }
}
