# Testing aggregates

An aggregate is pure domain logic. No database, no clock, no network. It should be the easiest thing
in your system to test, and the test should read like the rule it encodes.

`DDDToolkit.Testing` is a small package that makes that true for domain events. Without it every
assertion about a raised event starts with a cast:

```csharp
var events = ((IHasDomainEvents)order).DequeueDomainEvents();
events.Should().ContainSingle(e => e is OrderCancelled);
```

That cast is deliberate, because [draining events is the persistence layer's job](domain-events.md#reading-and-draining).
A test is the one place it gets in the way. This package writes it for you, and gives you failures
that say what went wrong.

```bash
dotnet add package DDDToolkit.Testing
```

The package references `DDDToolkit` and nothing else. It brings no test framework and no assertion
library, so it works the same under xunit, NUnit, MSTest, FluentAssertions, Shouldly or plain
`if`-and-throw. A failed assertion throws `AggregateAssertionException`, which every runner reports
as a failed test.

## A complete test

Here is the aggregate. It is an ordinary declaration, nothing testing-specific about it:

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

And here is the test:

```csharp
using DDDToolkit.Testing;

public class OrderTests
{
    private static Order Draft() => new(OrderId.CreateUnique(), CustomerId.CreateUnique());

    [Fact]
    public void AddingALineRecordsIt()
    {
        AggregateScenario.Given(Draft())
            .When(order => order.AddLine(Sku.Of("SKU-1"), 3))
            .RaisedExactly<LineAdded>();
    }

    [Fact]
    public void ALineCarriesWhatWasOrdered()
    {
        var order = Draft();

        AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 3))
            .Raised(new LineAdded(order.Id, Sku.Of("SKU-1"), 3));
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
            .WhenThrows<ArgumentOutOfRangeException>(order => order.AddLine(Sku.Of("SKU-1"), 0));
    }
}
```

Three things are worth pointing out.

`Given` takes the aggregate your test built. It does not replay an event stream, because a DDDToolkit
aggregate is not event sourced. The arrange step is the constructor and whatever methods put the
aggregate into the state the rule is about. Anything else would be pretending.

`When` reports only the events that call raised. The `OrderPlaced` from the constructor is still on
the aggregate, but it is not in the batch you assert on, so you never have to filter it out.

`WhenThrows` asserts two things: that the expected exception came out, and that nothing was raised on
the way out. That second half is the one people forget. An aggregate that raises `OrderCancelled` and
then decides the order cannot be cancelled has published something that never happened.

## The assertions

Every method below is on the batch that `When` returns. They all return the batch, so they chain.

| Assertion | Passes when |
|---|---|
| `Raised<T>()` | At least one `T` was raised. A subtype of `T` counts. |
| `Raised<T>(e => ...)` | At least one `T` satisfies the predicate. |
| `Raised(expected)` | An event carries that payload. `EventId` and `OccurredAt` are ignored. |
| `RaisedNo<T>()` | No `T` was raised. |
| `RaisedNothing()` | Nothing at all was raised. |
| `RaisedExactly<T1, T2>()` | Exactly these types, in this order, and nothing else. |
| `RaisedExactly(params Type[])` | The same, for more than three types. |
| `RaisedExactlyThese(params IDomainEvent[])` | Exactly these events, in this order, compared by payload. |
| `SingleEvent<T>()` | Exactly one `T` was raised. Returns it. |
| `EventsOf<T>()` | Never fails. Returns every `T` that was raised. |

The batch is also an `IReadOnlyList<IDomainEvent>`, so anything the table does not cover you can
still do with LINQ and your own assertion library:

```csharp
var raised = AggregateScenario.Given(order).When(o => o.AddLine(sku, 3));

raised.SingleEvent<LineAdded>().Quantity.Should().BeGreaterThan(0);
raised.Select(e => e.OccurredAt).Should().BeInAscendingOrder();
```

### Comparing payloads

`Raised(expected)` compares the payload and nothing else:

```csharp
.Raised(new LineAdded(order.Id, Sku.Of("SKU-1"), 3));
```

That comparison exists because record equality cannot do the job. Two events with identical payloads
are never equal: `EventId` is a fresh `Guid` on every instance and `OccurredAt` is the moment of
construction. So `Assert.Equal(expected, raised)` fails every time, and teams work around it by
asserting member by member. The kit compares every public member of the event except the two the
`IDomainEvent` contract supplies, and reports the first one that differs.

Collections inside a payload compare element by element, so a `IReadOnlyList<Sku>` built with a
different concrete list type still matches.

## Failure messages

A testing package with unhelpful failures is worse than no package. Every message names the
expectation and lists what was actually raised, with payloads:

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

Keep the scenario in a variable and assert step by step. Each `When` sees only its own events, so
nothing needs draining in between:

```csharp
var scenario = AggregateScenario.Given(Draft());

scenario.PendingEvents.RaisedExactly<OrderPlaced>();

scenario.When(o => o.AddLine(Sku.Of("SKU-1"), 2)).RaisedExactly<LineAdded>();
scenario.When(o => o.Confirm()).RaisedExactly<OrderConfirmed>();
scenario.When(o => o.Ship("TRACK-1")).RaisedExactly<OrderShipped>();

scenario.Subject.TrackingCode.Should().Be("TRACK-1");
```

| Member | What it does |
|---|---|
| `Subject` | The aggregate, for asserting on its state. |
| `PendingEvents` | Everything the aggregate is holding, without taking it off. |
| `Drain()` | Takes the events off, the way a save does, and returns them. |
| `IgnorePendingEvents()` | Throws away what the arrange step raised. |

`Drain()` is worth using when the scenario spans something that would have been a save. It models
what `SaveChanges` does to the aggregate, so the second half of the test starts from the state the
second request would really see.

Asynchronous methods have `WhenAsync` and `WhenThrowsAsync`, which take a `Func<TAggregate, Task>`.

## Without a scenario

For a test that calls one method, two extension methods on `IHasDomainEvents` are enough:

```csharp
order.Cancel("out of stock");

order.PendingEvents().RaisedExactly<OrderPlaced, OrderCancelled>();
```

`PendingEvents()` leaves the events on the aggregate. `DrainEvents()` takes them off and returns the
same kind of batch. `AsScenario()` is the same thing as `AggregateScenario.Given`.

## What this kit does not do

**It does not control the clock, and it does not need to.** The assertions here never compare
`EventId` or `OccurredAt`, so nothing in this package cares what they say. The hook belongs to the
core package rather than to this one: `DomainEventClock.Use(timeProvider)` replaces the clock both
initialisers read, for the current asynchronous flow only, so a test can fix the timestamp of an
event the aggregate raised for itself.

```csharp
using var scope = DomainEventClock.Use(clock);

AggregateScenario.Given(Draft())
    .When(order => order.Cancel("out of stock"))
    .Raised<OrderCancelled>(e => e.OccurredAt == clock.GetUtcNow());
```

See [Deterministic time in tests](domain-events.md#deterministic-time-in-tests) for the scope rules
and for fixing the whole of `EventId`.

Reach for it when the time is what you are asserting on. When the time is something the domain
reasons about, it belongs in the payload instead, where a rule can read it:

```csharp
public sealed record OrderCancelled(OrderId OrderId, string Reason, DateTimeOffset CancelledAt) : DomainEvent;
```

Then pass the time in from your own clock abstraction and assert on `CancelledAt` like any other
member. `EventId` and `OccurredAt` stay what they are: the identity and the wall-clock stamp of the
occurrence, useful for idempotency and ordering, not for describing the domain.

**It is not an event sourcing kit.** There is no `Given(events)` that rebuilds an aggregate from a
stream, because these aggregates are not built that way. Arrange with the constructor and with real
method calls.

**It does not test persistence.** Nothing here touches a `DbContext`. Whether your events reach a
handler, survive a rollback or arrive twice is a question about dispatch, not about the aggregate;
see [Entity Framework](entity-framework.md) for the interceptor, the outbox and the concurrency
token, and test those against a real database.

**It does not assert on your aggregate's state.** `Subject` hands you the aggregate and your own
assertion library takes it from there. Two libraries fighting over one assertion style is worse than
none.

**It does not assert on invariants.** There is no `.Violates<T>()`, because there does not need to
be: both stages are public on every entity, so your own assertion library already covers them.

```csharp
var order = new Order(OrderId.CreateUnique(), customerId);
order.Place();

Assert.Throws<InvariantViolationException>(() => order.EnsureInvariants());

// Or without an exception, asserting on the rule rather than on the message:
order.GetInvariantViolations().Should().ContainSingle(v => v.Code == Order.MustHaveLines.ViolationCode);
```

Asking the root asks the whole aggregate, so a test for a child's rule needs no loop and no second
subject. The violation names the child that reported it, which is what the assertion should be about:

```csharp
order.AddLine(sku: "", quantity: 1);

order.GetInvariantViolations().Should().ContainSingle(v =>
    v.Code == OrderLine.MustNameASku.ViolationCode && v.EntityId!.Equals(order.Lines[0].Id));
```

A rule written as a nested `IInvariant<T>` is also an ordinary type, so it can be tested on its own
with no aggregate mutation and nothing to catch: `new Order.MustHaveLines().Check(order)` returns
`null` when the rule holds. See [Invariants](invariants.md#by-hand).

## See also

- [Domain events](domain-events.md) for raising, draining and stable names.
- [Invariants](invariants.md) for the two stages, the two shapes of rule, and checking both without a
  database.
- [Entities and aggregates](entities-and-aggregates.md) for what an aggregate root is and why only
  the root raises events.
- [Entity Framework](entity-framework.md) for delivering the events you asserted on here.
