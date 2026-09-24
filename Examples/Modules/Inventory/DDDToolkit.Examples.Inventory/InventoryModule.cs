using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Inventory.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Examples.Inventory;

/// <summary>Everything Inventory needs from the host, registered by Inventory.</summary>
public static class InventoryModule
{
    /// <summary>
    /// Registers Inventory: its context, its outbox, the two policies it follows, and the stock the
    /// example starts with.
    /// </summary>
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, ModuleHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.Database.AddContext<InventoryContext, InventoryContextFactory>(services, InventoryContext.Schema);

        services.AddDDDToolkitEntityFramework(options => options
            .UseOutbox<InventoryContext>(outbox =>
            {
                outbox.AddInventoryIntegrationEvents();
                host.Publish(outbox);
            })
            .MapIntegrationEvents(contracts => contracts.AddInventoryIntegrationEvents()));

        services.AddModuleIntegrationEvents<InventoryContext>(module => module.AddInventoryIntegrationEvents());

        services.AddOutboxBackgroundService<InventoryContext>(pollingInterval: TimeSpan.FromSeconds(1));

        services.AddHostedService<StockTheShelves>();

        // GraphQL, when the host serves it: Inventory's own source schema, for a Fusion gateway to compose.
        if (host.GraphQL is { } graphql)
        {
            graphql(services.AddInventorySourceSchema());
        }

        return services;
    }

    /// <summary>
    /// Puts the example's starting stock on the shelves the first time the module starts. Only two mugs,
    /// which is how the <c>.http</c> file shows an order refused for want of stock.
    /// </summary>
    private sealed class StockTheShelves(IServiceScopeFactory scopes) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryContext>();

            if (await inventory.StockItems.AnyAsync(cancellationToken))
            {
                return;
            }

            inventory.StockItems.AddRange(
                new StockItem(StockItemId.CreateSequential(), "COFFEE-1KG", onHand: 20),
                new StockItem(StockItemId.CreateSequential(), "MUG", onHand: 2),
                new StockItem(StockItemId.CreateSequential(), "GRINDER", onHand: 5));

            await inventory.SaveChangesAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
