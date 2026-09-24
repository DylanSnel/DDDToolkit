# Integration events

When something happens in one module, another module often has to react. Ordering places an order and
Billing raises an invoice for it. Billing cannot simply handle Ordering's domain event: that means
referencing Ordering's domain assembly, with its identifiers, its value objects and whatever else the
event touches, and from then on Ordering cannot change any of it without breaking Billing's build.

An integration event is what Ordering publishes instead: a small record, usually of primitives, owned by
Ordering, that Billing reads without knowing anything else about Ordering. This page is about declaring it, getting it
to whoever has to react, and consuming it safely at the other end. It starts where
[the outbox](event-delivery.md#the-outbox) stops: the outbox makes a domain event durable, and this is
the other half.

Most of the time the module that reacts is in the same process. That is the case this page is written
around, because it is the common one. When a module moves to a process of its own, only the way the
message travels changes; that is on [Transports](transports.md).

`DDDToolkit.EntityFramework` gives you four things. A seam between the event you raise and the message
you publish, so the two can change at different speeds. A sink interface, so the outbox has somewhere to
deliver to, with an in-process module sink ready made. An inbox, so a consumer can be handed the same
message twice without doing the work twice. And versioning, so a payload written by last year's build is
still readable by this year's.

It does not give you a bus. Between processes it hands its messages to pgmq, Wolverine or MassTransit, and
keeps only the outbox and the inbox on either side; see [Transports](transports.md).

## Two kinds of event

They look the same in C# and they are not the same thing.

| | Domain event | Integration event |
|---|---|---|
| Who reads it | Code inside one module | Other modules, other services, other teams |
| Who owns the shape | You, today | Everyone who deployed against it |
| Renaming a field | A refactor | A breaking change |
| Carries | Your identifiers, your value objects | Primitives, usually |
| Lifetime | As long as the aggregate | As long as the oldest consumer |

A domain event is internal. `OrderPlaced(OrderId, CustomerName, Money)` uses your types because the only
things that read it are in the same module. The moment something outside that module reads it, that
stops being true. Now the record is a published schema, and every field is a promise.

Note the boundary in the first row. It is the **module**, not the process. A record that only another
assembly in the same solution deserializes is already a published schema, because you cannot change it
without changing them.

That is why an integration event is part of the module's published contract: the short list of types
other modules may use, where everything else stays the module's own to change. `[IntegrationEvent]` is
enough to put a type on that list. Why a module publishes anything at all, what else belongs on the list,
and why the example shop keeps it in a `*.Contracts` project of its own is on
[Module contracts](module-contracts.md).

## Saying what gets published

The domain event is Ordering's own, written in Ordering's types:

```csharp
public sealed record OrderPlaced(OrderId OrderId, CustomerName Customer, Money Total) : DomainEvent;
```

The contract is what the other modules read, written in primitives:

```csharp
[IntegrationEvent("ordering.order-placed", Version = 2)]
public sealed record OrderPlacedV2(Guid OrderId, string Customer, decimal Total, string Currency);
```

`[IntegrationEvent]` pins the name and the version. Without it a contract falls back to
`[DomainEventName]`, and without that to the class name, with version 1. See
[Domain events](domain-events.md#stable-names) for why the name has to be pinned at all, and
[Versioning and upcasting](#versioning-and-upcasting) for what the version is for.

The outbox does not publish the contract until you say how one becomes the other. By default, nothing is
mapped and the domain event is published as it stands, under the name the outbox stored, reusing the JSON
already in the row. No second type, no second serialization, no configuration. If you are not ready to
split the two yet, you pay nothing for the seam being there.

When you are ready, register a conversion:

```csharp
options.UseOutbox<OrderingContext>(outbox =>
{
    outbox.RegisterEventsFromAssemblyContaining<Order>();

    outbox.PublishAs<OrderPlaced, OrderPlacedV2>(e => new OrderPlacedV2(
        e.OrderId.Value,
        e.Customer.Value,
        e.Total.Amount,
        e.Total.Currency));
});
```

Now `OrderPlaced` can grow a field, lose a field or be renamed, and the wire is untouched until you change
`OrderPlacedV2` on purpose. What the conversion makes is what every sink is handed; the sink for the other
modules in this process comes [below](#the-common-case-another-module-in-this-process).

Two more things that map:

```csharp
// This event is nobody else's business. Local handlers still get it; no sink is called.
outbox.DoNotPublish<CustomerCreditChecked>();

// Publish only some occurrences. Returning null drops that one message.
outbox.PublishAs<OrderPlaced, OrderPlacedV2>(e => e.Total.Amount < 1000 ? null : Map(e));
```

### A class per published event

A lambda is fine for one line. It stops being fine when a module publishes five events and its
registration turns into the place where every contract is assembled, and it cannot grow: it has no
services and it cannot await. The translation is application code, and it belongs next to the aggregate
it publishes for, not in the composition root.

So the same seam also takes a class, the outbound counterpart of the `IIntegrationEventHandler<TContract>`
a consuming module writes ([further down](#the-common-case-another-module-in-this-process)):

```csharp
public sealed class PublishOrderPlaced : IOutboundIntegrationEvent<OrderPlaced, OrderPlacedV2>
{
    public ValueTask<OrderPlacedV2?> CreateAsync(OrderPlaced placed, CancellationToken cancellationToken)
        => new(new OrderPlacedV2(placed.OrderId.Value, placed.Customer.Value, placed.Total.Amount, placed.Total.Currency));
}
```

```csharp
options.UseOutbox<OrderingContext>(outbox =>
{
    outbox.AddOrderingIntegrationEvents();   // generated: every domain event and every IOutboundIntegrationEvent in the module
});
```

`AddOrderingIntegrationEvents()` is written by the compiler when the module builds, and is named after the
module. It takes the place of both the `RegisterEventsFromAssemblyContaining` line and the `PublishAs`
lambda; [Registered when the module compiles](#registered-when-the-module-compiles) shows what is in it.

A class may implement the interface more than once to publish several events. Everything else is as for
`PublishAs`: returning `null` drops that occurrence, a domain event has one entry however it was registered
(a second one throws at start-up), and events with no entry are published as they stand.

The class is built with `new` once per message, each constructor parameter taken from the scope the outbox
processor runs in. Throwing from it fails the delivery the way a failing sink does: the error is recorded
on the row and the message is retried.

Name it for what it does, like a handler: `PublishOrderPlaced` next to `BookShipment` and `RecordPayment`.
Avoid "Publisher", because it sends nothing. The sink does that.

### Where the mapping happens

The outbox row always stores the domain event. The conversion runs at delivery, not at save.

That is a deliberate trade. It keeps the row a faithful record of what actually happened in the domain, it
means the in-process path and the sink path read the same row, and it means fixing a wrong mapping is a
deployment rather than a data migration: reset `Attempts` and the rows go out again in the new shape. The
cost is that the domain event type must still exist and still deserialize when the processor runs, which
is what [Versioning and upcasting](#versioning-and-upcasting) is about.

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
    outbox.AddOrderingIntegrationEvents();   // generated: its domain events and its outbound classes
    outbox.SendToModules();
}));
services.AddOutboxBackgroundService<OrderingContext>(TimeSpan.FromSeconds(2));
```

`SendToModules()` is the sink: it offers each message the outbox delivers to the modules in this process
that asked for it. The background service is the processor that reads the outbox rows after the commit
and hands them to the sink; see [Running the processor](event-delivery.md#running-the-processor).

A consuming module writes a handler for the contract:

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

`[IntegrationEventConsumer]` names the handler for the inbox, which remembers that `billing.invoicer` has
applied a message; [further down](#each-handler-has-its-own-inbox-row) is why the name matters. `message`
is the envelope the contract arrived in, with its id, its published name and when the thing happened;
[Transports](transports.md#why-an-envelope-and-not-the-event) lists what it carries.

The consuming module then says which contracts it reads and which handlers run under its inbox:

```csharp
// inside AddBillingModule
services.AddDDDToolkitEntityFramework(options =>
    options.MapIntegrationEvents(contracts => contracts.AddBillingIntegrationEvents()));   // generated
services.AddModuleIntegrationEvents<BillingContext>(module => module.AddBillingIntegrationEvents());   // generated
```

`MapIntegrationEvents` registers the contracts Billing reads, so that a delivered payload can be turned
back into an `OrderPlacedV2`. `AddModuleIntegrationEvents<BillingContext>` signs Billing up as a consumer:
its handlers, and the inbox in `BillingContext` that guards them. The `Add{Module}IntegrationEvents()`
methods are written by a source generator when the module compiles, so nothing is found, read or created
by reflection when it runs; see [Registered when the module compiles](#registered-when-the-module-compiles).

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

`IIntegrationEventHandler<OrderPlacedV2>`, never `IIntegrationEventHandler<OrderPlaced>`. A handler typed
on `OrderPlaced` forces the billing module to reference the ordering module's domain assembly, which means
`OrderId`, `Money`, and whatever else that record touches. Ordering can no longer rename a field without
breaking a build somewhere else, and the two modules are one module with extra folders.

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
    modelBuilder.AddDomainEventInbox(Database);
}
```

A module that publishes as well as consumes maps its outbox next to it, with
`modelBuilder.AddDomainEventOutbox(Database)`.

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

## Registered when the module compiles

`DDDToolkit.EntityFramework.Analyzers` writes a module's integration event registration as code, one
`Add{Module}IntegrationEvents()` for each of the three places that need it, in the namespace
`{assembly}.IntegrationEvents`. `{Module}` is the project's `<DDD_Module>`, or its assembly name without
the dots; [Store it with Entity Framework](getting-started.md#store-it-with-entity-framework) introduces
the setting.

Ordering, in the example on this page, declares one domain event, `OrderPlaced`, and one outbound class,
`PublishOrderPlaced`. Its build writes the registration for the outbox:

```csharp title="IntegrationEventExtensions.g.cs, shortened"
namespace Ordering.IntegrationEvents;

public static class IntegrationEventExtensions
{
    public static OutboxOptions AddOrderingIntegrationEvents(this OutboxOptions outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        outbox.RegisterEvent<OrderPlaced>("OrderPlaced", 1);
        outbox.PublishWith<OrderPlaced, OrderPlacedV2>("ordering.order-placed", 2, static services => new PublishOrderPlaced());
        return outbox;
    }

    // ... the two overloads for the contract registry and the module's handlers, which register
    // nothing: Ordering has no handler
}
```

Billing declares one handler, `RaiseInvoice`. Its build writes the other two:

```csharp title="IntegrationEventExtensions.g.cs, shortened"
namespace Billing.IntegrationEvents;

public static class IntegrationEventExtensions
{
    // ... the overload for the outbox, which registers nothing: Billing raises no domain event

    public static IntegrationEventContractRegistry AddBillingIntegrationEvents(this IntegrationEventContractRegistry contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        contracts.Register<OrderPlacedV2>("ordering.order-placed", 2);
        return contracts;
    }

    public static ModuleIntegrationEvents<TContext> AddBillingIntegrationEvents<TContext>(this ModuleIntegrationEvents<TContext> module) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(module);
        module.Handle<OrderPlacedV2, RaiseInvoice>("ordering.order-placed", "billing.invoicer", static services => new RaiseInvoice(ServiceProviderServiceExtensions.GetRequiredService<BillingContext>(services)));
        return module;
    }
}
```

Line by line, that is everything the two modules register:

- `RegisterEvent<OrderPlaced>("OrderPlaced", 1)` puts the domain event in the outbox's map from stored
  name to type, which the processor reads a row back through. The name is the event's
  `[DomainEventName]`, and `OrderPlaced` has none, so it is the class name; the version is its
  `[IntegrationEvent(Version = n)]`, otherwise 1. Every concrete domain event declared in the project is
  listed, whether it leaves the module or not, because the outbox stores all of them.
- `PublishWith<OrderPlaced, OrderPlacedV2>(...)` is the entry `PublishAs` would have made, with a class
  instead of a lambda: the contract's published name and version, read off its `[IntegrationEvent]`, and
  the code that builds `PublishOrderPlaced` for each message. The published name is also how this process
  knows it publishes `ordering.order-placed` itself, so a [transport](transports.md) does not ask a broker
  for it.
- `contracts.Register<OrderPlacedV2>("ordering.order-placed", 2)` tells the contract registry which type a
  payload of that name and version is read as. The module sink and the inbox read through it. Only the
  contracts this module's handlers take are listed, not the ones it publishes.
- `module.Handle<OrderPlacedV2, RaiseInvoice>(...)` does three things. It registers `RaiseInvoice` in the
  container as scoped, built by the lambda. It adds the handler to Billing's consumers under
  `billing.invoicer`, the name its inbox rows carry. And it records that this process handles
  `ordering.order-placed`, which is what a transport subscribes to.

`BillingContext` in that lambda comes from the handler's constructor. The constructor with the most
parameters is the one called, and each parameter is taken from the scope the message is delivered in:
`GetRequiredService` for an ordinary parameter, `GetService` for a reference type with a default value,
and the scope itself for an `IServiceProvider`.

Everything the run-time registration would read off attributes is read by the compiler instead and written
out as literals: the stored name and version of every domain event (`[DomainEventName]`,
`[IntegrationEvent]`), the published name and version of every contract, the consumer name of every
handler (`[IntegrationEventConsumer]`, otherwise the class's full name). The outbox and the processor then
look names up in the registries rather than asking a type. Nothing is scanned, read or activated by
reflection; add a handler class and the next build registers it.

A class the registration cannot build with `new` is left out and reported as a warning, DDD00033: one
with no constructor the module can call, one whose two longest constructors are equally long, one whose
parameter types the module cannot see, one with `ref`, `out` or `params` parameters. The reflection-based
registrations (`RegisterEventsFromAssemblyContaining`, `RegisterFromAssemblyContaining`,
`Handle<TContract, THandler>()`) work for code without the generator.

## Enriching a contract, and what not to read

Because a class can take services, it can add something the domain event does not carry, such as
reading a product's name from the module's own read model. Be careful what you read, because of when it
runs. The processor gets to the row later than the event happened, and a retry runs the class again. A
query there reads the state *now*, not the state when the event was raised. If one sink accepted the
first attempt and another failed, the retry can even hand the second sink a different payload under the
same message id, which the first consumer's inbox then ignores.

| What the contract needs | Where it comes from |
|---|---|
| Already in the domain event | Translate it. This is almost always the answer. |
| Stable or cosmetic data from this module (a name, a category) | A read from the module's own context in the class is fine. |
| Data that has to be right as of the event (a price, an address, a status) | **The domain event.** Add it there, not in the class. |
| Data owned by another module | Neither. Keep a read model of it in this module, fed by that module's events. |

The class reads and never writes. With
[`DeliverInTransaction`](transports.md#when-a-module-becomes-its-own-deployable-pgmq) a write would commit
together with the mark that says the message went out.

## There is one way out, and a "no" takes it too

An integration event is always a translation of a domain event, and a domain event only reaches the
outbox when an aggregate raised it: the save collects them from the tracked aggregates and writes them in
the same transaction as the change. That is the whole guarantee, and nothing publishes around it. A
domain service decides and an aggregate records the decision. A handler that decides calls a method on
an aggregate. Neither publishes anything itself, because a message sent outside the save can go out for
a change that rolled back, or be lost for one that committed.

That includes the answer no. "Not enough stock" or "payment declined" is an outcome other modules act on,
so it is recorded like any other. The shop's `StockReservation.Refused(...)` stores a refused reservation
and raises `StockRefused`, and `PublishStockRefused` translates it. Recording it also makes a retried
message harmless: the second attempt finds the decision already made instead of deciding again, and
perhaps differently.

Not every "no" is an outcome. On the way in, three cases look alike and are handled differently:

| The failure | Example | What to do |
|---|---|---|
| A rule of this module | Only euros accepted; a name is already taken | Record a refusal on an aggregate and publish it. |
| The contract carries something invalid | A negative total, an empty currency | The producer has a bug. Throw, with the reasons: the message is retried, stops at `MaxAttempts`, and waits in the table with its `LastError`. |
| A race behind a unique index | Two consumers create the same name at once | Let the `DbUpdateException` throw. On the retry the explicit check sees the other row and takes the refusal branch. |

For the second row, turn the contract into your value objects with `TryToValid` rather than
`ToValid()`, so the exception says which field was wrong instead of only that one was:

```csharp
if (!new Money(contract.Total, contract.Currency).TryToValid(out var total, out var errors))
{
    throw new InvalidOperationException($"{message.Name} {message.MessageId} carries an invalid total: {string.Join("; ", errors.Select(e => e.Message))}");
}
```

A synchronous caller is different: an invalid request changed nothing and nobody else needs to hear
about it, so it gets a `ValidationProblem` and nothing is published.

## Where the classes live

In the example shop each module keeps both directions next to the aggregate or read model they belong
to, split by kind and by direction:

```
Application/
    Orders/
        DomainEvents/                  OrderLog               (this module's own events, in process)
        IntegrationEvents/
            Inbound/                   RecordPayment, CancelWithoutStock, ...
            Outbound/                  PublishOrderPlaced, PublishOrderConfirmed, ...
    ReadModels/
        CatalogPrices/
            CatalogPrice.cs
            IntegrationEvents/
                Inbound/               RecordListedPrice, RecordChangedPrice
```

Every outbound class has an owner, because every domain event is raised by an aggregate. An inbound one
belongs to the aggregate or read model it changes. The overview of what a module promises the others is
its `*.Contracts` project; see [Module contracts](module-contracts.md#a-project-of-its-own).

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
agree. `RegisterEvent<TEvent>()` and `RegisterEventsFromAssembly` refuse two different names when they
register the event. The generated registration takes the name from one attribute and the version from the
other without comparing them, so there it is up to you to keep them the same; a mismatch shows only when
an older row cannot be read.

Keeping two types under one domain event name is fine. `RegisterEvent` and `RegisterEventsFromAssembly`
keep the newest as the type new events are written as, and the older one is only ever read.

A row whose shape nobody kept is not guessed at. The message fails, `LastError` names the version and the
call that would fix it, and the row waits for a human.

An outbox table created by an earlier 3.0 build may not have the `Version` column yet; see
[From an earlier 3.0 build](migrating-to-3.md#from-an-earlier-30-build).

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

## The guarantees, honestly

- **At-least-once, end to end.** Every hop can repeat. The outbox marks a row processed only after delivery
  returned, a queue redelivers what was not archived, and a retry redelivers to sinks that already accepted
  the message. Nothing anywhere is exactly-once on the wire.
- **Two exceptions, both narrow.** With
  [`DeliverInTransaction`](transports.md#when-a-module-becomes-its-own-deployable-pgmq) and a sink that
  writes to the same database, the delivery and the mark commit together, so that one hop does not repeat.
  And the module sink's inbox rows mean a consumer that already applied a message is skipped rather than
  run again.
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

[pgmq](transports.md#when-a-module-becomes-its-own-deployable-pgmq) creates its own tables in its own
`pgmq` schema. It is not part of your model and you do not migrate it.

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

## Where to look next

- [Transports](transports.md) for pgmq, Wolverine, MassTransit, and writing a sink of your own.
- [Module contracts](module-contracts.md) for why a module publishes contracts, and where to keep them.
- [Domain events](domain-events.md) for raising, draining and `[DomainEventName]`.
- [Delivering domain events](event-delivery.md) for the outbox itself, the processor, retries and
  `MaxAttempts`.
- [GraphQL](graphql.md#pushing-integration-events-to-subscribers) for the subscription sink.
- `Tests/DDDToolkit.EntityFramework.Tests/ModuleIntegrationEventTests.cs` for the module sink and the
  per-consumer retry behaviour.
- `Tests/DDDToolkit.EntityFramework.Tests/OutboundIntegrationEventTests.cs` for the outbound classes.
- `Tests/DDDToolkit.Analyzers.Tests/Integrations/IntegrationEventsGeneratorTests.cs` for the generated
  registration and DDD00033.
- `Tests/DDDToolkit.EntityFramework.Tests/EventVersioningTests.cs` for upcasting on both read paths.
- `Tests/DDDToolkit.EntityFramework.Tests/IntegrationEventTests.cs` for the sink contract and the
  multi-sink failure behaviour.
- `Tests/DDDToolkit.EntityFramework.Tests/InboxTests.cs` for the crash between handling and marking, and
  for the outbox and inbox working together.
- `Tests/DDDToolkit.EntityFramework.Tests/MessagingSchemaTests.cs` for the schema override and the
  migration helpers.
