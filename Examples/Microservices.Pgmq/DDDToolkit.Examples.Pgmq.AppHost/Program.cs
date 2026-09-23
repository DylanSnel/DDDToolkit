using DDDToolkit.Examples.Microservices;

// The shop as three services over pgmq: one Postgres with the pgmq extension, the same service project
// started three times, once per ShopService, and the gateway in front of them. Run this, then work through
// the .http file against the gateway's address.

var builder = DistributedApplication.CreateBuilder(args);

// Postgres with pgmq built in, the extension Supabase ships as Queues.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgmq/pg17-pgmq", "v1.5.1")
    .WithImageRegistry("ghcr.io")
    .WithInitFiles(Path.Combine(builder.AppHostDirectory, "database"));

// The server's own database, where the init script installed pgmq, as on one Supabase project.
var shop = postgres.AddDatabase("shop", databaseName: "postgres");

var gateway = builder.AddProject<Projects.DDDToolkit_Examples_Gateway>("gateway", launchProfileName: null)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");

foreach (var service in Enum.GetValues<ShopService>())
{
    var name = ShopServices.NameOf(service);

    // No launch profile: three copies of one project need three ports, so Aspire hands each its own.
    var project = builder.AddProject<Projects.DDDToolkit_Examples_Pgmq_Service>(name, launchProfileName: null)
        .WithHttpEndpoint()
        .WithEnvironment("Shop__Service", name)
        .WithReference(shop)
        .WaitFor(shop)
        .WithHttpHealthCheck("/health");

    gateway.WithReference(project).WaitFor(project);
}

builder.Build().Run();
