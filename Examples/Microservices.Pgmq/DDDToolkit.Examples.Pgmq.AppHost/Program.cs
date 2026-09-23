// The shop as three services over pgmq: one Postgres with the pgmq extension, three service projects and the
// gateway in front of them. Run this, then work through the .http file against the gateway's address.

var builder = DistributedApplication.CreateBuilder(args);

// Postgres with pgmq built in, the extension Supabase ships as Queues.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgmq/pg17-pgmq", "v1.5.1")
    .WithImageRegistry("ghcr.io")
    .WithInitFiles(Path.Combine(builder.AppHostDirectory, "database"));

// The server's own database, where the init script installed pgmq, as on one Supabase project.
var shop = postgres.AddDatabase("shop", databaseName: "postgres");

var storefront = builder.AddProject<Projects.DDDToolkit_Examples_Pgmq_Storefront>("storefront", launchProfileName: null)
    .WithHttpEndpoint()
    .WithReference(shop)
    .WaitFor(shop)
    .WithHttpHealthCheck("/health");

var payments = builder.AddProject<Projects.DDDToolkit_Examples_Pgmq_Payments>("payments", launchProfileName: null)
    .WithHttpEndpoint()
    .WithReference(shop)
    .WaitFor(shop)
    .WithHttpHealthCheck("/health");

var fulfilment = builder.AddProject<Projects.DDDToolkit_Examples_Pgmq_Fulfilment>("fulfilment", launchProfileName: null)
    .WithHttpEndpoint()
    .WithReference(shop)
    .WaitFor(shop)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.DDDToolkit_Examples_Gateway>("gateway", launchProfileName: null)
    .WithHttpEndpoint()
    .WithReference(storefront).WaitFor(storefront)
    .WithReference(payments).WaitFor(payments)
    .WithReference(fulfilment).WaitFor(fulfilment)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
