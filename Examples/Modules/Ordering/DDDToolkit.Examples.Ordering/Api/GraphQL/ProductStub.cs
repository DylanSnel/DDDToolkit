using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.Ordering.Api.GraphQL;

/// <summary>
/// A product as Ordering knows it: its SKU, and nothing else. The name and the price are Catalog's.
/// </summary>
public sealed record ProductStub(string Sku);

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
/// service, which runs both modules, joins the two in-process instead.
/// </para>
/// </remarks>
public sealed class ProductStubType : ObjectType<ProductStub>
{
    protected override void Configure(IObjectTypeDescriptor<ProductStub> descriptor)
    {
        descriptor.Name("Product");
        descriptor.BindFieldsExplicitly();
        descriptor.Directive(new EntityKey("sku"));
        descriptor.Field(product => product.Sku);
    }
}

/// <summary>The product a line is for, as the SKU the line holds.</summary>
[ExtendObjectType<OrderLine>]
public sealed class OrderLineProductStub
{
    public ProductStub? GetProduct([Parent] OrderLine line) => new(line.Sku);
}

public static class ProductStubGraphQL
{
    /// <summary>
    /// Adds <c>OrderLine.product</c> to a source schema that a Fusion gateway composes with Catalog's.
    /// </summary>
    public static IRequestExecutorBuilder AddOrderingProductStub(this IRequestExecutorBuilder graphql)
    {
        ArgumentNullException.ThrowIfNull(graphql);
        return graphql
            .AddType<ProductStubType>()
            .AddTypeExtension<OrderLineProductStub>();
    }
}
