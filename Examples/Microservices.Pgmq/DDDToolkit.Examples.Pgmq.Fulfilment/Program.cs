using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Inventory;
using DDDToolkit.Examples.Inventory.Api;
using DDDToolkit.Examples.Inventory.Api.GraphQL;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Examples.Shipping.Api;
using DDDToolkit.Examples.Shipping.Api.GraphQL;
using DDDToolkit.HotChocolate;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.Postgres;
using Npgsql;

// The shop's fulfilment service, over pgmq. Inventory and Shipping: the warehouse.
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
    ["inventory.stock-reserved"] = ["storefront", "payments"],
    ["inventory.stock-reservation-failed"] = ["storefront"],
};
builder.Services.AddPgmqSink(queues, pgmq => pgmq.UseQueues(message => sendTo.GetValueOrDefault(message.Name, [])));

var host = new ModuleHost(
    ModuleDatabase.Postgres(connectionString),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToPgmq();
    });

builder.Services.AddInventoryModule(host);
builder.Services.AddShippingModule(host);

// Receiving. This service's own queue, read into the same modules the module sink feeds: a handler cannot
// tell which way a message came.
builder.Services.AddPgmqConsumer(queues, "fulfilment");

// pgmq is an extension, installed once by whoever deploys the database: the AppHost here, Supabase on a
// project with Queues. This only checks, so a database without it fails at start-up, by name.
builder.Services.AddHostedService(_ => new RequirePgmq(queues));

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
