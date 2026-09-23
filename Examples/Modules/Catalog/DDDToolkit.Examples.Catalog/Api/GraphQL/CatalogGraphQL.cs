using DDDToolkit.Examples.Catalog.GraphQl;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.Catalog.Api.GraphQL;

/// <summary>Catalog's part of a GraphQL schema. The host builds the schema and calls this.</summary>
public static class CatalogGraphQL
{
    public static IRequestExecutorBuilder AddCatalogGraphQL(this IRequestExecutorBuilder graphql)
    {
        ArgumentNullException.ThrowIfNull(graphql);

        return graphql
            // The toolkit's generated bindings: ProductId prints as UUID, and a node id carries it.
            .AddCatalogGraphQlRuntimeBindings()
            .AddType<ProductType>()
            .AddTypeExtension<CatalogQueries>()
            .AddTypeExtension<CatalogMutations>()
            .AddDataLoader<ProductBySkuDataLoader>()
            .AddDataLoader<ProductByIdDataLoader>();
    }
}
