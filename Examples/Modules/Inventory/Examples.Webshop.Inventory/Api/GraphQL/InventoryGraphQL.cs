using DDDToolkit.HotChocolate;
using Examples.Webshop.Inventory.GraphQl;
using Examples.Webshop.Ordering.Contracts.GraphQl;
using GreenDonut;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Webshop.Inventory.Api.GraphQL;

/// <summary>Inventory's part of a GraphQL schema. The host builds the schema and calls this.</summary>
public static class InventoryGraphQL
{
    public static IRequestExecutorBuilder AddInventoryGraphQL(this IRequestExecutorBuilder graphql)
    {
        ArgumentNullException.ThrowIfNull(graphql);

        return graphql
            .AddInventoryGraphQlRuntimeBindings()
            .AddOrderingGraphQlRuntimeBindings()
            // HotChocolate's generated registration, named in Module.cs: the methods marked [Query] as fields,
            // the Query type they are fields of, the types of this file and of ProductStock.cs, and the loaders.
            .AddInventoryTypes();
    }

    /// <summary>The name of Inventory's source schema.</summary>
    public const string SourceSchemaName = "inventory";

    /// <summary>
    /// Inventory's source schema: a GraphQL schema of its own, named <see cref="SourceSchemaName"/>, for a
    /// Fusion gateway to compose with the other modules', in the same process or across services. It holds
    /// its stock and reservations, and the stock of a Product, by SKU.
    /// </summary>
    public static IRequestExecutorBuilder AddInventorySourceSchema(this IServiceCollection services)
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
            .AddInventoryGraphQL();
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

public static class InventoryQueries
{
    [Query]
    public static async Task<IReadOnlyList<StockItem>> GetStockAsync([Service] InventoryContext inventory, CancellationToken cancellationToken)
        => await inventory.StockItems.OrderBy(item => item.Sku).ToListAsync(cancellationToken);

    [Query]
    public static async Task<IReadOnlyList<StockReservation>> GetReservationsAsync([Service] InventoryContext inventory, CancellationToken cancellationToken)
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
