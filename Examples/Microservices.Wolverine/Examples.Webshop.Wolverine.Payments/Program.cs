using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework;
using DDDToolkit.HotChocolate;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.Wolverine;
using Examples.Hosting;
using Examples.Webshop.Payments;
using Examples.Webshop.Payments.Api;
using Examples.Webshop.Payments.Api.GraphQL;
using Wolverine;
using Wolverine.RabbitMQ;

// The shop's payments service, over RabbitMQ with Wolverine as the transport. Payments on its own: the one
// service that talks to the payment provider.
//
// It runs on Postgres; Storefront runs on SQL Server. The modules cannot tell, and neither can the messages.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

var host = new ModuleHost(
    ModuleDatabase.Postgres(builder.Configuration.GetConnectionString("payments-db")
        ?? throw new InvalidOperationException("ConnectionStrings:payments-db is not set. Run Examples.Webshop.Wolverine.AppHost.")),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToWolverine();
    });

builder.Services.AddPaymentsModule(host);

// RabbitMQ the way Wolverine uses it: conventional routing. Every contract this service publishes goes to a
// fanout exchange of its own type, and every contract it has to be sent gets a queue of this service's,
// bound to that type's exchange. Nothing here names another service or a contract: the modules registered
// above say what this service handles, which is why they come first. Inline both ways, so the outbox hears
// that RabbitMQ has a message before it marks the row done, and a message is acknowledged only after the
// modules applied it.
builder.UseWolverine(wolverine =>
{
    wolverine.UseRabbitMq(new Uri(builder.Configuration.GetConnectionString("rabbitmq")
            ?? throw new InvalidOperationException("ConnectionStrings:rabbitmq is not set. Run Examples.Webshop.Wolverine.AppHost.")))
        .AutoProvision()
        .UseConventionalRouting(conventions => conventions
            .UseIntegrationEventNames()
            .QueueNameForListener(type => $"payments.{type.Name}")
            .ConfigureListeners((listener, _) => listener.ProcessInline())
            .ConfigureSending((sender, _) => sender.SendInline()));

    // A handler per contract this service has to be sent, handing it to the modules; retried, then
    // Wolverine's error queue.
    wolverine.ReceiveIntegrationEvents(builder.Services.IntegrationEventSubscriptions());

    // A contract this process publishes goes to RabbitMQ, never straight to a local handler.
    wolverine.Policies.DisableConventionalLocalRouting();
});

// GraphQL: this service's source schema, which the gateway composes with the other two. Besides its own
// payments it declares Order, by id alone, with the one field Payments adds to it: payment.
builder.Services
    .AddGraphQLServer()
    .AddSourceSchemaDefaults()
    .AddGlobalObjectIdentification(options => options.MarkNodeFieldAsLookup = true)
    .AddDDDToolkitTypes()
    .AddDDDToolkitErrors()
    .AddPaymentsGraphQL();

var app = builder.Build();

app.MapPaymentsEndpoints();
app.MapGraphQL();
app.MapDefaultEndpoints();

await app.RunAsync();
