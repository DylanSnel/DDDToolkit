# Diagnostics

A source generator that produces nothing when it is misused is the hardest kind of bug to find: the
type looks annotated and behaves like a plain class. Every misuse below reports a diagnostic instead.

| Id | Severity | Meaning |
|---|---|---|
| [DDD00001](#ddd00001) | Error | Value objects must be records |
| [DDD00002](#ddd00002) | Error | Entities must be classes |
| [DDD00003](#ddd00003) | Error | Entity ids must be records |
| [DDD00004](#ddd00004) | Warning | Entity id structs should be readonly |
| [DDD00005](#ddd00005) | Error | DDDToolkit types must be partial |
| [DDD00006](#ddd00006) | Error | DDDToolkit types cannot be generic |
| [DDD00007](#ddd00007) | Error | The generated identifier name is already taken |
| [DDD00008](#ddd00008) | Error | The identifier type argument is not supported |
| [DDD00009](#ddd00009) | Error | A type is either an entity or an aggregate root |
| [DDD00010](#ddd00010) | Error | Value object properties must use protected setters |
| [DDD00011](#ddd00011) | Error | Value object properties must use init setters |
| [DDD00013](#ddd00013) | Error | Value objects cannot be sealed |
| [DDD00020](#ddd00020) | Error | Generated collection properties must be get-only |
| [DDD00021](#ddd00021) | Warning | Reference another aggregate by its id |

Most of these say the generator could not do what you asked. [DDD00021](#ddd00021) is the odd one
out: it is a rule about the model rather than about the declaration, nothing stops being generated
when it fires, and it is a warning you can suppress or turn off.

---

## DDD00001

**Value objects must be records.**

```csharp
[ValueObject]
public partial class Address { }        // DDD00001
```

`[ValueObject]` and `[SingleValueObject<T>]` generate structural equality members and an always-valid
twin that derives from your type. Both require a reference record.

```csharp
[ValueObject]
public partial record Address { }
```

Nothing is generated for the type until it is a record, so expect follow-on errors about missing
members until you fix this one.

---

## DDD00002

**Entities must be classes.**

```csharp
[AggregateRoot<OrderId>]
public partial record Order { }         // DDD00002
```

Entities have identity, not value semantics, and derive from a generated base class. A record would
give you value equality across all properties, which is wrong for an entity: an order whose status
changed is still the same order.

```csharp
[AggregateRoot<OrderId>]
public partial class Order { }
```

---

## DDD00003

**Entity ids must be records.**

```csharp
[EntityId<Guid>]
public partial class OrderId { }        // DDD00003
```

Identifiers rely on record equality. Use a record struct for an allocation-free id, or a record class
when you need inheritance or the always-valid twin:

```csharp
[EntityId<Guid>]
public readonly partial record struct OrderId;

[EntityId<Guid>]
public partial record OrderId;
```

See [Identifiers](identifiers.md) for which to choose.

---

## DDD00004

**Entity id structs should be readonly.**

```csharp
[EntityId<Guid>]
public partial record struct OrderId;   // DDD00004, generation still happens
```

A warning, not an error: the identifier is generated either way. Adding `readonly` states that the id
cannot be mutated after construction and lets the compiler skip defensive copies when the struct is
passed around.

```csharp
[EntityId<Guid>]
public readonly partial record struct OrderId;
```

---

## DDD00005

**DDDToolkit types must be partial.**

```csharp
[AggregateRoot<OrderId>]
public class Order { }                  // DDD00005
```

The generator adds a second declaration of your type, which requires `partial`. Add the keyword:

```csharp
[AggregateRoot<OrderId>]
public partial class Order { }
```

---

## DDD00006

**DDDToolkit types cannot be generic.**

```csharp
[EntityId<Guid>]
public readonly partial record struct Reference<T>;      // DDD00006

public partial class Repository<T>
{
    [AggregateRoot<OrderId>]
    public partial class Entry { }                        // DDD00006, through its container
}
```

A type nested in a non-generic container is fine and generates normally.

The generated members have to name your type from places that cannot see a type parameter. A struct
identifier carries `[JsonConverter(typeof(Reference<T>.SystemTextJsonConverter))]`, and an attribute
argument may not name an open generic. The `Add<Module>Converters` and
`Add<Module>GraphQlRuntimeBindings` registrations live outside the type and cannot name it at all.

The rule covers a type nested inside a generic type for the same reason: the type parameter is still
in scope, so the same references are still unspeakable.

Give the type a concrete identity instead:

```csharp
[EntityId<Guid>]
public readonly partial record struct OrderReference;
```

---

## DDD00007

**The generated identifier name is already taken.**

```csharp
public sealed class OrderId { }         // something else, in the same namespace

[AggregateRoot<Guid>("ORD")]
public partial class Order { }          // DDD00007
```

`[AggregateRoot<Guid>]` and `[Entity<Guid>]` name a raw value, so the toolkit generates the
identifier as well, called `OrderId`. Another type of that name in the same namespace or containing
type would be a duplicate definition. Without this diagnostic the compiler would report CS0101
against generated code you never wrote.

Either point the attribute at the identifier you already have:

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;

[AggregateRoot<OrderId>]
public partial class Order { }
```

Or rename whichever of the two types should not be called `OrderId`.

The one exception is a `partial record struct` of that name with no `[EntityId<T>]` on it. That is
taken as your own half of the generated identifier and reports nothing, which is how you add members
to it:

```csharp
public readonly partial record struct OrderId
{
    public string Short => Value.ToString("N")[..8];
}
```

Such a part must be `partial`, must be a `record struct`, and must not state an accessibility
different from the entity's. A part that states `internal` where the generated part says `public`
would be CS0262, so it reports DDD00007 instead.

Nothing is generated for the entity until the clash is gone, so expect follow-on errors about its
missing base class.

---

## DDD00008

**The identifier type argument is not supported.**

```csharp
public sealed class Money { }

[AggregateRoot<Money>]
public partial class Order { }          // DDD00008
```

The type argument of `[Entity<T>]` and `[AggregateRoot<T>]` is one of two things: an identifier you
already have, meaning any type carrying `[EntityId<T>]` or implementing `IEntityId`, or the raw value
an identifier should wrap. A reference type that is not a `string` is neither. It can be null and it
is not copied by value, and an identifier has to be both.

```csharp
[AggregateRoot<Guid>("ORD")]            // a value the toolkit can wrap
public partial class Order { }

[AggregateRoot<OrderId>]                // an identifier you declared yourself
public partial class Order { }
```

`Guid`, `int`, `long`, `string`, `DateOnly` and your own structs all work. `Guid?` does not: an
optional identifier is `OrderId?`, not an identifier over a nullable value.

Nothing is generated for the entity until the type argument is one of the two.

---

## DDD00009

**A type is either an entity or an aggregate root.**

```csharp
[Entity<ThingId>]
[AggregateRoot<ThingId>]
public partial class Widget { }         // DDD00009
```

An aggregate root is a consistency boundary. A child entity lives inside one. A type cannot be both,
and the two attributes generate different base types for the same declaration.

Keep whichever describes the type. Use `[AggregateRoot<T>]` for something you load, save and reference
from elsewhere, and `[Entity<T>]` for something that only exists inside one aggregate. See
[Entities and aggregates](entities-and-aggregates.md).

Before this diagnostic existed, both attributes on one class made the generator throw and contribute
nothing at all, leaving only a `CS8785` about a crashed generator and no hint about the cause.

---

## DDD00010

**Value object properties must use protected setters.**

```csharp
[ValueObject]
public partial record Address
{
    public string City { get; init; }   // DDD00010
}
```

A record with a publicly writable property can be cloned with `with` into a state that never passed
validation:

```csharp
var invalid = address with { City = "" };
```

Making the setter `protected` keeps `with` available inside the type and its always-valid twin while
closing it to callers:

```csharp
public string City { get; protected init; }
```

---

## DDD00011

**Value object properties must use init setters.**

```csharp
[ValueObject]
public partial record Address
{
    public string City { get; protected set; }   // DDD00011
}
```

A non-init setter lets any deriving type change the value after construction. Value objects are
immutable, so the setter must be `init`:

```csharp
public string City { get; protected init; }
```

DDD00010 and DDD00011 are separate rules and a property with a plain `public set` reports both. The
fix for both is `protected init`.

---

## DDD00013

**Value objects cannot be sealed.**

```csharp
[SingleValueObject<string>]
public sealed partial record EmailAddress { }   // DDD00013
```

The generator emits `ValidEmailAddress`, which derives from `EmailAddress`. A sealed record cannot be
derived from, so the twin cannot exist. Remove `sealed`.

Record structs are never affected: they are implicitly sealed but get no twin, since a struct cannot
be derived from at all.

---

## DDD00020

**Generated collection properties must be get-only.**

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public partial IReadOnlyList<OrderLine> Lines { get; set; }   // DDD00020
}
```

The generator implements the property as a read-only view over a private backing field. A setter
would let a caller replace the whole collection and bypass the aggregate's invariants, which is the
thing the read-only view exists to prevent.

```csharp
public partial IReadOnlyList<OrderLine> Lines { get; }
```

Mutate through the generated field instead:

```csharp
public void AddLine(OrderLine line) => _lines.Add(line);
```

See [Entities and aggregates](entities-and-aggregates.md#read-only-collections).

---

## DDD00021

**Reference another aggregate by its id.**

```csharp
[AggregateRoot<CustomerId>]
public partial class Customer { }

[AggregateRoot<OrderId>]
public partial class Order
{
    public Customer Buyer { get; private set; }              // DDD00021
    public IReadOnlyList<Customer> Watchers { get; }         // DDD00021
}
```

Hold the other aggregate's id instead:

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public CustomerId Buyer { get; private set; }

    public void PlaceFor(Customer customer) => Buyer = customer.Id;
}
```

Each aggregate is a separate loading and consistency boundary. A field or property typed as another
root pulls that root inside this one: Entity Framework builds a navigation from it, one save then
writes two roots, and neither `Version` guards its own aggregate any more. See
[Entities and aggregates](entities-and-aggregates.md#reference-other-aggregates-by-id) for the longer
version.

A warning, not an error. The code compiles, everything is still generated, and a team that disagrees
can turn the rule off for a project:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);DDD00021</NoWarn>
</PropertyGroup>
```

`#pragma warning disable DDD00021` around one property does **not** work, and neither does
`[SuppressMessage]`. That is a limitation of source generators rather than a choice: a generator
reports its diagnostics with a location rebuilt from a file path and holding no syntax tree, which is
what keeps the pipeline cacheable, and the compiler has no tree to match a pragma against. `NoWarn` is
read from the compilation options, so it reaches them. The same is true of every DDD diagnostic; this
is the only one you are likely to want to switch off.

### What reports and what does not

| | Reports |
|---|---|
| A field or property typed as another root | Yes |
| A collection, array or dictionary holding another root | Yes |
| A `Customer?` property | Yes |
| A root holding another instance of its own type, such as `Employee.Manager` | Yes |
| A child `[Entity<T>]` of the same aggregate | No |
| A child entity navigating back to the root that owns it | No |
| A property typed as the other aggregate's id | No |
| A method parameter or return type | No |
| A `static` member | No |

Two rows deserve a word.

**The back-navigation.** A child entity may hold the root that owns it, which is the inverse
navigation Entity Framework wants:

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public partial IReadOnlyList<OrderLine> Lines { get; }
}

[Entity<OrderLineId>]
public partial class OrderLine
{
    public Order Order { get; private set; }      // allowed
}
```

The exemption is checked, not assumed: `Order` has to hold `OrderLine` back, through a collection or
a single property, and the reference from the child has to be single valued. A child entity pointing
at some other root still reports, because that reference does widen the boundary.

**Methods.** `order.PlaceFor(customer)` takes the other root, reads what it needs and stores nothing,
which is how two aggregates are meant to cooperate. Entity Framework cannot see a method either, so
no navigation comes of it. The rule is about what an aggregate *holds*.

The rule reads the declared type of a member, so a property typed as an interface that an aggregate
root happens to implement is not recognised, and neither is `object`.

---

## Nothing was generated and there is no diagnostic

Check, in order:

1. **The generator package is referenced.** `DDDToolkit` brings the core generators; the Entity
   Framework, FluentValidation and HotChocolate generators come with their own packages.
2. **The type is `partial`** and the attribute is the generic one, `[EntityId<Guid>]` rather than a
   hand-written attribute with the same name.
3. **The build output**, with `EmitCompilerGeneratedFiles` turned on, as described in
   [Getting started](getting-started.md#see-the-generated-code).

If a generator throws, the compiler reports it as CS8785 or CS8784 rather than as a DDD diagnostic.
That is a bug in the toolkit; please report it with the declaration that triggered it.
