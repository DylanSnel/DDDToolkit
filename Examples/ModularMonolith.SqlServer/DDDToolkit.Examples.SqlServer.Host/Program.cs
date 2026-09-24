using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.GraphQL;
using DDDToolkit.HotChocolate.Subscriptions;
using DDDToolkit.Examples.Catalog;
using DDDToolkit.Examples.Catalog.Api;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Inventory;
using DDDToolkit.Examples.Inventory.Api;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Api;
using DDDToolkit.Examples.Payments;
using DDDToolkit.Examples.Payments.Api;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Examples.Shipping.Api;
using DDDToolkit.Mediator;

// The same five modules as the Supabase host, the same endpoints and the same messages, on SQL Server.
// Compare the two Program.cs files: the only line that differs in substance is the database.
//
// And one thing follows from it. On Supabase the migrations are Supabase's to apply and the application
// only checks; here nobody else is going to apply them, so each module migrates its own schema on
// start-up, before its outbox poller starts. Run it through the AppHost next door, which starts SQL
// Server in Docker and hands this host its connection string.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

var database = builder.Configuration.GetConnectionString("SqlServer") is { Length: > 0 } sqlServer
    ? ModuleDatabase.SqlServer(sqlServer)
    : throw new InvalidOperationException(
        "ConnectionStrings:SqlServer is not set. Run DDDToolkit.Examples.SqlServer.AppHost, which starts SQL Server and sets it.");

// The other modules, through the module sink, and whoever holds a GraphQL subscription to an order.
var host = ModuleHost.InProcess(database).AlsoSendTo<GraphQlSubscriptionSink>()
    // GraphQL: each module registers its own source schema; the subscriptions' transport is this host's.
    .WithGraphQL(graphql => graphql.AddInMemorySubscriptions());

builder.Services.AddCatalogModule(host);
builder.Services.AddOrderingModule(host);
builder.Services.AddInventoryModule(host);
builder.Services.AddPaymentsModule(host);
builder.Services.AddShippingModule(host);

// One GraphQL schema over the five modules' source schemas, composed by Fusion in this process, at
// /graphql next to the REST endpoints.
builder.Services.AddModuleGateway();

var app = builder.Build();

app.UseWebSockets();

app.MapCatalogEndpoints();
app.MapOrderingEndpoints();
app.MapInventoryEndpoints();
app.MapPaymentsEndpoints();
app.MapShippingEndpoints();
app.MapModuleGateway();
app.MapDefaultEndpoints();

await app.RunAsync();
