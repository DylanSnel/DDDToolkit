using DDDToolkit.Auth.Supabase.AspNetCore;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Catalog.Api;
using DDDToolkit.Examples.Catalog;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Inventory.Api;
using DDDToolkit.Examples.Inventory;
using DDDToolkit.Examples.Ordering.Api;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Payments.Api;
using DDDToolkit.Examples.Payments;
using DDDToolkit.Examples.Shipping.Api;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.HotChocolate.Fusion.InMemory;
using DDDToolkit.HotChocolate.Subscriptions;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.Postgres;
using Npgsql;

// There is no export command here. The project file turns the Supabase export on, and the build writes
// supabase/migrations from every [SupabaseMigrations] factory this host references; see the csproj.

var builder = WebApplication.CreateBuilder(args);

// Traces, metrics and logs to the Aspire dashboard when the AppHost runs this; nothing noticeable when
// it runs on its own.
builder.AddServiceDefaults();

// Mediator is registered scoped, not with its singleton default: a handler that injects a DbContext
// needs the scope's context, and a singleton cannot depend on a scoped service. The generator reads
// this very call at compile time, so the lifetime has to be written here.
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

// What is the host's to decide: domain events that stay inside a module are published through
// Mediator. Everything else about storage and messaging is each module's own business.
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

// Two ways to run. With no connection string, each module gets a SQLite file of its own: five databases
// rather than one is the cheapest way to be sure no query and no transaction ever crosses the boundary.
// With ConnectionStrings:Supabase (the "supabase" launch profile, or the AppHost), the modules share one
// Postgres database, as they would on one Supabase project, each in a schema of its own.
var supabase = builder.Configuration.GetConnectionString("Supabase") is { Length: > 0 } connectionString
    ? connectionString
    : null;

var database = supabase is not null ? ModuleDatabase.Supabase(supabase) : ModuleDatabase.Sqlite();

// Who is asking. With Supabase:Url set too, a request may carry the access token Supabase Auth gave a
// signed-in user, the one supabase-js holds, and every module's queries for that request run as that user.
// Supabase's policies then decide what each caller sees, as they would for the Data API: an order is its
// customer's, by the rules in Ordering's Domain/Aggregates/Orders/Access, which the build writes into
// supabase/migrations as ..._access.ordering.ddd.sql. A request without a token runs as anon, and can
// still place and follow an order as a guest. Work outside a request, such as the outbox
// pollers, runs as the role the host logged in as. Supabase:JwtSecret is for the CLI's local stack and
// the AppHost's container, which sign tokens with a secret instead of keys the project publishes.
var signedIn = supabase is not null && builder.Configuration["Supabase:Url"] is { Length: > 0 };
if (signedIn)
{
    builder.Services.AddAuthentication().AddSupabaseJwtBearer(builder.Configuration["Supabase:Url"]!, jwt =>
    {
        if (builder.Configuration["Supabase:JwtSecret"] is { Length: > 0 } secret)
        {
            jwt.UseSupabaseJwtSecret(secret);
        }
    });

    builder.Services.AddSupabaseRowLevelSecurity();
    database = database.WithRowLevelSecurity();
}

// How the modules hear from each other. No module names another here or anywhere: each one says what it
// publishes and what it listens to, and the host only chooses the road between them.
var host = builder.Configuration["Messaging"] is "pgmq"
    ? OverSupabaseQueues(builder.Services, database, supabase)
    : ModuleHost.InProcess(database);

// Whoever holds a GraphQL subscription to an order hears about it too, whichever road the modules take.
host = host.AlsoSendTo<GraphQlSubscriptionSink>()
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

if (signedIn)
{
    app.UseAuthentication();
}

// On Supabase the migrations are Supabase's to apply, from supabase/migrations. The application only
// checks, over every module that registered its migrations, and refuses to start while one is missing.
// On SQLite no module registers any, and the modules create their files themselves.
await app.Services.EnsureSupabaseMigrationsAppliedAsync();

// Each module brings its own endpoints, so a host that runs a different selection of modules serves
// exactly their part of the API.
app.MapCatalogEndpoints();
app.MapOrderingEndpoints();
app.MapInventoryEndpoints();
app.MapPaymentsEndpoints();
app.MapShippingEndpoints();
app.MapInMemoryFusionGateway();
app.MapDefaultEndpoints();

await app.RunAsync();

// The modules talk through Supabase Queues instead of in process. Every module's outbox sends to one
// queue in the same database, and this host reads it back into the modules, through the same inboxes the
// module sink uses: a handler cannot tell which road a message took. Nothing else changes, which is the
// point: this is the step before a module moves out. Its messages already travel through the database
// rather than a method call, so moving it changes where it runs, not how it hears.
//
// One queue for the whole shop, because Supabase ships pgmq 1.5, and the topic routing that would give
// each module a queue bound to what it handles (Microservices.Pgmq uses it) came in pgmq 1.11. The
// extension itself is turned on by a migration, supabase/migrations/..._enable_queues.sql.
static ModuleHost OverSupabaseQueues(IServiceCollection services, ModuleDatabase database, string? supabase)
{
    var queues = NpgsqlDataSource.Create(supabase
        ?? throw new InvalidOperationException("Messaging=pgmq needs ConnectionStrings:Supabase: the queues live in the database."));

    services.AddPgmqSink(queues, pgmq => pgmq.UseQueue("shop"));
    services.AddPgmqConsumer(queues, "shop");

    return new ModuleHost(database, outbox => outbox.SendToPgmq());
}
