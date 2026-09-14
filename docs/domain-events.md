# Domain events

A domain event records something that happened. The toolkit gives every event an identity and a
timestamp, restricts raising to the aggregate that owns the event, and restricts draining to the
persistence layer.

## Declaring an event

```csharp
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;

[DomainEventName("ordering.order-placed")]
public sealed record OrderPlaced(OrderId OrderId, CustomerId Customer) : DomainEvent;
```

`DomainEvent` supplies:

```csharp
public Guid EventId { get; init; }            // Guid.CreateVersion7(), time ordered
public DateTimeOffset OccurredAt { get; init; }   // UtcNow
```

Both are `init`, so code that constructs the event can supply its own values:

```csharp
var evt = new OrderPlaced(id, customer) { OccurredAt = recorded };
```

That covers replaying events you already have. It does not cover testing, because the code that
constructs an event is the aggregate, not your test. See
[Deterministic time in tests](#deterministic-time-in-tests).

You can implement `IDomainEvent` directly instead of deriving from `DomainEvent`; you then provide
`EventId` and `OccurredAt` yourself.

## Why the id and the timestamp

`EventId` is the idempotency key. Any at-least-once delivery mechanism will hand the same event to a
handler twice eventually, and the handler needs a stable way to recognise it. `OccurredAt` records
when the thing happened, which is not the same as when a handler runs.

## Stable names

`[DomainEventName]` decouples the wire name from the class name, so renaming or moving the class does
not break anything that stored or published the old name:

```csharp
[DomainEventName("ordering.order-placed")]
public sealed record OrderPlaced(...) : DomainEvent;
```

```csharp
DomainEventName.Of<OrderPlaced>();   // "ordering.order-placed"
DomainEventName.Of(someEvent);       // same, from an instance
```

Without the attribute the name is the class name, which is fine until the first rename. Add the
attribute to any event that is serialized, stored or published.

## Raising

Raising is `protected` on `AggregateRoot<TId>`, so an event can only be created by the aggregate it
describes:

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public void Cancel(CancellationReason reason)
    {
        if (Status == OrderStatus.Shipped)
        {
            throw new InvalidOperationException("A shipped order cannot be cancelled.");
        }

        Status = OrderStatus.Cancelled;
        RaiseDomainEvent(new OrderCancelled(Id, reason));
    }
}
```

Nothing outside can inject an event into an aggregate. That matters when events are your integration
contract: an event that says the order was cancelled should only exist if the order actually
cancelled.

Child entities marked `[Entity<TId>]` have no event list. A change to a child is a change to its
aggregate, so the root raises the event.

## Reading and draining

```csharp
IReadOnlyList<IDomainEvent> pending = order.DomainEvents;
```

`DomainEvents` is a read-only view, marked `[Internal]` so it stays out of your tables, your JSON and
your GraphQL schema.

Draining is deliberately awkward to reach. `IHasDomainEvents` is implemented explicitly:

```csharp
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    IReadOnlyList<IDomainEvent> DequeueDomainEvents();   // returns and empties
    void ClearDomainEvents();                            // discards
}
```

```csharp
var events = ((IHasDomainEvents)order).DequeueDomainEvents();
```

You will not normally write that line: the Entity Framework integration does it during save. The cast
is the point. Application code that can silently call `ClearDomainEvents()` can write a row whose
event never happened, and in a system where events are the integration contract that is a data
integrity problem, not a style issue.

## Delivery

Raising an event puts it in a list on the aggregate. Getting it to a handler is a separate concern
with a real trade-off, handled by `DDDToolkit.EntityFramework`.

**In-process dispatch** runs handlers during `SaveChanges`, before the commit. Handler changes to the
same `DbContext` ride along in the same transaction, and a throwing handler aborts the save. It is
simple and transactional, but it is best-effort: nothing survives a process crash, and handlers must
be fast because they hold the transaction open.

**Outbox delivery** writes one row per event in the same transaction as the aggregate, and a separate
reader delivers them afterwards. The write is atomic with the data, so an event is never lost and
never published for a transaction that rolled back. Delivery is at-least-once, so handlers must be
idempotent, keyed on `EventId`.

Choose in-process for side effects inside the same database, and the outbox for anything that leaves
the process. The configuration for both is covered in the Entity Framework documentation.

## Testing

Events make aggregates testable without a database: act, then assert on what was raised.

```csharp
var order = new Order(OrderId.CreateSequential(), customerId);
order.Cancel(CancellationReason.OutOfStock);

var events = ((IHasDomainEvents)order).DequeueDomainEvents();

events.Should().ContainSingle(e => e is OrderCancelled);
```

### Deterministic time in tests

`OccurredAt` and `EventId` are `init`, but the aggregate is what calls `new OrderCancelled(...)`. A
test that calls `order.Cancel(reason)` never reaches that constructor, so it cannot pass an
initialiser and the event is stamped with the wall clock.

`DomainEventClock` replaces the clock both initialisers read:

```csharp
using DDDToolkit.BaseTypes;

var clock = new FakeTimeProvider(new DateTimeOffset(2024, 1, 21, 12, 0, 0, TimeSpan.Zero));

using var scope = DomainEventClock.Use(clock);

var order = new Order(orderId, customerId);
order.Cancel(CancellationReason.OutOfStock);

order.DomainEvents.Should().OnlyContain(e => e.OccurredAt == clock.GetUtcNow());
```

Any `TimeProvider` will do. The toolkit does not ship a fake one: `FakeTimeProvider` from
`Microsoft.Extensions.TimeProvider.Testing` already exists, and a five-line subclass of
`TimeProvider` works just as well.

The clock is held in an `AsyncLocal`, not in a static property, so it applies to the flow that opened
the scope and to whatever that flow calls, including awaited work. Two test classes running in
parallel do not see each other's clock, and a test that forgets to dispose the scope cannot poison
the tests that run after it. Disposing restores the previous clock, so scopes nest.

It does not reach work that was already running when you opened the scope, such as a hosted service
started earlier; events raised there keep the system clock.

`EventId` follows the same clock. A version 7 `Guid` is a timestamp plus random bits, so a fixed
clock fixes the ordering and leaves the id unique. When you need the whole id to be predictable,
supply the factory as well:

```csharp
var next = 0;
using var scope = DomainEventClock.Use(clock, _ => new Guid(++next, 0, 0, new byte[8]));
```

The values still have to be distinct. `EventId` is the primary key of the outbox table and the key
handlers deduplicate on, so a repeated one makes the save fail.

One sharp edge: a clock set before 1970 throws, because a version 7 `Guid` encodes Unix
milliseconds and cannot represent an earlier instant. Start fake clocks at a realistic date.

Outside tests, leave the clock alone. An event that reports a time other than the time it happened
is a lie told to every consumer downstream.
