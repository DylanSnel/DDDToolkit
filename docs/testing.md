# Testing aggregates

`DDDToolkit.Testing` is a package for testing what an aggregate does: which domain events a method raises, and
whether the method refuses. Such a test needs no database, clock or network, because an aggregate is plain
domain code.

Without the package, every assertion about a raised event starts with a cast to `IHasDomainEvents`:

```csharp
var events = ((IHasDomainEvents)order).DequeueDomainEvents();
events.Should().ContainSingle(e => e is OrderCancelled);
```

The toolkit puts `DequeueDomainEvents()` behind that cast on purpose, because
[draining events is the job of the persistence layer](domain-events.md#reading-and-draining). The package makes
the cast for you, and its failure messages list the events that were raised.

```bash
dotnet add package Temp.DDDToolkit.Testing --prerelease
```

The package references `DDDToolkit` and nothing else. It brings no test framework and no assertion library, so it
works with xunit, NUnit, MSTest, FluentAssertions, Shouldly, or a plain `if` and `throw`. A failed assertion
throws `AggregateAssertionException`, and every test runner reports that as a failed test.

## A complete test

The aggregate is an ordinary declaration. Nothing in it is specific to tests:

```csharp
[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public Order(OrderId id, CustomerId customer) : base(id)
    {
        Customer = customer;
        RaiseDomainEvent(new OrderPlaced(id, customer));
    }

    public CustomerId Customer { get; private set; }

    public OrderStatus Status { get; private set; } = OrderStatus.Draft;

    public partial IReadOnlyList<OrderLine> Lines { get; }

    public void AddLine(Sku sku, int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "A line needs at least one item.");
        }

        if (Status != OrderStatus.Draft)
        {
            throw new InvalidOperationException($"A {Status} order cannot take new lines.");
        }

        _lines.Add(new OrderLine(sku, quantity));
        RaiseDomainEvent(new LineAdded(Id, sku, quantity));
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Shipped)
        {
            throw new InvalidOperationException("A shipped order cannot be cancelled.");
        }

        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        Status = OrderStatus.Cancelled;
        RaiseDomainEvent(new OrderCancelled(Id, reason));
    }
}
```

The tests:

```csharp
using DDDToolkit.Testing;

public class OrderTests
{
    private static Order Draft() => new(OrderId.CreateUnique(), CustomerId.CreateUnique());

    [Fact]
    public void AddingALineRecordsIt()
    {
        AggregateScenario.Given(Draft())
            .When(order => order.AddLine(Sku.From("SKU-1"), 3))
            .RaisedExactly<LineAdded>();
    }

    [Fact]
    public void ALineCarriesWhatWasOrdered()
    {
        var order = Draft();

        AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.From("SKU-1"), 3))
            .Raised(new LineAdded(order.Id, Sku.From("SKU-1"), 3));
    }

    [Fact]
    public void CancellingTwiceCancelsOnce()
    {
        var order = Draft();
        order.Cancel("out of stock");

        AggregateScenario.Given(order)
            .When(o => o.Cancel("out of stock"))
            .RaisedNothing();
    }

    [Fact]
    public void AnEmptyQuantityIsRefusedBeforeAnythingHappens()
    {
        AggregateScenario.Given(Draft())
            .WhenThrows<ArgumentOutOfRangeException>(order => order.AddLine(Sku.From("SKU-1"), 0));
    }
}
```

- **`Given` takes the aggregate that your test built.** It replays no event stream, because a DDDToolkit
  aggregate is not event sourced. The arrange step is the constructor, plus the method calls that put the
  aggregate into the state that the rule is about.
- **`When` returns only the events of that call.** The `OrderPlaced` event from the constructor is still on the
  aggregate, but it is not in the batch that you assert on. So you never filter it out.
- **`WhenThrows` checks two things.** The call threw the expected exception, or a subtype of it, and the call
  raised no event before it threw. The second check catches an aggregate that raises `OrderCancelled` and then
  refuses to cancel. Such an aggregate has published an event about something that did not happen.
  `WhenThrows` returns the exception, so you can assert on its message.

## The assertions

The methods below are on the batch that `When` returns, a `RaisedEvents`. The assertions return the batch, so
they chain. `RaisedNothing()` returns nothing. `SingleEvent<T>()` and `EventsOf<T>()` return events.

| Assertion | Passes when |
|---|---|
| `Raised<T>()` | At least one `T` was raised. A subtype of `T` counts. |
| `Raised<T>(e => ...)` | At least one `T` satisfies the predicate. |
| `Raised(expected)` | An event of the same type carries that payload. `EventId` and `OccurredAt` are not compared. |
| `RaisedNo<T>()` | No `T` was raised. |
| `RaisedNothing()` | No event was raised. |
| `RaisedExactly<T1>()`, `<T1, T2>()`, `<T1, T2, T3>()` | Exactly these types were raised, in this order, and no other event. A subtype does not count. |
| `RaisedExactly(params Type[])` | The same, for any number of types. |
| `RaisedExactlyThese(params IDomainEvent[])` | Exactly these events were raised, in this order, compared by payload. |
| `SingleEvent<T>()` | Exactly one `T` was raised. Returns it. |
| `EventsOf<T>()` | Never fails. Returns every `T` that was raised. |

The batch is also an `IReadOnlyList<IDomainEvent>`. For a check that the table does not cover, use LINQ and your
own assertion library:

```csharp
var raised = AggregateScenario.Given(order).When(o => o.AddLine(sku, 3));

raised.SingleEvent<LineAdded>().Quantity.Should().BeGreaterThan(0);
raised.Select(e => e.OccurredAt).Should().BeInAscendingOrder();
```

### Comparing payloads

`Raised(expected)` compares the payload of an event, without `EventId` and `OccurredAt`:

```csharp
.Raised(new LineAdded(order.Id, Sku.From("SKU-1"), 3));
```

Record equality cannot compare two events. Each instance gets a new `EventId`, and `OccurredAt` is the moment the
instance was made. So `Assert.Equal(expected, raised)` always fails, even when the payloads are the same.
`Raised(expected)` compares every public property of the event except the two that `IDomainEvent` declares. A
failure names the first property that differs.

A collection in the payload is compared element by element. So an `IReadOnlyList<Sku>` built with another list
type still matches.

## Failure messages

Every failure message names the expectation, and lists the events that were raised, with their payloads:

```text
Expected Order to raise exactly OrderShipped.
The first difference is at index 0: expected OrderShipped, was OrderConfirmed.

1 event was raised while running the action:
  [0] OrderConfirmed { OrderId = ORD_edbace54-787b-4345-bd30-bff77a479567, LineCount = 1 }
```

```text
Expected Order to raise OrderCancelled { OrderId = ORD_edba..., Reason = "duplicate order" }, but no raised event carried that payload.
  OrderCancelled { OrderId = ORD_edba..., Reason = "out of stock" }: Reason was "out of stock", expected "duplicate order"

1 event was raised while running the action:
  [0] OrderCancelled { OrderId = ORD_edba..., Reason = "out of stock" }

EventId and OccurredAt are never compared.
```

```text
SloppyOrder threw InvalidOperationException as expected, but it raised 1 event first.
An aggregate that raises an event and then refuses the command leaves an event describing something that never happened.

1 event was raised while running the action:
  [0] OrderCancelled { OrderId = ORD_00000000-0000-0000-0000-000000000000, Reason = "late" }
```

## Scenarios with more than one step

Keep the scenario in a variable, and assert after each step. Each `When` returns only the events of its own call,
so you drain nothing between the steps:

```csharp
var scenario = AggregateScenario.Given(Draft());

scenario.PendingEvents.RaisedExactly<OrderPlaced>();

scenario.When(o => o.AddLine(Sku.From("SKU-1"), 2)).RaisedExactly<LineAdded>();
scenario.When(o => o.Confirm()).RaisedExactly<OrderConfirmed>();
scenario.When(o => o.Ship("TRACK-1")).RaisedExactly<OrderShipped>();

scenario.Subject.TrackingCode.Should().Be("TRACK-1");
```

| Member | What it does |
|---|---|
| `Subject` | Returns the aggregate, for assertions on its state. |
| `PendingEvents` | Returns every event on the aggregate, and leaves the events there. |
| `Drain()` | Takes the events off the aggregate, as a save does, and returns them. |
| `IgnorePendingEvents()` | Clears the events that the arrange step raised. |

- Use `Drain()` where the real flow saves. It does to the aggregate what `SaveChanges` does. So the rest of the
  test starts from the state that the next request sees.
- `When` fails with `AggregateAssertionException` when the call removes events from the aggregate. It then
  cannot tell which events the call raised. Assert on `PendingEvents` in that case.
- Asynchronous methods have `WhenAsync` and `WhenThrowsAsync`, which take a `Func<TAggregate, Task>`.

## Assertions without a scenario

For a test that calls one method, two extension methods on `IHasDomainEvents` are enough:

```csharp
order.Cancel("out of stock");

order.PendingEvents().RaisedExactly<OrderPlaced, OrderCancelled>();
```

- `PendingEvents()` leaves the events on the aggregate.
- `DrainEvents()` takes the events off and returns the same kind of batch.
- `AsScenario()` does the same as `AggregateScenario.Given`.

## Testing invariants

An invariant test needs no scenario and no database. Every entity has two public methods for its invariants.
`EnsureInvariants()` throws, and `GetInvariantViolations()` returns a list. A test calls one of them, and your
own assertion library checks the result. For example, the order has a named rule, `Order.MustHaveLines`, which
requires at least one line on a confirmed order ([Invariants](invariants.md#a-named-invariant) shows how to write
one):

```csharp
var order = Draft();
order.Confirm();

Assert.Throws<InvariantViolationException>(() => order.EnsureInvariants());

// Or without an exception, asserting on the rule rather than on the message:
order.GetInvariantViolations().Should().ContainSingle(v => v.Code == Order.MustHaveLines.ViolationCode);
```

Prefer the second form. The code is the name of the rule and does not change. The message is written for people,
and can be reworded or translated.

`GetInvariantViolations()` on the root also checks every child entity. So a test of a child's rule needs no loop
and no second subject. The violation names the child that reported it in `EntityId`, and the assertion checks
that:

```csharp
var order = Draft();
order.AddLine(Sku.From(""), 1);

order.GetInvariantViolations().Should().ContainSingle(v =>
    v.Code == OrderLine.MustNameASku.ViolationCode && v.EntityId!.Equals(order.Lines[0].Id));
```

A rule written as a nested `IInvariant<T>` is an ordinary type. A test can call the rule on its own, without
changing the aggregate and without catching an exception. `new Order.MustHaveLines().Check(order)` returns `null`
when the rule holds ([Running the checks by hand](invariants.md#running-the-checks-by-hand)).

## Deterministic time in tests

`OccurredAt` and `EventId` are `init` properties. The aggregate makes the event with `new OrderCancelled(...)`, so
a test that calls `order.Cancel(reason)` cannot set them. The event gets the time of the system clock.

`DomainEventClock.Use(clock)` replaces the clock that both properties read, for the current asynchronous flow.
`DomainEventClock` is in the core package (namespace `DDDToolkit.BaseTypes`), because the assertions of this
package never compare `EventId` or `OccurredAt`. Use it in a test that asserts on the time itself:

```csharp
using DDDToolkit.BaseTypes;

var clock = new FakeTimeProvider(new DateTimeOffset(2024, 1, 21, 12, 0, 0, TimeSpan.Zero));

using var scope = DomainEventClock.Use(clock);

AggregateScenario.Given(Draft())
    .When(order => order.Cancel("out of stock"))
    .Raised<OrderCancelled>(e => e.OccurredAt == clock.GetUtcNow());
```

Any `TimeProvider` works. The toolkit ships no fake clock. Use `FakeTimeProvider` from
`Microsoft.Extensions.TimeProvider.Testing`, or a five-line subclass of `TimeProvider`.

- `DomainEventClock` keeps the clock in an `AsyncLocal`. The clock applies to the flow that opened the scope, and
  to everything that flow calls, awaited work included.
- Test classes that run in parallel each see their own clock. A test that forgets to dispose its scope does not
  change the clock of the tests after it.
- Disposing the scope restores the previous clock, so scopes nest.
- The clock does not reach work that was already running when the scope opened, such as a hosted service that
  started earlier. Events raised there get the system clock.

`EventId` follows the same clock. A version 7 `Guid` is a timestamp plus random bits. So a fixed clock fixes the
order of the ids, and the ids stay unique. To make the whole id predictable, pass a factory as well:

```csharp
var next = 0;
using var scope = DomainEventClock.Use(clock, _ => new Guid(++next, 0, 0, new byte[8]));
```

The factory must return a new value on every call. `EventId` is the primary key of the outbox table, and handlers
deduplicate on it. A repeated value makes the save fail.

Without a factory, a clock set before 1970 makes every new event throw `ArgumentOutOfRangeException`. A version 7
`Guid` holds Unix milliseconds and cannot hold an earlier time. Start a fake clock at a realistic date.

Use the clock when the test asserts on `OccurredAt`. When the domain itself uses the time, put the time in the
payload, where a rule can read it:

```csharp
public sealed record OrderCancelled(OrderId OrderId, string Reason, DateTimeOffset CancelledAt) : DomainEvent;
```

Pass the time in from your own clock, and assert on `CancelledAt` like any other property. `EventId` and
`OccurredAt` then keep their own purpose: the identity of the event and the system time it happened at, for
idempotency and ordering.

Call `DomainEventClock.Use` in tests, and when you replay recorded events. In production code, an event with a
time other than the time it happened gives every consumer a wrong time.

## What the package does not do

- **No clock.** The assertions of this package never compare `EventId` or `OccurredAt`. The core package sets the
  clock ([Deterministic time in tests](#deterministic-time-in-tests)).
- **No event sourcing.** There is no `Given(events)` that rebuilds an aggregate from a stream, because DDDToolkit
  aggregates are not built from events. Arrange with the constructor and with real method calls.
- **No persistence.** Nothing in the package touches a `DbContext`. Whether an event reaches a handler, survives
  a rollback or arrives twice depends on dispatch. Test that against a real database.
  [Entity Framework](entity-framework.md) covers the interceptor, the outbox and the concurrency token.
- **No assertions on state.** `Subject` returns the aggregate, and your own assertion library checks its state.
  The package adds no second assertion style next to yours.
- **No assertions on invariants.** There is no `.Violates<T>()`. Both invariant methods are public on every
  entity, so your own assertion library covers them ([Testing invariants](#testing-invariants)).

## See also

- [Domain events](domain-events.md) for raising, draining and stable names.
- [Invariants](invariants.md) for the two stages, the two shapes of rule, and checking both without a
  database.
- [Entities and aggregates](entities-and-aggregates.md) for what an aggregate root is, and why only the root
  raises events.
- [Entity Framework](entity-framework.md) for delivering the events you asserted on here.
