using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Catalog;
using DDDToolkit.Examples.Catalog.Api;
using DDDToolkit.Examples.Catalog.Api.GraphQL;
using DDDToolkit.Examples.Catalog.Domain.Products;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.MassTransit.Storefront;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Api;
using DDDToolkit.Examples.Ordering.Api.GraphQL;
using DDDToolkit.Examples.Ordering.Domain.Orders;
using DDDToolkit.HotChocolate;
using DDDToolkit.HotChocolate.Subscriptions;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.MassTransit;
using HotChocolate;
using HotChocolate.Types;

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

var host = new ModuleHost(
    ModuleDatabase.SqlServer(builder.Configuration.GetConnectionString("storefront-db")
        ?? throw new InvalidOperationException("ConnectionStrings:storefront-db is not set. Run DDDToolkit.Examples.MassTransit.AppHost.")),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToMassTransit();
    })
    .AlsoSendTo<GraphQlSubscriptionSink>();

builder.Services.AddCatalogModule(host);
builder.Services.AddOrderingModule(host);

// After the modules: they say what this service handles, and the bus is bound to exactly that.
builder.Services.AddRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")
    ?? throw new InvalidOperationException("ConnectionStrings:rabbitmq is not set. Run DDDToolkit.Examples.MassTransit.AppHost."));

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
