using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Inventory;
using DDDToolkit.Examples.Inventory.Api;
using DDDToolkit.Examples.Inventory.Api.GraphQL;
using DDDToolkit.Examples.MassTransit.Fulfilment;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Examples.Shipping.Api;
using DDDToolkit.Examples.Shipping.Api.GraphQL;
using DDDToolkit.HotChocolate;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.MassTransit;

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
        ?? throw new InvalidOperationException("ConnectionStrings:fulfilment-db is not set. Run DDDToolkit.Examples.MassTransit.AppHost.")),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToMassTransit();
    });

builder.Services.AddInventoryModule(host);
builder.Services.AddShippingModule(host);

// After the modules: they say what this service handles, and the bus is bound to exactly that.
builder.Services.AddRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")
    ?? throw new InvalidOperationException("ConnectionStrings:rabbitmq is not set. Run DDDToolkit.Examples.MassTransit.AppHost."));

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
