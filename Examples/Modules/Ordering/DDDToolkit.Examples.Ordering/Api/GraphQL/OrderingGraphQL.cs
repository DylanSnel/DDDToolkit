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
}
