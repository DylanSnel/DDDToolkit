using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Host;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Mediator;
using Microsoft.EntityFrameworkCore;

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

// One database per module. Two connection strings rather than one is the cheapest way to be sure no
// query and no transaction ever crosses the boundary by accident. They are SQLite files next to the
// built binary, so running the example leaves nothing behind in the source tree.
string Database(string module) => builder.Configuration.GetConnectionString(module)
    ?? $"Data Source={Path.Combine(AppContext.BaseDirectory, module.ToLowerInvariant() + ".db")}";

builder.Services.AddDbContext<OrderingContext>((services, options) => options
    .UseSqlite(Database("Ordering"))
    .UseDDDToolkit(services));

builder.Services.AddDbContext<ShippingContext>((services, options) => options
    .UseSqlite(Database("Shipping"))
    .UseDDDToolkit(services));

var app = builder.Build();

// A real deployment migrates. This is an example, so it creates the two databases on start-up.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<OrderingContext>().Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<ShippingContext>().Database.EnsureCreatedAsync();
}

app.MapOrderingEndpoints();
app.MapShippingEndpoints();

await app.RunAsync();
