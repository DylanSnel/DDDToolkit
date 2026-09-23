# DDDToolkit.Messaging.MassTransit

MassTransit as the transport between one process's outbox and another's inbox. The toolkit's outbox writes
the message in the aggregate's transaction; MassTransit carries it; the toolkit's inbox applies it once.
MassTransit's own outbox and sagas are not involved.

```csharp
// the outbox of a module
options.UseOutbox<OrderingContext>(outbox => outbox.SendToMassTransit());

// the receiving process
builder.Services.AddMassTransit(bus =>
{
    bus.AddIntegrationEventConsumer();
    bus.UsingRabbitMq((context, rabbit) =>
    {
        rabbit.ReceiveEndpoint("fulfilment", endpoint =>
        {
            endpoint.ConfigureConsumeTopology = false;
            endpoint.Bind("integration-events", exchange =>
            {
                exchange.ExchangeType = "topic";
                exchange.RoutingKey = "ordering.order-confirmed";
            });
            endpoint.UseMessageRetry(retry => retry.Intervals(250, 1000, 5000));
            endpoint.ConfigureConsumer<IntegrationEventEnvelopeConsumer>(context);
        });
    });
});
```

## Licence

This package is built on **MassTransit 8**, the last major version under the Apache 2.0 licence, and
declares it as its dependency. MassTransit 9 and later are sold under a commercial licence. Moving to
them is a decision for whoever deploys the software, and this package does not make it for you. If you
would rather not depend on MassTransit at all, `DDDToolkit.Messaging.Wolverine` does the same job with
Wolverine, under the MIT licence.
