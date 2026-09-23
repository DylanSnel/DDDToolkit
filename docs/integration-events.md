# Integration events

The [outbox](entity-framework.md#the-outbox) makes a domain event durable. This page is about the other
half: getting it to whoever has to react, and consuming it safely at the other end.

Most of the time that is another module in the same process. That is the case this page starts with,
because it is the common one and because the toolkit used to get it wrong.

`DDDToolkit.EntityFramework` gives you four things. A seam between the event you raise and the message
you publish, so the two can change at different speeds. A sink interface, so the outbox has somewhere to
deliver to, with an in-process module sink ready made. An inbox, so a consumer can be handed the same
message twice without doing the work twice. And versioning, so a payload written by last year's build is
still readable by this year's.

It does not give you a bus. There is no adapter here for RabbitMQ, Azure Service Bus, Kafka or SQS, and
there is not going to be one. [MassTransit](https://masstransit.io/) and
[Wolverine](https://wolverinefx.net/) are far more mature at that job than anything this repository
would write. What it does ship is a sink for Postgres queues
([pgmq](#when-a-module-becomes-its-own-deployable-pgmq)), because there the queue is a table and the
guarantees change.

## Two kinds of event

They look the same in C# and they are not the same thing.

| | Domain event | Integration event |
|---|---|---|
| Who reads it | Code inside one module | Other modules, other services, other teams |
| Who owns the shape | You, today | Everyone who deployed against it |
| Renaming a field | A refactor | A breaking change |
| Carries | Your identifiers, your value objects | Primitives, usually |
| Lifetime | As long as the aggregate | As long as the oldest consumer |

A domain event is internal. `OrderPlaced(OrderId, CustomerId, Money)` uses your types because the only
things that read it are in the same module. The moment something outside that module reads it, that
stops being true. Now the record is a published schema, and every field is a promise.

Note the boundary in the first row. It is the **module**, not the process. A record that only another
assembly in the same solution deserializes is already a published schema, because you cannot change it
without changing them.

## The common case: another module in this process

Integration events exist so that another module can pick something up. In a modular monolith that module
is in the same process, which makes this the most valuable sink in the package and the one to reach for
first.

Each module registers its own half, next to its own context. The producing module says what it publishes
and that it goes to the other modules:

```csharp
// inside AddOrderingModule
services.AddDDDToolkitEntityFramework(options => options.UseOutbox<OrderingContext>(outbox =>
{
    outbox.RegisterEventsFromAssemblyContaining<Order>();
    outbox.PublishAs<OrderPlaced, OrderPlacedV2>(e => new OrderPlacedV2(e.OrderId.Value, e.Total.Amount));
    outbox.SendToModules();
}));
services.AddOutboxBackgroundService<OrderingContext>(TimeSpan.FromSeconds(2));
```

A consuming module says which contracts it reads and which handlers run under its inbox:

```csharp
// inside AddBillingModule
services.AddDDDToolkitEntityFramework(options =>
    options.MapIntegrationEvents(contracts => contracts.RegisterFromAssemblyContaining<OrderPlacedV2>()));
services.AddModuleIntegrationEvents<BillingContext>(module => module.Handle<OrderPlacedV2, RaiseInvoice>());
```

```csharp
[IntegrationEventConsumer("billing.invoicer")]
public sealed class RaiseInvoice(BillingContext context) : IIntegrationEventHandler<OrderPlacedV2>
{
    public Task HandleAsync(OrderPlacedV2 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        context.Invoices.Add(new Invoice(contract.OrderId, contract.Total));
        return Task.CompletedTask;
    }
}
```

Neither module names the other. `SendToModules()` offers every message to every module registered with
`AddModuleIntegrationEvents`, and each runs only its own handlers, under its own inbox, in its own
context. A third module that wants `OrderPlacedV2` registers itself the same way, and nothing in Ordering or
Billing changes. The host only calls `AddOrderingModule` and `AddBillingModule`; see
[the example](../Examples/ModularMonolith.Supabase).

`AddDDDToolkitEntityFramework` can be called by every module like this because each call configures the
same options. What is genuinely process-wide, such as `DispatchWithMediator()`, stays in the host, and
setting the dispatch delegate twice throws rather than letting one module silently replace another's.

Three things in that handler are worth reading twice.

### It is typed on the contract, not on the domain event

`IIntegrationEventHandler<OrderPlacedV2>`, never `IIntegrationEventHandler<OrderPlaced>`. The toolkit
used to dispatch the domain event itself to local handlers, and that is the thing this replaces. A
handler typed on `OrderPlaced` forces the billing module to reference the ordering module's domain
assembly, which means `OrderId`, `Money`, and whatever else that record touches. Ordering can no longer
rename a field without breaking a build somewhere else, and the two modules are one module with extra
folders.

`OrderPlacedV2` is a record of primitives that both modules can see. Ordering owns it and publishes it,
billing reads it, and neither knows anything else about the other.

The payload is read from `message.Payload` and deserialized into the consumer's own object whenever the
contract is registered, which is exactly what would happen if the message had come off a queue. That is
deliberate: a handler cannot accidentally mutate an object the producer is still holding, and a module
that is extracted later behaves the same way on its first day out. When nothing is registered under the
message's name, the sink falls back to `message.Body`, which is the object the outbox built.

### It goes through the outbox, and that is what makes it different from a method call

A direct call from ordering into billing runs inside ordering's transaction. Billing throws, ordering's
order is rolled back, and you have coupled the two in the worst possible way: an unrelated module's bug
can now refuse an order.

The outbox breaks that. Ordering commits, the row is durable, and the consumers run afterwards on the
background service's next poll. Whether billing succeeds is billing's problem and a later retry.

### Each handler has its own inbox row

Delivery is at-least-once, so the message will be replayed. Every handler runs inside the inbox, keyed by
the message id and that handler's consumer name, so its writes and the row that says "applied" are
written by one `SaveChanges` inside one transaction.

The consequence is what you want on a retry:

| | First run | Retry |
|---|---|---|
| `billing.invoicer` | applied, row written | skipped |
| `search.indexer` | threw, nothing written | runs again |

The message as a whole counts as failed while any handler is failing, so the outbox row stays pending and
is picked up again. Handlers that already succeeded are not run twice.

`[IntegrationEventConsumer("billing.invoicer")]` is what the inbox keys on. Without it the name falls
back to the full CLR type name, which changes when you rename or move the class, and the inbox cannot
tell that from a new consumer: it replays the whole backlog through it. Name your consumers, the same way
you name your events.

### What it needs in the model

The inbox table, in the consuming context:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddDomainEventOutbox(Database);
    modelBuilder.AddDomainEventInbox(Database);
}
```

`AddModuleIntegrationEvents<TContext>(...)` registers the module, its inbox and its handlers together.
Handlers should write through that same `TContext`, because that is the context whose transaction the
inbox row is in. Two handlers under one consumer name in one module are refused at registration: the
inbox could not tell them apart, and the second would never run.

A failure names the module as well as the consumer, `BillingContext/billing.invoicer`, and a second
module failing does not make the first run again: each module's inbox remembers what that module applied.

Delivering needs no reflection. `Handle<TContract, THandler>()` captures a typed call to the handler when
it is registered, and the sink uses that.

### What it does not do

It does not give a consumer its own retry schedule. One outbox row is one message, so a consumer that
keeps failing keeps the row pending until `MaxAttempts`, and then the row stops being picked up and its
`LastError` is there to be read. If one consumer needs a different retry policy from the others, give it
its own outbox table and its own processor.

It does not order anything. See [the guarantees](#the-guarantees-honestly).

## When a module becomes its own deployable: pgmq

`DDDToolkit.Messaging.Postgres` sends published messages to a
[pgmq](https://github.com/pgmq/pgmq) queue.

pgmq is a Postgres extension whose queues are ordinary tables, and `pgmq.send` is an ordinary insert.
That one fact is the whole reason this package exists: the enqueue obeys the transaction it is called in.
No broker can do that, which is why every broker needs an outbox in front of it. On Postgres you get the
outbox guarantee from the queue itself.

Supabase Queues is this extension with a UI on top, so a Supabase project already has it. The sink
knows nothing about Supabase; it talks to Postgres through Npgsql and SQL. To have the Supabase CLI
apply your migrations, see `DDDToolkit.EntityFramework.Supabase` in
[Entity Framework → Supabase](entity-framework.md#supabase).

```bash
dotnet add package DDDToolkit.Messaging.Postgres
```

```csharp
builder.Services.AddPgmqSink<OrderingContext>(pgmq => pgmq.UseQueue("ordering_events"));

builder.Services.AddDDDToolkitEntityFramework(options =>
{
    options.UseOutbox(outbox =>
    {
        outbox.RegisterEventsFromAssemblyContaining<Program>();
        outbox.PublishAs<OrderPlaced, OrderPlacedV2>(e => new OrderPlacedV2(e.OrderId.Value, e.Total.Amount));
        outbox.SendToPgmq<OrderingContext>();
        outbox.DeliverInTransaction = true;
    });
});
```

`PgmqSink<TContext>` sends on that context's connection, and joins that context's current transaction if
it has one. `DeliverInTransaction` is what makes that pay: the outbox processor then wraps one message's
delivery and its "processed" mark in a single transaction. Either the message is on the queue and the row
is marked, or neither happened. The handoff from the outbox to the queue is exactly once.

Turn `DeliverInTransaction` on for a sink that writes to the same database, and leave it off otherwise. A
send to a broker or an HTTP endpoint cannot be rolled back, so widening the transaction around it buys
nothing. It also changes what `SendToModules` guarantees: the inbox joins the caller's transaction, so
with one transaction around the whole message a failing consumer rolls back the consumers that already
succeeded. Use one or the other on a given outbox, not both.

### Enqueueing in the aggregate's own transaction

You can go further and skip the outbox, because `pgmq.send` is just an insert:

```csharp
await using var transaction = await context.Database.BeginTransactionAsync();

context.Orders.Add(order);
await context.SaveChangesAsync();

await PgmqQueue.SendAsync(
    (NpgsqlConnection)context.Database.GetDbConnection(),
    (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction(),
    "ordering_events",
    JsonSerializer.Serialize(new OrderPlacedV2(order.Id.Value, order.Total.Amount)));

await transaction.CommitAsync();
```

The order and the message now commit together, with nothing in between.

The outbox is still the better default. It keeps the queue name and the payload shape out of the code
path that writes the aggregate, it retries for you, it lets you add a second sink without touching the
aggregate, and it survives the queue being briefly unreachable. Reach for the direct call when you have a
specific reason and can name it.

### Reading the queue

The receiving process registers its modules exactly as a monolith does, with
`AddModuleIntegrationEvents`, and a `PgmqConsumer` for its queue:

```csharp
builder.Services.AddShippingModule(...);   // AddModuleIntegrationEvents<ShippingContext>(m => m.Handle<OrderConfirmedV1, BookShipment>())
builder.Services.AddPgmqConsumer(dataSource, "fulfilment");
```

The consumer reads the queue, rebuilds each envelope from its headers
(`IntegrationEventHeaders.ToMessage`), and hands it to `IntegrationEventReceiver`, which offers it to
every module in the process the way the module sink does: each handler inside its module's inbox. So
`BookShipment` is the same class whether the message came from Ordering next door or through a queue,
and a message delivered twice is applied once.

pgmq is at-least-once like everything else. Reading hides a message for a visibility timeout rather than
removing it, and the consumer archives a message only after every module applied it. When a handler
throws, the message is left alone and becomes visible again after `VisibilityTimeout`: a retry is a wait.
After `MaxDeliveries` reads it is archived as poison and logged as an error, so one bad message cannot
hold up the queue; it stays readable in the archive table. A message without the toolkit's headers has
no identity to deduplicate on, and is archived unread.

`PgmqQueue.ReadAsync` and `ArchiveAsync` are there for anything the consumer does not do.
`PgmqMessage.MessageId` is pgmq's own counter, not the integration message id; idempotency keys on the
`messageId` header, the domain event's `EventId`, the same one the
[inbox](#the-inbox-on-the-other-side) stores.

### One queue per consumer, and fan-out

pgmq is a queue, not a topic: a message read by one consumer is gone for the others. When several
deployables consume, give each its own queue and enqueue every message on the queue of each service
that wants it:

```csharp
builder.Services.AddPgmqSink<OrderingContext>(pgmq => pgmq.UseQueues(message => message.Name switch
{
    "ordering.order-placed" => ["fulfilment", "payments"],
    "ordering.order-confirmed" => ["fulfilment"],
    _ => [],
}));
```

The enqueues ride one connection and one transaction, so a message reaches all of its queues or none.
`Examples/Microservices.Pgmq` routes the whole shop this way.

### What the sink sends

The body is the envelope's `Payload`, stored as `jsonb`. The envelope's routing fields go into pgmq's
`headers` column, so a consumer can filter without parsing the body and a human reading the table can
tell what a row is:

```json
{
  "messageId": "0199...", "name": "ordering.order-placed", "version": "2",
  "contentType": "application/json", "occurredAt": "2026-09-13T12:00:00.0000000+00:00",
  "aggregateType": "Order", "aggregateId": "ORD_0199..."
}
```

Turn that off with `pgmq.SendHeaders = false`.

### Queues, creation and the missing extension

By default the sink sends everything to one queue called `integration_events` and creates it on first use.
`pgmq.create` is idempotent, and the create runs on a connection of its own rather than on yours, because
creating a table is DDL and Postgres rolls DDL back like anything else. A queue is deployment state; it
should not vanish when a business transaction changes its mind.

```csharp
pgmq.UseQueue("ordering_events");                                   // one queue, named
pgmq.UseQueue(message => message.Name.Replace('.', '_'));           // one queue per event name
pgmq.CreateQueueIfMissing = false;                                  // queues come from a migration
```

pgmq builds table names from the queue name, so keep them short, lower case, and free of anything that is
not a letter, a digit or an underscore. A published name like `ordering.order-placed` has to be rewritten,
not passed through. Turn creation off when the application's database user may not create tables; the sink
then fails on a missing queue instead of hiding it.

If the extension is not installed you get a `PgmqNotInstalledException` naming the database and saying
what to run, rather than `schema "pgmq" does not exist` from somewhere deep in the driver:

```sql
CREATE EXTENSION IF NOT EXISTS pgmq;
```

The extension has to be on the server first. `ghcr.io/pgmq/pg17-pgmq` is an image that ships it, and
managed Postgres that offers queues generally has it already.

### For a queue in another database

`PgmqSink` (no type argument) opens its own connections from an `NpgsqlDataSource`:

```csharp
builder.Services.AddPgmqSink(NpgsqlDataSource.Create(connectionString), pgmq => pgmq.UseQueue("events"));

// and on the outbox:
outbox.SendToPgmq();
```

There is no shared transaction on that path, so it is at-least-once like any other remote sink. Use it
when the queue genuinely lives somewhere else.

## Through a broker: Wolverine

`DDDToolkit.Messaging.Wolverine` makes Wolverine the transport between the outbox of one process and the
inbox of another. Wolverine carries the message; the toolkit keeps the outbox that writes it in the
aggregate's transaction and the inbox that applies it once. Wolverine's own outbox, inbox and sagas are
not used, so there is one of each rather than two that disagree.

Every message travels as an `IntegrationEventEnvelope`: the same headers the pgmq sink writes
(`IntegrationEventHeaders`), next to the payload, in one object. One type for every contract, so routing
never needs the contracts' assemblies; route on `envelope.Name`, the contract's published name.

```csharp
builder.UseWolverine(wolverine =>
{
    wolverine.UseRabbitMq(rabbitUri).AutoProvision();

    // sending: every envelope to a topic exchange, keyed on the contract's name
    wolverine.PublishMessagesToRabbitMqExchange<IntegrationEventEnvelope>("integration-events", envelope => envelope.Name)
        .ExchangeType(ExchangeType.Topic)
        .SendInline();

    // receiving: this service's queue, bound to the contracts it consumes, acknowledged after the inbox
    wolverine.ListenToRabbitQueue("fulfilment", queue => queue.BindExchange("integration-events", "ordering.order-confirmed"))
        .ProcessInline();

    wolverine.ReceiveIntegrationEvents();                    // the envelope's handler, retries, the error queue
    wolverine.Policies.DisableConventionalLocalRouting();    // what this process publishes goes to the broker
});

// the outbox of each module
options.UseOutbox<OrderingContext>(outbox => outbox.SendToWolverine());
```

`WolverineSink` publishes through `IMessageBus`, so where the envelope goes is Wolverine's routing: RabbitMQ
here, anything else Wolverine publishes to elsewhere. `IntegrationEventEnvelopeHandler` rebuilds the
message and hands it to `IntegrationEventReceiver`, which delivers it to the modules of the process, each
handler inside its module's inbox. When a handler throws, Wolverine retries with a cooldown and then moves
the envelope to its error queue; a retry cannot apply anything twice.

Two things to get right. Listen inline (`ProcessInline()`), so an envelope is acknowledged after the
modules applied it; a buffered listener acknowledges first, and a crash in between loses the message. And
reference `WolverineFx.RuntimeCompilation` in the process, because Wolverine compiles its handler adapters
at start-up and since 6.x ships that compiler separately. `Examples/Microservices.Wolverine` runs the shop
this way.

## For screens: the GraphQL subscription sink

`DDDToolkit.HotChocolate` has a third sink, and it is a different axis from the two above.

The module sink and pgmq are integration: durable, retried, and the receiver gets the message whether or
not it was running at the time. A GraphQL subscription is none of that. Nothing is stored, only the
clients holding a socket right now are reached, and a client that reconnects has missed whatever happened
while it was away.

Use it to keep a browser in step with the server. Never as the path by which some other part of the
system learns that an order was placed.

```csharp
builder.Services.AddIntegrationEventSubscriptions(map => map.Publish<OrderPlacedV2>("orderPlaced"));

outbox.SendTo<GraphQlSubscriptionSink>();
```

See [Subscriptions](graphql.md#pushing-integration-events-to-subscribers) for the subscription field, the
transports, and why it publishes the contract rather than the domain event.

## Writing a sink of your own

One method, one message, one cancellation token. Return and the transport accepted it. Throw and it did
not.

```csharp
public interface IIntegrationEventSink
{
    Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default);
}
```

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

`SendTo<TSink>()` takes the sink from the scope the processor runs in when you registered it there, and
otherwise builds it with its constructor services injected. `SendTo(sink)` takes an instance you already
have, which is what tests usually want.

### Why an envelope and not the event

The sink receives an `IntegrationEventMessage`, not an `IDomainEvent` and not the outbox row. Both of
those were considered and neither is right.

Handing a sink the `IDomainEvent` makes every sink responsible for serializing it, which means every sink
has to know your JSON options, and it quietly puts the domain type on the wire. Handing it the
`OutboxMessage` row is worse: that row carries `Attempts`, `ProcessedAt` and `LastError`, which are this
process's bookkeeping and no transport's business, and it drags Entity Framework into the signature. A
sink project would then have to reference `DDDToolkit.EntityFramework` to send an HTTP request.

The envelope is neither. It lives in the core `DDDToolkit` package, it has no Entity Framework types at
all, and it carries exactly what a transport routes on:

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

By default, nothing is mapped and the domain event is published as it stands, under the name the outbox
stored, reusing the JSON already in the row. No second type, no second serialization, no configuration.
If you are not ready to split the two yet, you pay nothing for the seam being there.

When you are ready, register a conversion:

```csharp
[IntegrationEvent("ordering.order-placed", Version = 2)]
public sealed record OrderPlacedV2(Guid OrderId, string Customer, decimal Total, string Currency);
```

```csharp
options.UseOutbox<OrderingContext>(outbox =>
{
    outbox.RegisterEventsFromAssemblyContaining<Order>();
    outbox.SendToModules();

    outbox.PublishAs<OrderPlaced, OrderPlacedV2>(e => new OrderPlacedV2(
        e.OrderId.Value,
        e.Customer.Value,
        e.Total.Amount,
        e.Total.Currency));
});
```

Now `OrderPlaced` can grow a field, lose a field or be renamed, and the wire is untouched until you change
`OrderPlacedV2` on purpose.

Two more things that map:

```csharp
// This event is nobody else's business. Local handlers still get it; no sink is called.
outbox.DoNotPublish<CustomerCreditChecked>();

// Publish only some occurrences. Returning null drops that one message.
outbox.PublishAs<OrderPlaced, OrderPlacedV2>(e => e.Total.Amount < 1000 ? null : Map(e));
```

`[IntegrationEvent]` pins the name and the version. Without it a contract falls back to
`[DomainEventName]`, and without that to the class name, with version 1. See
[Domain events](domain-events.md#stable-names) for why the name has to be pinned at all.

### Where the mapping happens

The outbox row always stores the domain event. The conversion runs at delivery, not at save.

That is a deliberate trade. It keeps the row a faithful record of what actually happened in the domain, it
means the in-process path and the sink path read the same row, and it means fixing a wrong mapping is a
deployment rather than a data migration: reset `Attempts` and the rows go out again in the new shape. The
cost is that the domain event type must still exist and still deserialize when the processor runs, which
is what the next section is about.

## Versioning and upcasting

`[DomainEventName]` keeps the name stable while you rename the class. Nothing kept the **shape** stable,
and the shape is the harder promise. Once the outbox has published a payload, somebody has stored it,
queued it, or is about to read it back. A payload written before a deployment has to stay readable after
it.

Two places have to hold, and the toolkit covers both.

### The outbox reading its own old rows

Every outbox row records the shape it was written in, in a `Version` column, taken from
`[IntegrationEvent(Version = n)]` on the event type. An event that never changed shape says nothing and is
version 1.

When the processor reads a row whose version matches the type registered under that name, which is every
row until you bump something, nothing changes. When it does not match, the processor reads the payload as
the type registered for that older version and then upcasts it.

```csharp
[DomainEventName("ordering.order-placed")]
[IntegrationEvent("ordering.order-placed", Version = 1)]
public sealed record OrderPlacedV1(OrderId OrderId, Money Total) : DomainEvent;

[DomainEventName("ordering.order-placed")]
[IntegrationEvent("ordering.order-placed", Version = 2)]
public sealed record OrderPlaced(OrderId OrderId, Money Total, Channel Channel) : DomainEvent;
```

```csharp
options.MapIntegrationEvents(contracts => contracts
    .UpcastFrom<OrderPlacedV1, OrderPlaced>(v1 => new OrderPlaced(v1.OrderId, v1.Total, Channel.Unknown)));
```

Both attributes carry the same name on purpose. `[DomainEventName]` is what the outbox stores in the row,
`[IntegrationEvent]` is what says which shape that row is, and a version means nothing unless the two
agree. Giving them different names is refused at registration rather than left to be discovered later.

Keeping two types under one domain event name is fine. `RegisterEvent` and `RegisterEventsFromAssembly`
keep the newest as the type new events are written as, and the older one is only ever read.

A row whose shape nobody kept is not guessed at. The message fails, `LastError` names the version and the
call that would fix it, and the row waits for a human.

### A consumer reading a message

The same registry reads on the receiving side, so a handler is written against one shape and never
branches on a version number:

```csharp
await inbox.ExecuteOnceAsync<OrderPlacedV2>(message, "billing.invoicer", (order, received, token) =>
{
    context.Invoices.Add(new Invoice(order.OrderId, order.Total));
    return Task.CompletedTask;
});
```

A v1 message arrives, is deserialized as `OrderPlacedV1`, is upcast, and reaches the handler as
`OrderPlacedV2`. The module sink does the same thing for every handler it calls, so a handler typed on the
current contract keeps working when an older message turns up.

### The rules

- **Bump the version when you break the payload.** Removing a field, renaming one, or changing its meaning
  is a break. Adding an optional one is not.
- **Register the step, not the jump.** v1 to v2 and v2 to v3, and a v1 payload arrives as v3. Adding v4
  later is one more line instead of a rewrite of every upcaster you have.
- **An upcaster invents the fields the old payload never had.** That is unavoidable and it is the reason a
  version bump is a decision. Write a substitute the consumer can tell apart, such as an explicit
  `Unknown`, not a value that looks like real data.
- **Never delete the old record.** It is the only thing that can read a payload written against it. It
  costs a file.
- **An upcaster is a pure function.** It runs on the read path, possibly for every message in a backlog.
  Do not put a database call in it.

The version also travels on the envelope, so a consumer that has not adopted upcasting can still branch on
`message.Version` without parsing the body first.

## Which delivery wins

| Configured | What the processor does |
|---|---|
| `DispatchInProcess` only | Calls the delegate with the domain event |
| One or more `SendTo` | Publishes the message to every sink. The delegate is not called |
| Both | Sinks win. The delegate is skipped |
| Both, plus `AlsoDispatchInProcess = true` | Delegate first with the domain event, then the sinks with the contract |

```csharp
options.UseOutbox<OrderingContext>(outbox =>
{
    outbox.SendToModules();
    outbox.AlsoDispatchInProcess = true;   // handlers inside this module, and the other modules
});
```

With `AlsoDispatchInProcess`, a throwing local handler fails the message before any sink sees it. That is
the right order: publishing a message whose local side effect failed would be a lie.

Use the delegate for handlers inside the producing module, which may legitimately see the domain event,
and the module sink for everything outside it.

A processor with neither a sink nor a delegate throws at construction, naming both calls.

## When one sink fails and another does not

Every sink is attempted, in registration order. A sink that throws does not stop the sinks behind it.

The message as a whole then counts as failed. It is not marked processed, `Attempts` goes up, and
`LastError` records an `IntegrationEventDeliveryException` naming the sinks that threw. The next run hands
the message to **all** the sinks again, including the ones that already accepted it.

That is the honest consequence of one row per message. Per-sink progress would need one row per sink per
message, which is a different table and a different set of failure modes, and it is not what this package
does. Two sinks over the same outbox means both must tolerate a repeat. If one of your transports cannot,
give it its own outbox table and its own processor, or put a queue in front of it.

The module sink is the exception, and only because it keeps that bookkeeping itself: it has an inbox row
per consumer in each consuming module, so its handlers do make per-consumer progress across a retry.

## The inbox on the other side

Everything above is at-least-once. A consumer will eventually see the same message twice. The inbox is how
you make that harmless. `SendToModules` uses it for you; this is what to do when a message arrives from
somewhere else.

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddDomainEventInbox(Database);
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
        await inbox.ExecuteOnceAsync<OrderPlacedV2>(message, "billing.invoicer", (order, received, token) =>
        {
            context.Invoices.Add(new Invoice(order.OrderId, order.Total));
            return Task.CompletedTask;
        },
        cancellationToken);

        // Acknowledge the message either way. A repeat is not an error.
    }
}
```

`ExecuteOnceAsync` returns `true` when your handler ran and `false` when this consumer had already applied
the message. Both are success.

What it does, in order:

1. Read the payload as the shape you asked for, applying any upcasters.
2. Look for a row keyed on this message and this consumer. If it exists, return `false` and do nothing.
3. Start a transaction, unless the caller already has one, in which case join it.
4. Add the inbox row.
5. Run your handler.
6. `SaveChanges`, which writes the handler's changes and the row together.
7. Commit, unless the transaction is the caller's.

Steps 4 to 7 are the whole point. The row that says "applied" and the effect of applying it are written by
one `SaveChanges` inside one transaction, so there is no instant where one exists without the other. A
crash anywhere in between rolls back both, and the next delivery applies the message cleanly.

The key is the pair, not the message. Two consumers of the same message each get a row and each run once,
so adding a consumer later does not mean replaying its backlog through the consumers that are already up
to date. Pick consumer names you will not want to change, the same way you pick `[DomainEventName]`.

There are overloads that hand you the raw envelope, or just the message id, when you would rather
deserialize yourself.

### What the inbox cannot do

It cannot undo work outside the database. If your handler sends a mail and the transaction then rolls back,
the row is gone but the mail is sent. The fix is the same shape as the outbox itself: write a row the
transaction owns, and let something else act on that row afterwards.

For the same reason it cannot stop two copies of a message from both running at the same moment. A
broker that delivers in parallel can hand two copies to two handlers before either has saved; both find
no inbox row and both run. One save then inserts the inbox row, and the other fails on it and rolls back
everything it wrote, so the database ends up with one effect. Anything the losing copy did outside that
transaction, a counter in memory or a call to another system, happened twice. Only what a handler does
through its module's context is exactly-once.

It also does not order anything. If message B arrives before message A, the inbox applies B. Handlers that
care about order have to say so themselves, usually with a version or a sequence number in the payload.

## The guarantees, honestly

- **At-least-once, end to end.** Every hop can repeat. The outbox marks a row processed only after delivery
  returned, a queue redelivers what was not archived, and a retry redelivers to sinks that already accepted
  the message. Nothing anywhere is exactly-once on the wire.
- **Two exceptions, both narrow.** With `DeliverInTransaction` and a sink that writes to the same database,
  the delivery and the mark commit together, so that one hop does not repeat. And the module sink's inbox
  rows mean a consumer that already applied a message is skipped rather than run again.
- **Idempotency is keyed on `MessageId`.** It is the domain event's `EventId`, stable across every
  redelivery, and it is what the inbox stores. If you write your own deduplication, key it on that and
  nothing else.
- **Ordering is best effort.** The processor loads oldest first, but a failed message is retried after
  messages written later, two processors can interleave, and a queue makes its own decisions. Do not build
  anything on delivery order.
- **Delivery is not immediate.** It happens on the next poll of the background service, not at commit.
- **Nothing is lost and nothing is published for a rolled back transaction.** That part the outbox does
  guarantee, because the rows are written by the same transaction as the aggregate.

## Tables, schema and migrations

The outbox and the inbox both live in a `ddd` schema by default, away from your domain tables. Plumbing is
easier to grant, purge and ignore when it is not mixed in with the business.

```csharp
modelBuilder.AddDomainEventOutbox(Database);                            // ddd.OutboxMessages
modelBuilder.AddDomainEventInbox(Database);                             // ddd.InboxMessages
modelBuilder.AddDomainEventInbox(Database, "Consumed", schema: "msg");  // msg.Consumed
modelBuilder.AddDomainEventInbox(Database, schema: null);               // the provider's default schema
```

SQLite has no schemas. Its provider drops the schema when it writes an identifier, so the tables are plain
`OutboxMessages` and `InboxMessages` there and everything works unchanged. Nothing to configure and nothing
to work around.

pgmq creates its own tables in its own `pgmq` schema. It is not part of your model and you do not migrate
it.

The inbox table is four columns:

| Column | Type | Meaning |
|---|---|---|
| `MessageId` | `Guid`, key | The message's id |
| `Consumer` | `string`, key, 256 | Who applied it |
| `MessageName` | `string?`, 256 | The published name, for when you are reading the table by hand |
| `ProcessedAt` | `DateTimeOffset` | When the handler finished |

If you scaffold migrations there is nothing else to do. Both tables are part of your model as soon as the
calls are in `OnModelCreating`, so `dotnet ef migrations add` writes them, schema included.

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

They take the same `tableName` and `schema` you gave the model builder, so pass the same arguments in both
places. They create the schema first where the provider has schemas, and leave the schema off entirely
where it does not. The shapes are checked against the model in the tests, so a hand-written migration and a
scaffolded one produce the same table.

Upgrading an outbox table that predates the `Version` column: add it as a non-nullable `int` with a default
of 1, which is what every existing row was written as. A column added without a default reads as 0, and the
processor treats that as 1 for the same reason, so an upgrade that forgets the default still works.

## Where to look next

- [Domain events](domain-events.md) for raising, draining and `[DomainEventName]`.
- [Entity Framework](entity-framework.md#domain-event-delivery) for the outbox itself, the processor,
  retries and `MaxAttempts`.
- [GraphQL](graphql.md#pushing-integration-events-to-subscribers) for the subscription sink.
- `Tests/DDDToolkit.EntityFramework.Tests/ModuleIntegrationEventTests.cs` for the module sink and the
  per-consumer retry behaviour.
- `Tests/DDDToolkit.EntityFramework.Tests/PgmqSinkTests.cs` for the transactional enqueue, against a real
  Postgres.
- `Tests/DDDToolkit.EntityFramework.Tests/EventVersioningTests.cs` for upcasting on both read paths.
- `Tests/DDDToolkit.EntityFramework.Tests/OutboxTransactionTests.cs` for `DeliverInTransaction`.
- `Tests/DDDToolkit.EntityFramework.Tests/IntegrationEventTests.cs` for the sink contract and the
  multi-sink failure behaviour.
- `Tests/DDDToolkit.EntityFramework.Tests/InboxTests.cs` for the crash between handling and marking, and
  for the outbox and inbox working together.
- `Tests/DDDToolkit.EntityFramework.Tests/MessagingSchemaTests.cs` for the schema override and the
  migration helpers.
