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
using DDDToolkit.Messaging.Postgres;
using HotChocolate;
using HotChocolate.Types;
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
    ?? throw new InvalidOperationException("ConnectionStrings:shop is not set. Run DDDToolkit.Examples.Pgmq.AppHost.");

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

var queues = NpgsqlDataSource.Create(connectionString);

// Sending. pgmq has no exchange that routes by topic: a queue is a table, and the sender writes into it. So
// this service says which queue each of its contracts goes to, one per consuming service, and the enqueues
// share a transaction: a message reaches all its queues or none. What is not listed stays in the process.
Dictionary<string, string[]> sendTo = new()
{
    ["ordering.order-placed"] = ["payments", "fulfilment"],
    ["ordering.order-cancelled"] = ["payments", "fulfilment"],
    ["ordering.order-confirmed"] = ["fulfilment"],
};
builder.Services.AddPgmqSink(queues, pgmq => pgmq.UseQueues(message => sendTo.GetValueOrDefault(message.Name, [])));

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

// Receiving. This service's own queue, read into the same modules the module sink feeds: a handler cannot
// tell which way a message came.
builder.Services.AddPgmqConsumer(queues, "storefront");

// pgmq is an extension, installed once by whoever deploys the database: the AppHost here, Supabase on a
// project with Queues. This only checks, so a database without it fails at start-up, by name.
builder.Services.AddHostedService(_ => new RequirePgmq(queues));

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

/// <summary>Fails the start, naming the problem, when the pgmq extension is not installed.</summary>
internal sealed class RequirePgmq(NpgsqlDataSource dataSource) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await PgmqQueue.EnsureInstalledAsync(connection, transaction: null, cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>The product a line is for: Ordering's SKU, joined to Catalog's lookup, both in this service.</summary>
[ExtendObjectType<OrderLine>]
internal sealed class OrderLineProduct
{
    public Task<Product?> GetProductAsync([Parent] OrderLine line, ProductBySkuDataLoader products, CancellationToken cancellationToken)
        => products.LoadAsync(line.Sku, cancellationToken);
}
