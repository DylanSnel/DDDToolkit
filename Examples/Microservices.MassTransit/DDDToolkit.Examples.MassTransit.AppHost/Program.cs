// The shop as three services over RabbitMQ, with MassTransit as the transport. The same topology as the
// Wolverine sample's, all on SQL Server this time: one server, a database per service. The only thing the
// three share is the broker, and the only thing they say on it is published contracts.

var builder = DistributedApplication.CreateBuilder(args);

var rabbitmq = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();
var sqlServer = builder.AddSqlServer("sqlserver");

var storefrontDb = sqlServer.AddDatabase("storefront-db", "storefront");
var paymentsDb = sqlServer.AddDatabase("payments-db", "payments");
var fulfilmentDb = sqlServer.AddDatabase("fulfilment-db", "fulfilment");

var storefront = builder.AddProject<Projects.DDDToolkit_Examples_MassTransit_Storefront>("storefront", launchProfileName: null)
    .WithHttpEndpoint()
    .WithReference(storefrontDb).WaitFor(storefrontDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithHttpHealthCheck("/health");

var payments = builder.AddProject<Projects.DDDToolkit_Examples_MassTransit_Payments>("payments", launchProfileName: null)
    .WithHttpEndpoint()
    .WithReference(paymentsDb).WaitFor(paymentsDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithHttpHealthCheck("/health");

var fulfilment = builder.AddProject<Projects.DDDToolkit_Examples_MassTransit_Fulfilment>("fulfilment", launchProfileName: null)
    .WithHttpEndpoint()
    .WithReference(fulfilmentDb).WaitFor(fulfilmentDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.DDDToolkit_Examples_Gateway>("gateway", launchProfileName: null)
    .WithHttpEndpoint()
    .WithReference(storefront).WaitFor(storefront)
    .WithReference(payments).WaitFor(payments)
    .WithReference(fulfilment).WaitFor(fulfilment)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
