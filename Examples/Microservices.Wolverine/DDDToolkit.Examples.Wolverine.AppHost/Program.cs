using DDDToolkit.Examples.Microservices;

// The shop as three services over RabbitMQ, with Wolverine as the transport. Each service has a database of
// its own, and not all of the same kind: Storefront on SQL Server, Payments and Fulfilment on Postgres. The
// only thing the three share is the broker, and the only thing they say on it is published contracts.

var builder = DistributedApplication.CreateBuilder(args);

var rabbitmq = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();

var sqlServer = builder.AddSqlServer("sqlserver");
var postgres = builder.AddPostgres("postgres");

var databases = new Dictionary<ShopService, (IResourceBuilder<IResourceWithConnectionString> Database, string Provider)>
{
    [ShopService.Storefront] = (sqlServer.AddDatabase("storefront-db", "storefront"), "SqlServer"),
    [ShopService.Payments] = (postgres.AddDatabase("payments-db", "payments"), "Postgres"),
    [ShopService.Fulfilment] = (postgres.AddDatabase("fulfilment-db", "fulfilment"), "Postgres"),
};

var gateway = builder.AddProject<Projects.DDDToolkit_Examples_Gateway>("gateway", launchProfileName: null)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");

foreach (var service in Enum.GetValues<ShopService>())
{
    var name = ShopServices.NameOf(service);
    var (database, provider) = databases[service];

    // The connection string's name says which provider it is for: ModuleDatabase.FromConnectionStrings
    // reads ConnectionStrings:SqlServer or ConnectionStrings:Postgres.
    var project = builder.AddProject<Projects.DDDToolkit_Examples_Wolverine_Service>(name, launchProfileName: null)
        .WithHttpEndpoint()
        .WithEnvironment("Shop__Service", name)
        .WithReference(database, connectionName: provider)
        .WithReference(rabbitmq)
        .WaitFor(database)
        .WaitFor(rabbitmq)
        .WithHttpHealthCheck("/health");

    gateway.WithReference(project).WaitFor(project);
}

builder.Build().Run();
