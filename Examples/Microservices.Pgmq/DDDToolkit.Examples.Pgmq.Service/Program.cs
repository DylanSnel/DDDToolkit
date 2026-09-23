using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Microservices;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.Postgres;
using Npgsql;

// One of the shop's three services, talking to the other two through pgmq. The AppHost starts this project
// three times, as storefront, payments and fulfilment; which modules a copy runs is configuration, and
// the only thing in this file that is about pgmq is the transport: the sink below and the consumer under it.
//
// All three share one Postgres, the way three services share one Supabase project: every module in its
// own schema, and the queues in pgmq's. That is what pgmq buys over a broker: a queue that is a table in
// the database the outbox is already in, with nothing new to run.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var service = Enum.Parse<ShopService>(
    builder.Configuration["Shop:Service"] ?? throw new InvalidOperationException("Shop:Service is not set: storefront, payments or fulfilment."),
    ignoreCase: true);
var connectionString = builder.Configuration.GetConnectionString("shop")
    ?? throw new InvalidOperationException("ConnectionStrings:shop is not set. Run DDDToolkit.Examples.Pgmq.AppHost.");

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

var queues = NpgsqlDataSource.Create(connectionString);

// Sending. A message goes to the other modules in this process through the module sink, as in the
// monolith, and onto the queue of every other service that consumes it: one queue per service, and
// ShopServices says which. The enqueues share a transaction, so a message reaches all its queues or none.
builder.Services.AddPgmqSink(queues, pgmq => pgmq.UseQueues(message =>
    ShopServices.RecipientsOf(message.Name, service).Select(ShopServices.NameOf)));

var host = new ModuleHost(
    ModuleDatabase.Postgres(connectionString),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToPgmq();
    });

builder.Services.AddShopService(service, host);

// Receiving. This service's own queue, read into the same modules the module sink feeds: a handler
// cannot tell which way a message came.
builder.Services.AddPgmqConsumer(queues, ShopServices.NameOf(service));

// pgmq is an extension, installed once by whoever deploys the database: the AppHost here, Supabase on a
// project with Queues. This only checks, so a database without it fails at start-up, by name.
builder.Services.AddHostedService(_ => new RequirePgmq(queues));

var app = builder.Build();

app.MapShopService(service);
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
