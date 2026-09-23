using DDDToolkit.EntityFramework;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.MassTransit.Payments;
using DDDToolkit.Examples.Payments;
using DDDToolkit.Examples.Payments.Api;
using DDDToolkit.Mediator;
using DDDToolkit.Messaging.MassTransit;

// The shop's payments service, over RabbitMQ with MassTransit 8 as the transport (RabbitMq.cs). Payments on
// its own: the one service that talks to the payment provider.
//
// On a SQL Server database of its own, as are the other two. MassTransit 8 is the last version under the
// Apache 2.0 licence; see the README of DDDToolkit.Messaging.MassTransit before moving to 9.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

builder.Services.AddRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")
    ?? throw new InvalidOperationException("ConnectionStrings:rabbitmq is not set. Run DDDToolkit.Examples.MassTransit.AppHost."));

var host = new ModuleHost(
    ModuleDatabase.SqlServer(builder.Configuration.GetConnectionString("payments-db")
        ?? throw new InvalidOperationException("ConnectionStrings:payments-db is not set. Run DDDToolkit.Examples.MassTransit.AppHost.")),
    outbox =>
    {
        outbox.SendToModules();
        outbox.SendToMassTransit();
    });

builder.Services.AddPaymentsModule(host);

var app = builder.Build();

app.MapPaymentsEndpoints();
app.MapDefaultEndpoints();

await app.RunAsync();
