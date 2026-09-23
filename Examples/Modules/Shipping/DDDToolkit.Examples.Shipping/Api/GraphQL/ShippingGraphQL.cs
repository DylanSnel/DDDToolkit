using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Ordering.Contracts.GraphQl;
using DDDToolkit.Examples.Shipping.GraphQl;
using GreenDonut;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.Shipping.Api.GraphQL;

/// <summary>Shipping's part of a GraphQL schema. The host builds the schema and calls this.</summary>
public static class ShippingGraphQL
{
    public static IRequestExecutorBuilder AddShippingGraphQL(this IRequestExecutorBuilder graphql)
    {
        ArgumentNullException.ThrowIfNull(graphql);

        return graphql
            .AddShippingGraphQlRuntimeBindings()
            .AddOrderingContractsGraphQlRuntimeBindings()
            .AddType<ShipmentType>()
            .AddTypeExtension<ShippingQueries>()
            .AddDataLoader<ShipmentByIdDataLoader>()
            .AddDataLoader<ShipmentByOrderDataLoader>();
    }
}

/// <summary>A shipment: a node, pointing at its order by the order's node id.</summary>
public sealed class ShipmentType : ObjectType<Shipment>
{
    protected override void Configure(IObjectTypeDescriptor<Shipment> descriptor)
    {
        descriptor.BindFieldsExplicitly();

        descriptor
            .ImplementsNode()
            .IdField(shipment => shipment.Id)
            .ResolveNode(async (context, id) =>
                await context.DataLoader<ShipmentByIdDataLoader>().LoadAsync(id, context.RequestAborted));

        descriptor.Field(shipment => shipment.Order).ID("Order");
        descriptor.Field(shipment => shipment.Destination);
        descriptor.Field(shipment => shipment.ConfirmedAt);
    }
}

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class ShippingQueries
{
    public async Task<IReadOnlyList<Shipment>> GetShipmentsAsync(ShippingContext shipping, CancellationToken cancellationToken)
        => await shipping.Shipments.ToListAsync(cancellationToken);
}

public sealed class ShipmentByIdDataLoader(IServiceScopeFactory scopes, IBatchScheduler batchScheduler, DataLoaderOptions options)
    : BatchDataLoader<ShipmentId, Shipment>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<ShipmentId, Shipment>> LoadBatchAsync(IReadOnlyList<ShipmentId> ids, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ShippingContext>().Shipments
            .Where(shipment => ids.Contains(shipment.Id))
            .ToDictionaryAsync(shipment => shipment.Id, cancellationToken);
    }
}

/// <summary>The shipment of each order, batched: Shipping's lookup for <c>Order.shipment</c>.</summary>
public sealed class ShipmentByOrderDataLoader(IServiceScopeFactory scopes, IBatchScheduler batchScheduler, DataLoaderOptions options)
    : BatchDataLoader<OrderId, Shipment>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<OrderId, Shipment>> LoadBatchAsync(IReadOnlyList<OrderId> orders, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ShippingContext>().Shipments
            .Where(shipment => orders.Contains(shipment.Order))
            .ToDictionaryAsync(shipment => shipment.Order, cancellationToken);
    }
}
