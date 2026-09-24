using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Catalog;
using DDDToolkit.Examples.Catalog.Api;
using DDDToolkit.Examples.Catalog.Api.GraphQL;
using DDDToolkit.Examples.Catalog.Domain.Products;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Api;
using DDDToolkit.Examples.Ordering.Api.GraphQL;
using DDDToolkit.Examples.Ordering.Domain.Orders;
using DDDToolkit.HotChocolate;
using DDDToolkit.HotChocolate.Subscriptions;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.Wolverine;
using HotChocolate;
using HotChocolate.Types;
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

var host = new ModuleHost(
    ModuleDatabase.SqlServer(builder.Configuration.GetConnectionString("storefront-db")
        ?? throw new InvalidOperationException("ConnectionStrings:storefront-db is not set. Run DDDToolkit.Examples.Wolverine.AppHost.")),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToWolverine();
    })
    .AlsoSendTo<GraphQlSubscriptionSink>();

builder.Services.AddCatalogModule(host);
builder.Services.AddOrderingModule(host);

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
            .QueueNameForListener(type => $"storefront.{type.Name}")
            .ConfigureListeners((listener, _) => listener.ProcessInline())
            .ConfigureSending((sender, _) => sender.SendInline()));

    // A handler per contract this service has to be sent, handing it to the modules; retried, then
    // Wolverine's error queue.
    wolverine.ReceiveIntegrationEvents(builder.Services.IntegrationEventSubscriptions());

    // A contract this process publishes goes to RabbitMQ, never straight to a local handler.
    wolverine.Policies.DisableConventionalLocalRouting();
});

// GraphQL: this service's source schema, which the gateway composes with the other two into the shop's
// one schema. Catalog and Ordering both run here, so a line's product is joined in-process, the way the
// monolith does it; Payments and Shipping add their fields to Order from their own services.
builder.Services
    .AddGraphQLServer()
    .AddSourceSchemaDefaults()
    .AddGlobalObjectIdentification(options => options.MarkNodeFieldAsLookup = true)
    .AddDDDToolkitTypes()
    .AddDDDToolkitErrors()
    .AddQueryType()
    .AddMutationType()
    .AddSubscriptionType()
    .AddInMemorySubscriptions()
    .AddCatalogGraphQL()
    .AddOrderingGraphQL()
    .AddTypeExtension<OrderLineProduct>();

var app = builder.Build();

app.MapCatalogEndpoints();
app.MapOrderingEndpoints();
app.MapGraphQL();
app.MapDefaultEndpoints();

await app.RunAsync();

/// <summary>The product a line is for: Ordering's SKU, joined to Catalog's lookup, both in this service.</summary>
[ExtendObjectType<OrderLine>]
internal sealed class OrderLineProduct
{
    public Task<Product?> GetProductAsync([Parent] OrderLine line, ProductBySkuDataLoader products, CancellationToken cancellationToken)
        => products.LoadAsync(line.Sku, cancellationToken);
}
