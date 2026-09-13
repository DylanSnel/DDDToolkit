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

## Requirements

The declaration must be a `partial class`. A record or struct carrying `[Entity<T>]` or
`[AggregateRoot<T>]` reports [DDD00002](diagnostics.md#ddd00002); a non-partial class reports
[DDD00005](diagnostics.md#ddd00005).

The type argument is either an identifier, which is any type carrying `[EntityId<T>]` or implementing
`IEntityId`, or the raw value an identifier should wrap, which must be a value type or a `string`.
Anything else reports [DDD00008](diagnostics.md#ddd00008). When the toolkit generates the identifier
and something else already holds the name it would take, that reports
[DDD00007](diagnostics.md#ddd00007).
