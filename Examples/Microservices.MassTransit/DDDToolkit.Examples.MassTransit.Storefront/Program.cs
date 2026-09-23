using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Catalog;
using DDDToolkit.Examples.Catalog.Api;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.MassTransit.Storefront;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Api;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.MassTransit;

// The shop's storefront service, over RabbitMQ with MassTransit 8 as the transport (RabbitMq.cs). Catalog and
// Ordering: what a customer browses and buys. Ordering reads Catalog's prices through the module sink, next
// door, as in the monolith; only what another service consumes leaves the process.
//
// On a SQL Server database of its own, as are the other two. MassTransit 8 is the last version under the
// Apache 2.0 licence; see the README of DDDToolkit.Messaging.MassTransit before moving to 9.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

builder.Services.AddRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")
    ?? throw new InvalidOperationException("ConnectionStrings:rabbitmq is not set. Run DDDToolkit.Examples.MassTransit.AppHost."));

var host = new ModuleHost(
    ModuleDatabase.SqlServer(builder.Configuration.GetConnectionString("storefront-db")
        ?? throw new InvalidOperationException("ConnectionStrings:storefront-db is not set. Run DDDToolkit.Examples.MassTransit.AppHost.")),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToMassTransit();
    });

builder.Services.AddCatalogModule(host);
builder.Services.AddOrderingModule(host);

var app = builder.Build();

app.MapCatalogEndpoints();
app.MapOrderingEndpoints();
app.MapDefaultEndpoints();

await app.RunAsync();
