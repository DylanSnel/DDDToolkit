// The shop as three services over RabbitMQ, with Wolverine as the transport. Each service has a database of
// its own, and not all of the same kind: Storefront on SQL Server, Payments and Fulfilment on Postgres. The
// only thing the three share is the broker, and the only thing they say on it is published contracts.

var builder = DistributedApplication.CreateBuilder(args);

var rabbitmq = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();

var sqlServer = builder.AddSqlServer("sqlserver");
var postgres = builder.AddPostgres("postgres");

var storefrontDb = sqlServer.AddDatabase("storefront-db", "storefront");
var paymentsDb = postgres.AddDatabase("payments-db", "payments");
var fulfilmentDb = postgres.AddDatabase("fulfilment-db", "fulfilment");

var storefront = builder.AddProject<Projects.DDDToolkit_Examples_Wolverine_Storefront>("storefront", launchProfileName: null)
    .WithHttpEndpoint()
    .WithReference(storefrontDb).WaitFor(storefrontDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithHttpHealthCheck("/health");

var payments = builder.AddProject<Projects.DDDToolkit_Examples_Wolverine_Payments>("payments", launchProfileName: null)
    .WithHttpEndpoint()
    .WithReference(paymentsDb).WaitFor(paymentsDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithHttpHealthCheck("/health");

var fulfilment = builder.AddProject<Projects.DDDToolkit_Examples_Wolverine_Fulfilment>("fulfilment", launchProfileName: null)
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
