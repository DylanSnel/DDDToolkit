using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Microservices;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.Wolverine;
using Wolverine;
using Wolverine.RabbitMQ;

// One of the shop's three services, talking to the other two through RabbitMQ, with Wolverine as the
// transport. The AppHost starts this project three times; which modules a copy runs is configuration, and
// everything about Wolverine is in the one UseWolverine block below.
//
// Each service owns its own database here, and they are not all the same kind: Storefront runs on SQL
// Server, Payments and Fulfilment on Postgres. The modules cannot tell, and neither can the messages.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var service = Enum.Parse<ShopService>(
    builder.Configuration["Shop:Service"] ?? throw new InvalidOperationException("Shop:Service is not set: storefront, payments or fulfilment."),
    ignoreCase: true);
var name = ShopServices.NameOf(service);

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

const string Exchange = "integration-events";

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

    // Receiving: this service's queue, bound to the contracts ShopServices routes to it. Inline, so an
    // envelope is acknowledged only after the modules applied it.
    wolverine.ListenToRabbitQueue(name, queue =>
        {
            foreach (var contract in ShopServices.ContractsFor(service))
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
    ModuleDatabase.FromConnectionStrings(builder.Configuration.GetConnectionString),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToWolverine();
    });

builder.Services.AddShopService(service, host);

var app = builder.Build();

app.MapShopService(service);
app.MapDefaultEndpoints();

await app.RunAsync();
