using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Inventory;
using DDDToolkit.Examples.Inventory.Api;
using DDDToolkit.Examples.Inventory.Api.GraphQL;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Examples.Shipping.Api;
using DDDToolkit.Examples.Shipping.Api.GraphQL;
using DDDToolkit.HotChocolate;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.Wolverine;
using Wolverine;
using Wolverine.RabbitMQ;

// The shop's fulfilment service, over RabbitMQ with Wolverine as the transport. Inventory and Shipping: the
// warehouse.
//
// It runs on Postgres; Storefront runs on SQL Server. The modules cannot tell, and neither can the messages.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

var host = new ModuleHost(
    ModuleDatabase.Postgres(builder.Configuration.GetConnectionString("fulfilment-db")
        ?? throw new InvalidOperationException("ConnectionStrings:fulfilment-db is not set. Run DDDToolkit.Examples.Wolverine.AppHost.")),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToWolverine();
    });

builder.Services.AddInventoryModule(host);
builder.Services.AddShippingModule(host);

// RabbitMQ the way Wolverine uses it: conventional routing. Every contract this service publishes goes to a
// fanout exchange of its own type, and every contract it has to be sent gets a queue of this service's,
// bound to that type's exchange. Nothing here names another service or a contract: the modules registered
// above say what this service handles, which is why they come first. Inline both ways, so the outbox hears
// that RabbitMQ has a message before it marks the row done, and a message is acknowledged only after the
// modules applied it.
builder.UseWolverine(wolverine =>
{
    wolverine.UseRabbitMq(new Uri(builder.Configuration.GetConnectionString("rabbitmq")
            ?? throw new InvalidOperationException("ConnectionStrings:rabbitmq is not set. Run DDDToolkit.Examples.Wolverine.AppHost.")))
        .AutoProvision()
        .UseConventionalRouting(conventions => conventions
            .QueueNameForListener(type => $"fulfilment.{type.Name}")
            .ConfigureListeners((listener, _) => listener.ProcessInline())
            .ConfigureSending((sender, _) => sender.SendInline()));

    // A handler per contract this service has to be sent, handing it to the modules; retried, then
    // Wolverine's error queue.
    wolverine.ReceiveIntegrationEvents(builder.Services.IntegrationEventSubscriptions());

    // A contract this process publishes goes to RabbitMQ, never straight to a local handler.
    wolverine.Policies.DisableConventionalLocalRouting();
});

// GraphQL: this service's source schema, which the gateway composes with the other two. Besides stock
// and shipments it declares Order, by id alone, with the one field Shipping adds to it: shipment.
builder.Services
    .AddGraphQLServer()
    .AddSourceSchemaDefaults()
    .AddGlobalObjectIdentification(options => options.MarkNodeFieldAsLookup = true)
    .AddDDDToolkitTypes()
    .AddDDDToolkitErrors()
    .AddQueryType()
    .AddInventoryGraphQL()
    .AddShippingGraphQL()
    .AddShippingOrderStub();

var app = builder.Build();

app.MapInventoryEndpoints();
app.MapShippingEndpoints();
app.MapGraphQL();
app.MapDefaultEndpoints();

await app.RunAsync();
