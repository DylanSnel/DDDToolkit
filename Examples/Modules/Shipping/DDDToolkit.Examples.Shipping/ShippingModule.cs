using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Ordering.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Examples.Shipping;

/// <summary>
/// Everything Shipping needs from the host, registered by Shipping: its context, the contracts it reads,
/// the integration events it handles under its own inbox, and the start-up check on its Supabase
/// migrations.
/// </summary>
public static class ShippingModule
{
    /// <summary>
    /// Registers Shipping. With a Supabase connection string the module lives in the <c>shipping</c>
    /// schema of that database, and Supabase applies its migrations. Without one it gets a SQLite file of
    /// its own, created from the model on start-up.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="supabaseConnectionString">The Supabase database, or <see langword="null"/> for SQLite.</param>
    public static IServiceCollection AddShippingModule(this IServiceCollection services, string? supabaseConnectionString)
    {
        services.AddDbContext<ShippingContext>((provider, options) =>
        {
            if (supabaseConnectionString is null)
            {
                options.UseSqlite($"Data Source={Path.Combine(AppContext.BaseDirectory, "shipping.db")}")
                    .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning));
            }
            else
            {
                ShippingContext.UsePostgres(options, supabaseConnectionString);
            }

            options.UseDDDToolkit(provider);
        });

        if (supabaseConnectionString is null)
        {
            services.AddHostedService<CreateShippingDatabase>();
        }
        else
        {
            // The start-up check covers this context. The export does not need this line: the build
            // finds ShippingContextFactory by its [SupabaseMigrations] marker.
            services.AddSupabaseMigrations<ShippingContext, ShippingContextFactory>();
        }

        // The payload shapes Shipping reads. The inbox needs them to turn a delivered message into the
        // record BookShipment asked for, and an upcaster from an older version would go here too.
        services.AddDDDToolkitEntityFramework(options =>
            options.MapIntegrationEvents(contracts => contracts.RegisterFromAssemblyContaining<OrderPlacedV1>()));

        // Shipping signs up as a consumer, with its own inbox. Ordering never names Shipping: it only says
        // SendToModules(), and every module registered here is offered every message.
        services.AddModuleIntegrationEvents<ShippingContext>(module => module.Handle<OrderConfirmedV1, BookShipment>());

        return services;
    }

    /// <summary>Creates the SQLite file before anything else starts.</summary>
    private sealed class CreateShippingDatabase(IServiceScopeFactory scopes) : IHostedLifecycleService
    {
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ShippingContext>().Database.EnsureCreatedAsync(cancellationToken);
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
