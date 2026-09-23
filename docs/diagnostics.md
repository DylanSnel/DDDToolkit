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
| [DDD00022](#ddd00022) | Warning | Use only what another module publishes |
| [DDD00023](#ddd00023) | Warning | Do not hold another module's entity |
| [DDD00024](#ddd00024) | Warning | An invariant must be nested inside the entity it is about |
| [DDD00025](#ddd00025) | Warning | An invariant is nested inside a type it is not about |
| [DDD00026](#ddd00026) | Warning | Two invariants of one entity share a code |
| [DDD00027](#ddd00027) | Error | An invariant needs an accessible parameterless constructor |
| [DDD00028](#ddd00028) | Error | A key part belongs on an entity or aggregate root |
| [DDD00029](#ddd00029) | Warning | A key part should not have a public setter |
| [DDD00030](#ddd00030) | Error | Declare all key parts of a type in one file |
| [DDD00031](#ddd00031) | Error | A [SupabaseMigrations] factory must be one the build can create |

Most of these say the generator could not do what you asked. The rest are a different kind: they are
rules about the model rather than about the declaration, and each of them names code that compiles,
reads well and does not do what it looks like it does. [DDD00021](#ddd00021) is about the boundary
between two aggregates; [DDD00022](#ddd00022) and [DDD00023](#ddd00023) are about the boundary
between two [modules](modules.md) and say nothing at all until a project declares itself one;
[DDD00024](#ddd00024) to [DDD00027](#ddd00027) are about [invariants](invariants.md), where the
failure worth catching is a rule that is written, tested, and never run; [DDD00028](#ddd00028) to
[DDD00030](#ddd00030) are about [composite keys](composite-keys.md), and the section after them lists
the one key-part mistake that can only be caught when the Entity Framework model is built.
[DDD00031](#ddd00031) is about the [Supabase export](entity-framework.md#supabase), where the failure
worth catching is a module whose migrations never reach Supabase.

That split is what the numbering is for. DDD00001 to DDD00019 are reserved for "the generator could
not do what you asked", and DDD00020 upwards for rules about the model. Severity does not follow the
split. [DDD00020](#ddd00020) and [DDD00027](#ddd00027) are errors even though they sit in the second
group, because in both the generator drops the member rather than emitting something wrong, and a
warning would leave you with a rule that silently never runs.

The table above is the complete list: DDD00012 and DDD00014 to DDD00019 have never been assigned, and
the gaps are room to grow rather than something that was removed. Ids are stable and are never reused,
so a number that is missing here is missing from the compiler too.

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

The copy does not go through validation, and on the always-valid twin that produces a
`ValidAddress` holding an invalid value, even from code that only knows about `Address`.
[Why `with` is closed to callers](value-objects.md#why-with-is-closed-to-callers) goes through the
mechanics, and why checking the copy at the `with` is not possible.

A code fix makes that change for you: `protected init`, or `private protected init` on an
`internal` property. Callers that need a changed copy use the generated
[`With(...)`](value-objects.md#changing-a-value-with) instead of `with`.

A positional record does not report DDD00010. Its parameters would become `public init` properties,
so the generator declares them itself as `protected init`; see
[Positional records](value-objects.md#positional-records).

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
fix for both is `protected init`, and the code fix described under [DDD00010](#ddd00010) applies it.

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

## DDD00022

**Use only what another module publishes.**

```csharp
// Crm.csproj: [assembly: Module("Crm")]
namespace Crm;

[AggregateRoot<CustomerId>]
public partial class Customer { }

// Sales.csproj: [assembly: Module("Sales")]
namespace Sales;

public sealed class OrderReport
{
    public string Describe(Customer customer) => customer.Name;   // DDD00022
}
```

Either publish the type, in the module that owns it:

```csharp
namespace Crm;

[ModuleContract]
public sealed record CustomerSummary(CustomerId Id, string Name);
```

or go through something already published, which for an aggregate is usually its id and its
integration events:

```csharp
namespace Sales;

public sealed class OrderReport
{
    public string Describe(CustomerSummary customer) => customer.Name;
}
```

A module is an assembly carrying `[assembly: Module("Name")]`. Its published contract is every type
marked `[ModuleContract]`, every type marked `[IntegrationEvent]`, and anything nested inside one of
those. Everything else the assembly declares is the owning team's business, `public` or not.

The rule is silent unless both assemblies declare a module, so it reports nothing in a codebase that
has not opted in, and never against the framework, a NuGet package or a shared kernel. Two assemblies
that declare the same module name are one module and no boundary runs between them.

It reports where you *name* another module's unpublished type: a parameter, a field, a base type, a
generic argument, an attribute, a `typeof`, a `new`, a static call, a `using` alias. It cannot see a
type you never name (`var`), an extension method called on an instance, an inherited member, or
anything resolved by reflection. [Modules](modules.md#what-the-analyzer-cannot-catch) has the full
list, which is worth reading before you trust the rule.

A warning, for the same reason as DDD00021: it states a design decision, and a team adopting modules
wants the list before it has to fix it. This one comes from an analyzer rather than from a generator,
so `#pragma warning disable DDD00022`, `[SuppressMessage]` and `NoWarn` all work, and
`<WarningsAsErrors>$(WarningsAsErrors);DDD00022</WarningsAsErrors>` turns it into a build break once
the list is empty.

---

## DDD00023

**Do not hold another module's entity.**

```csharp
// Sales
[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public Customer Buyer { get; private set; }   // DDD00023, Customer belongs to Crm
}
```

Hold the other module's published id, and react to what it publishes:

```csharp
[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public CustomerId Buyer { get; private set; }

    public void PlaceFor(CustomerId customer) => Buyer = customer;
}
```

A property typed as another module's entity is a navigation. Entity Framework maps it, a query in one
module loads rows owned by the other, and one `SaveChanges` writes into both inside one transaction.
The two modules can then no longer be tested, migrated or separated on their own, and nothing in the
code looks wrong.

Publishing the entity does not help and does not silence this rule. `[ModuleContract]` says you may
name a type; it cannot say you may make that type part of your own transaction.

### What reports and what does not

| | Reports |
|---|---|
| A field or property typed as another module's `[Entity<T>]` or `[AggregateRoot<T>]` | Yes |
| A collection, array or dictionary holding one | Yes |
| One that the other module publishes with `[ModuleContract]` | Yes |
| A property typed as the other module's id | No |
| An entity of the same module | No |
| An entity of an assembly that declares no module | No |
| A method parameter or return type | No |
| A `static` member | No |
| The generated backing field of a collection property | No, the property is reported instead |

Like DDD00022 this is a warning and comes from an analyzer, so pragmas, `[SuppressMessage]`, `NoWarn`
and `WarningsAsErrors` all work on it.

Holding another module's *aggregate root* reports [DDD00021](#ddd00021) as well: one says hold the id,
the other says do not reach across the boundary, and both are answered by the same edit. See
[Modules](modules.md) for the longer version.

---

## DDD00024

**An invariant must be nested inside the entity it is about.**

```csharp
// Ordering/MustHaveLines.cs
public sealed class MustHaveLines : IInvariant<Order>      // DDD00024
{
    public string Code => "ORDER_HAS_NO_LINES";

    public InvariantFailure? Check(Order order) => order.Lines.Count == 0 ? "..." : null;
}
```

Nest it inside the entity it judges, in a part of your own:

```csharp
// Ordering/Invariants/MustHaveLines.cs
public partial class Order
{
    public sealed class MustHaveLines : IInvariant<Order>
    {
        // the same body
    }
}
```

An entity runs the rules it finds among its own nested types. That is what lets a rule read the
entity's private state, and it is what keeps discovery free of a scan over the whole compilation. A
rule declared anywhere else compiles, reads well, passes the unit test you wrote for it, and never
runs on a save. It is the one failure the invariants feature is built to make impossible to ship
unnoticed, which is why this fires at all.

This is the only one of the four that comes from a real analyzer rather than from the generator, and
for a plain reason: a type outside an entity is by definition not among any entity's nested types, so
only a pass over the compilation can see it. That also means `#pragma warning disable DDD00024`,
`[SuppressMessage]`, `NoWarn` and `WarningsAsErrors` all work on it, which the three below cannot
offer.

A warning rather than an error, because an author who really did mean to hand the rule to something
else of their own should be able to say so and move on.

### What reports and what does not

| | Reports |
|---|---|
| A top-level type implementing `IInvariant<T>` | Yes |
| One nested inside a class that is not an `[Entity<T>]` or `[AggregateRoot<T>]` | Yes |
| One that takes its `Check` from a base class | Yes, the concrete type is the rule |
| An `abstract` base shared between several rules | No, it is not a rule |
| An interface deriving from `IInvariant<T>` | No |
| An open generic rule, such as `Rule<T>` | No |
| One nested inside the wrong entity | No, that is [DDD00025](#ddd00025) |

See [Invariants](invariants.md#why-a-rule-is-a-nested-type).

---

## DDD00025

**An invariant is nested inside a type it is not about.**

```csharp
public partial class Order
{
    public sealed class LineMustCostSomething : IInvariant<OrderLine>   // DDD00025
    {
        public string Code => "LINE_IS_FREE";

        public InvariantFailure? Check(OrderLine line) => line.Price <= 0 ? "..." : null;
    }
}
```

Nest it inside the type it is about instead:

```csharp
public partial class OrderLine
{
    public sealed class MustCostSomething : IInvariant<OrderLine> { }
}
```

An entity runs the nested rules that are about *itself*. This one is in the right kind of place and
still never runs, which makes it harder to spot than [DDD00024](#ddd00024) rather than easier. In
practice the type argument was copied from a rule next to it.

A rule about a base class or an interface of the entity is accepted and reported by nothing:
`IInvariant<T>` is contravariant in `T`, so `IInvariant<IHasCustomer>` nested inside `Order` runs on
an `Order` perfectly well. Only a type argument the entity cannot be handed to is reported.

Reported by the generator, so `NoWarn` reaches it and a pragma does not. The same is true of
DDD00026 and DDD00027; [DDD00021](#ddd00021) explains why.

---

## DDD00026

**Two invariants of one entity share a code.**

```csharp
public partial class Order
{
    public sealed class MustHaveLines : IInvariant<Order>
    {
        public string Code => "ORDER_INVALID";
    }

    public sealed class MustStayWithinTheCreditLimit : IInvariant<Order>
    {
        public string Code => "ORDER_INVALID";      // DDD00026
    }
}
```

The code exists so that a caller can act on a broken rule without matching on its message, which is
the difference between a rule you can branch on and a string you can only display. Two rules
answering to one code takes that back: `GetInvariantViolations()` returns two entries the caller
cannot tell apart, and the branch that was supposed to catch one catches both.

Both rules are still generated and both still run. Nothing about this stops compiling; what stops
working is the thing the code was for.

Only the second rule is reported, and only codes this pass can read as a constant are compared at
all:

| `Code` written as | Compared |
|---|---|
| `=> "ORDER_INVALID"` | Yes |
| `{ get; } = "ORDER_INVALID"` | Yes |
| `=> ViolationCode`, a `const` | Yes, the constant's value |
| `=> $"ORDER_{Reason}"` or anything computed | No |
| Inherited from a base class as a constant | Yes |

A code built at run time is left alone rather than guessed at, because a wrong guess would report two
rules as sharing a code they do not share.

---

## DDD00027

**An invariant needs an accessible parameterless constructor.**

```csharp
public partial class Order
{
    public sealed class MustStayWithinTheCreditLimit : IInvariant<Order>
    {
        // DDD00027, the generated code inside Order cannot write new MustStayWithinTheCreditLimit()
        public MustStayWithinTheCreditLimit(decimal limit) => _limit = limit;
    }
}
```

The generator creates one instance of every rule per entity type and reuses it for every check, so a
rule has to be constructible without arguments, and has to be stateless for that reuse to be sound.
The limit in the example belongs to the entity, not to the rule, and the rule reads it off the
`Order` it is handed:

```csharp
public InvariantFailure? Check(Order order)
    => order.Total > order.CreditLimit ? $"An order may not exceed {order.CreditLimit}." : null;
```

An error rather than a warning. Nothing is generated for a rule the generated code cannot build, so a
warning would leave the rule silently dropped, which is the exact silence the other three exist to
prevent. The entity itself is still generated and everything else about it still works: you get this
one error rather than a page of follow-on ones.

Accessibility is asked of the compiler rather than read off the modifiers, because the two do not
line up here. A rule nested inside the entity may be `private` and is still reachable from the
generated code, which is written inside that same entity; a `private` constructor on it is not.

| | Reports |
|---|---|
| A constructor taking parameters, and no other | Yes |
| A `private` constructor on a rule nested in the entity | Yes |
| No constructor at all, so the implicit public one | No |
| A `private` rule with an accessible constructor | No |
| An `abstract` base or an open generic rule | No, neither is a rule |

See [Invariants](invariants.md#why-a-rule-is-a-nested-type).

---

## DDD00028

**A key part belongs on an entity or aggregate root.**

```csharp
[ValueObject]
public partial record Address
{
    [KeyPart]
    public RegionId Region { get; protected init; }   // DDD00028
}
```

`[KeyPart]` puts a property into a primary key ahead of the identifier. Only an `[AggregateRoot<T>]`
or an `[Entity<T>]` has an identifier and a key, so anywhere else the attribute would do nothing,
silently. Remove it, or move the property to the entity that is keyed on it. See
[Composite keys](composite-keys.md).

---

## DDD00029

**A key part should not have a public setter.**

```csharp
[AggregateRoot<ProjectId>]
public partial class Project
{
    [KeyPart]
    public RegionId RegionId { get; set; }   // DDD00029
}
```

Set it once, in the constructor, and make it get-only:

```csharp
[KeyPart]
public RegionId RegionId { get; }
```

A key part is part of the primary key, and a primary key does not change once the row exists: Entity
Framework refuses to save a modified key value, and every owned child's foreign key carries the same
value. `{ get; }`, `private set`, `protected set` and `init` are all fine; only a public, non-init
setter reports. A warning: everything is still generated.

---

## DDD00030

**Declare all key parts of a type in one file.**

```csharp
// Project.cs
[AggregateRoot<ProjectId>]
public partial class Project
{
    [KeyPart] public RegionId RegionId { get; }
}

// Project.Period.cs
public partial class Project
{
    [KeyPart] public int Period { get; }       // DDD00030, reported on Project
}
```

Move them into one part of the class, in the order you want the key:

```csharp
public partial class Project
{
    [KeyPart] public RegionId RegionId { get; }
    [KeyPart] public int Period { get; }
}
```

Key parts join the primary key in declaration order. Within one file that order is plain; between
the files of a partial class there is none, only the order the compiler happens to read the files in,
and a key whose column order could change with a file rename is not a key you want. Nothing is
generated for the type until the key parts are together.

---

## DDD00031

**A [SupabaseMigrations] factory must be one the build can create.**

Reported in the project that turns the Supabase export on (`<SupabaseMigrationsExport>`), normally the
host, about a factory in a module it references.

```csharp
[SupabaseMigrations]
internal sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>  // DDD00031: the host cannot see it
```

The build creates every marked factory from code generated into the host, as
`SupabaseMigrationSource.For<TContext, TFactory>()`. That needs a public, non-abstract, non-generic
class with a public parameterless constructor, implementing `IDesignTimeDbContextFactory<TContext>`
for a context the host can see too:

```csharp
[SupabaseMigrations]
public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
{
    public OrderingContext CreateDbContext(string[] args) { ... }
}
```

The message names what is missing. A factory that fails is left out of the export, which is why this is
an error: a module whose migrations were silently skipped would be found by a failing deployment, or by
a branch database without its tables, instead of by the build.

---

## Building the model fails: the owned type must carry the key part

Not a compiler diagnostic, because it depends on how the Entity Framework model is put together, but
it is reported as early as that allows: when the context builds its model, before the first query.

```
'Project' is keyed on 'RegionId', so the foreign key of its owned 'Milestone' (through
'Project.Milestones') must carry it too, but 'Milestone' has no such property. ...
```

`Project` has `[KeyPart] RegionId`, so every table it owns carries `RegionId` in its foreign key, and
`Milestone` has nowhere to keep it. Give the child the property and set it from the parent:

```csharp
[Entity<MilestoneId>]
public partial class Milestone
{
    public Milestone(RegionId regionId, MilestoneId id) : base(id) => RegionId = regionId;

    [KeyPart]
    public RegionId RegionId { get; }
}
```

The property must have the same name and the same type as the owner's. If the child really should not
carry it, configure that ownership's foreign key yourself with `OwnsMany(...).WithOwner().HasForeignKey(...)`;
the convention leaves explicit configuration alone.

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
