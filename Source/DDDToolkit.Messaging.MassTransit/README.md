# DDDToolkit.Messaging.MassTransit

MassTransit as the transport between one process's outbox and another's inbox. The toolkit's outbox writes
the message in the aggregate's transaction; MassTransit carries it; the toolkit's inbox applies it once.
MassTransit's own outbox and sagas are not involved.

Each contract travels as a message type of its own, the way MassTransit sends anything: it gets its own
exchange, and a receive endpoint that consumes it is bound to that exchange by MassTransit's topology.

```csharp
// the outbox of a module
options.UseOutbox<OrderingContext>(outbox => outbox.SendToMassTransit());

// the receiving process, after registering its modules
builder.Services.AddMassTransit(bus =>
{
    // a consumer per contract the modules handle and another service publishes
    bus.AddIntegrationEventConsumers(builder.Services.IntegrationEventSubscriptions());
    bus.UsingRabbitMq((context, rabbit) =>
    {
        // optional: exchanges named after the event, ordering.order-placed.v1, not the CLR type
        rabbit.UseIntegrationEventNames();

        rabbit.ReceiveEndpoint("fulfilment", endpoint =>
        {
            endpoint.UseMessageRetry(retry => retry.Intervals(250, 1000, 5000));
            endpoint.ConfigureConsumers(context);
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
