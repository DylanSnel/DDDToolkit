using DDDToolkit.Examples.Catalog.GraphQl;
using DDDToolkit.HotChocolate;
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

    /// <summary>The name of Catalog's source schema.</summary>
    public const string SourceSchemaName = "catalog";

    /// <summary>
    /// Catalog's source schema: a GraphQL schema of its own, named <see cref="SourceSchemaName"/>, for a
    /// Fusion gateway to compose with the other modules', in the same process or across services. It holds
    /// its products, the queries and mutations on them, and productBySku, the lookup a Product is fetched by.
    /// </summary>
    public static IRequestExecutorBuilder AddCatalogSourceSchema(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddGraphQLServer(SourceSchemaName)
            // A schema a gateway composes: lookups inferred as keys, node fields shareable.
            .AddSourceSchemaDefaults()
            // Relay, with node(id:) as the lookup a gateway fetches this module's part of a type through.
            .AddGlobalObjectIdentification(options => options.MarkNodeFieldAsLookup = true)
            .AddDDDToolkitTypes()
            .AddDDDToolkitErrors()
            .AddQueryType()
            .AddMutationType()
            .AddCatalogGraphQL();
    }
}
