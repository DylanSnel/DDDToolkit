using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Ordering.Contracts.GraphQl;
using DDDToolkit.Examples.Ordering.GraphQl;
using DDDToolkit.HotChocolate;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.Ordering.Api.GraphQL;

/// <summary>Ordering's part of a GraphQL schema. The host builds the schema and calls this.</summary>
public static class OrderingGraphQL
{
    /// <summary>The topic an order's updates are pushed to.</summary>
    public static string Topic(OrderId id) => $"order:{id}";

    public static IRequestExecutorBuilder AddOrderingGraphQL(this IRequestExecutorBuilder graphql)
    {
        ArgumentNullException.ThrowIfNull(graphql);

        // Which of Ordering's contracts reach subscribed clients, and on which topic. The host still has
        // to send Ordering's outbox to GraphQlSubscriptionSink; this only says what that sink pushes.
        graphql.Services.AddIntegrationEventSubscriptions(map => map
            .Publish<OrderConfirmedV1>(message => message.Body is OrderConfirmedV1 confirmed ? Topic(confirmed.OrderId) : null)
            .Publish<OrderCancelledV1>(message => message.Body is OrderCancelledV1 cancelled ? Topic(cancelled.OrderId) : null));

        return graphql
            // OrderId lives in the contracts, OrderLineId in the module: one generated call each.
            .AddOrderingContractsGraphQlRuntimeBindings()
            .AddOrderingGraphQlRuntimeBindings()
            .AddType<OrderType>()
            .AddType<OrderLineType>()
            .AddTypeExtension<OrderingQueries>()
            .AddTypeExtension<OrderingMutations>()
            .AddTypeExtension<OrderingSubscriptions>()
            .AddDataLoader<OrderByIdDataLoader>();
    }

    /// <summary>The name of Ordering's source schema.</summary>
    public const string SourceSchemaName = "ordering";

    /// <summary>
    /// Ordering's source schema: a GraphQL schema of its own, named <see cref="SourceSchemaName"/>, for a
    /// Fusion gateway to compose with the other modules', in the same process or across services. It holds
    /// its orders, and a line's product as the Product with that SKU, for Catalog to fill in. Its
    /// subscriptions need a transport, which is the host's to choose: <c>AddInMemorySubscriptions()</c> on the
    /// builder this returns, for a single process.
    /// </summary>
    public static IRequestExecutorBuilder AddOrderingSourceSchema(this IServiceCollection services)
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
            .AddSubscriptionType()
            .AddOrderingGraphQL()
            .AddOrderingProductStub();
    }
}
