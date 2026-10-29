# Migrating to 3.0

This page is for an upgrade from 2.0.22 to 3.0. It lists every breaking change, with the code you have now and the
code you need. The [changelog](../CHANGELOG.md) has the full list of changes. This page covers only the changes
that stop your solution from compiling, or that change how it behaves.

Plan an afternoon for a medium-sized solution. Most of the work is mechanical, and the compiler finds the event
changes. The compiler cannot find the change to when handlers run, in
[section 6](#6-events-are-dispatched-before-the-save-on-both-paths).

## Breaking changes

| Change | Section |
|---|---|
| `net8.0` is no longer supported | [1](#1-retarget-to-net-10) |
| Child entities no longer have domain events | [2](#2-domain-events-moved-to-the-aggregate-root) |
| `AddDomainEvent` is gone | [3](#3-adddomainevent-becomes-raisedomainevent) |
| The public `ClearDomainEvents` is gone | [4](#4-draining-goes-through-ihasdomainevents) |
| `IDomainEvent` has members now | [5](#5-idomainevent-carries-an-id-and-a-timestamp) |
| Handlers run before the save on both paths | [6](#6-events-are-dispatched-before-the-save-on-both-paths) |
| `UseDomainEvents(Func<...>)` is obsolete | [7](#7-the-entity-framework-registration-changed) |
| Aggregate roots gain a `Version` column | [8](#8-aggregate-roots-gain-a-version-column) |
| Hand-written identifiers need `IEquatable<T>` | [9](#9-hand-written-identifiers-need-iequatable) |
| Misapplied attributes now fail the build | [10](#10-misapplied-attributes-now-fail-the-build) |
| `CheckInvariants`, `EnsureInvariants`, `GetInvariantViolations` and their `Own` pair are now generated member names | [11](#11-entities-gain-an-invariant-seam) |
| A save now runs your aggregates' invariants | [11](#11-entities-gain-an-invariant-seam) |
| The packages have new ids for now, `Temp.DDDToolkit.*` | [12](#the-package-ids) |
| HotChocolate 16, Entity Framework 10, FluentValidation 12 | [12](#12-package-versions) |
| MediatR is no longer referenced | [13](#13-mediatr-is-replaced-by-mediator-in-the-examples) |

Three more changes are optional, and recommended:
[struct identifiers](#14-optional-make-your-identifiers-structs),
[generated collections](#15-optional-let-the-generator-write-your-collections) and
[module boundaries](#16-optional-declare-your-modules).

## 1. Retarget to .NET 10

The runtime libraries target `net10.0`. There is no `net8.0` build, so every project of the solution moves to
`net10.0`.

```xml
<TargetFramework>net8.0</TargetFramework>
```

```xml
<TargetFramework>net10.0</TargetFramework>
```

Pin the SDK in `global.json`, so everyone builds with the same SDK:

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" }
}
```

The generators still target `netstandard2.0` and reference no other assembly at run time, so they load in any
recent SDK. In 2.x they referenced other assemblies. Newer SDKs could not resolve those, and the build failed with
`CS8784`. This release fixes that error.

## 2. Domain events moved to the aggregate root

In 2.x every `Entity<TId>` had a list of domain events. In 3.0 only `AggregateRoot<TId>` has one, because only the
root is a consistency boundary.

If a child entity raised events, the root must raise them instead. Give the child an `internal` method that makes
the change, and let the root call it and raise the event:

```csharp
[Entity<OrderLineId>]
public partial class OrderLine
{
    public void ChangeQuantity(int quantity)
    {
        Quantity = quantity;
        AddDomainEvent(new LineQuantityChanged(Id, quantity));   // 2.x
    }
}
```

```csharp
[Entity<OrderLineId>]
public partial class OrderLine
{
    internal void ChangeQuantity(int quantity) => Quantity = quantity;
}

[AggregateRoot<OrderId>]
public partial class Order
{
    public void ChangeLineQuantity(OrderLineId lineId, int quantity)
    {
        var line = _lines.Single(l => l.Id == lineId);
        line.ChangeQuantity(quantity);
        RaiseDomainEvent(new LineQuantityChanged(Id, lineId, quantity));
    }
}
```

Do this even where the compiler does not force it. In 2.x the Entity Framework interceptor collected events from
every tracked entity. So a child could publish an event that the root did not know about, while the root decides
whether a change is allowed.

Code that reads the events of a child also stops compiling:

```csharp
var events = orderLine.DomainEvents;      // 2.x
var events = order.DomainEvents;          // 3.0, the root owns them
```

## 3. `AddDomainEvent` becomes `RaiseDomainEvent`

`AddDomainEvent` was public, so any code with a reference to an order could add an event to it.
`RaiseDomainEvent` is `protected`.

```csharp
public void Cancel(CancellationReason reason)
{
    Status = OrderStatus.Cancelled;
    AddDomainEvent(new OrderCancelled(Id, reason));      // 2.x
}
```

```csharp
public void Cancel(CancellationReason reason)
{
    Status = OrderStatus.Cancelled;
    RaiseDomainEvent(new OrderCancelled(Id, reason));    // 3.0
}
```

Inside the aggregate, this is a rename. Outside the aggregate, the code needs a new design. Code like this:

```csharp
order.AddDomainEvent(new OrderExported(order.Id));       // 2.x, from an application service
```

has no direct replacement, on purpose. When the fact belongs to the aggregate, give the aggregate a method:

```csharp
order.MarkExported();                                    // which raises the event itself
```

When the fact is not about the order, it belongs to the application. Publish it with whatever you use for
application messages.

## 4. Draining goes through `IHasDomainEvents`

`ClearDomainEvents()` was a public method on every entity. It is now an explicit interface implementation of
`IHasDomainEvents`, next to a new `DequeueDomainEvents()`:

```csharp
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    IReadOnlyList<IDomainEvent> DequeueDomainEvents();   // returns and empties
    void ClearDomainEvents();                            // discards
}
```

```csharp
order.ClearDomainEvents();                                       // 2.x
((IHasDomainEvents)order).ClearDomainEvents();                   // 3.0
var events = ((IHasDomainEvents)order).DequeueDomainEvents();    // 3.0, read and empty in one step
```

The cast is on purpose. Application code that calls `ClearDomainEvents()` by accident can save a row and lose its
event. You rarely write either line, because the Entity Framework integration drains the aggregates during the
save.

A dispatcher of your own from 2.x looks like this:

```csharp
var entities = context.ChangeTracker.Entries<IHasDomainEvents>()
    .Select(e => e.Entity).Where(e => e.DomainEvents.Any()).ToList();
var events = entities.SelectMany(e => e.DomainEvents).ToList();
entities.ForEach(e => e.ClearDomainEvents());
```

In 3.0 it becomes:

```csharp
var events = context.ChangeTracker.Entries<IHasDomainEvents>()
    .SelectMany(entry => entry.Entity.DequeueDomainEvents())
    .ToList();
```

Better, delete the dispatcher and use `PublishDomainEventsInterceptor` ([Event delivery](event-delivery.md)).

## 5. `IDomainEvent` carries an id and a timestamp

In 2.x `IDomainEvent` was empty, so every consumer had to work out the identity and the time of an event from
elsewhere. It now has two members:

```csharp
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
```

Every event type stops compiling until it supplies them. The one-line fix is to derive from the record
`DomainEvent`, which supplies both:

```csharp
public record OrderPlaced(OrderId OrderId) : IDomainEvent;                 // 2.x
```

```csharp
using DDDToolkit.BaseTypes;

[DomainEventName("ordering.order-placed")]
public sealed record OrderPlaced(OrderId OrderId) : DomainEvent;           // 3.0
```

`EventId` is a version 7 `Guid`, so it is unique and sorts by time. `OccurredAt` is the UTC time when the event was
made. Both are `init` properties, so a replay can set recorded values.

If you have your own marker interface, list `DomainEvent` before it:

```csharp
public interface IBaseDomainEvent : IDomainEvent, INotification;

public sealed record OrderPlaced(OrderId OrderId) : DomainEvent, IBaseDomainEvent;
```

You can still implement `IDomainEvent` directly. You then write the two properties yourself.

- `[DomainEventName]` is new and optional. Without it, an event gets a name by convention: its module and its class
  name in kebab case, such as `ordering.order-placed`.
- Add the attribute when you rename a class whose name is already stored or published, to keep the old name
  ([Stable names](domain-events.md#stable-names)).
- The outbox uses the name as the message name. `DomainEventName.For` returns it. Up to 3.1 the method was
  `DomainEventName.Of`.
- Early 3.0 builds used the class name alone. The outbox still reads the rows they wrote.

### Making the timestamp deterministic in tests

The aggregate makes its own events, so a test cannot set these properties. Use `DomainEventClock` instead:

```csharp
using var scope = DomainEventClock.Use(fakeClock);

var order = new Order(orderId, customerId);

order.DomainEvents.Single().OccurredAt.Should().Be(fakeClock.GetUtcNow());
```

See [Deterministic time in tests](testing.md#deterministic-time-in-tests).

## 6. Events are dispatched before the save, on both paths

The compiler cannot find this change. Read this section even if everything builds.

In 2.x the two save paths behaved differently. `SaveChanges` dispatched from `SavingChanges`, before the database
write. `SaveChangesAsync` dispatched from `SavedChangesAsync`, after the write. So a handler saw uncommitted or
committed data, depending on which overload the code called.

In 3.0 both paths dispatch before the write, so handlers always see uncommitted data.

The effect on your handlers:

- A handler that changes tracked entities on the same `DbContext` now has those changes saved by the same
  `SaveChanges` call, inside the same transaction. In 2.x, on the async path, they needed a second save.
- A handler that throws now aborts the save. In 2.x, on the async path, the row was already written.
- A handler that expects the row to be committed is now wrong. Examples are a handler that reads the row through a
  second context, and a handler that passes the id to a background job. The row is not in the database yet.

Search your code for that last case. The fix is the outbox. The outbox writes the event in the same transaction as
the aggregate, and delivers it after the commit, at least once.

```csharp
builder.Services.AddDDDToolkitEntityFramework(options =>
{
    options.DispatchInProcess(/* your delegate */);
    options.UseOutbox(outbox => outbox.RegisterEventsFromAssemblyContaining<Program>());
});
builder.Services.AddOutboxBackgroundService<OrderingContext>(TimeSpan.FromSeconds(2));
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddDomainEventOutbox(Database);
```

Outbox handlers must be idempotent, keyed on `EventId`
([The outbox in detail](event-delivery.md#the-outbox-in-detail)).

In 3.0, `SaveChanges` throws when an aggregate has pending events and no delivery mode is configured. In 2.x the
events were dropped without an error.

## 7. The Entity Framework registration changed

```csharp
builder.Services.UseDomainEvents(async (sp, events) =>                     // 2.x
{
    var mediator = sp.GetRequiredService<IMediator>();
    foreach (var e in events) await mediator.Publish(e);
});

builder.Services.AddDbContext<OrderingContext>((sp, options) =>
{
    options.UseSqlServer(connectionString);
    options.AddDomainEventInterceptor(sp);
});
```

```csharp
builder.Services.AddDDDToolkitEntityFramework(options =>                   // 3.0
    options.DispatchInProcess(async (sp, events, cancellationToken) =>
    {
        var publisher = sp.GetRequiredService<IPublisher>();
        foreach (var e in events) await publisher.Publish(e, cancellationToken);
    }));

builder.Services.AddDbContext<OrderingContext>((sp, options) => options
    .UseSqlServer(connectionString)
    .UseDDDToolkit(sp));
```

The differences:

- The delegate takes a `CancellationToken`, and an `IReadOnlyList<IDomainEvent>` instead of a
  `List<IDomainEvent>`.
- `UseDDDToolkit` adds the concurrency interceptor as well as the event interceptor.
- The options object holds the outbox settings, `MaxDispatchRounds` and `TimeProvider`.

The old overload still compiles and still works:

```csharp
[Obsolete] UseDomainEvents(Func<IServiceProvider, List<IDomainEvent>, Task> interceptorAction)
```

It forwards to `DispatchInProcess`, so it also dispatches before the save now. The obsolete warning is a reminder
to move, and nothing breaks before you do. `AddDomainEventInterceptor` stays too, as an alias of `UseDDDToolkit`.

### The new call `AddDDDToolkitConventions`

A third call has no 2.x equivalent:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.AddDDDToolkitConventions();       // new in 3.0
    configurationBuilder.AddOrderingConverters();          // you already had this
}
```

`AddDDDToolkitConventions()` maps `Version` as a concurrency token, maps generated read-only collections of
primitives, and ignores `[Internal]` members. Without it, Entity Framework skips a generated collection property
without an error, because it only discovers primitive properties that have a setter.

## 8. Aggregate roots gain a `Version` column

`AggregateRoot<TId>` now has `public long Version { get; private set; }`, and `AddDDDToolkitConventions` maps it as
a concurrency token. Every table of an aggregate root needs a new column, so scaffold a migration before you
deploy:

```bash
dotnet ef migrations add AggregateVersion
```

Existing rows get `0`. That is correct, because the version only has to match from the next save on.

From then on, a save of a stale aggregate throws `ConcurrencyConflictException`. In 2.x the save succeeded without
an error:

```csharp
try
{
    await context.SaveChangesAsync(cancellationToken);
}
catch (ConcurrencyConflictException conflict)
{
    // reload, reapply, retry, or report the conflict to the user
}
```

If an aggregate must not have a version check, configure the property yourself in `OnModelCreating`. Explicit
configuration wins over the convention.

## 9. Hand-written identifiers need `IEquatable`

`Entity<TId>` used to be constrained to `IEntityId`. It is now constrained to `IEntityId, IEquatable<TId>`. That
lets equality compare ids without boxing.

Identifiers from `[EntityId<T>]` already meet this constraint, because the compiler implements `IEquatable<T>` for
every record and record struct. Only a hand-written identifier is affected:

```csharp
public sealed class LegacyId : IEntityId { }                    // 2.x
public sealed class LegacyId : IEntityId, IEquatable<LegacyId> { }   // 3.0
```

Equality on `Entity<TId>` is also null-safe now. `GetHashCode` on an entity whose id is null returns 0 and does not
throw, and `==` handles null on both sides.

## 10. Misapplied attributes now fail the build

2.x reported five diagnostics: DDD00001, DDD00002, DDD00010, DDD00011 and DDD00013. For every other mistake, the
generator wrote nothing and reported nothing, or the compiler refused the attribute with `CS0592`, which named no
cause. The attributes now accept both classes and structs, so the generator reports its own diagnostic instead.

Nine diagnostics can appear in your first build:

| You wrote | 3.0 reports |
|---|---|
| `[EntityId<T>]` on a plain class or struct | [DDD00003](diagnostics.md#ddd00003) |
| `[EntityId<T>]` on a record struct that is not `readonly` | [DDD00004](diagnostics.md#ddd00004), a warning |
| Any of them on a type that is not `partial` | [DDD00005](diagnostics.md#ddd00005) |
| Any of them on a generic type | [DDD00006](diagnostics.md#ddd00006) |
| The generated identifier's name is already taken | [DDD00007](diagnostics.md#ddd00007) |
| A type argument that is neither an identifier nor something an identifier can wrap | [DDD00008](diagnostics.md#ddd00008) |
| `[Entity<T>]` and `[AggregateRoot<T>]` on the same class | [DDD00009](diagnostics.md#ddd00009) |
| A setter on a generated collection property | [DDD00020](diagnostics.md#ddd00020) |
| A field or property typed as another aggregate root | [DDD00021](diagnostics.md#ddd00021), a warning |

DDD00001, DDD00002, DDD00010, DDD00011 and DDD00013 also fire in more cases than before, because a struct or a
record struct can now carry the attribute.

DDD00021 is likely to fire often on a 2.x model. 2.x reported nothing for a navigation such as `Order.Customer`.
3.0 reports that such a navigation widens the aggregate boundary. DDD00021 is a warning, and the generator still
writes everything. [Reference other aggregates by id](entities-and-aggregates.md#reference-other-aggregates-by-id)
explains why. To deal with it later, turn it off for the project:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);DDD00021</NoWarn>
</PropertyGroup>
```

Two more diagnostics cannot fire on an upgraded 2.x solution. [DDD00022](diagnostics.md#ddd00022) and
[DDD00023](diagnostics.md#ddd00023) stay silent until two assemblies declare themselves [modules](modules.md)
([section 16](#16-optional-declare-your-modules)).

Each diagnostic points at a real problem. In 2.x those types got no generated code, and the solution ran without
it. For a generic type (DDD00006), 2.x generated a second, unrelated, non-generic type that compiled on its own.
That is why the compiler errors named members that "do not exist".

`[DontCompare]` and `[Internal]` are unchanged, and report no diagnostics. `[DomainEventName]` is new in 3.0 and
optional. [Diagnostics](diagnostics.md) lists every diagnostic, with a fix for each.

## 11. Entities gain an invariant seam

The generator now writes five members into every `[Entity<T>]` and `[AggregateRoot<T>]`:

```csharp
partial void CheckInvariants();
public override void EnsureInvariants();
public override IReadOnlyList<InvariantViolation> GetInvariantViolations();
public override void EnsureOwnInvariants();
public override IReadOnlyList<InvariantViolation> GetOwnInvariantViolations();
```

Of the four public methods, `EnsureInvariants` and `GetInvariantViolations` check the whole aggregate, including
the child entities of this object. The `Own` pair checks this object alone. The `Own` pair is for code that already
walks the graph, which in practice is the save ([Checking the whole aggregate](invariants.md#checking-the-whole-aggregate)).

This has two effects.

**The names are taken.** A 2.x class that already declares a member named `CheckInvariants`, `EnsureInvariants`,
`GetInvariantViolations`, `EnsureOwnInvariants` or `GetOwnInvariantViolations` clashes with the generated member.
Rename your member. The compiler points at the line. This case is rare, and it is the only way this feature can
break a build.

**A save now runs them.** `UseDDDToolkit` registers an `InvariantInterceptor`. Before every `SaveChanges`, it
checks every entity that the save adds or modifies, child entities included, and the aggregate root of every
changed child. Until you write a rule, those calls do nothing. The compiler removes a `partial void` without an
implementation, and every call to it. So an unchanged 2.x aggregate behaves as before, and the check costs
nothing. You notice the interceptor only once you write a rule.

There is nothing to turn on and nothing to migrate. A rule looks like this:

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    partial void CheckInvariants()
    {
        if (Status != OrderStatus.Draft && Lines.Count == 0)
        {
            throw InvariantViolation("A placed order must have at least one line.");
        }
    }
}
```

A rule that existing data already breaks makes the saves of those rows fail. The interceptor checks only the
aggregates that a save touches, so loading still works. Run the rule as a query over production data before you
deploy it ([Invariants](invariants.md)).

## 12. Package versions

### The package ids

For now, 3.x is published as `Temp.DDDToolkit`, `Temp.DDDToolkit.EntityFramework` and so on. The nuget.org account
that owns the `DDDToolkit.*` ids cannot publish at the moment. So updating `DDDToolkit` to the latest version still
gives you 2.0.22. Replace each reference with the prefixed id instead:

```xml
<!-- 2.0.22 -->
<PackageReference Include="DDDToolkit" Version="2.0.22" />
<PackageReference Include="DDDToolkit.EntityFramework" Version="2.0.22" />

<!-- 3.0.0 -->
<PackageReference Include="Temp.DDDToolkit" Version="3.0.0" />
<PackageReference Include="Temp.DDDToolkit.EntityFramework" Version="3.0.0" />
```

Only the ids changed.

- The assemblies and namespaces are still `DDDToolkit.*`, so no `using` changes.
- The analyzer packages still arrive as dependencies.
- Remove every 2.0.22 reference. NuGet sees `DDDToolkit` and `Temp.DDDToolkit` as different packages, so it does
  not pick one of the two. Both would put an assembly named `DDDToolkit` in the build.

### The dependencies

| Package | 2.0.22 | 3.0.0 |
|---|---|---|
| Microsoft.EntityFrameworkCore | 8.0.8 | 10.0.12 |
| FluentValidation | 11.9.2 | 12.1.1 |
| HotChocolate.AspNetCore | 14.0.0-rc.1 | 16.6.6 |
| HotChocolate.Execution | 14.0.0-rc.1 | replaced by `HotChocolate` 16.6.6 |

Two points about the move to HotChocolate 16:

- `HotChocolate.Execution` has no stable 16 release. The execution engine is now in the `HotChocolate` package, so
  replace the reference.
- `GraphQLTypeAttribute<TSchemaType>` is constrained on `ITypeDefinition`, which replaced `INamedType`. For a custom
  scalar type in that attribute, only the constraint changed.

Your own HotChocolate code meets one more rename on 16.6.6: the type-system configuration classes.
`DefinitionBase` and `ObjectTypeDefinition` do not exist in the 16.6.6 assemblies. The replacements are
`TypeSystemConfiguration`, `ObjectTypeConfiguration`, `InterfaceTypeConfiguration`,
`InputObjectTypeConfiguration` and `FieldConfiguration`, all in `HotChocolate.Types.Descriptors.Configurations`.
So a `TypeInterceptor` override now takes a `TypeSystemConfiguration`.

The parts that the toolkit relies on most did not change:

| Still the same in 16.6.6 | Where |
|---|---|
| `BindRuntimeType<TRuntimeType, TSchemaType>()` | `Microsoft.Extensions.DependencyInjection` |
| `AddTypeConverter<T>()` | `Microsoft.Extensions.DependencyInjection` |
| `IChangeTypeProvider` and the `ChangeType` delegate | `HotChocolate.Utilities` |
| `[InterfaceType<T>]` with a `static partial void Configure` | `HotChocolate.Types.Analyzers` |

The schema changes too. `[Internal]` members are now removed during type discovery. In 2.x they were only flagged
at completion. So the types that they referenced no longer appear in the schema. If your 2.x schema had
FluentValidation's `ValidationFailure` or the always-valid twins, those types are gone. That fixes a bug, but check
your persisted queries before you deploy ([GraphQL](graphql.md)).

## 13. MediatR is replaced by Mediator in the examples

MediatR has a commercial license from version 13, so the repository stayed on 12.x. The examples now publish
through [Mediator](https://github.com/martinothamar/Mediator), which is MIT licensed and source generated. The
`DDDToolkit.Mediator` package writes the dispatch delegate for you:

```csharp
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());
```

**Moving is optional.** The core package has no mediator dependency. In-process delivery is a delegate, and both
libraries fit it. To stay on MediatR, keep your delegate and change only its signature:

```csharp
options.DispatchInProcess(async (sp, events, cancellationToken) =>
{
    var mediator = sp.GetRequiredService<IMediator>();
    foreach (var e in events) await mediator.Publish(e, cancellationToken);
});
```

If you move, two differences matter:

- Mediator is source generated, so call `AddMediator` in the assembly that references `Mediator.SourceGenerator`.
- Its default service lifetime is singleton. A handler that injects your `DbContext` needs
  `ServiceLifetime.Scoped`. The generator reads the lifetime from the `AddMediator` call at compile time, so set it
  there.

`DispatchWithMediator()` throws for an event that does not implement Mediator's `INotification`, and names the
event type. It does not skip the event, because the interceptor has already dequeued it. A skipped event would be
lost, with no row, no log and nothing to retry.

## 14. Optional: make your identifiers structs

`[EntityId<T>]` now accepts a `readonly partial record struct`, and that is the recommended shape. A struct
identifier allocates nothing, and the generator writes more members for it than for the record form:

```csharp
[EntityId<Guid>("ORD")]
public partial record OrderId;                          // 2.x, still works
```

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;          // 3.0, recommended
```

For a struct identifier, the generator writes `Value`, `Empty`/`IsEmpty`, `CreateUnique`/`CreateSequential`,
`ToString()` with the prefix, `Parse`/`TryParse`, `IParsable<T>`, `IComparable<T>`, explicit conversions and a JSON
converter.

The column does not change. Both forms store the wrapped value, so a `Guid` id is a `Guid` column either way, and
no migration is needed.

Two things change:

- A struct has no `null`. A property that used `null` for "not set" uses `OrderId?` or `IsEmpty` instead.
- A struct identifier has no always-valid twin, because it has no invalid state.

You can often delete the declaration. For an identifier that only its own aggregate uses, the generator writes
the identifier from the aggregate:

```csharp
[AggregateRoot<Guid>("ORD")]
public partial class Order { }       // also generates OrderId
```

Keep the explicit declaration for an identifier that other aggregates, DTOs or API contracts use. See
[Identifiers](identifiers.md).

## 15. Optional: let the generator write your collections

For a get-only `partial` collection property, the generator writes a backing field, a read-only view and Entity
Framework's `[BackingField]`:

```csharp
private readonly List<OrderLine> _lines = new();                   // 2.x
public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
```

```csharp
public partial IReadOnlyList<OrderLine> Lines { get; }             // 3.0
```

The generator writes `_lines`, so the rest of the aggregate does not change. This helps most where 2.x code
exposed the list itself:

```csharp
public List<Order> Orders { get; private set; } = new();           // any caller can Add
public partial IReadOnlyList<Order> Orders { get; }                // only the aggregate can
```

`IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IEnumerable<T>` and `IReadOnlySet<T>` are supported. A setter is an
error ([DDD00020](diagnostics.md#ddd00020)). See
[Entities and aggregates](entities-and-aggregates.md#read-only-collections).

## 16. Optional: declare your modules

Modules have no 2.x equivalent, so there is nothing to migrate, and nothing breaks until you declare a module. For
a modular monolith, this is the most useful part of this release that you turn on yourself.

Mark an assembly as a module, and mark what it publishes:

```csharp
[assembly: Module("Ordering")]
```

```csharp
[ModuleContract]
[EntityId<Guid>("CUS")]
public readonly partial record struct CustomerId;
```

An analyzer then reports where one module names a type that another module does not publish
([DDD00022](diagnostics.md#ddd00022)), or stores an entity of another module ([DDD00023](diagnostics.md#ddd00023)).
Both are warnings. Both stay silent unless *both* assemblies carry `[assembly: Module]`. So the attribute on one
project changes nothing, and the attribute on a second project gives you a list of warnings and no broken build.

From 3.2 on, `DDD_Module` also declares the module. `DDD_Module` is the MSBuild property that names the generated
`Add{Module}Converters` method. The build writes the attribute from it, so a project that sets `DDD_Module` is a
module ([A module named by its folder](modules.md#a-module-named-by-its-folder)). In a project that declares the
attribute itself, the attribute names the generated code, whatever the property says. Adopt modules one project at
a time. [Modules](modules.md#adopting-modules-in-an-existing-codebase) gives the order.

## From an earlier 3.0 build

Skip this section for an upgrade from 2.0.22. Three things changed during the 3.0 builds, after some databases had
already been created with them. A fourth change came in 3.1.0 and renames generated methods.

### Outbox and inbox timestamps

**On PostgreSQL, nothing changes.** Npgsql maps both a UTC `DateTime` and a `DateTimeOffset` to `timestamptz`. The
column and its bytes stay the same, so there is no migration to write.

**On SQL Server, the column type changes** from `datetime2` to `datetimeoffset`. A migration scaffolded after the
upgrade has an `ALTER COLUMN` for each timestamp column of the outbox and the inbox. The stored instant stays the
same. SQL Server reads an existing `datetime2` as the same time at `+00:00`, which is correct, because the outbox
always wrote UTC values. So the meaning of a row does not change, but the table does, and you must run the
migration.

To keep the old shape instead, say so explicitly:

```csharp
modelBuilder.AddDomainEventOutbox(Database, timestamps: DomainEventTimestamps.UtcDateTime);
modelBuilder.AddDomainEventInbox(Database, timestamps: DomainEventTimestamps.UtcDateTime);
```

That keeps the columns you have, on every provider, and needs no migration.

If you upgrade the code on SQL Server and do neither, **reading the outbox throws**. The insert still works,
because SQL Server converts the parameter to the type of the column, and stores the correct instant. The read
fails. The driver returns a `datetime2` as a `DateTime`, the model expects a `DateTimeOffset`, and the first row
throws `InvalidCastException`.

That failure is intended. A database without the migration fails the first time the processor polls, so it cannot
differ from the model unnoticed. `Tests/DDDToolkit.EntityFramework.Providers.Tests/Providers/ProviderMappingTests.cs`
tests both halves against a real SQL Server.

Rows written before you notice are correct. The column already holds the right instant, so the migration needs no
data repair afterwards.

**On SQLite, nothing changes.** The toolkit always stores a UTC `DateTime` on SQLite, because SQLite cannot order
by a `DateTimeOffset`.

[Timestamps](event-delivery.md#timestamps) lists the column type of each provider.

### The outbox `Version` column

An outbox table from before the `Version` column needs that column, as a non-nullable `int` with a default of 1.
Every existing row was written as version 1. A column added without a default reads as 0, and the processor treats
0 as 1. So an upgrade that forgets the default still works
([Versioning and upcasting](integration-events.md#versioning-and-upcasting)).

### The outbox `NextAttemptAt` column

An outbox table from before `NextAttemptAt` needs that column: nullable, of the same type as `ProcessedAt`, and
without an index. A scaffolded migration writes this. Existing rows read `null`, which means due now, so no row
changes ([Failures, retries and poison messages](event-delivery.md#failures-retries-and-poison-messages)).

### The names of generated registrations

Up to 3.0.1, `Add{Module}Converters`, `Add{Module}IntegrationEvents` and `Add{Module}GraphQlRuntimeBindings` took
their name from `DDD_Module` and ignored `[assembly: Module]`. Now the attribute wins, as it always did for
`{Module}EventNames`. `DDD_Module` is the default when there is no attribute. A project without the attribute sees
no change. A project with the attribute has three cases:

- **The attribute and the same `DDD_Module`**, such as `[assembly: Module("Ordering")]` and
  `<DDD_Module>Ordering</DDD_Module>`. The method is `AddOrderingConverters` before and after. Nothing changes,
  and the property can go.
- **The attribute and no `DDD_Module`.** The method was `AddAcmeOrderingConverters`, named after the assembly, and
  is now `AddOrderingConverters`. Rename the calls.
- **The attribute and another `DDD_Module`**, such as `OrderingContracts` in a contracts project. The method was
  `AddOrderingContractsConverters`, and is now `AddOrderingConverters`. Rename the calls in other modules, and
  remove the call in the module itself.

The last case is the contracts project of a module. Its methods now have the same names as the methods of the
module, and the module's methods call those of the contracts. So the module's context and schema make one call
instead of two. Remove the `using` for the generated namespace of the contracts together with the call. Otherwise
the remaining call is ambiguous. The build points at every place. A call to a name that no longer exists is
`CS1061`, and two imported classes that declare the same method give `CS0121`.

## What did not change

- `[ValueObject]` and `[SingleValueObject<T>]` keep their shape, their `Valid` twin, `ToValid()` and
  `InvalidValueObjectException`.
- `[DontCompare]` and `[Internal]` work as before.
- The `ColumnLength` argument of `[EntityId<T>]` still becomes `HaveMaxLength`.
- The generated `Add{Module}Converters` keeps its name and its place, and `DDD_Module` still names it in every
  project that does not declare `[assembly: Module]`.
- `Entity<TId>.Id` still has a `protected set`, so a 2.x constructor that wrote `Id = id` after `base()` still
  compiles. `base(id)` is the better form.
- Validation still runs through `protected bool Validate()`, or through a generated body when
  `DDDToolkit.FluentValidation` is referenced. The second overload
  `protected override void Validate(ValidationErrorBuilder errors)` is new, and so is `TryToValid`, which does not
  throw, next to `ToValid()`. Nothing you already wrote has to change
  ([Failure handling](value-objects.md#failure-handling)).
- Integration events, sinks, the inbox, versioning and modules are new. Each is off until you turn it on, and none
  replaces a 2.x feature.

One behavior of value objects did change. In 2.x, `record with` on a value object copied the cached validation
result. So a copy reported the result of its source, even when the changed property was the one that had been
validated. A copy now validates again when asked. Remove any workaround for this.

## Checklist

1. Retarget every project to `net10.0` and pin the SDK.
2. Update the package references: `DDDToolkit.*` to `Temp.DDDToolkit.*` 3.0.0, and
   `HotChocolate.Execution` to `HotChocolate`.
3. Build. Fix the `IDomainEvent` errors by deriving your events from `DomainEvent`.
4. Fix the `AddDomainEvent` errors. Rename the call to `RaiseDomainEvent` inside aggregates, and give the
   aggregate a method where the call was outside one.
5. Move any events raised by child entities up to their root.
6. Fix the `ClearDomainEvents` errors with a cast to `IHasDomainEvents`, or delete the code and use the
   interceptor.
7. Fix the diagnostics that the generators report. Each message names the type. DDD00021 is a warning about
   references to other aggregates, and can wait behind a `NoWarn` if the list is long.
8. Rename any member of your own named `CheckInvariants`, `EnsureInvariants`, `GetInvariantViolations`,
   `EnsureOwnInvariants` or `GetOwnInvariantViolations`. The generator now writes all five names into every
   entity.
9. Replace `UseDomainEvents(...)` and `AddDomainEventInterceptor(...)` with `AddDDDToolkitEntityFramework(...)` and
   `UseDDDToolkit(...)`.
10. Add `AddDDDToolkitConventions()` to `ConfigureConventions`.
11. Scaffold a migration for the `Version` column.
12. Read section 6, and decide for each handler whether it can run before the commit. Move the handlers that cannot
    to the outbox.
13. Run your tests. Then, before you deploy, check your GraphQL schema and your persisted queries.

The list turns on no new feature. Invariants, integration events, sinks, the inbox and modules are all opt-in.
Turn them on in a later change, after the upgrade builds and its tests pass.
