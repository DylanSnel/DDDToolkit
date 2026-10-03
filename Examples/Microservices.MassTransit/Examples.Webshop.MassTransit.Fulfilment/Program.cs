using DDDToolkit.EntityFramework;
using DDDToolkit.HotChocolate;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.MassTransit;
using Examples.Hosting;
using Examples.Webshop.Inventory;
using Examples.Webshop.Inventory.Api;
using Examples.Webshop.Inventory.Api.GraphQL;
using Examples.Webshop.MassTransit.Fulfilment;
using Examples.Webshop.Shipping;
using Examples.Webshop.Shipping.Api;
using Examples.Webshop.Shipping.Api.GraphQL;

// The shop's fulfilment service, over RabbitMQ with MassTransit 8 as the transport (RabbitMq.cs). Inventory
// and Shipping: the warehouse.
//
// On a SQL Server database of its own, as are the other two. MassTransit 8 is the last version under the
// Apache 2.0 licence; see the README of DDDToolkit.Messaging.MassTransit before moving to 9.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

var host = new ModuleHost(
    ModuleDatabase.SqlServer(builder.Configuration.GetConnectionString("fulfilment-db")
        ?? throw new InvalidOperationException("ConnectionStrings:fulfilment-db is not set. Run Examples.Webshop.MassTransit.AppHost.")),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToMassTransit();
    });

builder.Services.AddInventoryModule(host);
builder.Services.AddShippingModule(host);

// After the modules: they say what this service handles, and the bus is bound to exactly that.
builder.Services.AddRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")
    ?? throw new InvalidOperationException("ConnectionStrings:rabbitmq is not set. Run Examples.Webshop.MassTransit.AppHost."));

// GraphQL: this service's source schema, which the gateway composes with the other two. Besides stock
// and shipments it declares Product, by SKU, with the stock Inventory keeps of it, and Order, by id
// alone, with the one field Shipping adds to it: shipment.
builder.Services
    .AddGraphQLServer()
    .AddSourceSchemaDefaults()
    .AddGlobalObjectIdentification(options => options.MarkNodeFieldAsLookup = true)
    .AddDDDToolkitTypes()
    .AddDDDToolkitErrors()
    .AddInventoryGraphQL()
    .AddShippingGraphQL();

var app = builder.Build();

app.MapInventoryEndpoints();
app.MapShippingEndpoints();
app.MapGraphQL();
app.MapDefaultEndpoints();

await app.RunAsync();
