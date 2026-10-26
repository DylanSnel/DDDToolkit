using DDDToolkit.EntityFramework;
using Examples.Webshop.Catalog.Api;
using Examples.Webshop.Catalog;
using Examples.Hosting;
using Examples.Webshop.Inventory.Api;
using Examples.Webshop.Inventory;
using Examples.Webshop.Ordering.Api;
using Examples.Webshop.Ordering;
using Examples.Webshop.Payments.Api;
using Examples.Webshop.Payments;
using Examples.Webshop.Shipping.Api;
using Examples.Webshop.Shipping;
using DDDToolkit.HotChocolate.Fusion.InMemory;
using DDDToolkit.HotChocolate.Subscriptions;
using DDDToolkit.Mediator;

// The same five modules as the Supabase host, the same endpoints and the same messages, on SQL Server.
// Compare the two Program.cs files: what differs in substance is the database, and what follows from it.
//
// On Supabase the migrations are Supabase's to apply, and the application only checks them, with its one
// AddSupabaseMigrations(); here nobody else is going to apply them, so each module migrates its own schema
// on start-up, before its outbox poller starts. Run it through the AppHost next door, which starts SQL
// Server in Docker and hands this host its connection string.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

var database = builder.Configuration.GetConnectionString("SqlServer") is { Length: > 0 } sqlServer
    ? ModuleDatabase.SqlServer(sqlServer)
    : throw new InvalidOperationException(
        "ConnectionStrings:SqlServer is not set. Run Examples.Webshop.SqlServer.AppHost, which starts SQL Server and sets it.");

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
builder.Services.AddInMemoryFusionGateway();

var app = builder.Build();

app.UseWebSockets();

app.MapCatalogEndpoints();
app.MapOrderingEndpoints();
app.MapInventoryEndpoints();
app.MapPaymentsEndpoints();
app.MapShippingEndpoints();
app.MapInMemoryFusionGateway();
app.MapDefaultEndpoints();

await app.RunAsync();
