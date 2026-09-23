using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Payments;
using DDDToolkit.Examples.Payments.Api;
using DDDToolkit.Examples.Payments.Api.GraphQL;
using DDDToolkit.HotChocolate;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.Postgres;
using Npgsql;

// The shop's payments service, over pgmq. Payments on its own: the one service that talks to the payment
// provider.
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
    });

builder.Services.AddPaymentsModule(host);

// Receiving. This service's own queue, which binds itself at start-up to every contract its modules handle
// and another service publishes, as a RabbitMQ queue binds to a topic exchange. It is read into the same
// modules the module sink feeds: a handler cannot tell which way a message came.
builder.Services.AddPgmqConsumer(queues, "payments", consumer => consumer.BindTopics = true);

// pgmq is an extension, installed once by whoever deploys the database: the AppHost here, Supabase on a
// project with Queues. This only checks, so a database without it fails at start-up, by name.
builder.Services.AddHostedService(_ => new RequirePgmq(queues));

// GraphQL: this service's source schema, which the gateway composes with the other two. Besides its own
// payments it declares Order, by id alone, with the one field Payments adds to it: payment.
builder.Services
    .AddGraphQLServer()
    .AddSourceSchemaDefaults()
    .AddGlobalObjectIdentification(options => options.MarkNodeFieldAsLookup = true)
    .AddDDDToolkitTypes()
    .AddDDDToolkitErrors()
    .AddQueryType()
    .AddPaymentsGraphQL()
    .AddPaymentsOrderStub();

var app = builder.Build();

app.MapPaymentsEndpoints();
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
