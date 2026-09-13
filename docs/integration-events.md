# Integration events

The [outbox](entity-framework.md#the-outbox) makes a domain event durable. This page is about the
other half: getting it out of the process, and consuming it safely at the other end.

`DDDToolkit.EntityFramework` gives you three things for that. A sink interface, so the outbox has
somewhere to deliver to. A seam between the event you raise and the message you publish, so the two
can change at different speeds. And an inbox, so a consumer can be handed the same message twice
without doing the work twice.

It does not give you a bus. There is no adapter here for RabbitMQ, Azure Service Bus, Kafka or SQS,
and there is not going to be one. [MassTransit](https://masstransit.io/) and
[Wolverine](https://wolverinefx.net/) are far more mature at that job than anything this repository
would write. What is missing without this page is smaller and more specific: a shape for the outbox to
hand a message to, whatever you put behind it.

## Two kinds of event

They look the same in C# and they are not the same thing.

| | Domain event | Integration event |
|---|---|---|
| Who reads it | Code in this solution | Other systems, other teams |
| Who owns the shape | You, today | Everyone who deployed against it |
| Renaming a field | A refactor | A breaking change |
| Carries | Your identifiers, your value objects | Primitives, usually |
| Lifetime | As long as the aggregate | As long as the oldest consumer |

A domain event is internal. `OrderPlaced(OrderId, CustomerId, Money)` uses your types because the only
things that read it are in your build. The moment it crosses a process boundary that stops being true.
Someone deserializes it in another repository, in another language, in a service you cannot redeploy.
Now the record is a published schema, and every field is a promise.

Keeping one type for both jobs works right up until it does not, and by then you have consumers.

## Why the outbox needs a sink

The outbox writes one row per event in the same transaction as the aggregate, which is what makes it
durable. But a stored row is not a delivered message. Something has to read the row and carry it
somewhere.

Without a sink, `OutboxProcessor<TContext>` reads the row and hands it to the same in-process delegate
that would have run at save time. That is genuinely useful, because now the handler survives a crash.
It is also the one thing an outbox exists for that it cannot do: reach something outside this process.
The message goes round in a circle.

`IIntegrationEventSink` is the exit.

```csharp
public interface IIntegrationEventSink
{
    Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default);
}
```

## Writing a sink

One method, one message, one cancellation token. Return and the transport accepted it. Throw and it
did not.

```csharp
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;

public sealed class WebhookSink(HttpClient client) : IIntegrationEventSink
{
    public async Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        using var content = new StringContent(message.Payload, Encoding.UTF8, message.ContentType);
        content.Headers.Add("X-Message-Id", message.MessageId.ToString());
        content.Headers.Add("X-Message-Name", $"{message.Name}/v{message.Version}");

        var response = await client.PostAsync("/events", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
```

A sink for a real broker is the same shape. Topic from `Name`, deduplication id from `MessageId`,
partition key from `AggregateId`, body from `Payload`:

```csharp
public sealed class ServiceBusSink(ServiceBusSender sender) : IIntegrationEventSink
{
    public Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken)
        => sender.SendMessageAsync(
            new ServiceBusMessage(message.Payload)
            {
                MessageId = message.MessageId.ToString(),
                Subject = message.Name,
                PartitionKey = message.AggregateId,
                ContentType = message.ContentType,
                ApplicationProperties = { ["version"] = message.Version },
            },
            cancellationToken);
}
```

Register it where you configure the outbox:

```csharp
builder.Services.AddDDDToolkitEntityFramework(options =>
{
    options.UseOutbox(outbox =>
    {
        outbox.RegisterEventsFromAssemblyContaining<Program>();
        outbox.SendTo<ServiceBusSink>();
    });
});

builder.Services.AddOutboxBackgroundService<OrderingContext>(TimeSpan.FromSeconds(2));
```

`SendTo<TSink>()` takes the sink from the scope the processor runs in when you registered it there,
and otherwise builds it with its constructor services injected. `SendTo(sink)` takes an instance you
already have, which is what tests usually want.

### Why an envelope and not the event

The sink receives an `IntegrationEventMessage`, not an `IDomainEvent` and not the outbox row. Both of
those were considered and neither is right.

Handing a sink the `IDomainEvent` makes every sink responsible for serializing it, which means every
sink has to know your JSON options, and it quietly puts the domain type on the wire. Handing it the
`OutboxMessage` row is worse: that row carries `Attempts`, `ProcessedAt` and `LastError`, which are
this process's bookkeeping and no transport's business, and it drags Entity Framework into the
signature. A sink project would then have to reference `DDDToolkit.EntityFramework` to send an HTTP
request.

The envelope is neither. It lives in the core `DDDToolkit` package, it has no Entity Framework types
at all, and it carries exactly what a transport routes on:

| Member | What it is |
|---|---|
| `MessageId` | The idempotency key. The domain event's `EventId`, which is also the outbox row's key |
| `Name` | What consumers route on |
| `Version` | The schema version of `Payload` |
| `Payload` | The serialized body |
| `ContentType` | `application/json` unless you change it |
| `OccurredAt` | When the thing happened, not when delivery was attempted |
| `AggregateType` | CLR type name of the aggregate it came from |
| `AggregateId` | The aggregate's key as text. Useful as a partition key |
| `Body` | The object `Payload` came from, for transports that speak CLR objects |

One identifier runs the whole way. `EventId` on the event, `Id` on the outbox row, `MessageId` on the
envelope, `MessageId` on the consumer's inbox row. That is deliberate, and it is what makes the
guarantees below add up.

## Saying what gets published

By default, nothing is mapped and the domain event is published as it stands, under the name the
outbox stored, reusing the JSON already in the row. No second type, no second serialization, no
configuration. If you are not ready to split the two yet, you pay nothing for the seam being there.

When you are ready, register a conversion:

```csharp
[IntegrationEvent("ordering.order-placed", Version = 2)]
public sealed record OrderPlacedV2(Guid OrderId, string Customer, decimal Total, string Currency);
```

```csharp
options.UseOutbox(outbox =>
{
    outbox.RegisterEventsFromAssemblyContaining<Program>();
    outbox.SendTo<ServiceBusSink>();

    outbox.PublishAs<OrderPlaced, OrderPlacedV2>(e => new OrderPlacedV2(
        e.OrderId.Value,
        e.Customer.Value,
        e.Total.Amount,
        e.Total.Currency));
});
```

Now `OrderPlaced` can grow a field, lose a field or be renamed, and the wire is untouched until you
change `OrderPlacedV2` on purpose.

Three more things that map:

```csharp
// This event is nobody else's business. Local handlers still get it; no sink is called.
outbox.DoNotPublish<CustomerCreditChecked>();

// Publish only some occurrences. Returning null drops that one message.
outbox.PublishAs<OrderPlaced, OrderPlacedV2>(e => e.Total.Amount < 1000 ? null : Map(e));
```

`[IntegrationEvent]` pins the name and the version. Without it a contract falls back to
`[DomainEventName]`, and without that to the class name, with version 1. See
[Domain events](domain-events.md#stable-names) for why the name has to be pinned at all.

Bump `Version` when you break the payload. Leave it when you only add something optional. The version
travels on the envelope, so a consumer can branch on it without parsing the body first.

### Where the mapping happens

The outbox row always stores the domain event. The conversion runs at delivery, not at save.

That is a deliberate trade. It keeps the row a faithful record of what actually happened in the
domain, it means the in-process path and the sink path read the same row, and it means fixing a wrong
mapping is a deployment rather than a data migration: reset `Attempts` and the rows go out again in
the new shape. The cost is that the domain event type must still exist and still deserialize when the
processor runs, which was already true before sinks existed.

## Which delivery wins

| Configured | What the processor does |
|---|---|
| `DispatchInProcess` only | Calls the delegate with the domain event |
| One or more `SendTo` | Publishes the message to every sink. The delegate is not called |
| Both | Sinks win. The delegate is skipped |
| Both, plus `AlsoDispatchInProcess = true` | Delegate first with the domain event, then the sinks with the contract |

```csharp
options.UseOutbox(outbox =>
{
    outbox.SendTo<ServiceBusSink>();
    outbox.AlsoDispatchInProcess = true;   // local projection and the bus, from one row
});
```

With `AlsoDispatchInProcess`, a throwing local handler fails the message before any sink sees it. That
is the right order: publishing a message whose local side effect failed would be a lie.

A processor with neither a sink nor a delegate throws at construction, naming both calls.

## When one sink fails and another does not

Every sink is attempted, in registration order. A sink that throws does not stop the sinks behind it.

The message as a whole then counts as failed. It is not marked processed, `Attempts` goes up, and
`LastError` records an `IntegrationEventDeliveryException` naming the sinks that threw. The next run
hands the message to **all** the sinks again, including the ones that already accepted it.

That is the honest consequence of one row per message. Per-sink progress would need one row per sink
per message, which is a different table and a different set of failure modes, and it is not what this
package does. Two sinks over the same outbox means both must tolerate a repeat. If one of your
transports cannot, give it its own outbox table and its own processor, or put a queue in front of it.

## The inbox on the other side

Everything above is at-least-once. A consumer will eventually see the same message twice. The inbox is
how you make that harmless.

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddDomainEventInbox();
}
```

```csharp
builder.Services.AddDomainEventInbox<BillingContext>();
```

```csharp
public sealed class OrderPlacedConsumer(DomainEventInbox<BillingContext> inbox, BillingContext context)
{
    public async Task Consume(IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        await inbox.ExecuteOnceAsync(message, "billing.invoicer", async (received, token) =>
        {
            var order = JsonSerializer.Deserialize<OrderPlacedV2>(received.Payload)!;
            context.Invoices.Add(new Invoice(order.OrderId, order.Total));
        },
        cancellationToken);

        // Acknowledge the message either way. A repeat is not an error.
    }
}
```

`ExecuteOnceAsync` returns `true` when your handler ran and `false` when this consumer had already
applied the message. Both are success.

What it does, in order:

1. Look for a row keyed on this message and this consumer. If it exists, return `false` and do nothing.
2. Start a transaction, unless the caller already has one, in which case join it.
3. Add the inbox row.
4. Run your handler.
5. `SaveChanges`, which writes the handler's changes and the row together.
6. Commit, unless the transaction is the caller's.

Steps 3 to 6 are the whole point. The row that says "applied" and the effect of applying it are
written by one `SaveChanges` inside one transaction, so there is no instant where one exists without
the other. A crash anywhere in between rolls back both, and the next delivery applies the message
cleanly.

The key is the pair, not the message. Two consumers of the same message each get a row and each run
once, so adding a consumer later does not mean replaying its backlog through the consumers that are
already up to date. Pick consumer names you will not want to change, the same way you pick
`[DomainEventName]`.

### What the inbox cannot do

It cannot undo work outside the database. If your handler sends a mail and the transaction then rolls
back, the row is gone but the mail is sent. The fix is the same shape as the outbox itself: write a
row the transaction owns, and let something else act on that row afterwards.

It also does not order anything. If message B arrives before message A, the inbox applies B. Handlers
that care about order have to say so themselves, usually with a version or a sequence number in the
payload.

## The guarantees, honestly

- **At-least-once, end to end.** Every hop can repeat. The outbox marks a row processed only after
  delivery returned, a broker redelivers what was not acknowledged, and a retry redelivers to sinks
  that already accepted the message. Nothing anywhere is exactly-once on the wire.
- **Idempotency is keyed on `MessageId`.** It is the domain event's `EventId`, stable across every
  redelivery, and it is what the inbox stores. If you write your own deduplication, key it on that and
  nothing else.
- **Ordering is best effort.** The processor loads oldest first, but a failed message is retried after
  messages written later, two processors can interleave, and a broker makes its own decisions. Do not
  build anything on delivery order.
- **Delivery is not immediate.** It happens on the next poll of the background service, not at commit.
- **Nothing is lost and nothing is published for a rolled back transaction.** That part the outbox
  does guarantee, because the rows are written by the same transaction as the aggregate.

## Tables, schema and migrations

The outbox and the inbox both live in a `ddd` schema by default, away from your domain tables. Plumbing
is easier to grant, purge and ignore when it is not mixed in with the business.

```csharp
modelBuilder.AddDomainEventOutbox();                          // ddd.OutboxMessages
modelBuilder.AddDomainEventInbox();                           // ddd.InboxMessages
modelBuilder.AddDomainEventInbox("Consumed", schema: "msg");  // msg.Consumed
modelBuilder.AddDomainEventInbox(schema: null);               // the provider's default schema
```

SQLite has no schemas. Its provider drops the schema when it writes an identifier, so the tables are
plain `OutboxMessages` and `InboxMessages` there and everything works unchanged. Nothing to configure
and nothing to work around.

The inbox table is four columns:

| Column | Type | Meaning |
|---|---|---|
| `MessageId` | `Guid`, key | The message's id |
| `Consumer` | `string`, key, 256 | Who applied it |
| `MessageName` | `string?`, 256 | The published name, for when you are reading the table by hand |
| `ProcessedAt` | `DateTimeOffset` | When the handler finished |

If you scaffold migrations there is nothing else to do. Both tables are part of your model as soon as
the calls are in `OnModelCreating`, so `dotnet ef migrations add` writes them, schema included.

If you write migrations by hand:

```csharp
public partial class AddMessaging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateDomainEventOutbox();
        migrationBuilder.CreateDomainEventInbox();
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropDomainEventInbox();
        migrationBuilder.DropDomainEventOutbox();
    }
}
```

They take the same `tableName` and `schema` you gave the model builder, so pass the same arguments in
both places. They create the schema first where the provider has schemas, and leave the schema off
entirely where it does not. The shapes are checked against the model in the tests, so a hand-written
migration and a scaffolded one produce the same table.

## Where to look next

- [Domain events](domain-events.md) for raising, draining and `[DomainEventName]`.
- [Entity Framework](entity-framework.md#domain-event-delivery) for the outbox itself, the processor,
  retries and `MaxAttempts`.
- `Tests/DDDToolkit.EntityFramework.Tests/IntegrationEventTests.cs` for the sink contract and the
  multi-sink failure behaviour.
- `Tests/DDDToolkit.EntityFramework.Tests/InboxTests.cs` for the crash between handling and marking,
  and for the outbox and inbox working together.
- `Tests/DDDToolkit.EntityFramework.Tests/MessagingSchemaTests.cs` for the schema override and the
  migration helpers.
