using DDDToolkit.Examples.Inventory.GraphQl;
using DDDToolkit.Examples.Ordering.Contracts.GraphQl;
using GreenDonut;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.Inventory.Api.GraphQL;

/// <summary>Inventory's part of a GraphQL schema. The host builds the schema and calls this.</summary>
public static class InventoryGraphQL
{
    public static IRequestExecutorBuilder AddInventoryGraphQL(this IRequestExecutorBuilder graphql)
    {
        ArgumentNullException.ThrowIfNull(graphql);

        return graphql
            .AddInventoryGraphQlRuntimeBindings()
            .AddOrderingContractsGraphQlRuntimeBindings()
            .AddType<StockItemType>()
            .AddType<StockReservationType>()
            .AddTypeExtension<InventoryQueries>()
            .AddDataLoader<StockItemByIdDataLoader>();
    }
}

/// <summary>One SKU's stock. A node, so a stock screen can refetch one item.</summary>
public sealed class StockItemType : ObjectType<StockItem>
{
    protected override void Configure(IObjectTypeDescriptor<StockItem> descriptor)
    {
        descriptor.BindFieldsExplicitly();

        descriptor
            .ImplementsNode()
            .IdField(item => item.Id)
            .ResolveNode(async (context, id) =>
                await context.DataLoader<StockItemByIdDataLoader>().LoadAsync(id, context.RequestAborted));

        descriptor.Field(item => item.Sku);
        descriptor.Field(item => item.OnHand);
        descriptor.Field(item => item.Reserved);
        descriptor.Field(item => item.Available);
    }
}

/// <summary>What Inventory decided for one order, pointing at the order by its node id.</summary>
public sealed class StockReservationType : ObjectType<StockReservation>
{
    protected override void Configure(IObjectTypeDescriptor<StockReservation> descriptor)
    {
        descriptor.BindFieldsExplicitly();

        descriptor.Field(reservation => reservation.Id);
        descriptor.Field(reservation => reservation.Order).ID("Order");
        descriptor.Field(reservation => reservation.Status);
        descriptor.Field(reservation => reservation.Refusal);
    }
}

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class InventoryQueries
{
    public async Task<IReadOnlyList<StockItem>> GetStockAsync(InventoryContext inventory, CancellationToken cancellationToken)
        => await inventory.StockItems.OrderBy(item => item.Sku).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StockReservation>> GetReservationsAsync(InventoryContext inventory, CancellationToken cancellationToken)
        => await inventory.StockReservations.ToListAsync(cancellationToken);
}

public sealed class StockItemByIdDataLoader(IServiceScopeFactory scopes, IBatchScheduler batchScheduler, DataLoaderOptions options)
    : BatchDataLoader<StockItemId, StockItem>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<StockItemId, StockItem>> LoadBatchAsync(IReadOnlyList<StockItemId> ids, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<InventoryContext>().StockItems
            .Where(item => ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
    }
}
