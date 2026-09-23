using DDDToolkit.Examples.Ordering.Contracts;
using GreenDonut;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.Ordering.Api.GraphQL;

/// <summary>
/// Orders by id, batched, with their lines. What <c>node(id:)</c> asks for an order, and the lookup a
/// composing layer uses to put an order under something another module owns.
/// </summary>
/// <remarks>
/// Each batch gets a scope, and so an <see cref="OrderingContext"/>, of its own: HotChocolate resolves
/// fields in parallel, and a context shared between two resolvers would be used by both at once.
/// </remarks>
public sealed class OrderByIdDataLoader(IServiceScopeFactory scopes, IBatchScheduler batchScheduler, DataLoaderOptions options)
    : BatchDataLoader<OrderId, Order>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<OrderId, Order>> LoadBatchAsync(IReadOnlyList<OrderId> ids, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrderingContext>();

        return await orders.Orders
            .Where(order => ids.Contains(order.Id))
            .ToDictionaryAsync(order => order.Id, cancellationToken);
    }
}
