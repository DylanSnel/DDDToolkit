using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Host;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

// dotnet run -- export-supabase [directory]
// Writes both modules' Entity Framework migrations into one Supabase migrations directory, then exits.
// Run it after every dotnet ef migrations add. Without a directory it finds the Supabase project the way
// the CLI does, by looking upwards for supabase/config.toml, which here is Examples/ModularMonolith.
if (args is ["export-supabase", .. var rest])
{
    var directory = rest is [var given] ? given : SupabaseMigrations.FindDirectory();
    var inSync = true;

    using (var ordering = new OrderingContextFactory().CreateDbContext(args))
    using (var shipping = new ShippingContextFactory().CreateDbContext(args))
    {
        foreach (var report in new[] { SupabaseMigrations.Export(ordering, directory), SupabaseMigrations.Export(shipping, directory) })
        {
            foreach (var entry in report.Entries)
            {
                Console.WriteLine($"{entry.Status,-12} {entry.MigrationId}");
            }

            inSync &= report.IsInSync;
        }
    }

    return inSync ? 0 : 1;
}

var builder = WebApplication.CreateBuilder(args);

// Mediator is registered scoped, not with its singleton default: a handler that injects a DbContext
// needs the scope's context, and a singleton cannot depend on a scoped service. The generator reads
// this very call at compile time, so the lifetime has to be written here.
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

builder.Services.AddDDDToolkitEntityFramework(options =>
{
    // Every payload shape this process can read back. The inbox needs it to turn a stored message into
    // the record a handler asked for, and it is also where an upcaster from an older version would go.
    options.MapIntegrationEvents(contracts => contracts.RegisterFromAssemblyContaining<OrderPlacedV1>());

    // Handlers inside the producing module, published through Mediator. See OrderPlacedLog.
    options.DispatchWithMediator();

    options.UseOutbox(outbox =>
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

        // The sink that matters in a modular monolith: hand the message to the other modules in this
        // process, each handler guarded by the inbox of the context it writes through.
        outbox.SendToModules<ShippingContext>();

        // Sinks normally replace the in-process delegate. This asks for both: the local handler sees
        // the domain event, the other modules see the contract.
        outbox.AlsoDispatchInProcess = true;
    });
});

builder.Services.AddModuleIntegrationEvents<ShippingContext>();
builder.Services.AddIntegrationEventHandler<OrderPlacedV1, BookShipment>();

// Delivery happens on this poll, not at commit. Ordering's transaction has already committed by then,
// which is exactly why a failing consumer cannot refuse an order.
builder.Services.AddOutboxBackgroundService<OrderingContext>(pollingInterval: TimeSpan.FromSeconds(1));

// Two ways to run. With no connection string, each module gets a SQLite file of its own next to the
// built binary: two databases rather than one is the cheapest way to be sure no query and no
// transaction ever crosses the boundary, and running the example leaves nothing behind in the source
// tree. With ConnectionStrings:Supabase (the "supabase" launch profile), both modules share one
// Postgres database, as they would on one Supabase project, each in a schema of its own.
var supabase = builder.Configuration.GetConnectionString("Supabase");

string Sqlite(string module) => builder.Configuration.GetConnectionString(module)
    ?? $"Data Source={Path.Combine(AppContext.BaseDirectory, module.ToLowerInvariant() + ".db")}";

builder.Services.AddDbContext<OrderingContext>((services, options) =>
{
    if (supabase is null)
    {
        // SQLite has no schemas, so the module's schema is dropped. Saying so on every start teaches nothing.
        options.UseSqlite(Sqlite("Ordering")).ConfigureWarnings(w => w.Ignore(SqliteEventId.SchemaConfiguredWarning));
    }
    else
    {
        OrderingContext.UsePostgres(options, supabase);
    }

    options.UseDDDToolkit(services);
});

builder.Services.AddDbContext<ShippingContext>((services, options) =>
{
    if (supabase is null)
    {
        options.UseSqlite(Sqlite("Shipping")).ConfigureWarnings(w => w.Ignore(SqliteEventId.SchemaConfiguredWarning));
    }
    else
    {
        ShippingContext.UsePostgres(options, supabase);
    }

    options.UseDDDToolkit(services);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var ordering = scope.ServiceProvider.GetRequiredService<OrderingContext>().Database;
    var shipping = scope.ServiceProvider.GetRequiredService<ShippingContext>().Database;

    if (supabase is null)
    {
        // SQLite is the throwaway path, so it creates the two databases from the model.
        await ordering.EnsureCreatedAsync();
        await shipping.EnsureCreatedAsync();
    }
    else
    {
        // On Supabase the migrations are Supabase's to apply, from supabase/migrations. The application
        // only checks: if it applied them as well, the CLI would try to apply them a second time.
        var pending = (await ordering.GetPendingMigrationsAsync()).Concat(await shipping.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"The database is missing {string.Join(", ", pending)}. Apply supabase/migrations first: " +
                "'supabase db reset' locally, 'supabase db push' for a linked project.");
        }
    }
}

app.MapOrderingEndpoints();
app.MapShippingEndpoints();

await app.RunAsync();

return 0;
