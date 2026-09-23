using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Ordering.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Examples.Ordering;

/// <summary>
/// Everything Ordering needs from the host, registered by Ordering: its context, its outbox and the
/// poller that empties it, and its place in the Supabase migrations. The host calls
/// <see cref="AddOrderingModule"/> and learns nothing else about how Ordering stores its orders.
/// </summary>
public static class OrderingModule
{
    /// <summary>
    /// Ordering's migrations, for the Supabase export and the start-up check. Built from the design-time
    /// factory <c>dotnet ef</c> uses, so exporting needs no host.
    /// </summary>
    public static readonly SupabaseMigrationSource SupabaseMigrations = SupabaseMigrationSource.For<OrderingContext, OrderingContextFactory>();

    /// <summary>
    /// Registers Ordering. With a Supabase connection string the module lives in the <c>ordering</c>
    /// schema of that database, and Supabase applies its migrations. Without one it gets a SQLite file of
    /// its own next to the built binary, created from the model on start-up, which is what running the
    /// example without any setup uses.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="supabaseConnectionString">The Supabase database, or <see langword="null"/> for SQLite.</param>
    public static IServiceCollection AddOrderingModule(this IServiceCollection services, string? supabaseConnectionString)
    {
        services.AddDbContext<OrderingContext>((provider, options) =>
        {
            if (supabaseConnectionString is null)
            {
                // SQLite has no schemas, so the module's schema is dropped. Saying so on every start teaches nothing.
                options.UseSqlite($"Data Source={Path.Combine(AppContext.BaseDirectory, "ordering.db")}")
                    .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning));
            }
            else
            {
                OrderingContext.UsePostgres(options, supabaseConnectionString);
            }

            options.UseDDDToolkit(provider);
        });

        if (supabaseConnectionString is null)
        {
            services.AddHostedService<CreateOrderingDatabase>();
        }
        else
        {
            services.AddSupabaseMigrations(SupabaseMigrations);
        }

        // Ordering produces integration events, so it owns an outbox: what it publishes, as what, and
        // that it goes to whichever modules want it. It does not know which modules those are.
        services.AddDDDToolkitEntityFramework(options => options.UseOutbox<OrderingContext>(outbox =>
        {
            outbox.RegisterEventsFromAssemblyContaining<Order>();

            // The seam. OrderPlaced is Ordering's to change; OrderPlacedV1 is what everyone deployed
            // against. The conversion runs at delivery, so the stored row stays a faithful record of what
            // happened in the domain.
            outbox.PublishAs<OrderPlaced, OrderPlacedV1>(placed => new OrderPlacedV1(
                placed.OrderId,
                placed.ShipTo.City,
                placed.ShipTo.PostalCode,
                placed.LineCount));

            outbox.SendToModules();

            // Sinks normally replace the in-process delegate. This asks for both: the local handler
            // (OrderPlacedLog) sees the domain event, the other modules see the contract.
            outbox.AlsoDispatchInProcess = true;
        }));

        // Delivery happens on this poll, not at commit. Ordering's transaction has already committed by
        // then, which is exactly why a failing consumer cannot refuse an order.
        services.AddOutboxBackgroundService<OrderingContext>(pollingInterval: TimeSpan.FromSeconds(1));

        return services;
    }

    /// <summary>
    /// Creates the SQLite file before anything else starts, the outbox poller included: StartingAsync runs
    /// for every lifecycle service before any hosted service's StartAsync.
    /// </summary>
    private sealed class CreateOrderingDatabase(IServiceScopeFactory scopes) : IHostedLifecycleService
    {
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<OrderingContext>().Database.EnsureCreatedAsync(cancellationToken);
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
