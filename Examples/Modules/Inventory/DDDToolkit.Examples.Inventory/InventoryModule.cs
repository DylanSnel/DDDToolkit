using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Inventory.Contracts;
using DDDToolkit.Examples.Ordering.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, string? supabaseConnectionString)
    {
        services.AddDbContext<InventoryContext>((provider, options) =>
        {
            if (supabaseConnectionString is null)
            {
                options.UseSqlite($"Data Source={Path.Combine(AppContext.BaseDirectory, "inventory.db")}")
                    .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning));
            }
            else
            {
                InventoryContext.UsePostgres(options, supabaseConnectionString);
            }

            options.UseDDDToolkit(provider);
        });

        if (supabaseConnectionString is null)
        {
            services.AddHostedService<CreateInventoryDatabase>();
        }
        else
        {
            services.AddSupabaseMigrations<InventoryContext, InventoryContextFactory>();
        }

        services.AddDDDToolkitEntityFramework(options => options
            .UseOutbox<InventoryContext>(outbox =>
            {
                outbox.RegisterEventsFromAssemblyContaining<StockItem>();
                outbox.PublishAs<StockReserved, StockReservedV1>(reserved => new StockReservedV1(reserved.OrderId));
                outbox.PublishAs<StockRefused, StockReservationFailedV1>(refused => new StockReservationFailedV1(refused.OrderId, refused.Reason));
                outbox.SendToModules();
            })
            .MapIntegrationEvents(contracts => contracts.RegisterFromAssemblyContaining<OrderPlacedV1>()));

        services.AddModuleIntegrationEvents<InventoryContext>(module => module
            .Handle<OrderPlacedV1, ReserveStock>()
            .Handle<OrderCancelledV1, ReleaseStock>());

        services.AddOutboxBackgroundService<InventoryContext>(pollingInterval: TimeSpan.FromSeconds(1));

        services.AddHostedService<StockTheShelves>();

        return services;
    }

    private sealed class CreateInventoryDatabase(IServiceScopeFactory scopes) : IHostedLifecycleService
    {
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<InventoryContext>().Database.EnsureCreatedAsync(cancellationToken);
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
