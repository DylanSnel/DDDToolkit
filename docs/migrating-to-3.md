# Migrating to 3.0

This page is for somebody on 2.0.22 who wants to upgrade. Every break is listed with the code you
have now and the code you need. The [changelog](../CHANGELOG.md) has the full list of changes; this
page only covers the ones that make your solution stop compiling or stop behaving the same.

Budget an afternoon for a medium sized solution. Most of the work is mechanical: the compiler finds
the event changes for you. The one thing it cannot find for you is the change to when handlers run,
in [section 6](#6-events-are-dispatched-before-the-save-on-both-paths).

## What actually breaks

| Break | Section |
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
| `EnsureInvariants` and `CheckInvariants` are now generated member names | [11](#11-entities-gain-an-invariant-seam) |
| A save now runs your aggregates' invariants | [11](#11-entities-gain-an-invariant-seam) |
| HotChocolate 16, Entity Framework 10, FluentValidation 12 | [12](#12-package-versions) |
| MediatR is no longer referenced | [13](#13-mediatr-is-replaced-by-mediator-in-the-examples) |

Then there are three changes you do not have to make, but should: [struct
identifiers](#14-optional-make-your-identifiers-structs), [generated
collections](#15-optional-let-the-generator-write-your-collections) and
[module boundaries](#16-optional-declare-your-modules).

## 1. Retarget to .NET 10

The runtime libraries target `net10.0`. There is no `net8.0` build, so the whole solution moves.

```xml
<TargetFramework>net8.0</TargetFramework>
```

```xml
<TargetFramework>net10.0</TargetFramework>
```

Pin the SDK so everyone builds with the same one:

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" }
}
```

The generators still target `netstandard2.0` and reference nothing at run time, so they load in any
recent SDK. In 2.x they referenced other assemblies, which stopped resolving under newer SDKs and
surfaced as `CS8784`. If that is the error that brought you here, this release is the fix.

## 2. Domain events moved to the aggregate root

In 2.x every `Entity<TId>` had a domain event list. In 3.0 only `AggregateRoot<TId>` has one, because
only the root is a consistency boundary.

If a child entity raised events, the root has to raise them instead. Give the child a method that
reports what happened and let the root turn that into an event:

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

This is worth doing even where the compiler does not force it. In 2.x the Entity Framework
interceptor collected events from every tracked entity, so a child could publish something the root
did not know about, and the root is the thing that decides whether the change was legal.

Reading events off a child also stops compiling:

```csharp
var events = orderLine.DomainEvents;      // 2.x
var events = order.DomainEvents;          // 3.0, the root owns them
```

## 3. `AddDomainEvent` becomes `RaiseDomainEvent`

`AddDomainEvent` was public, so anything holding a reference to an order could put an event into it.
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

Inside the aggregate this is a rename. Outside it, it is a redesign. Code like this:

```csharp
order.AddDomainEvent(new OrderExported(order.Id));       // 2.x, from an application service
```

has no direct replacement, and that is deliberate. Either the aggregate owns the fact, in which case
give it a method:

```csharp
order.MarkExported();                                    // which raises the event itself
```

or the fact is not about the order at all, in which case it is an application concern and belongs in
whatever you use to publish application messages, not on the aggregate.

## 4. Draining goes through `IHasDomainEvents`

`ClearDomainEvents()` was a public method on every entity. It is now an explicit interface
implementation, along with a new `DequeueDomainEvents()`:

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

The cast is the point. Application code that can call `ClearDomainEvents()` by accident can write a
row whose event never happened. You will rarely write either line: the Entity Framework integration
drains the aggregates during save.

If you wrote your own dispatcher in 2.x, it looked something like this:

```csharp
var entities = context.ChangeTracker.Entries<IHasDomainEvents>()
    .Select(e => e.Entity).Where(e => e.DomainEvents.Any()).ToList();
var events = entities.SelectMany(e => e.DomainEvents).ToList();
entities.ForEach(e => e.ClearDomainEvents());
```

```csharp
var events = context.ChangeTracker.Entries<IHasDomainEvents>()
    .SelectMany(entry => entry.Entity.DequeueDomainEvents())
    .ToList();
```

Better still, delete it and use `PublishDomainEventsInterceptor`. See
[Entity Framework](entity-framework.md#domain-event-delivery).

## 5. `IDomainEvent` carries an id and a timestamp

In 2.x `IDomainEvent` was empty, so every consumer had to reconstruct identity and timing from
context. It now has two members:

```csharp
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
```

Every event type you have stops compiling until it supplies them. The one line fix is to derive from
the `DomainEvent` record base, which supplies both:

```csharp
public record OrderPlaced(OrderId OrderId) : IDomainEvent;                 // 2.x
```

```csharp
using DDDToolkit.BaseTypes;

[DomainEventName("ordering.order-placed")]
public sealed record OrderPlaced(OrderId OrderId) : DomainEvent;           // 3.0
```

`EventId` is a version 7 `Guid`, so it is unique and sorts by time. `OccurredAt` is the current UTC
time. Both are `init`, so a replay can supply recorded values.

If you have a marker interface of your own, put `DomainEvent` in front of it:

```csharp
public interface IBaseDomainEvent : IDomainEvent, INotification;

public sealed record OrderPlaced(OrderId OrderId) : DomainEvent, IBaseDomainEvent;
```

You can still implement `IDomainEvent` directly. You then write the two properties yourself.

`[DomainEventName]` is new and optional, but add it to anything you serialize, store or publish. The
wire name is otherwise the class name, which is fine until the first rename. The outbox uses it as
the message name, and `DomainEventName.Of` resolves it.

### Making the timestamp deterministic in tests

Because the aggregate constructs its own events, your test cannot pass an initialiser. Use
`DomainEventClock` instead:

```csharp
using var scope = DomainEventClock.Use(fakeClock);

var order = new Order(orderId, customerId);

order.DomainEvents.Single().OccurredAt.Should().Be(fakeClock.GetUtcNow());
```

See [Domain events](domain-events.md#deterministic-time-in-tests).

## 6. Events are dispatched before the save, on both paths

This is the change the compiler cannot find for you, so read it even if everything builds.

In 2.x the two save paths behaved differently. `SaveChanges` dispatched from `SavingChanges`, before
the database write. `SaveChangesAsync` dispatched from `SavedChangesAsync`, after it. So a handler
saw uncommitted data or committed data depending only on which overload the caller happened to use.

In 3.0 both paths dispatch before the write. Handlers always see the same thing.

What that means for your handlers:

- A handler that changes tracked entities on the same `DbContext` now has those changes saved by the
  same `SaveChanges` call, inside the same transaction. In 2.x, on the async path, they needed a
  second save.
- A handler that throws now aborts the save. In 2.x, on the async path, the row was already written.
- A handler that assumed the row was committed, for example one that reads it back through a second
  context or hands the id to a background job, is now wrong. The row is not there yet.

That last case is the one to go looking for. The fix is the outbox: the event is written in the same
transaction as the aggregate and delivered after the commit, at least once.

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

Outbox handlers must be idempotent, keyed on `EventId`. See
[the outbox](entity-framework.md#the-outbox-in-detail).

One more thing that is new rather than changed: if an aggregate has pending events and no delivery
mode is configured, `SaveChanges` throws instead of silently dropping them.

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

Three differences. The delegate takes a `CancellationToken` and an `IReadOnlyList<IDomainEvent>`
instead of a `List<IDomainEvent>`. `UseDDDToolkit` adds the concurrency interceptor as well as the
event one. And the options object is where the outbox, `MaxDispatchRounds` and `TimeProvider` live.

The old overload still works and still compiles:

```csharp
[Obsolete] UseDomainEvents(Func<IServiceProvider, List<IDomainEvent>, Task> interceptorAction)
```

It forwards to `DispatchInProcess`, so it dispatches before the save now, like everything else. Treat
the warning as a reminder, not an emergency. `AddDomainEventInterceptor` is kept too, as an alias of
`UseDDDToolkit`.

### One new call: `AddDDDToolkitConventions`

There is a third call that has no 2.x equivalent:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.AddDDDToolkitConventions();       // new in 3.0
    configurationBuilder.AddOrderingConverters();          // you already had this
}
```

It maps `Version` as a concurrency token, maps generated read-only collections of primitives, and
ignores `[Internal]` members. Without it, a generated collection property is silently skipped,
because Entity Framework only discovers primitive properties that have a setter.

## 8. Aggregate roots gain a `Version` column

`AggregateRoot<TId>` now has `public long Version { get; private set; }`, and
`AddDDDToolkitConventions` maps it as a concurrency token. Every aggregate root table needs a new
column, so scaffold a migration before you deploy:

```bash
dotnet ef migrations add AggregateVersion
```

Existing rows get `0`, which is correct: the version only has to agree with itself from the next save
onwards.

From then on, a save against a stale aggregate throws `ConcurrencyConflictException` instead of
succeeding silently:

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

If some aggregate genuinely must not be version checked, configure the property yourself in
`OnModelCreating`; explicit configuration wins over the convention.

## 9. Hand-written identifiers need `IEquatable`

`Entity<TId>` used to be constrained to `IEntityId`. It is now constrained to
`IEntityId, IEquatable<TId>`, which is what lets equality avoid boxing.

Identifiers generated by `[EntityId<T>]` satisfy this already, because records and record structs
implement `IEquatable<T>` for free. Only a hand-written identifier is affected:

```csharp
public sealed class LegacyId : IEntityId { }                    // 2.x
public sealed class LegacyId : IEntityId, IEquatable<LegacyId> { }   // 3.0
```

Equality on `Entity<TId>` is also null-safe now. `GetHashCode` on an entity whose id is null returns
0 instead of throwing, and `==` handles null on both sides.

## 10. Misapplied attributes now fail the build

2.x reported five diagnostics: DDD00001, DDD00002, DDD00010, DDD00011 and DDD00013. Everything else
it got wrong generated nothing and said nothing, or was rejected by the compiler with `CS0592`, which
named no cause. The attributes now accept both classes and structs, so the generator reports its own
diagnostic instead of the compiler refusing the attribute.

Nine diagnostics are new to a first build, and these are the ones you can expect:

| You wrote | 3.0 says |
|---|---|
| `[EntityId<T>]` on a plain class or struct | [DDD00003](diagnostics.md#ddd00003) |
| `[EntityId<T>]` on a non readonly record struct | [DDD00004](diagnostics.md#ddd00004), a warning |
| Any of them on a non partial type | [DDD00005](diagnostics.md#ddd00005) |
| Any of them on a generic type | [DDD00006](diagnostics.md#ddd00006) |
| The generated identifier's name is already taken | [DDD00007](diagnostics.md#ddd00007) |
| A type argument that is neither an identifier nor something one can wrap | [DDD00008](diagnostics.md#ddd00008) |
| `[Entity<T>]` and `[AggregateRoot<T>]` on the same class | [DDD00009](diagnostics.md#ddd00009) |
| A setter on a generated collection property | [DDD00020](diagnostics.md#ddd00020) |
| A field or property typed as another aggregate root | [DDD00021](diagnostics.md#ddd00021), a warning |

DDD00001, DDD00002, DDD00010, DDD00011 and DDD00013 also fire in more cases than they used to, now
that a struct or a record struct can carry the attribute at all.

DDD00021 is the one most likely to be noisy on a 2.x model, because 2.x said nothing about an
`Order.Customer` navigation and this release says it widens the aggregate boundary. It is a warning,
everything is still generated, and [Reference other aggregates by
id](entities-and-aggregates.md#reference-other-aggregates-by-id) has the argument. If you are not
ready to have it now, turn it off for the project and come back to it:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);DDD00021</NoWarn>
</PropertyGroup>
```

Two more exist and neither can fire on an upgraded 2.x solution: [DDD00022](diagnostics.md#ddd00022)
and [DDD00023](diagnostics.md#ddd00023) are silent until two assemblies declare themselves
[modules](modules.md). See [section 16](#16-optional-declare-your-modules).

They are all real: in 2.x those types were producing nothing, and you were living with whatever the
missing code did not do. DDD00006 in particular used to produce a second, unrelated, non-generic type
that compiled on its own, which is why the errors you saw talked about members that "do not exist".

`[DontCompare]` and `[Internal]` are unchanged, and neither has ever reported anything.
`[DomainEventName]` is new in 3.0 and optional. The full list with a fix for each is in
[Diagnostics](diagnostics.md).

## 11. Entities gain an invariant seam

Every `[Entity<T>]` and `[AggregateRoot<T>]` now gets two generated members:

```csharp
partial void CheckInvariants();
public override void EnsureInvariants();
```

Two things follow, one mechanical and one behavioural.

**The names are taken.** A 2.x class that already declares a member called `CheckInvariants` or
`EnsureInvariants` collides with the generated one. Rename yours; the compiler points at the line.
This is rare, but it is the only way this feature can stop a build.

**A save now runs them.** `UseDDDToolkit` registers an `InvariantInterceptor` that calls
`EnsureInvariants()` on every aggregate root a `SaveChanges` adds or modifies. Until you implement
the seam that call does nothing at all: the compiler erases an unimplemented `partial void` and every
call to it, so an unchanged 2.x aggregate behaves exactly as before and costs nothing. You only
notice the interceptor once you write a rule.

There is nothing to switch on and nothing to migrate. When you are ready to use it:

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

A rule that the data in your database already breaks will start failing saves of those rows. The
interceptor only checks aggregates the save touches, so nothing breaks on load, but it is worth
running the rule over production data as a query before you deploy it. See
[Invariants](invariants.md).

## 12. Package versions

| Package | 2.0.22 | 3.0.0 |
|---|---|---|
| Microsoft.EntityFrameworkCore | 8.0.8 | 10.0.12 |
| FluentValidation | 11.9.2 | 12.1.1 |
| HotChocolate.AspNetCore | 14.0.0-rc.1 | 16.6.6 |
| HotChocolate.Execution | 14.0.0-rc.1 | replaced by `HotChocolate` 16.6.6 |

Two things to know about the HotChocolate jump.

`HotChocolate.Execution` has no stable 16 release. The execution engine ships in the `HotChocolate`
package now, so change the reference rather than looking for a newer version of the old one.

`GraphQLTypeAttribute<TSchemaType>` is constrained on `ITypeDefinition`, which replaced `INamedType`.
If you named a custom scalar type in that attribute, the constraint is the only thing that changed.

There is a schema change too. `[Internal]` members are now removed during type discovery rather than
flagged at completion, so the types they referenced stop appearing in the schema. If your 2.x schema
contained FluentValidation's `ValidationFailure` or the always-valid twins, they are gone. That is
the bug being fixed, but check your persisted queries before you deploy. See
[GraphQL](graphql.md).

## 13. MediatR is replaced by Mediator in the examples

MediatR is commercially licensed from version 13, which is why the repository stayed on a 12.x version. The
examples now publish through [Mediator](https://github.com/martinothamar/Mediator), which is MIT and
source generated, and there is a `DDDToolkit.Mediator` package that writes the dispatch delegate for
you:

```csharp
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());
```

**You do not have to move.** The toolkit core has never had a mediator dependency and still does not:
in-process delivery is a delegate, and both packages fit it. If you are staying on MediatR, keep your
delegate and change only its signature:

```csharp
options.DispatchInProcess(async (sp, events, cancellationToken) =>
{
    var mediator = sp.GetRequiredService<IMediator>();
    foreach (var e in events) await mediator.Publish(e, cancellationToken);
});
```

If you do move, the differences that bite are that Mediator is source generated, so `AddMediator` has
to be called in the assembly that carries `Mediator.SourceGenerator`, and that its default service
lifetime is singleton. A handler that injects your `DbContext` needs `ServiceLifetime.Scoped`, and
the generator reads that from the `AddMediator` call at compile time, so it has to be written there.

An event that does not implement Mediator's `INotification` makes `DispatchWithMediator()` throw
naming the event type. It is not skipped: the interceptor has already dequeued the event by then, so
skipping would destroy it with no row, no log and nothing to retry.

## 14. Optional: make your identifiers structs

`[EntityId<T>]` now accepts a `readonly partial record struct`, and that is the recommended shape.
The identifier costs no allocation and gets a fuller surface than the record form:

```csharp
[EntityId<Guid>("ORD")]
public partial record OrderId;                          // 2.x, still works
```

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;          // 3.0, recommended
```

A struct identifier gets `Value`, `Empty`/`IsEmpty`, `CreateUnique`/`CreateSequential`, `ToString()`
with the prefix, `Parse`/`TryParse`, `IParsable<T>`, `IComparable<T>`, explicit conversions and a
JSON converter.

The column does not change. Both forms store the wrapped value, so a `Guid` id is a `Guid` column
either way and no migration is needed.

Two things do change. A struct has no `null`, so a property that used `null` to mean "not set" uses
`OrderId?` or `IsEmpty` instead. And a struct identifier has no always-valid twin, because there is
no invalid state to exclude.

While you are there, you can often delete the declaration entirely. An identifier used only by its
own aggregate can be generated from the aggregate:

```csharp
[AggregateRoot<Guid>("ORD")]
public partial class Order { }       // also generates OrderId
```

Keep the explicit declaration for an identifier that other aggregates, DTOs or API contracts refer
to. See [Identifiers](identifiers.md).

## 15. Optional: let the generator write your collections

A get-only `partial` collection property gets a backing field, a read-only view and Entity Framework's
`[BackingField]`:

```csharp
private readonly List<OrderLine> _lines = new();                   // 2.x
public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
```

```csharp
public partial IReadOnlyList<OrderLine> Lines { get; }             // 3.0
```

`_lines` still exists, written by the generator, so the rest of the aggregate does not change. This
is worth doing mostly where 2.x code exposed the list itself:

```csharp
public List<Order> Orders { get; private set; } = new();           // any caller can Add
public partial IReadOnlyList<Order> Orders { get; }                // only the aggregate can
```

`IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IEnumerable<T>` and `IReadOnlySet<T>` are supported.
A setter is an error ([DDD00020](diagnostics.md#ddd00020)). See
[Entities and aggregates](entities-and-aggregates.md#read-only-collections).

## 16. Optional: declare your modules

This one has no 2.x equivalent at all, so there is nothing to migrate and nothing that breaks until
you ask for it. If your solution is a modular monolith, it is the most valuable thing in this release
that the compiler will not hand you.

Mark an assembly as a module and say what it publishes:

```csharp
[assembly: Module("Ordering")]
```

```csharp
[ModuleContract]
[EntityId<Guid>("CUS")]
public readonly partial record struct CustomerId;
```

An analyzer then reports where one module names another module's unpublished type
([DDD00022](diagnostics.md#ddd00022)) or holds another module's entity as stored state
([DDD00023](diagnostics.md#ddd00023)). Both are warnings and both are silent unless *both* assemblies
carry `[assembly: Module]`, so adding the attribute to one project changes nothing, and adding it to
a second gives you a list rather than a build break.

Do not confuse it with `DDD_Module`, which is unchanged and unrelated: that MSBuild property names
the generated `Add{Module}Converters` method and describes no boundary to anybody. Adopt one project
at a time; [Modules](modules.md#adopting-this-on-an-existing-codebase) has the order to do it in.

## What did not change

- `[ValueObject]` and `[SingleValueObject<T>]` keep their shape, their `Valid` twin, `ToValid()` and
  `InvalidValueObjectException`.
- `[DontCompare]` and `[Internal]` mean what they always meant.
- The `ColumnLength` argument of `[EntityId<T>]` still becomes `HaveMaxLength`.
- The generated `Add{Module}Converters` keeps its name and its place, and `DDD_Module` still names it.
- `Entity<TId>.Id` still has a `protected set`, so a 2.x constructor that wrote `Id = id` after
  `base()` still compiles. `base(id)` is the better form.
- Validation still runs through `protected bool Validate()`, or a generated body when
  `DDDToolkit.FluentValidation` is referenced. There is a second overload now,
  `protected override void Validate(ValidationErrorBuilder errors)`, and a non-throwing `TryToValid`
  beside `ToValid()`, but nothing you already wrote has to move. See
  [Failure handling](value-objects.md#failure-handling).
- Everything about integration events, sinks, the inbox, versioning and modules is new surface. None
  of it is on unless you call for it, and none of it replaces anything 2.x had.

One behaviour inside value objects did change, quietly and for the better. In 2.x, `record with` on a
value object copied the cached validity verdict, so a copy reported its source's verdict even when
the property that changed was the one that had been validated. A copy now revalidates on demand. If
you had a workaround for that, remove it.

## A checklist

1. Retarget every project to `net10.0` and pin the SDK.
2. Update the package references, including `HotChocolate.Execution` to `HotChocolate`.
3. Build. Fix the `IDomainEvent` errors by deriving your events from `DomainEvent`.
4. Fix the `AddDomainEvent` errors by renaming to `RaiseDomainEvent` inside aggregates, and by giving
   the aggregate a method where the call was outside one.
5. Move any events raised by child entities up to their root.
6. Fix the `ClearDomainEvents` errors with a cast to `IHasDomainEvents`, or delete the code and use
   the interceptor.
7. Fix whatever diagnostics the generators report. Read the message; each one names the type.
   DDD00021 is a warning about aggregate references and can wait behind a `NoWarn` if the list is
   long.
8. Rename any member of your own called `CheckInvariants` or `EnsureInvariants`; both names are now
   generated onto every entity.
9. Replace `UseDomainEvents(...)` and `AddDomainEventInterceptor(...)` with
   `AddDDDToolkitEntityFramework(...)` and `UseDDDToolkit(...)`.
10. Add `AddDDDToolkitConventions()` to `ConfigureConventions`.
11. Scaffold a migration for the `Version` column.
12. Read section 6 and decide, per handler, whether it can run before the commit. Move the ones that
    cannot to the outbox.
13. Run your tests. Then, before you deploy, look at your GraphQL schema and your persisted queries.

Nothing on that list is the new surface. Invariants, integration events, sinks, the inbox and modules
are all opt-in, and none of them is worth turning on in the same change as the upgrade. Get green
first.
