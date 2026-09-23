using DDDToolkit.Examples.Microservices;

// The shop as three services over RabbitMQ, with MassTransit as the transport. The same topology as the
// Wolverine sample's, all on SQL Server this time: one server, a database per service. The only thing the
// three share is the broker, and the only thing they say on it is published contracts.

var builder = DistributedApplication.CreateBuilder(args);

var rabbitmq = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();
var sqlServer = builder.AddSqlServer("sqlserver");

var gateway = builder.AddProject<Projects.DDDToolkit_Examples_Gateway>("gateway", launchProfileName: null)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");

foreach (var service in Enum.GetValues<ShopService>())
{
    var name = ShopServices.NameOf(service);
    var database = sqlServer.AddDatabase($"{name}-db", name);

    // ModuleDatabase.FromConnectionStrings reads ConnectionStrings:SqlServer.
    var project = builder.AddProject<Projects.DDDToolkit_Examples_MassTransit_Service>(name, launchProfileName: null)
        .WithHttpEndpoint()
        .WithEnvironment("Shop__Service", name)
        .WithReference(database, connectionName: "SqlServer")
        .WithReference(rabbitmq)
        .WaitFor(database)
        .WaitFor(rabbitmq)
        .WithHttpHealthCheck("/health");

    gateway.WithReference(project).WaitFor(project);
}

builder.Build().Run();
