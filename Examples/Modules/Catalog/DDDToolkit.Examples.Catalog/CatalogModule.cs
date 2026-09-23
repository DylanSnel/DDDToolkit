using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Catalog.Contracts;
using DDDToolkit.Examples.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Examples.Catalog;

/// <summary>Everything Catalog needs from the host, registered by Catalog.</summary>
public static class CatalogModule
{
    /// <summary>
    /// Registers Catalog: its context, its outbox, and the products the example starts with. With a
    /// Supabase connection string it lives in the <c>catalog</c> schema; without one, in a SQLite file.
    /// </summary>
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, string? supabaseConnectionString)
    {
        services.AddDbContext<CatalogContext>((provider, options) =>
        {
            if (supabaseConnectionString is null)
            {
                options.UseSqlite($"Data Source={Path.Combine(AppContext.BaseDirectory, "catalog.db")}")
                    .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning));
            }
            else
            {
                CatalogContext.UsePostgres(options, supabaseConnectionString);
            }

            options.UseDDDToolkit(provider);
        });

        if (supabaseConnectionString is null)
        {
            services.AddHostedService<CreateCatalogDatabase>();
        }
        else
        {
            services.AddSupabaseMigrations<CatalogContext, CatalogContextFactory>();
        }

        services.AddDDDToolkitEntityFramework(options => options.UseOutbox<CatalogContext>(outbox =>
        {
            outbox.RegisterEventsFromAssemblyContaining<Product>();
            outbox.PublishAs<ProductListed, ProductListedV1>(listed =>
                new ProductListedV1(listed.Sku, listed.Name, listed.Price.Amount, listed.Price.Currency));
            outbox.PublishAs<ProductPriceChanged, ProductPriceChangedV1>(changed =>
                new ProductPriceChangedV1(changed.Sku, changed.Price.Amount, changed.Price.Currency));
            outbox.SendToModules();
        }));

        services.AddOutboxBackgroundService<CatalogContext>(pollingInterval: TimeSpan.FromSeconds(1));

        services.AddHostedService<ListTheStartingRange>();

        return services;
    }

    /// <summary>Creates the SQLite file before anything else starts.</summary>
    private sealed class CreateCatalogDatabase(IServiceScopeFactory scopes) : IHostedLifecycleService
    {
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CatalogContext>().Database.EnsureCreatedAsync(cancellationToken);
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Lists three products the first time the module starts, so the example has something to sell.
    /// </summary>
    /// <remarks>
    /// They are listed the way any product is, through the constructor, so each raises
    /// <see cref="ProductListed"/> and Ordering learns their prices from the outbox like it would for
    /// one added over HTTP. Nothing is written into another module's tables. The grinder costs more than
    /// the example's payment provider accepts, which is how the <c>.http</c> file shows a declined
    /// payment.
    /// </remarks>
    private sealed class ListTheStartingRange(IServiceScopeFactory scopes) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

            if (await catalog.Products.AnyAsync(cancellationToken))
            {
                return;
            }

            catalog.Products.AddRange(
                new Product(ProductId.CreateSequential(), "COFFEE-1KG", "Coffee beans, 1 kg", new Money(12.50m, Money.Euro).ToValid()),
                new Product(ProductId.CreateSequential(), "MUG", "Mug", new Money(8.00m, Money.Euro).ToValid()),
                new Product(ProductId.CreateSequential(), "GRINDER", "Espresso grinder", new Money(1450.00m, Money.Euro).ToValid()));

            await catalog.SaveChangesAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
