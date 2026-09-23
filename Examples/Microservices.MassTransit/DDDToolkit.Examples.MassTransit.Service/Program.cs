using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.MassTransit.Service;
using DDDToolkit.Examples.Microservices;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.MassTransit;

// One of the shop's three services, talking to the other two through RabbitMQ, with MassTransit as the
// transport (RabbitMqTransport.cs). The same service as the Wolverine sample's, with a different transport
// block; each service has a SQL Server database of its own here.
//
// MassTransit 8, the last major version under the Apache 2.0 licence. See the README of
// DDDToolkit.Messaging.MassTransit before moving to 9.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var service = Enum.Parse<ShopService>(
    builder.Configuration["Shop:Service"] ?? throw new InvalidOperationException("Shop:Service is not set: storefront, payments or fulfilment."),
    ignoreCase: true);

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

builder.Services.AddRabbitMqTransport(
    service,
    builder.Configuration.GetConnectionString("rabbitmq")
        ?? throw new InvalidOperationException("ConnectionStrings:rabbitmq is not set. Run DDDToolkit.Examples.MassTransit.AppHost."));

var host = new ModuleHost(
    ModuleDatabase.FromConnectionStrings(builder.Configuration.GetConnectionString),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToMassTransit();
    });

builder.Services.AddShopService(service, host);

var app = builder.Build();

app.MapShopService(service);
app.MapDefaultEndpoints();

await app.RunAsync();
