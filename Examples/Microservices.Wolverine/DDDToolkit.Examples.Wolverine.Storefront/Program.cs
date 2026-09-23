using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Catalog;
using DDDToolkit.Examples.Catalog.Api;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Api;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.Wolverine;
using Wolverine;
using Wolverine.RabbitMQ;

// The shop's storefront service, over RabbitMQ with Wolverine as the transport. Catalog and Ordering: what a
// customer browses and buys. Ordering reads Catalog's prices through the module sink, next door, as in the
// monolith; only what another service consumes leaves the process.
//
// It runs on SQL Server; Payments and Fulfilment run on Postgres. The modules cannot tell, and neither can
// the messages.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

const string Exchange = "integration-events";

// What this service wants from the others, by the contracts' published names. The senders do not know it
// exists: they publish to the exchange, and this service's queue is bound to what it consumes.
string[] consumes =
[
    "inventory.stock-reserved",
    "inventory.stock-reservation-failed",
    "payments.payment-succeeded",
    "payments.payment-failed",
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
    wolverine.ListenToRabbitQueue("storefront", queue =>
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
    ModuleDatabase.SqlServer(builder.Configuration.GetConnectionString("storefront-db")
        ?? throw new InvalidOperationException("ConnectionStrings:storefront-db is not set. Run DDDToolkit.Examples.Wolverine.AppHost.")),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToWolverine();
    });

builder.Services.AddCatalogModule(host);
builder.Services.AddOrderingModule(host);

var app = builder.Build();

app.MapCatalogEndpoints();
app.MapOrderingEndpoints();
app.MapDefaultEndpoints();

await app.RunAsync();
