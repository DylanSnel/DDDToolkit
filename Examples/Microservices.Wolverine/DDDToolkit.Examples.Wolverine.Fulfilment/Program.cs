using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Inventory;
using DDDToolkit.Examples.Inventory.Api;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Examples.Shipping.Api;
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

const string Exchange = "integration-events";

// What this service wants from the others, by the contracts' published names. The senders do not know it
// exists: they publish to the exchange, and this service's queue is bound to what it consumes.
string[] consumes =
[
    "ordering.order-placed",
    "ordering.order-cancelled",
    "ordering.order-confirmed",
];

builder.UseWolverine(wolverine =>
{
    wolverine.UseRabbitMq(new Uri(builder.Configuration.GetConnectionString("rabbitmq")
            ?? throw new InvalidOperationException("ConnectionStrings:rabbitmq is not set. Run DDDToolkit.Examples.Wolverine.AppHost.")))
        .AutoProvision();

    // Sending: every envelope to one topic exchange, keyed on the contract's name. Inline, so the outbox
    // hears that RabbitMQ has it before it marks the row done.
    wolverine.PublishMessagesToRabbitMqExchange<IntegrationEventEnvelope>(Exchange, envelope => envelope.Name)
        .ExchangeType(ExchangeType.Topic)
        .SendInline();

    // Receiving: this service's queue. Inline, so an envelope is acknowledged only after the modules
    // applied it.
    wolverine.ListenToRabbitQueue("fulfilment", queue =>
        {
            foreach (var contract in consumes)
            {
                queue.BindExchange(Exchange, contract);
            }
        })
        .ProcessInline();

    // The envelope's handler, handing it to the modules; retried, then Wolverine's error queue.
    wolverine.ReceiveIntegrationEvents();

    // An envelope this process publishes goes to RabbitMQ, never straight to the local handler.
    wolverine.Policies.DisableConventionalLocalRouting();
});

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

var app = builder.Build();

app.MapInventoryEndpoints();
app.MapShippingEndpoints();
app.MapDefaultEndpoints();

await app.RunAsync();
