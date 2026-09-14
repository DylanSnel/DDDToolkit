# Entity Framework

`DDDToolkit.EntityFramework` is the persistence half of the toolkit. It teaches Entity Framework Core
about the types the generators produce, so a domain model written the way the other pages describe
maps to tables without hand-written configuration. It also carries the two behaviours that need a
save to hang off: domain event delivery and optimistic concurrency.

The package does four things:

| | What it gives you |
|---|---|
| Generated converters | A `ValueConverter` per identifier and single value object, and one registration call per assembly |
| Conventions | Read-only collections mapped, `Version` made a concurrency token, `[Internal]` members ignored |
| Domain event delivery | In-process dispatch during `SaveChanges`, or a transactional outbox |
| Optimistic concurrency | `Version` incremented per save, stale writes turned into `ConcurrencyConflictException` |

It does not give you a repository abstraction or a message bus. `DbContext` is already the unit of
work, and the delegate that hands events to your publisher is one you write. If your publisher is
[Mediator](https://github.com/martinothamar/Mediator), the companion package `DDDToolkit.Mediator`
writes that delegate for you; see [In-process dispatch](#in-process-dispatch). If the events have to
leave the process, the outbox delivers to a sink you implement; see
[Integration events](integration-events.md).

## Install

```bash
dotnet add package DDDToolkit.EntityFramework
```

The package brings its own source generator, so referencing it is all the configuration there is. It
targets .NET 10 and Entity Framework Core 10.

## Wiring it up

Three calls. One in your service registration, one on the `DbContextOptionsBuilder`, and one or more
in `ConfigureConventions`. `DispatchWithMediator()` is the short way to hand domain events to
[Mediator](https://github.com/martinothamar/Mediator); it lives in a separate package and is
optional, because delivery is a delegate you can write yourself. See
[Domain event delivery](#domain-event-delivery) below for both.

```csharp
using DDDToolkit.EntityFramework;
using DDDToolkit.Mediator;

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

builder.Services.AddDbContext<OrderingContext>((services, options) => options
    .UseSqlite(connectionString)
    .UseDDDToolkit(services));
```

```csharp
using DDDToolkit.EntityFramework.Conventions;
using Ordering.Domain.Converters;

public class OrderingContext(DbContextOptions<OrderingContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddOrderingConverters();
    }
}
```

### `AddDDDToolkitEntityFramework`

Registers the delivery configuration and the two interceptors. The options object is a singleton,
`PublishDomainEventsInterceptor` is scoped so that it can hand handlers the scope that owns the
`DbContext` being saved, and `AggregateVersionInterceptor` is a singleton because it holds no state.

The argument configures delivery. See [Domain event delivery](#domain-event-delivery) below. If your
aggregates never raise events you can call it with no argument at all, but you still need it, because
it is what registers the interceptors.

### `UseDDDToolkit`

Adds both interceptors to the context, domain events first and concurrency second. Pass the
`IServiceProvider` that the `AddDbContext` callback gives you, not the root provider. That provider
belongs to the same scope as the context, so a handler that injects `OrderingContext` receives the
very instance that is saving.

### `AddDDDToolkitConventions` and `Add{Module}Converters`

`AddDDDToolkitConventions` adds the three toolkit conventions to the model. It is the same for every
context, so it takes no arguments.

`Add{Module}Converters` is generated, one per assembly that declares identifiers or single value
objects. The name comes from the `DDD_Module` property described in
[Getting started](getting-started.md#name-your-module), so a project with
`<DDD_Module>Ordering</DDD_Module>` gets `AddOrderingConverters`. It lives in a `Converters`
namespace under the assembly name, in a static class called `ConverterExtensions`.

Call one per assembly. A solution with a shared kernel and two modules calls three:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.AddDDDToolkitConventions();
    configurationBuilder.AddSharedKernelConverters();
    configurationBuilder.AddOrderingConverters();
    configurationBuilder.AddBillingConverters();
}
```

An assembly that declares no identifier and no single value object produces no method, because there
would be nothing for it to register.

## What is generated and what is a convention

The split is not arbitrary. A value converter is specific to one type, so it has to be written per
type and registered per assembly, which is work for a generator. A convention is a rule about the
shape of a model, identical in every context, so it lives in the runtime package.

The generator emits, for each type:

| You wrote | Generated |
|---|---|
| `[EntityId<T>]` | A nested `ValueConverter`, for example `OrderId.OrderIdConverter`, storing `Value` |
| `[SingleValueObject<T>]` | The same, plus a converter for the always-valid twin |
| `[ValueObject]` | `[ComplexType]` on the record and on its twin |
| `[Entity<TId>]` | `[Owned]` on the class |

and once per assembly, the registration method, which registers every converter twice:

```csharp
modelConfigurationBuilder.Properties<OrderId>().HaveConversion<OrderId.OrderIdConverter>();
modelConfigurationBuilder.DefaultTypeMapping<OrderId>().HasConversion<OrderId.OrderIdConverter>();
```

Both lines are needed and neither covers the other's ground. `Properties<T>()` configures properties
of that type, which is most of what a model contains. `DefaultTypeMapping<T>()` configures the type
where no property is involved: query parameters, constants, and the element type of a primitive
collection. The read-only collection convention reads that default type mapping to find the converter
for the elements of a generated `IReadOnlyList<T>`, because Entity Framework does not carry property
configuration down to collection elements.

`ColumnLength` on an identifier or single value object becomes `HaveMaxLength` on the property
registration and `HasMaxLength` on the type mapping. See [Identifiers](identifiers.md#column-length).

The conventions, added by `AddDDDToolkitConventions`, are:

| Convention | What it does |
|---|---|
| `InternalMemberConvention` | Ignores every member carrying `[Internal]`, on entity types and complex types alike |
| `ReadOnlyCollectionConvention` | Maps generated get-only collections of primitives and converted types as primitive collections, applying the element converter |
| `AggregateRootVersionConvention` | Makes `Version` on every aggregate root a concurrency token |

`InternalMemberConvention` is why `DomainEvents` never reaches your tables. It is marked `[Internal]`
by the generator, so the convention ignores it exactly as `[NotMapped]` would.

Explicit configuration in `OnModelCreating` still wins over any of this. The conventions fill in what
you did not say.

## Mapping

Everything in this section works with no `OnModelCreating` code at all. The mapping tests in
`Tests/DDDToolkit.EntityFramework.Tests/MappingTests.cs` cover each case against SQLite.

### Identifiers

A struct identifier works as a primary key, as an ordinary property, and as a nullable property. The
converter stores the underlying value, so the column is a `Guid`, an `int` or a `string`, not a
serialized object, and the identifier is usable in a query predicate:

```csharp
var order = context.Orders.Single(o => o.Id == orderId);          // the key
var theirs = context.Orders.Count(o => o.Customer == customerId); // a plain property
var unassigned = context.Orders.Count(o => o.Courier == null);    // a nullable property
```

Class identifiers, declared as `partial record` rather than `readonly partial record struct`, map the
same way and get a converter for their always-valid twin as well.

### Value objects

A `[ValueObject]` record is annotated `[ComplexType]`, so its properties are stored inline in the
owning table. A `PersonName` with `FirstName` and `LastName` becomes `Name_FirstName` and
`Name_LastName` columns on the owner, not a table of its own.

A `[SingleValueObject<T>]` is a converted scalar, so it becomes one column, and a nullable one stores
and reads `null`. See [Value objects](value-objects.md#entity-framework).

### Child entities and owned collections

`[Entity<TId>]` produces `[Owned]`, so a child entity is loaded and saved with its aggregate. A
generated `partial IReadOnlyList<OrderLine> Lines { get; }` is discovered as an owned collection: the
`[BackingField]` annotation on the generated property points Entity Framework at the private list, so
it reads and writes the field and never tries to write through the read-only view.

Removing an element removes the row. Callers still cannot cast `Lines` back to `List<OrderLine>` and
mutate it.

### Read-only collections of primitives

A generated `IReadOnlyList<T>` where `T` is a primitive, a converted identifier or a single value
object is mapped as an Entity Framework primitive collection, which on a relational provider is a
JSON column. The elements are stored as their converted values:

```csharp
[EntityId<int>("TAG")]
public readonly partial record struct TagId;

public partial IReadOnlyList<TagId> Tags { get; }
```

Two tags produce the column value `[1,2]`, not a pair of objects.

This needs the convention. Entity Framework discovers primitive properties only when they have a
setter, and a generated collection property is get-only, so without `AddDDDToolkitConventions` the
property would be skipped silently and the data would never reach the database. The convention finds
it, sets the element type, and copies the converter, max length, unicode, precision and scale from
the type mapping the generated registration installed.

The convention only considers public get-only properties. A `protected partial IReadOnlyList<T>` is
not mapped as a primitive collection.

"A JSON column" is true on SQL Server and SQLite and not on PostgreSQL, which stores the same
collection as a native array. LINQ queries port across that difference and hand-written SQL does not.
See [Primitive collections do not port below LINQ](#primitive-collections-do-not-port-below-linq)
before you write a report against one of these columns.

### What is not mappable

Entity Framework Core 10 accepts only arrays and `IList<T>` implementations as primitive collections.
A generated `IReadOnlySet<T>` is backed by a `HashSet<T>`, so a set of primitives cannot be a
primitive collection.

Rather than leave the property silently unmapped, `ReadOnlyCollectionConvention` throws while the
model is built. The message names the type and property, says that the backing field is a `HashSet`
and that only arrays and `IList<T>` qualify, and tells you to declare `IReadOnlyList<T>` instead or
exclude the property with `[NotMapped]` or `Ignore()`. You see it on first use of the context, not at
save time.

```csharp
public partial IReadOnlySet<string> Keywords { get; }   // throws while building the model
public partial IReadOnlyList<string> Keywords { get; }  // maps
```

An `IReadOnlySet<T>` of entities is fine. That is a navigation, not a primitive collection, so
relationship discovery handles it and the children round-trip with their aggregate.

## Domain event delivery

Raising an event puts it on the aggregate. Getting it to a handler is the part with a real trade-off,
and the integration offers two modes. Configure exactly one for production.

Both modes deliver through the same delegate, so handlers are written once:

```csharp
Func<IServiceProvider, IReadOnlyList<IDomainEvent>, CancellationToken, Task>
```

The provider is the scope that owns the saving `DbContext`, and the events arrive in the order they
were raised.

Both modes keep the event inside this process. To send it somewhere else, add a sink to the outbox:
that is a third destination and a separate page, [Integration events](integration-events.md).

### In-process dispatch

With `DispatchInProcess` and no outbox, handlers run inside `SaveChanges`, before the database is
written.

#### The short way: `DispatchWithMediator()`

`DDDToolkit.Mediator` writes that delegate for you, against
[Mediator](https://github.com/martinothamar/Mediator):

```bash
dotnet add package DDDToolkit.Mediator
```

```csharp
using DDDToolkit.Mediator;

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());
```

It resolves `IPublisher` from the scope that owns the saving `DbContext` and publishes each event in
the order it was raised, awaiting one before starting the next. That is the delegate below plus a
check that each event is publishable at all, so it serves both delivery modes: add `UseOutbox` and the
processor delivers through the same call.

Three things are worth knowing before you reach for it.

**Your events have to implement `Mediator.INotification`.** Mediator cannot publish anything else. One
marker interface for the whole solution is the usual way to say it once:

```csharp
public interface IOrderingEvent : IDomainEvent, INotification;
```

An event that does not implement it makes the dispatch throw, naming the event type. It is not
skipped. By the time the delegate runs the interceptor has already dequeued the event from the
aggregate, so skipping would destroy it with no row, no log and nothing to retry.

**Register Mediator as scoped when handlers touch the `DbContext`.** Mediator registers every handler
as a singleton by default, and a singleton cannot depend on a scoped service. The lifetime is read off
your `AddMediator` call at compile time, so it has to be written there; setting it any other way
throws at start-up.

**Keep `Mediator.SourceGenerator` in your composition root.** Mediator generates its implementation
and `AddMediator` into whichever assembly the generator runs in, so reference the generator from the
project that builds the container and from nowhere else. Handlers in other projects are still
discovered, as long as those projects are referenced. `DDDToolkit.Mediator` itself references only
`Mediator.Abstractions`, so it adds no generator to your domain projects.

Mediator also reports `MSG0005` at build time for a notification that no handler handles. That is
usually the mistake it looks like, but an event you deliberately leave unhandled needs the warning
suppressed.

#### Why Mediator and not MediatR

MediatR did this job for the first two major versions of the toolkit and does it well. From version 13
it is commercially licensed. This repository prefers dependencies its users can take for free, so the
examples and this package target Mediator, which is MIT and source generated rather than reflection
based. Nothing here stops you using MediatR: write the delegate below and it publishes through
`IPublisher` exactly as it always did.

#### The delegate

`DispatchWithMediator()` is a convenience. The toolkit core has no mediator dependency and is not
getting one: in-process delivery is a delegate, and you can write it against any library or none.

```csharp
builder.Services.AddDDDToolkitEntityFramework(options =>
{
    options.DispatchInProcess(async (services, events, cancellationToken) =>
    {
        var publisher = services.GetRequiredService<IPublisher>();
        foreach (var domainEvent in events)
        {
            await publisher.Publish(domainEvent, cancellationToken);
        }
    });
});
```

What you get, either way:

- Anything a handler changes on the same `DbContext` rides the same save, and therefore the same
  transaction. The interceptor calls `DetectChanges` after each round so those changes are seen.
- A throwing handler aborts the save. Nothing is written.
- New events raised by handlers on tracked aggregates are dispatched in a further round.

What you do not get is durability. Delivery is best-effort. The events are dequeued from the
aggregate before dispatch, so once a handler has them they exist only in memory: if the save then
fails, the events are gone. Nothing survives a process crash. And a handler that talks to the outside
world has already sent the mail or made the HTTP call by the time a later failure rolls the
transaction back.

Do not call `SaveChanges` from a handler in this mode. The save is already in progress and it will
pick your changes up.

### The outbox

With `UseOutbox`, nothing is dispatched at save time. Instead one row per event is added to the
saving context, so the events commit atomically with the aggregate, and a separate processor delivers
them afterwards through the same dispatch delegate.

```csharp
builder.Services.AddDDDToolkitEntityFramework(options =>
{
    options.DispatchWithMediator();   // or your own DispatchInProcess(...) delegate
    options.UseOutbox(outbox => outbox.RegisterEventsFromAssemblyContaining<Program>());
});

builder.Services.AddOutboxBackgroundService<OrderingContext>(TimeSpan.FromSeconds(2));
```

An event is never lost and never published for a transaction that rolled back. Rolling back the
aggregate rolls back its events, because they are rows in the same transaction.

The cost is at-least-once delivery. A message is marked processed only after its handlers returned,
so a crash in between redelivers it. Handlers must be idempotent, keyed on `EventId`.

Written this way the outbox is durable but still in-process: the processor hands the event back to the
same delegate. Add a sink and the row leaves the process instead:

```csharp
options.UseOutbox(outbox =>
{
    outbox.RegisterEventsFromAssemblyContaining<Program>();
    outbox.SendTo<ServiceBusSink>();
});
```

Sinks, the published contract that is not your domain event, and the inbox that makes at-least-once
delivery safe to consume are all on [Integration events](integration-events.md).

### Choosing

| | In-process | Outbox |
|---|---|---|
| When handlers run | Inside `SaveChanges`, before the write | After the commit, on the processor |
| Survives a crash | No | Yes |
| Handler failure | Aborts the save | Recorded on the row, retried |
| Delivery | Best-effort, at most once | At-least-once |
| Handlers must be idempotent | No | Yes |
| Extra table | No | Yes |
| Extra moving part | No | A processor or background service |

Use in-process dispatch for side effects inside the same database, where the transaction is the
guarantee you want. Use the outbox for anything that leaves the process, and give it a sink to leave
through.

Nothing stops you from mapping the outbox table from the start and switching later. The example
context does exactly that, so moving from one mode to the other is a change in `Program.cs` with no
schema change.

### The dispatch loop

Both modes run through the same loop in `PublishDomainEventsInterceptor`, once per `SaveChanges`:

1. Dequeue the pending events of every tracked aggregate. If there are none, stop.
2. In outbox mode, add a row per event and go back to step 1.
3. Otherwise call the dispatch delegate with the whole batch, then call `DetectChanges`, then go back
   to step 1.

The loop exists because a handler may change a tracked aggregate, which may raise a further event.
`MaxDispatchRounds` caps it at 10 by default. If a further batch of events appears after that many
rounds, `SaveChanges` throws an `InvalidOperationException` naming the events still pending and
suggesting either a handler that triggers itself or a higher limit:

```csharp
options.MaxDispatchRounds = 20;
```

### When no delivery mode is configured

If an aggregate has pending events and neither `DispatchInProcess` nor `UseOutbox` was called,
`SaveChanges` throws rather than dropping the events. The message names the aggregate types involved
and both calls that would fix it. A save with no pending events needs no delivery mode, so a context
that only writes aggregates which raise nothing works out of the box.

### Prefer `SaveChangesAsync`

The dispatch delegate is asynchronous. Synchronous `SaveChanges` therefore blocks on it. That is safe
in console applications and in ASP.NET Core, which have no synchronization context, but it can
deadlock under a UI or legacy ASP.NET synchronization context. Both overloads are otherwise
identical: events are dispatched before the write either way.

## The outbox in detail

### The table

Map it in `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddDomainEventOutbox(Database);
}
```

`Database` is the context's own property. The method reads one thing from it, the provider name, which
is what lets the timestamp columns be the right shape for the database you are actually on. See
[Timestamps](#timestamps) below.

The table is `ddd.OutboxMessages` by default. The method takes `tableName` and `schema` if you want
something else, and `schema: null` puts it in the provider's default schema. SQLite has no schemas and
ignores the argument, so the table is plain `OutboxMessages` there. The mapping sets a primary key on
`Id`, an index on `ProcessedAt`, and the lengths below.

| Column | Type | Meaning |
|---|---|---|
| `Id` | `Guid`, key, never generated | The event's `EventId`. This is the idempotency key |
| `EventName` | `string`, required, 256 | The stable name, from `[DomainEventName]` or the class name |
| `Payload` | `string`, required | The event serialized with System.Text.Json |
| `OccurredAt` | `DateTimeOffset` | Taken from the event |
| `AggregateType` | `string?`, 512 | CLR type name of the aggregate that raised it |
| `AggregateId` | `string?`, 256 | The aggregate's key as text, parts joined with `\|` |
| `CreatedAt` | `DateTimeOffset` | When the row was written |
| `ProcessedAt` | `DateTimeOffset?` | When the handlers succeeded, `null` while pending |
| `Attempts` | `int` | How often delivery was attempted |
| `LastError` | `string?`, 4000 | Type and message of the last failure |

`CreatedAt` comes from `options.TimeProvider`, which defaults to `TimeProvider.System` and can be
replaced in tests.

Payloads are written with System.Text.Json through `outbox.JsonOptions`, which by default are
case-insensitive on read and carry the toolkit's `SingleValueObjectConverterFactory`, so identifiers
and single value objects are stored as their raw values rather than as objects. The same options read
the payload back, so change them with care once messages exist.

### Timestamps

`OccurredAt`, `CreatedAt` and `ProcessedAt` are `DateTimeOffset` in the model. What they become in the
database depends on the provider, and you can say otherwise:

```csharp
// The provider's own instant type.
modelBuilder.AddDomainEventOutbox(Database);

// A UTC DateTime column, on every provider.
modelBuilder.AddDomainEventOutbox(Database, timestamps: DomainEventTimestamps.UtcDateTime);
```

| Provider | `ProviderDefault` | `UtcDateTime` |
|---|---|---|
| PostgreSQL | `timestamp with time zone` | `timestamp with time zone` |
| SQL Server | `datetimeoffset` | `datetime2` |
| SQLite | UTC `DateTime` as text | UTC `DateTime` as text |

The one thing the outbox needs from these columns is that they sort as instants, because the processor
reads pending messages oldest first. SQLite stores a `DateTimeOffset` as text and refuses to order by
one, so on SQLite the toolkit converts to a UTC `DateTime` whatever you ask for. Every other provider
has an instant type that orders correctly, and now gets it.

Whichever column it lands in, the value is normalized to UTC on the way in. Write a `DateTimeOffset`
that carries `+02:00` and the row holds the same instant with an offset of zero, and reads back that
way. That makes the two shapes interchangeable in meaning, and it is also what keeps Npgsql happy:
PostgreSQL refuses a `DateTimeOffset` whose offset is not zero.

`AddDomainEventInbox` takes the same argument for its own `ProcessedAt`. Pass both the same thing.

If you write migrations by hand, `CreateDomainEventOutbox` and `CreateDomainEventInbox` take
`timestamps` too and default to the same `ProviderDefault`. They read `MigrationBuilder.ActiveProvider`,
so the hand-written table and the scaffolded one agree.

#### If you already have a database

This changed in 3.0 during development, so it can affect a database created by an earlier 3.0 build.
Read this before upgrading one.

**On PostgreSQL, nothing happens.** Npgsql maps both a UTC `DateTime` and a `DateTimeOffset` to
`timestamptz`. The column is the same column and the bytes in it are the same bytes. There is no
migration to write.

**On SQL Server the column type changes**, from `datetime2` to `datetimeoffset`. Scaffolding a
migration after upgrading produces an `ALTER COLUMN` for each timestamp column of the outbox and
the inbox. The stored
instant is preserved: SQL Server reads an existing `datetime2` as the same time at `+00:00`, which is
correct because every value the outbox ever wrote was already UTC. So the meaning of a row does not
change, but the table does, and you have to run the migration.

If you would rather not, keep the old shape explicitly:

```csharp
modelBuilder.AddDomainEventOutbox(Database, timestamps: DomainEventTimestamps.UtcDateTime);
modelBuilder.AddDomainEventInbox(Database, timestamps: DomainEventTimestamps.UtcDateTime);
```

That produces exactly the columns you have, on every provider, and no migration at all.

If you upgrade the code on SQL Server and do neither of those things, **reading the outbox throws**.
The insert still works, because SQL Server converts the parameter into the column it has, and the
instant it stores is correct. The read does not: `datetime2` comes back from the driver as a
`DateTime`, the model wants a `DateTimeOffset`, and you get an `InvalidCastException` on the first row.

That is the good outcome, and it is why this is safe to ship. An unmigrated database says so the first
time the processor polls, rather than quietly disagreeing with the model. Both halves are asserted
against a real SQL Server in
`Tests/DDDToolkit.EntityFramework.Providers.Tests/Providers/ProviderMappingTests.cs`.

Rows written before you notice are fine. The value that reached the column was already the right
instant, so running the migration afterwards needs no data repair.

**On SQLite, nothing happens**, because SQLite never had a choice.

### Registering event types

The row stores a name, so the processor needs a name-to-type map:

```csharp
options.UseOutbox(outbox =>
{
    outbox.RegisterEventsFromAssemblyContaining<Program>();
    outbox.RegisterEventsFromAssembly(typeof(OrderPlaced).Assembly);
    outbox.RegisterEvent<OrderCancelled>();
});
```

The three calls are additive, so use whichever suits; most applications need only the first. The
assembly scans register every concrete type implementing `IDomainEvent`. Registering two types
under the same stable name throws an `ArgumentException` naming both, which is the failure you want
at start-up rather than at delivery time.

This is where `[DomainEventName]` earns its keep. The name is written into the row, so a class rename
without the attribute orphans every row already stored under the old name. With the attribute the
wire name is pinned and the class can move or be renamed freely. See
[Domain events](domain-events.md#stable-names).

An event name nobody registered is not fatal. The processor records the failure on the row, with a
`LastError` that names the missing event and the registration call, increments `Attempts`, leaves
`ProcessedAt` null and carries on with the rest of the batch. Register the type and the next run
delivers the row.

### Running the processor

For a hosted application, the background service:

```csharp
builder.Services.AddOutboxBackgroundService<OrderingContext>(
    pollingInterval: TimeSpan.FromSeconds(2),
    batchSize: 100);
```

It registers the processor, the polling options and the hosted service. On each tick it drains the
outbox batch after batch until a batch delivers nothing, each batch in its own service scope so the
processor and the handlers get a fresh context. A failure in a tick is logged and the next tick tries
again.

To drive it yourself, from a job scheduler or a test, register the processor alone:

```csharp
builder.Services.AddOutboxProcessor<OrderingContext>();
```

```csharp
var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor<OrderingContext>>();
var delivered = await processor.ProcessPendingAsync(batchSize: 100, cancellationToken);
```

`ProcessPendingAsync` loads pending messages oldest first, by `CreatedAt`, then `OccurredAt`, then
`Id`, delivers each one on its own, and returns how many were delivered successfully. The processor
throws at construction if the outbox is not enabled, or if it has neither a sink nor a dispatch
delegate to deliver through.

Each message is saved through the same scoped context, so a handler that resolves that context and
changes an aggregate commits its change together with the processed mark. Events raised by that
change become new outbox rows.

### Failures, retries and poison messages

A handler that throws does not stop the batch. The exception type and message are written to
`LastError`, truncated at 4000 characters, `Attempts` is incremented, `ProcessedAt` stays null, and
the processor moves to the next message. A later run retries it, and on success `LastError` is
cleared.

`MaxAttempts` defaults to 10. Messages that reached it are no longer loaded:

```csharp
options.UseOutbox(outbox => outbox.MaxAttempts = 5);
```

They stay in the table with their last error for you to inspect. Reset `Attempts` to retry one.

Note that a batch in which every message fails returns zero, which ends the background service's
drain for that tick. The next tick picks the messages up again.

### Honest limits

- **Ordering is best-effort.** Messages are loaded oldest first, but a failed message is retried
  later than messages written after it, so delivery order is not the order of writing.
- **Concurrent processors can double-deliver.** The processor takes no lock, so two instances polling
  the same table may both pick up the same row. Combined with the crash window before
  `ProcessedAt` is written, that is the at-least-once guarantee: handlers must be idempotent, keyed
  on `EventId`, which is `OutboxMessage.Id`.
- **Delivery is not immediate.** It happens on the next poll, not at commit.

- **Two sinks share one row.** When a message goes to more than one sink and one of them refuses, the
  message as a whole is retried and the sinks that accepted it see it again. See
  [Integration events](integration-events.md#when-one-sink-fails-and-another-does-not).

`Tests/DDDToolkit.EntityFramework.Tests/OutboxTests.cs` exercises the transactional write, the
retries, `MaxAttempts`, unknown event names and the background service.

## Providers

Everything on this page is tested on SQLite, PostgreSQL and SQL Server. SQLite is where the fast suite
runs; the other two run in containers, in their own CI workflow, against the same aggregates and the
same context. Most of the mapping is identical on all three. This section is the part that is not, so
that none of it is a surprise in production.

### What differs, and what to do about it

| | SQLite | PostgreSQL | SQL Server |
|---|---|---|---|
| `Guid` identifier | `TEXT` | `uuid` | `uniqueidentifier` |
| Outbox timestamps | UTC `DateTime` | `timestamptz` | `datetimeoffset` |
| Primitive collection | JSON `TEXT` | `integer[]` | JSON `nvarchar` |
| Schemas | ignored | yes | yes |

Timestamps are covered above. Schemas are ignored by SQLite, which drops the schema when it writes an
identifier, so `ddd.OutboxMessages` is plain `OutboxMessages` there and nothing else changes. The
primitive collection is the one that needs a paragraph.

### Primitive collections do not port below LINQ

A generated `IReadOnlyList<TagId>` is mapped as an Entity Framework primitive collection. What that
becomes in the database is the provider's decision, not the toolkit's, and the providers decide
differently:

- **PostgreSQL** stores it as a real `integer[]`. Npgsql maps .NET collections of a primitive onto
  PostgreSQL's own array types, which is the better mapping and the reason it does it.
- **SQL Server and SQLite** store it as a JSON document in a string column, `[1,2]`.

**This is inherent to Entity Framework Core, not something the toolkit chose or can sensibly undo.**
The mapping is supplied by each provider's type mapping source. The toolkit's convention only finds
the get-only property, sets its element type and copies the element converter across; the store type
is chosen after that, by the provider, exactly as it would be for a hand-written
`modelBuilder.PrimitiveCollection(...)`. Forcing every provider onto the same store type would mean
overriding Npgsql's native array mapping with a worse one for the sake of a portability nobody asked
for, and would break every PostgreSQL index and query already written against the array.

So the practical rule is:

**LINQ ports.** `Contains`, `Count` and the rest translate on both providers, to `= ANY(...)` on
PostgreSQL and to `OPENJSON` on SQL Server. If your query goes through `IQueryable`, you can move
providers and it keeps working.

```csharp
// Translates on both.
var tagged = await context.Shelves
    .SelectMany(shelf => shelf.Books)
    .Where(book => book.Tags.Contains(new TagId(7)))
    .ToListAsync();
```

**SQL does not port.** Anything that reaches inside the column in hand-written SQL is provider
specific, and there is no spelling that runs on both:

```sql
-- PostgreSQL
SELECT COUNT(*) FROM "Book" WHERE 7 = ANY("Tags");

-- SQL Server
SELECT COUNT(*) FROM "Book" WHERE EXISTS (SELECT 1 FROM OPENJSON("Tags") WHERE CAST([value] AS int) = 7);
```

That matters for reports, ad hoc queries, data migrations and anything a DBA writes. Indexing differs
too: a PostgreSQL array takes a GIN index, a JSON string column does not.

If you need a query like that to port, the answer is not to fight the mapping. Model the collection as
a child entity with its own table, which is one row per tag and identical on every provider. You lose
the single-column read and gain a join. That is a design decision, so make it because you need the
portability, not by accident.

Both halves are asserted against real servers in
`Tests/DDDToolkit.EntityFramework.Providers.Tests/Providers/ProviderMappingTests.cs`.

### Running the provider tests yourself

```bash
dotnet test Tests/DDDToolkit.EntityFramework.Providers.Tests --filter "Provider=Postgres"
dotnet test Tests/DDDToolkit.EntityFramework.Providers.Tests --filter "Provider=SqlServer"
```

They need Docker. Without it they skip themselves, naming what could not be started, because a machine
without Docker is a normal machine and should not go red.

That skip is a problem in CI, where a green run would then say PostgreSQL and SQL Server pass without
either having started. Setting `DDDTOOLKIT_REQUIRE_CONTAINERS=1` turns every such skip into a failure
that says what was missing. Both workflows set it, and the provider workflow also counts the tests that
ran, so a filter that matched nothing cannot pass either.

The images are pinned exactly, in
`Tests/DDDToolkit.EntityFramework.Providers.Tests/Infrastructure/ContainerImages.cs`, with the reason
for each next to it. No floating tag, so a red build is always something we changed. PostgreSQL matches
the major of the pgmq image the Postgres package is tested against; SQL Server is the older release
still in mainstream support, which is the weaker of the two and therefore the one worth testing.

## Optimistic concurrency

Every aggregate root carries `long Version`. It is 0 on a new instance, becomes 1 when the aggregate
is inserted, and increases by exactly one per save that touches the aggregate. `AddDDDToolkitConventions`
maps it as a concurrency token, so the `UPDATE` carries the loaded version in its `WHERE` clause.

"Touches the aggregate" includes more than the root's own properties. `AggregateVersionInterceptor`
walks from each changed entry to the aggregate root it belongs to, through ownership or through a
foreign key whose principal is an aggregate root, and bumps that root. A change to an owned child, an
element added to or removed from an owned collection, or a change to a primitive collection on a
child, all version the root. Each root is bumped at most once per save, however many entries changed,
and a save that changes nothing does not bump anything.

Deleting an aggregate does not bump its version, but the token is still checked, so deleting an
aggregate somebody else has changed in the meantime conflicts like any other stale write.

The version is what makes the aggregate a unit of consistency: two people editing different lines of
the same order still conflict, which is the point.

When a save writes a stale version, you get a `ConcurrencyConflictException` naming the aggregate:

```csharp
try
{
    await context.SaveChangesAsync(cancellationToken);
}
catch (ConcurrencyConflictException conflict)
{
    // conflict.AggregateType is typeof(Order), conflict.AggregateId is the OrderId.
    // Reload the aggregate, reapply the change and save again, or report it to the user.
    return Conflict(conflict.Message);
}
```

There is no safe generic answer for the catch block, which is why the toolkit does not retry for you.
Reloading and reapplying is right when the change is a command you can repeat, such as adding a line.
Reporting the conflict is right when the user needs to see what changed underneath them. Blindly
retrying a computed value is wrong.

`conflict.InnerException` is the original `DbUpdateConcurrencyException` if you need the entries.

### Why the exception is rethrown from `SaveChangesFailed`

The interceptor translates the conflict in `ThrowingConcurrencyException`, but the update pipeline
wraps whatever that throws in a `DbUpdateException`, so callers would have to dig through inner
exceptions to find it. The interceptor therefore also handles `SaveChangesFailed`, unwraps the
`ConcurrencyConflictException` and rethrows it, which is what lets you write
`catch (ConcurrencyConflictException)` directly. It does the same for a provider that raised the
conflict without passing through `ThrowingConcurrencyException`.

Both the synchronous and the asynchronous save path behave the same.
`Tests/DDDToolkit.EntityFramework.Tests/ConcurrencyTests.cs` covers the version arithmetic, child-only
changes, deletes and both paths.

## Migrations

There is nothing special to do. The toolkit adds types and configuration to your own `DbContext`
model, so everything it maps appears in your ordinary migrations:

```bash
dotnet ef migrations add AddOutbox
dotnet ef database update
```

The outbox table is part of the model as soon as `AddDomainEventOutbox(Database)` is in `OnModelCreating`, so
the next migration you scaffold contains it, `ddd` schema and all. Opting in is that one call. There
is no separate package, no separate migration history table and no separate command. The same goes for
`AddDomainEventInbox(Database)` on the consuming side.

If you write migrations by hand rather than scaffolding them, `migrationBuilder.CreateDomainEventOutbox()`
and its inbox and drop counterparts write the same tables. See
[Integration events](integration-events.md#tables-schema-and-migrations).

The same applies to everything else on this page. Struct identifier columns, complex type columns,
primitive collection columns and the `Version` column are all just columns in your model, so a
`migrations add` after changing an aggregate produces the diff you would expect.

If you map the outbox table before you need it, as the example context does, switching from
in-process dispatch to the outbox later costs no migration at all.

## Where to look next

- [Getting started](getting-started.md) for the module name and your first aggregate.
- [Identifiers](identifiers.md) for what a struct identifier actually is, and `ColumnLength`.
- [Value objects](value-objects.md) for `[ValueObject]` and `[SingleValueObject<T>]`.
- [Entities and aggregates](entities-and-aggregates.md) for read-only collections and `[BackingField]`.
- [Domain events](domain-events.md) for raising, draining and stable names.
- [Integration events](integration-events.md) for sinks, published contracts and the inbox.
- [Diagnostics](diagnostics.md) for the build errors the generators report.

The runnable version of everything here is `Examples/ModularMonolith`: the host's `Program.cs` shows
the registration and the outbox, `Ordering/DDDToolkit.Examples.Ordering/OrderingContext.cs` shows the
conventions and the generated converters, and the host's `Endpoints.cs` shows the conflict catch
block.
