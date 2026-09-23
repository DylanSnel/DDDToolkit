using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Catalog;
using DDDToolkit.Examples.Catalog.Api;
using DDDToolkit.Examples.Inventory;
using DDDToolkit.Examples.Inventory.Api;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Api;
using DDDToolkit.Examples.Payments;
using DDDToolkit.Examples.Payments.Api;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Examples.Shipping.Api;
using DDDToolkit.Mediator;

// There is no export command here. The project file turns the Supabase export on, and the build writes
// supabase/migrations from every [SupabaseMigrations] factory this host references; see the csproj.

var builder = WebApplication.CreateBuilder(args);

// Mediator is registered scoped, not with its singleton default: a handler that injects a DbContext
// needs the scope's context, and a singleton cannot depend on a scoped service. The generator reads
// this very call at compile time, so the lifetime has to be written here.
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

// What is the host's to decide: domain events that stay inside a module are published through
// Mediator. Everything else about storage and messaging is each module's own business.
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

// Two ways to run. With no connection string, each module gets a SQLite file of its own: five databases
// rather than one is the cheapest way to be sure no query and no transaction ever crosses the boundary.
// With ConnectionStrings:Supabase (the "supabase" launch profile), the modules share one Postgres
// database, as they would on one Supabase project, each in a schema of its own.
var supabase = builder.Configuration.GetConnectionString("Supabase");

// The whole shop in one process. No module names another here or anywhere: each one says what it
// publishes and what it listens to, and the module sink carries the messages between them.
builder.Services.AddCatalogModule(supabase);
builder.Services.AddOrderingModule(supabase);
builder.Services.AddInventoryModule(supabase);
builder.Services.AddPaymentsModule(supabase);
builder.Services.AddShippingModule(supabase);

var app = builder.Build();

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

await app.RunAsync();
