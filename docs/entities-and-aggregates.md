# Entities and aggregates

An entity has identity: it is the same entity tomorrow even when every property changed. An aggregate
root is an entity that owns a cluster of other objects and forms the consistency boundary around
them. The toolkit distinguishes the two, and the distinction carries real behaviour.

| | `[Entity<TId>]` | `[AggregateRoot<TId>]` |
|---|---|---|
| Base type | `Entity<TId>` | `AggregateRoot<TId>` |
| Identity and equality | Yes | Yes |
| Domain events | No | Yes |
| Concurrency version | No | Yes |
| `CheckInvariants()` seam | Yes, but nothing calls it for you | Yes, called before every save |
| Entity Framework | Mapped as an owned type | Mapped as its own entity type |

Use `[AggregateRoot<TId>]` for the object you load, save and reference from elsewhere. Use
`[Entity<TId>]` for something that only exists inside one aggregate, like an order line.

## Declaring an aggregate

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public Order(OrderId id, CustomerId customer) : base(id)
    {
        Customer = customer;
        RaiseDomainEvent(new OrderPlaced(id, customer));
    }

    public CustomerId Customer { get; private set; }

    public OrderStatus Status { get; private set; } = OrderStatus.Draft;
}
```

The generator supplies the base class and a protected parameterless constructor for Entity Framework
and serializers. Your own constructor calls `base(id)`.

### Declaring the identifier with it

`OrderId` above is a type you declared with `[EntityId<Guid>]`. When the identifier is only ever used
to identify this one aggregate, you can skip that declaration and name the raw value instead:

```csharp
[AggregateRoot<Guid>("ORD")]
public partial class Order { }          // also generates OrderId
```

The toolkit then generates `OrderId` as well, as a `readonly partial record struct` with everything an
explicitly declared identifier gets. The name is the entity's name with `Id` appended, and the first
argument is the optional prefix. `[Entity<T>]` does the same for a child entity.

Keep the separate `[EntityId<Guid>]` declaration for an identifier that other aggregates, DTOs or API
contracts refer to; a type other people read deserves a declaration they can find. See
[Identifiers](identifiers.md#letting-the-entity-declare-the-id).

Equality comes from the base type and compares identifiers, so two instances of the same order loaded
in different contexts are equal. `==`, `!=`, `Equals` and `GetHashCode` are all consistent and
null-safe.

## Read-only collections

Exposing a `List<T>` from an aggregate lets any caller add to it and bypass your invariants. Exposing
`_items.AsReadOnly()` from a hand-written field is correct but tedious, and Entity Framework then
needs to be told about the field. The toolkit does both for you.

Declare the property you want, get-only and `partial`:

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public partial IReadOnlyList<OrderLine> Lines { get; }

    public void AddLine(OrderLine line) => _lines.Add(line);

    public void RemoveLine(OrderLineId id) => _lines.RemoveAll(l => l.Id == id);
}
```

The generator writes:

```csharp
private readonly List<OrderLine> _lines = new();

[BackingField(nameof(_lines))]
public partial IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
```

Callers see a read-only view that cannot be cast back to `List<T>`; the aggregate mutates through
`_lines`. Entity Framework reads and writes the field directly thanks to `[BackingField]`, so it
never tries to write through the read-only property.

### Supported property types

| Declared type | Backing field | View |
|---|---|---|
| `IReadOnlyList<T>` | `List<T>` | `AsReadOnly()` |
| `IReadOnlyCollection<T>` | `List<T>` | `AsReadOnly()` |
| `IEnumerable<T>` | `List<T>` | `AsReadOnly()` |
| `IReadOnlySet<T>` | `HashSet<T>` | `ReadOnlySet<T>` wrapper |

The field name is the property name in camel case with a leading underscore: `Lines` gives `_lines`,
`OrderLines` gives `_orderLines`. Accessibility and `virtual`, `override` and `sealed` are mirrored
from your declaration, so a `protected partial IReadOnlyList<T>` stays protected.

The `[BackingField]` annotation is only emitted when the project references Entity Framework, so the
same declaration works in a domain project with no persistence dependency.

### The property must be get-only

```csharp
public partial IReadOnlyList<OrderLine> Lines { get; set; }   // DDD00020
```

A setter would let a caller replace the whole collection, which defeats the purpose. Declaring one
reports [DDD00020](diagnostics.md#ddd00020) and the property is left unimplemented, so the build
fails loudly rather than silently producing something you did not ask for.

## Domain events

Only aggregate roots raise events, because only the root is a consistency boundary. Raising is
`protected`, so an event can only come from inside the aggregate that owns it:

```csharp
public void Ship(TrackingCode code)
{
    if (Status != OrderStatus.Paid)
    {
        throw new InvalidOperationException("Only a paid order can ship.");
    }

    Status = OrderStatus.Shipped;
    RaiseDomainEvent(new OrderShipped(Id, code));
}
```

Draining is not part of the public surface either. `IHasDomainEvents` is implemented explicitly, so
the persistence layer can collect events and application code cannot quietly discard them:

```csharp
IReadOnlyList<IDomainEvent> pending = order.DomainEvents;          // read-only view

var events = ((IHasDomainEvents)order).DequeueDomainEvents();      // persistence only
```

See [Domain events](domain-events.md).

## Optimistic concurrency

Every aggregate root carries a version:

```csharp
public long Version { get; private set; }
```

It is 0 for a new aggregate. The Entity Framework integration maps it as a concurrency token and
increments it on every save that touches the aggregate, including saves that only changed something
it owns. Two users editing the same order produce a `ConcurrencyConflictException` for the second
one, naming the aggregate type and id, rather than a generic `DbUpdateException`:

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

The version is what makes an aggregate a unit of consistency. Without it two concurrent saves are
last-write-wins, silently.

## Invariants

The version says nobody else changed the aggregate. It says nothing about whether the aggregate is
*consistent*. That is what the generated `CheckInvariants()` seam is for:

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

`UseDDDToolkit` registers an interceptor that calls `EnsureInvariants()` on every aggregate root a
`SaveChanges` adds or modifies, so a broken rule stops the save and nothing is written.
`EnsureInvariants()` is public, so a test can call it with no database in sight. An aggregate that
implements no seam costs nothing: the compiler erases an unimplemented `partial void` and every call
to it.

See [Invariants](invariants.md) for the interceptor order, for reporting several violations at once,
and for why an invariant across two aggregates is a design question rather than a missing feature.

## Auditing and soft delete

The toolkit has no `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `IAuditable`, `IsDeleted` or
`ISoftDeletable`, and it is not going to grow them. `Version` is the only bookkeeping field an
aggregate root gets, and it is there because optimistic concurrency is a correctness property of the
consistency boundary, not a reporting feature.

This is a deliberate position, not an omission. "Who last touched this row" is a question about your
rows and your users, and the answer belongs to your persistence layer. Putting it on the domain type
means every aggregate in the system carries four properties its behaviour never reads, your
constructors take a user, and your unit tests need a logged-in principal to build an order. A base
class in your own solution can do it in twenty lines and can say what your organisation actually
means by "modified", which no library can guess.

So decide which of these you are actually asking for.

**It is domain data.** "Who approved this order", "when was it cancelled" are facts the business
talks about and rules depend on. Model them as ordinary properties, set by the method that does the
thing:

```csharp
public void Approve(EmployeeId approver, DateTimeOffset at)
{
    Approver = approver;
    ApprovedAt = at;
    RaiseDomainEvent(new OrderApproved(Id, approver, at));
}
```

That is not auditing. It is the domain, and it is testable without a database.

**It is row bookkeeping.** Nothing in the domain reads it; you want it for support and forensics.
Keep it out of the domain type entirely and use Entity Framework shadow properties, so the columns
exist and the C# class does not:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                 .Where(type => typeof(IAggregateRoot).IsAssignableFrom(type.ClrType)))
    {
        modelBuilder.Entity(entityType.ClrType).Property<DateTimeOffset>("CreatedAt");
        modelBuilder.Entity(entityType.ClrType).Property<string?>("CreatedBy");
        modelBuilder.Entity(entityType.ClrType).Property<DateTimeOffset?>("UpdatedAt");
        modelBuilder.Entity(entityType.ClrType).Property<string?>("UpdatedBy");
    }
}
```

Fill them from an interceptor of your own, registered beside the toolkit's:

```csharp
public sealed class AuditInterceptor(ICurrentUser user, TimeProvider clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        foreach (var entry in eventData.Context!.ChangeTracker.Entries())
        {
            if (entry.Metadata.FindProperty("CreatedAt") is null)
            {
                continue;   // not an audited type
            }

            if (entry.State == EntityState.Added)
            {
                entry.Property("CreatedAt").CurrentValue = clock.GetUtcNow();
                entry.Property("CreatedBy").CurrentValue = user.Name;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property("UpdatedAt").CurrentValue = clock.GetUtcNow();
                entry.Property("UpdatedBy").CurrentValue = user.Name;
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
```

```csharp
db.UseDDDToolkit(serviceProvider)
  .AddInterceptors(serviceProvider.GetRequiredService<AuditInterceptor>());
```

Read a shadow property back with `context.Entry(order).Property<DateTimeOffset>("CreatedAt")`, or
query it with `EF.Property<DateTimeOffset>(order, "CreatedAt")`. If you would rather have real
properties, put them on a base class of your own between your aggregates and `AggregateRoot<TId>`;
the generators do not care what your aggregate inherits from as long as it ends up at
`AggregateRoot<TId>`.

**It is a history of what happened.** Then you already have it. The domain events an aggregate
raises are a record of every meaningful change, written by the aggregate that knows what the change
meant. Turn on the [outbox](entity-framework.md#the-outbox) and keep the rows instead of deleting
them, or write your own handler that appends them to an event table. An audit trail assembled from
`UpdatedBy` columns tells you a row changed; a trail of `OrderCancelled` tells you what happened, and
why.

**Soft delete** is the same answer. Entity Framework does it with a flag and a global query filter,
and neither needs the toolkit:

```csharp
modelBuilder.Entity<Order>().HasQueryFilter(order => !EF.Property<bool>(order, "IsDeleted"));
```

Two things to know before you reach for it. A filtered row still occupies its unique indexes, so a
"deleted" customer still owns their email address. And query filters do not apply to raw SQL or to
anything outside Entity Framework, so the flag is a convention your reporting jobs have to know
about. Often the honest model is a domain state, `OrderStatus.Cancelled`, which the rest of the
domain can reason about, rather than a row that pretends not to exist.

## Child entities

```csharp
[Entity<OrderLineId>]
public partial class OrderLine
{
    public OrderLine(OrderLineId id, ProductId product, int quantity) : base(id)
        => (Product, Quantity) = (product, quantity);

    public ProductId Product { get; private set; }

    public int Quantity { get; private set; }
}
```

A child entity has identity and equality but no events and no version, because it is not a
consistency boundary; its root is. With Entity Framework referenced it is annotated `[Owned]`, so it
is loaded and saved with the aggregate that owns it.

Child entities may declare partial collection properties exactly like roots.

## Reference other aggregates by id

An aggregate owns everything inside it and nothing outside it. Another aggregate is referenced by its
id:

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public CustomerId Buyer { get; private set; }        // an id, not a Customer

    public partial IReadOnlyList<OrderLine> Lines { get; }   // owned, so a reference
}
```

Holding the `Customer` itself instead reports [DDD00021](diagnostics.md#ddd00021).

### Why the id is what keeps the boundary

An aggregate is two boundaries at once, and a direct reference breaks both.

It is a **loading boundary**. `context.Orders.Find(id)` should load an order, its lines and nothing
else. The moment `Order` has a `Customer` property, Entity Framework has a navigation to follow.
Either it loads the customer with every order, or it leaves a proxy that loads one later, per order,
in a loop nobody wrote. An `OrderId`-shaped hole in the object graph is a decision you can see: the
code that needs the customer asks for it, by id, through the customer repository.

It is a **consistency boundary**. Every root carries its own `Version`, and the point of that number
is that one save changes one aggregate and one version says whether anyone else changed it. With a
direct reference, `order.Buyer.Rename(...)` inside an order method makes a single `SaveChanges` write
two roots in one transaction. Now a conflict on the customer rolls back the order, the order's
version says nothing about the customer, and the two aggregates are one aggregate wearing two names.

The id also survives things a reference does not. It serialises into an event, crosses a queue, goes
into a URL, and still means the same customer when the two aggregates end up in different services or
different databases. A reference only works while both objects are in the same unit of work.

None of this is unique to this toolkit; it is the oldest rule in the pattern. What the toolkit adds is
that it already knows which of your types are aggregate roots and which are ids, so it can check.

### The one reference that is allowed

A child entity may navigate back to the root that owns it:

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public partial IReadOnlyList<OrderLine> Lines { get; }
}

[Entity<OrderLineId>]
public partial class OrderLine
{
    public Order Order { get; private set; } = default!;   // allowed
}
```

This one does not widen anything. The line is loaded with the order, saved with the order and cannot
outlive it, and Entity Framework uses the inverse navigation when it maps the owned type.

The toolkit checks the claim rather than taking it: `Order` has to hold `OrderLine` back, in a
collection or in a single property, and the reference from the child has to be single valued. A child
entity holding a root that does not own it reports DDD00021 like anything else.

### When you disagree

DDD00021 is a warning, and nothing about generation changes when it fires. A model that really is
loaded and saved as one unit, or a legacy mapping you are not ready to unpick, can keep its reference
by turning the rule off for the project:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);DDD00021</NoWarn>
</PropertyGroup>
```

There is no per-member escape hatch. A `#pragma warning disable` cannot suppress a source generator's
diagnostic, so it is the whole project or nothing. See [DDD00021](diagnostics.md#ddd00021) for why,
and for the full list of what does and does not report.

## The four rules of aggregate design

Vaughn Vernon's *Effective Aggregate Design* gives four rules of thumb. They are the best short
summary of the pattern anyone has written, and they are a fair way to ask what a toolkit is actually
worth. Here is how this one scores against them.

| Rule | What the toolkit does |
|---|---|
| 1. Model true invariants in consistency boundaries | Gives you the boundary, the token, and a seam that runs your invariants at every commit. You write the rules. |
| 2. Design small aggregates | Nothing. Arguably it makes large ones easier to build. |
| 3. Reference other aggregates by identity | Generates the identity and warns when you do not use it. |
| 4. Use eventual consistency outside the boundary | Domain events, the outbox and the inbox. |

### Rule 1: model true invariants in consistency boundaries

Supported, as far as a library can go. The rules are still yours to write; where they run is not.

`[AggregateRoot<TId>]` draws the boundary and `Version` makes it real: one save is one aggregate, and
a second writer with a stale version is refused rather than merged. Child entities are owned, so they
load and save with the root and cannot be written behind its back. Private setters and a generated
protected constructor mean the only way into the state is through a method you wrote.

On top of that, every entity and aggregate root gets a generated `partial void CheckInvariants()`
seam, and `UseDDDToolkit` registers an interceptor that calls it before every `SaveChanges` that
writes that aggregate. A broken rule stops the save:

```csharp
partial void CheckInvariants()
{
    if (Status != OrderStatus.Draft && Lines.Count == 0)
    {
        throw InvariantViolation("A placed order must have at least one line.");
    }
}
```

That is the whole of what a library can promise here: the place to write the rule, and the guarantee
that it runs at the commit rather than wherever somebody remembered. [Invariants](invariants.md) has
the seam, the interceptor order, and the limitation that matters, which is that an invariant spanning
two aggregates cannot be checked this way and should not be.

A guard clause in the method that makes the change is still right, and the two are not in
competition. The guard refuses the command with a message the caller can act on; the seam is the net
under every path into the state, including the ones you add next year:

```csharp
public void Ship(TrackingCode code)
{
    if (Status != OrderStatus.Paid)
    {
        throw new InvalidOperationException("Only a paid order can ship.");
    }

    Status = OrderStatus.Shipped;
    RaiseDomainEvent(new OrderShipped(Id, code));
}
```

Value objects are a different thing again: `[ValueObject]` and `[SingleValueObject<T>]` carry rules
through `Validate` and the always-valid twin. See [Value objects](value-objects.md). That is
validation of one value, not an invariant across a cluster, and the two are worth keeping apart in
your head. [Invariants](invariants.md#why-here-and-not-in-a-validator) lays the two side by side.

The word "true" in the rule is doing the work anyway, and no tool can check it. A true invariant is a
rule that must hold at the end of every single transaction. A rule that may be a minute late is not
one, and dragging it inside the boundary to be safe is how aggregates get big.

### Rule 2: design small aggregates

Not supported. This is the rule the toolkit is least help with, and on one reading it works against
it.

The mechanism is [read-only collections](#read-only-collections). Declaring one is a single line:

```csharp
public partial IReadOnlyList<OrderLine> Lines { get; }
```

and you get the backing field, the read-only view and the Entity Framework mapping. That is a good
feature, and the cost of it is that the moment of friction is gone. Writing the field, the view and the
`[BackingField]` by hand used to take a minute, and a minute is long enough to wonder whether the
collection belongs there. A one-line declaration is not.

Nothing downstream catches it either. `[BackingField]` maps whatever you declared.
[DDD00021](diagnostics.md#ddd00021) checks the *type* in the collection, not the size of it: it stops
`IReadOnlyList<Customer>` and says nothing at all about `IReadOnlyList<OrderLine>` holding a hundred
thousand lines. There is no diagnostic for aggregate size, and there is not going to be a useful one,
because "too big" is a question about your invariants and your write patterns and a compiler can see
neither.

So the check is yours. Before adding a collection to an aggregate, ask:

**Does a rule in this aggregate read the whole collection?** Not one element, all of them. "The order
total may not exceed the credit limit" reads every line, so the lines belong inside. If no rule needs
the collection as a whole, it is not part of any invariant, and you are storing a query result in an
object graph.

**Can an element exist without this root?** If a `Customer` can outlive an `Order`, the collection is a
reference to another aggregate wearing a collection's clothes. Typed as `IReadOnlyList<Customer>` the
analyzer catches it. Typed as `IReadOnlyList<CustomerSummary>`, where `CustomerSummary` is a child
entity you invented to get around it, nothing catches it and the boundary is just as broken.

**Is it bounded by something the domain guarantees?** "An order has lines" is bounded by what one
person will buy in one go. "A customer has orders" is bounded by nothing: it grows for as long as the
customer stays. Unbounded collections are where aggregates go wrong, and they are obvious in the
domain language long before they are obvious in a profiler.

**How many people write to it at once?** Every write to any element takes the root's `Version`. Two
users adding a line to the same order is fine. Two hundred warehouse scanners adding events to the same
shipment is a queue of `ConcurrencyConflictException`, and the fix is a smaller aggregate, not a retry
loop.

**What would break if this were a list of ids?** Often the honest answer is "a few queries would get
longer". That is the trade, and it is usually the right one.

A large aggregate does not fail a build or a test. It fails in production, as lock contention and as
`SaveChanges` calls that load more than they needed, and by then it is in your schema. The toolkit
gives you no warning about it. This section is the warning.

### Rule 3: reference other aggregates by identity

Supported, and checked.

`[EntityId<T>]` gives you the id type, `[AggregateRoot<Guid>("ORD")]` generates it for you, and
[DDD00021](diagnostics.md#ddd00021) reports a field or property typed as another root. The analyzer
knows which of your types are roots and which are ids because the attributes told it, so this is one of
the few DDD rules a tool can genuinely check rather than lecture about.

It is a warning, not an error, and it covers fields and properties only. A method that takes another
root as a parameter is fine and is often the right shape:
`order.PlaceFor(customer)` reads better than `order.PlaceFor(customer.Id)` and stores the id either
way. [Reference other aggregates by id](#reference-other-aggregates-by-id) has the full reasoning and
the one navigation that is allowed.

### Rule 4: use eventual consistency outside the boundary

Supported, and it is the part of the toolkit with the most machinery behind it.

An aggregate raises a domain event. The event leaves the boundary, and whatever it touches catches up
afterwards:

- `RaiseDomainEvent` inside the aggregate, drained by the persistence layer. See
  [Domain events](domain-events.md).
- The [outbox](entity-framework.md#the-outbox) writes one row per event in the same transaction as the
  aggregate, so the event cannot be lost when the save succeeds or survive when it fails.
- [Integration events](integration-events.md) give the event a published contract and a sink, so the
  thing catching up can be in another process.
- The inbox makes the receiving side idempotent, which is what at-least-once delivery requires of it.

What this does not do is make eventual consistency free. Delivery is at-least-once and ordering is
best-effort, a handler can fail after other handlers succeeded, and a reader can see one aggregate
updated and another not yet. Those are properties of the approach, not gaps in the implementation, and
the pages above say where each one bites.

The rule that matters here is the one about rule 1: if you find yourself wanting a transaction across
two aggregates, the question is whether the rule forcing it is really a true invariant. If it is, the
two aggregates are one. If it is not, an event is the answer.

## Requirements

The declaration must be a `partial class`. A record or struct carrying `[Entity<T>]` or
`[AggregateRoot<T>]` reports [DDD00002](diagnostics.md#ddd00002); a non-partial class reports
[DDD00005](diagnostics.md#ddd00005).

The type argument is either an identifier, which is any type carrying `[EntityId<T>]` or implementing
`IEntityId`, or the raw value an identifier should wrap, which must be a value type or a `string`.
Anything else reports [DDD00008](diagnostics.md#ddd00008). When the toolkit generates the identifier
and something else already holds the name it would take, that reports
[DDD00007](diagnostics.md#ddd00007).

A field or property typed as another aggregate root reports
[DDD00021](diagnostics.md#ddd00021). That one is a warning: the code compiles and everything is still
generated.
