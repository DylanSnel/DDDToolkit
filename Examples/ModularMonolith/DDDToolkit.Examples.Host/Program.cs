using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Host;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Mediator;

// dotnet run -- export-supabase [directory]
// Writes every module's Entity Framework migrations into the Supabase migrations directory, then exits.
// Run it after every dotnet ef migrations add. It comes before the builder on purpose: the export needs
// the modules' design-time factories and nothing else, so no configuration is loaded and nothing starts.
// Without a directory it finds the Supabase project the way the CLI does, by looking upwards for
// supabase/config.toml, which here is Examples/ModularMonolith.
if (args is ["export-supabase", .. var rest])
{
    var reports = SupabaseMigrations.Export(
        [OrderingModule.SupabaseMigrations, ShippingModule.SupabaseMigrations],
        rest is [var directory] ? directory : null);

    foreach (var entry in reports.SelectMany(report => report.Entries))
    {
        Console.WriteLine($"{entry.Status,-12} {entry.MigrationId}");
    }

    return reports.All(report => report.IsInSync) ? 0 : 1;
}

var builder = WebApplication.CreateBuilder(args);

// Mediator is registered scoped, not with its singleton default: a handler that injects a DbContext
// needs the scope's context, and a singleton cannot depend on a scoped service. The generator reads
// this very call at compile time, so the lifetime has to be written here.
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

// What is the host's to decide: domain events that stay inside a module are published through
// Mediator. Everything else about storage and messaging is each module's own business.
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

// Two ways to run. With no connection string, each module gets a SQLite file of its own: two databases
// rather than one is the cheapest way to be sure no query and no transaction ever crosses the boundary.
// With ConnectionStrings:Supabase (the "supabase" launch profile), both modules share one Postgres
// database, as they would on one Supabase project, each in a schema of its own.
var supabase = builder.Configuration.GetConnectionString("Supabase");

builder.Services.AddOrderingModule(supabase);
builder.Services.AddShippingModule(supabase);

var app = builder.Build();

// On Supabase the migrations are Supabase's to apply, from supabase/migrations. The application only
// checks, over every module that registered its migrations, and refuses to start while one is missing.
// On SQLite no module registers any, and the modules create their files themselves.
await app.Services.EnsureSupabaseMigrationsAppliedAsync();

app.MapOrderingEndpoints();
app.MapShippingEndpoints();

await app.RunAsync();

return 0;
