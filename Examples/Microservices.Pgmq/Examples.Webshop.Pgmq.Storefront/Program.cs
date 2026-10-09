using DDDToolkit.EntityFramework;
using DDDToolkit.HotChocolate;
using DDDToolkit.HotChocolate.Subscriptions;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.Postgres;
using DDDToolkit.Startup;
using Examples.Hosting;
using Examples.Webshop.Catalog;
using Examples.Webshop.Catalog.Api;
using Examples.Webshop.Catalog.Api.GraphQL;
using Examples.Webshop.Ordering;
using Examples.Webshop.Ordering.Api;
using Examples.Webshop.Ordering.Api.GraphQL;
using Npgsql;

// The shop's storefront service, over pgmq. Catalog and Ordering: what a customer browses and buys. Ordering
// reads Catalog's prices through the module sink, next door, as in the monolith; only what another service
// consumes leaves the process.
//
// All three services share one Postgres, the way three services share one Supabase project: every module in
// its own schema, and the queues in pgmq's. That is what pgmq buys over a broker: a queue that is a table in
// the database the outbox is already in, with nothing new to run.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("shop")
    ?? throw new InvalidOperationException("ConnectionStrings:shop is not set. Run Examples.Webshop.Pgmq.AppHost.");

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

// pgmq is an extension, installed once by whoever deploys the database: the AppHost here, Supabase on a
// project with Queues. The sink and the consumer below bring a start-up check for it, which RunStartupChecks()
// runs before anything starts, so a database without it, or with a pgmq too old for topics, fails the start by name.
var queues = NpgsqlDataSource.Create(connectionString);

// Sending: by topic, pgmq's own publish and subscribe. Each message goes out under its contract's
// published name, and pgmq puts it on every queue bound to that name, all in one transaction. This service
// names no receiver.
builder.Services.AddPgmqSink(queues, pgmq => pgmq.UseTopics());

var host = new ModuleHost(
    ModuleDatabase.Postgres(connectionString),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToPgmq();
    })
    .AlsoSendTo<GraphQlSubscriptionSink>();

builder.Services.AddCatalogModule(host);
builder.Services.AddOrderingModule(host);

// Receiving. This service's own queue, which binds itself at start-up to every contract its modules handle
// and another service publishes, as a RabbitMQ queue binds to a topic exchange. It is read into the same
// modules the module sink feeds: a handler cannot tell which way a message came.
builder.Services.AddPgmqConsumer(queues, "storefront", consumer => consumer.BindTopics = true);

// GraphQL: this service's source schema, which the gateway composes with the other two into the shop's
// one schema. Catalog and Ordering both run here, so a line's product is joined in-process, the way the
// monolith does it; Payments and Shipping add their fields to Order from their own services.
builder.Services
    .AddGraphQLServer()
    .AddSourceSchemaDefaults()
    .AddGlobalObjectIdentification(options => options.MarkNodeFieldAsLookup = true)
    .AddDDDToolkitTypes()
    .AddDDDToolkitErrors()
    .AddInMemorySubscriptions()
    .AddCatalogGraphQL()
    .AddOrderingGraphQL()
    // What this project declares for GraphQL itself: a line's product, in GraphQL/OrderLineProduct.cs.
    .AddStorefrontTypes();

// Before the server binds its port, every check the registrations above brought: the modules' contexts are wired
// through the toolkit, and the database has pgmq, in a version with topics.
builder.Services.RunStartupChecks();

var app = builder.Build();

app.MapCatalogEndpoints();
app.MapOrderingEndpoints();
app.MapGraphQL();
app.MapDefaultEndpoints();

await app.RunAsync();
