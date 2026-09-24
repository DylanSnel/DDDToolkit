using HotChocolate.Types;

namespace DDDToolkit.Examples.Catalog.Api.GraphQL;

/// <summary>
/// How a <see cref="Product"/> looks in the schema. Code-first, next to the domain rather than on it:
/// <c>Product.cs</c> has no GraphQL attribute and no idea it is published.
/// </summary>
/// <remarks>
/// A Relay node, so any client can refetch it with <c>node(id:)</c>. The id is the <c>ProductId</c>
/// itself: the toolkit generates a node id serializer for every identifier, and
/// <c>AddCatalogGraphQlRuntimeBindings()</c> in <see cref="CatalogGraphQL"/> registers it.
/// </remarks>
public sealed class ProductType : ObjectType<Product>
{
    protected override void Configure(IObjectTypeDescriptor<Product> descriptor)
    {
        descriptor
            .ImplementsNode()
            .IdField(product => product.Id)
            .ResolveNode(async (context, id) =>
                await context.DataLoader<ProductByIdDataLoader>().LoadAsync(id, context.RequestAborted));
    }
}
