# Entities and aggregates

An order is placed, gets another line, has its address corrected, is paid and is shipped. Every
property changed, and it is still the same order. Something whose identity outlives its values is an
entity, and two entities are the same when their identifiers are, whatever else they hold.

Some rules are not about one entity. "A placed order has at least one line" is about an order and its
lines together, and it only holds if nobody can take a line away without the order knowing. An
aggregate is that group: a root entity, the order, and the objects it owns, the lines, treated as one
unit. The root owns the cluster and forms the consistency boundary around it. It is the only way in:
code outside holds the order, never a line on its own, and every change goes through a method on the
order, which knows the rules.

That is why the aggregate is the unit of consistency. Its rules hold after every change, and it is
changed as a whole or not at all. Two aggregates, such as an order and the customer who placed it, are
kept apart, and each answers only for itself.

The toolkit distinguishes the two, and the distinction carries real behaviour. Use
`[AggregateRoot<TId>]` for the object you load, save and reference from elsewhere. Use `[Entity<TId>]`
for something that only exists inside one aggregate, like an order line. This page declares both, then
the collections that hold one inside the other and the references between aggregates, then events,
invariants and the version. Storage comes last.

## Declaring an aggregate

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public Order(OrderId id, CustomerId customer) : base(id)
    {
        Customer = customer;
    }

    public CustomerId Customer { get; private set; }

    public OrderStatus Status { get; private set; } = OrderStatus.Draft;
}
```

`OrderId` is an identifier type, declared with `[EntityId<Guid>]`; see [Identifiers](identifiers.md).
The class has to be `partial` so the generator can add to it. It supplies the base class and a
protected parameterless constructor for Entity Framework and serializers, which is why your own
constructor calls `base(id)`:

```csharp title="Order.g.cs, shortened"
partial class Order : AggregateRoot<OrderId>
{
    protected Order()
    {
    }

    partial void CheckInvariants();

    // ... the invariant checks, see Invariants below
}
```

The base class brings the `Id`, and for a root the `Version` and `RaiseDomainEvent` described further
down.

Equality comes from the base type and compares identifiers, so two instances of the same order loaded
in different contexts are equal. `==`, `!=`, `Equals` and `GetHashCode` are all consistent and
null-safe.

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

The generator writes the same as for a root, with `Entity<OrderLineId>` as the base class:

```csharp title="OrderLine.g.cs, shortened"
partial class OrderLine : Entity<OrderLineId>
{
    protected OrderLine()
    {
    }

    // ...
}
```

A child entity has identity and equality but no events and no version, because it is not a
consistency boundary; its root is. With Entity Framework referenced, that package's generator adds a
part of its own that marks it owned, so it is loaded and saved with the aggregate that owns it:

```csharp title="OrderLine.EntityFramework.g.cs"
[Owned]
partial class OrderLine
{
}
```

Side by side:

| | `[Entity<TId>]` | `[AggregateRoot<TId>]` |
|---|---|---|
| Base type | `Entity<TId>` | `AggregateRoot<TId>` |
| Identity and equality | Yes | Yes |
| Domain events | No | Yes |
| Concurrency version | No | Yes |
| Invariants | Yes, run by a save that changes it | Yes, run by a save that changes it or anything it owns |
| Entity Framework | Mapped as an owned type | Mapped as its own entity type |

## Read-only collections

An order holds its lines. Exposing a `List<T>` from the aggregate lets any caller add to it and bypass
your invariants. Exposing `_items.AsReadOnly()` from a hand-written field is correct but tedious: a
field, a property and a wrapper for every collection. The toolkit writes them for you.

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

The generator writes the field behind it and the read-only view in front of it:

```csharp title="Order.g.cs, shortened"
partial class Order : AggregateRoot<OrderId>
{
    // ...

    private readonly List<OrderLine> _lines = new();

    private IReadOnlyList<OrderLine>? __linesView;

    [BackingField(nameof(_lines))]
    public partial IReadOnlyList<OrderLine> Lines => __linesView ??= _lines.AsReadOnly();
}
```

`Lines` appears twice because it is one property, not two: C# 13 partial properties split into a
declaring half, which you write, and an implementing half, which the generator writes. `_lines` is
usable from your half the moment you declare the property, which is why `AddLine` above compiles
without you having written the field.

Callers see a read-only view that cannot be cast back to `List<T>`; the aggregate mutates through
`_lines`. `[BackingField]` is for Entity Framework, and is described [below](#stored-with-entity-framework).

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

Child entities may declare partial collection properties exactly like roots.

### The property must be get-only

```csharp
public partial IReadOnlyList<OrderLine> Lines { get; set; }   // DDD00020
```

A setter would let a caller replace the whole collection, which defeats the purpose. Declaring one
reports [DDD00020](diagnostics.md#ddd00020) and the property is left unimplemented, so the build
fails loudly rather than silently producing something you did not ask for.

### Stored with Entity Framework

Entity Framework reads and writes the field directly thanks to `[BackingField]`, so it never tries to
write through the read-only property. See
[Child entities and owned collections](entity-framework.md#child-entities-and-owned-collections).

The `[BackingField]` annotation is only emitted when the project references Entity Framework, so the
same declaration works in a domain project with no persistence dependency.

### Why the view is kept

The view is held rather than rebuilt. `AsReadOnly()` is `new ReadOnlyCollection<T>(this)` with no
cache of its own, so an expression bodied property would build one wrapper per read and throw it
away: 24 bytes every time somebody looks. Holding it is safe because the list field is `readonly`, so
the collection the view wraps can never be swapped out from under it, and the view is a window on the
list rather than a copy, so it shows everything the aggregate does afterwards. See
[Performance](performance.md#reading-a-read-only-collection).

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

## Invariants

A rule about the whole aggregate, such as "a placed order has at least one line", belongs to the
root. The generator declared a `CheckInvariants()` seam in its half of the class; implement it in
yours:

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

A rule that deserves a name, a code a caller can branch on, or a test of its own is better written as
a nested `IInvariant<Order>` instead. The entity runs both.

Once the aggregate is stored with Entity Framework, every save that writes it runs them, and a broken
rule stops the save. An aggregate that states nothing costs nothing: the compiler erases an
unimplemented `partial void` and every call to it.

The same question can be asked without a database, and asking the root asks the whole aggregate:

```csharp
order.AddLine(line);

var broken = order.GetInvariantViolations();   // the order's rules, and every line's
order.EnsureInvariants();                      // the same, and throws instead of answering
```

See [Invariants](invariants.md) for the two stages, what one question covers and when the self-only
pair is the one you want, for when to reach for each shape of rule, for the interceptor order, and for
why an invariant across two aggregates is a design question rather than a missing feature.

## Declaring the identifier with it

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

## Optimistic concurrency

Every aggregate root carries a version, from its base class:

```csharp
public long Version { get; private set; }
```

It is 0 for a new aggregate. The Entity Framework integration increments it on every save that
touches the aggregate, including saves that only changed something it owns, and refuses a save that
carries a stale one: two users editing the same order produce a `ConcurrencyConflictException` for the
second one, naming the aggregate type and id.

The version is what makes an aggregate a unit of consistency. Without it two concurrent saves are
last-write-wins, silently. It says nobody else changed the aggregate; it says nothing about whether
the aggregate is *consistent*, which is what the [invariants](#invariants) are for.

See [Optimistic concurrency](entity-framework.md#optimistic-concurrency) for how the version is
mapped and checked, what counts as touching the aggregate, and what to do when the exception arrives.

## Designing aggregates

Declaring an aggregate is one attribute. Deciding what belongs inside it is the hard part, and it is
a question about your rules and your write patterns that no compiler can see. Vaughn Vernon's four
rules of aggregate design are the best short guide to it. [Designing aggregates](aggregate-design.md)
says why each rule exists, what the toolkit does for it, and, for the rule it cannot help with, what to
ask yourself instead.

## Persistence

Nothing above needs a database. With `DDDToolkit.EntityFramework` referenced, the same declarations are
mapped with no configuration: a root as an entity type, a child entity as an owned type, a read-only
collection through its backing field, and `Version` as a concurrency token. See
[Entity Framework](entity-framework.md).

What the toolkit deliberately does not add is the bookkeeping many persistence layers put on every
row.

### Auditing and soft delete

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

Nothing in this section is generated, and the toolkit adds no convention or interceptor for it. What
follows is plain Entity Framework, in your own context.

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
properties, declare them on each aggregate and give them an interface of your own, such as
`IAudited`, so the interceptor has one type to look for. A base class of your own does not work: the
generator writes the base class of every `[AggregateRoot<T>]` and `[Entity<T>]` itself, so a class
that also names a base fails to compile with CS0263 ("partial declarations must not specify
different base classes").

**It is a history of what happened.** Then you already have it. The domain events an aggregate
raises are a record of every meaningful change, written by the aggregate that knows what the change
meant. Turn on the [outbox](event-delivery.md#the-outbox) and keep the rows instead of deleting
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
