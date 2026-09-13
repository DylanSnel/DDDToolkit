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
