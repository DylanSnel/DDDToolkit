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
| [DDD00014](#ddd00014) | Warning | The generators cannot read the project's MSBuild properties |
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
| [DDD00032](#ddd00032) | Warning | Do not ask HotChocolate's generator for a toolkit identifier's node id serializer |
| [DDD00033](#ddd00033) | Warning | The generated integration event registration must be able to construct the class |
| [DDD00034](#ddd00034) | Warning | An event's class name and its Version disagree |
| [DDD00035](#ddd00035) | Error | An event's class name ends in something that is not a version |
| [DDD00036](#ddd00036) | Error | Two events of one module share a name and version |
| [DDD00037](#ddd00037) | Error | Two event names give one constant name |
| [DDD00038](#ddd00038) | Error | A row access rule, an access function or a database question has the shape the generator reads |
| [DDD00039](#ddd00039) | Error | A row access rule can only say what the database can check |
| [DDD00040](#ddd00040) | Error | A row access rule guards an aggregate root |
| [DDD00041](#ddd00041) | Error | A row access rule reads the aggregate's entities through an access function |
| [DDD00042](#ddd00042) | Error | A parent for entities is an abstract generic class whose first type parameter is the id |
| [DDD00043](#ddd00043) | Error | A template's first type argument is an entity id |
| [DDD00044](#ddd00044) | Error | A template takes a type from a class nobody declares |
| [DDD00045](#ddd00045) | Error | A template takes a type from a class declared more than once |
| [DDD00046](#ddd00046) | Error | A template attribute fills exactly the type parameters of its parent |
| [DDD00047](#ddd00047) | Error | A class is declared an entity or aggregate root once |
| [DDD00048](#ddd00048) | Error | A class a template takes meets its parent's constraints |
| [DDD00049](#ddd00049) | Error | A template registration needs a class declared with each of its templates |
| [DDD00050](#ddd00050) | Error | A type a template registration takes meets the method's constraints |
| [DDD00051](#ddd00051) | Error | A set-shaped question is asked once per statement, so its arguments do not read the row |
| [DDD00052](#ddd00052) | Error | A function named without its schema belongs to a module |
| [DDD00053](#ddd00053) | Error | A type argument of a template meets its parent's constraints |
| [DDD00054](#ddd00054) | Warning | Use the row access contributions your references offer |
| [DDD00055](#ddd00055) | Warning | A context's migration files are named after its module |
| [DDD00056](#ddd00056) | Error | A request interface is one a behavior can be written for |
| [DDD00057](#ddd00057) | Error | The Mediator library's pipeline behavior has the shape the generator writes a behavior for |
| [DDD00058](#ddd00058) | Error | A notification implements no request interface |
| [DDD00059](#ddd00059) | Warning | The member list of a resource is written from what the resource declares |
| [DDD00060](#ddd00060) | Warning | A member class names an aggregate root whose members it is |
| [DDD00061](#ddd00061) | Warning | A request that declares its access is sent, not handed to its handler |

Most of these say the generator could not do what you asked. The rest are a different kind: they are
rules about the model rather than about the declaration, and each of them names code that compiles,
reads well and does not do what it looks like it does. [DDD00021](#ddd00021) is about the boundary
between two aggregates; [DDD00022](#ddd00022) and [DDD00023](#ddd00023) are about the boundary
between two [modules](modules.md) and say nothing at all until a project declares itself one;
[DDD00024](#ddd00024) to [DDD00027](#ddd00027) are about [invariants](invariants.md), where the
failure worth catching is a rule that is written, tested, and never run; [DDD00028](#ddd00028) to
[DDD00030](#ddd00030) are about [composite keys](composite-keys.md), and the section after them lists
the one key-part mistake that can only be caught when the Entity Framework model is built.
[DDD00031](#ddd00031), [DDD00054](#ddd00054) and [DDD00055](#ddd00055) are about the
[Supabase export](supabase.md), where the failure worth catching is a module whose migrations, or the
policies a package offers, never reach Supabase, or whose files a rename writes a second time.
[DDD00032](#ddd00032) is about
[Relay node ids](graphql.md#relay-node-ids), where it is a node id that silently carries nothing.
[DDD00033](#ddd00033) is about the [generated integration event registration](integration-events.md#registered-when-the-module-compiles),
where it is an outbound class or a handler that is never registered.
[DDD00034](#ddd00034) to [DDD00037](#ddd00037) are about [event names](domain-events.md#stable-names), where
it is a stored row or a message read back as the wrong type, or as the wrong shape.
[DDD00038](#ddd00038) to [DDD00041](#ddd00041), [DDD00051](#ddd00051) and [DDD00052](#ddd00052) are about
[row access rules](row-level-security.md#row-access-rules-written-in-c), where it is a rule the database
enforces differently from the C# that states it, asks more often than it has to, or not at all.
[DDD00056](#ddd00056) to [DDD00058](#ddd00058) and [DDD00061](#ddd00061) are about the
[access behavior written for a request interface](access-requirements.md#the-generated-behavior-with-mediator), where it
is a message that reaches its handler with nothing having asked what it requires.
[DDD00059](#ddd00059) and [DDD00060](#ddd00060) are about the
[member list of a resource](membership.md#the-member-list-is-written-for-you), and say what the generator of
the Membership package could not tell, and which member class names the wrong resource.

That split is what the numbering is for. DDD00001 to DDD00019 are reserved for "the generator could
not do what you asked", and DDD00020 upwards for rules about the model, with one exception:
[DDD00042](#ddd00042) to [DDD00050](#ddd00050) and [DDD00053](#ddd00053), about
[supporting domains](writing-a-supporting-domain.md), [DDD00052](#ddd00052), about a function's name, and
[DDD00056](#ddd00056) and [DDD00057](#ddd00057), about an access behavior,
say what the generator could not do, and are numbered after the rest because they came later. Severity
does not follow the split. [DDD00020](#ddd00020) and [DDD00027](#ddd00027) are errors even though they
sit in the second group, because in both the generator drops the member rather than emitting something
wrong, and a warning would leave you with a rule that silently never runs. [DDD00014](#ddd00014) is a
warning in the first group: the generator could not read the name the project asked for, and what it
generates under the assembly's name instead still compiles.

The table above is the complete list: DDD00012 and DDD00015 to DDD00019 have never been assigned, and
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

## DDD00014

**The generators cannot read the project's MSBuild properties.**

```xml
<PropertyGroup>
  <DDD_Module>Billing</DDD_Module>   <!-- ignored: the generators never see it -->
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Temp.DDDToolkit.Analyzers" Version="3.1.0"
                    PrivateAssets="all" ExcludeAssets="build;buildTransitive" />   <!-- DDD00014 -->
</ItemGroup>
```

A generator only sees an MSBuild property the project lists as a `CompilerVisibleProperty`. The
`DDDToolkit.Analyzers` package lists the ones the toolkit's generators read, in a props file NuGet
imports into every project the generators arrive in. Here the generators arrived and that file did
not, so `DDD_Module` is ignored whatever the project sets, and `{Module}EventNames`,
`Add{Module}Converters`, `Add{Module}IntegrationEvents` and `Add{Module}GraphQlRuntimeBindings` are
named after the assembly. Before this warning existed that happened without a word.

The generator can tell the two cases apart. A property that is listed and not set reaches it as an
empty value, which means "use the assembly name" and is never reported. A property that is not listed
does not reach it at all.

Two ways it happens, and the fix for each:

- **The reference leaves the package's build assets out**, with `ExcludeAssets`, or with an
  `IncludeAssets` that names `analyzers` and not `build` and `buildTransitive`. Take the restriction
  off. `PrivateAssets="all"` is fine: it decides what travels on to projects that reference yours, not
  what yours gets.
- **The generator is referenced as an assembly**, not as a package: an `<Analyzer Include="...dll" />`,
  or a project reference with `OutputItemType="Analyzer"`. Nothing imports a props file then, so list
  the property yourself, in the project or in `Directory.Build.props`:

  ```xml
  <ItemGroup>
    <CompilerVisibleProperty Include="DDD_Module" />
  </ItemGroup>
  ```

It is reported once per project and has no line to point at, since no line of your code is wrong. An
assembly that declares `[assembly: Module]` is not reported: the module names its generated code, and
the property is not read. See
[DDD_Module, and the package that brings it](modules.md#ddd_module-and-the-package-that-brings-it).

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

## DDD00032

**Do not ask HotChocolate's generator for a toolkit identifier's node id serializer.**

```csharp
graphql.AddNodeIdValueSerializerFrom<OrderId>();   // DDD00032
```

`AddNodeIdValueSerializerFrom<T>()` is intercepted by HotChocolate's own generator, which writes a
serializer from the properties `T` declares in source. A toolkit identifier's `Value` is written by the
toolkit's generator, and source generators do not see each other's output, so HotChocolate's finds no
property and writes a serializer that stores nothing. Every order's node id becomes `Order:` and reads
back as an empty `OrderId`. Nothing fails to compile and nothing throws.

Remove the call. `Add{Module}GraphQlRuntimeBindings()` already registers a serializer for every
identifier over a `Guid`, `string`, `int`, `long` or `short`, in HotChocolate's own format; see
[Relay node ids](graphql.md#relay-node-ids).

---

## DDD00033

**The generated integration event registration must be able to construct the class.**

```csharp
public sealed class PublishOrderPlaced : IOutboundIntegrationEvent<OrderPlaced, OrderPlacedV2>
{
    private PublishOrderPlaced() { }   // DDD00033: no constructor this assembly can call
    // ...
}
```

The generated `Add{Module}IntegrationEvents()` registers every outbound class and every handler in the
module by writing `new` for it, and takes each constructor parameter from the scope the message is
delivered in. That only works for a class with one accessible constructor with the most parameters,
whose parameters are not `ref`, `out` or `params`, and whose parameter types the assembly can see. The
message says which of those failed.

A class the registration cannot construct is left out of it, so its domain event is never published, or
its contract is never handled. That is a warning rather than silence because nothing else would tell
you. Give the class a constructor the registration can call, or register it by hand.

The registration also names the domain events of the module's projects that do not reference Entity
Framework, such as a domain project next to the infrastructure project that holds the outbox. One of those
that this project cannot see, an `internal` event, is left out the same way, and reported on the project's
`[assembly: Module]` attribute:

```
'Sales.Domain.OrderAudited' is left out of the generated integration event registration: a domain event of
Sales.Domain, a project of this module, that this project cannot see; make it public
```

Make the event public. Left out, it is still stored when it is raised, under the same name, and never
delivered: the processor records the missing registration on its row at every attempt (see
[Registering event types](event-delivery.md#registering-event-types)).

---

## DDD00034

**An event's class name and its Version disagree.**

```csharp
[IntegrationEvent(Version = 3)]                     // DDD00034, at Version = 3
public sealed record OrderPlacedV2(OrderId OrderId);
```

A class name that ends in `V` and a number is that version of its event by convention, and `Version` on
`[IntegrationEvent]` states one explicitly. The stated version wins wherever the version is read: the
outbox, the published message, the generated registration. So this event is version 3, and the `V2` in its
name is ignored. Nothing is ambiguous, which is why this is a warning and not an error, but a name that
says 2 about an event that is 3 is how somebody ends up writing an upcaster for the wrong version.

There are two fixes. The first renames the class to the version it is, `OrderPlacedV3`, everywhere it is
used; the event does not change. The second removes `Version = 3`, which makes the event version 2, the
name's, for when the name was right and the attribute was not. See [Versions are in the class name](domain-events.md#versions-are-in-the-class-name).

---

## DDD00035

**An event's class name ends in something that is not a version.**

```csharp
public sealed record OrderPlacedV0(OrderId OrderId) : DomainEvent;    // DDD00035
public sealed record OrderPlacedV01(OrderId OrderId) : DomainEvent;   // DDD00035
```

Every event's class name is read for a version suffix. Versions start at 1 and are written without leading
zeros, so `V0` and `V01` cannot be one, and reading them as part of the name instead would give this event
a version rule of its own. Rename the class: `V1` for a first version, or a name that does not end in `V`
and digits. Digits that do not follow a `V`, as in `Level2Reached`, are part of the name and fine.

---

## DDD00036

**Two events of one module share a name and version.**

```csharp
[assembly: Module("Ordering")]

namespace Ordering.Orders  { public sealed record OrderPlaced(OrderId OrderId) : DomainEvent; }   // DDD00036
namespace Ordering.Returns { public sealed record OrderPlaced(OrderId OrderId) : DomainEvent; }   // DDD00036
```

An event is found by its name and version when a stored row or a delivered message is read back, and both
of these are `ordering.order-placed` version 1. The registry would refuse the second at start-up; this
refuses it when the module compiles, on both classes, because neither is more wrong than the other.

The code fix pins another name on the class you invoke it on, made from its namespace or containing type:
`[DomainEventName("ordering.returns-order-placed")]` on a domain event, the name in its
`[IntegrationEvent("...")]` on a contract. A class whose name is already pinned is offered nothing, since
that name was chosen by hand. Renaming one of the classes works as well.

Two domain events are compared with each other, and two contracts with each other. A domain event and the
contract it is published as share a name on purpose, and so do the versions of one event; neither is
reported. The toolkit does not make a name unique from the namespace by itself, because that name would
change when the class moved. See [Two events with one name](domain-events.md#two-events-with-one-name).

---

## DDD00037

**Two event names give one constant name.**

```csharp
[DomainEventName("ordering.order-placed")]
public sealed record OrderPlaced(OrderId OrderId) : DomainEvent;

[DomainEventName("ordering.order.placed")]                           // DDD00037, on both
public sealed record PlacedOrder(OrderId OrderId) : DomainEvent;
```

Every name gets a constant in the generated `{Module}EventNames`, named after the name without its module
and in PascalCase: both of these would be `OrderingEventNames.OrderPlaced`. Whichever name the constant
held, code that reached for it meaning the other event would bind a topic or a test to the wrong event and
never find out, so this is an error, on the events of both names. Pin one of the names to something that
reads differently. Until then the first name in ordinal order keeps the constant, only so that code already
using it does not add errors of its own to this one.

## DDD00038

**A row access rule, an access function or a database question has the shape the generator reads.**

```csharp
[RowAccess<Order>(RowOperations.Read)]
public static class ACustomerSeesTheirOrders                      // DDD00038: not partial
{
    public static bool Allows(Order order) => order.PlacedBy == null;   // DDD00038: no Caller
}
```

The generator writes the rule's SQL into another part of the class, so the class is `static partial`. And it
translates `Allows`, which is a static method taking the aggregate the rule is about and a `Caller`,
returning `bool`, with a single expression for a body: after `=>`, or as its only `return` statement.
Nothing is generated until the rule has that shape, and a rule without SQL never reaches the database.

The same holds, with a little more, for the others the generator reads:

- **An `[AccessFunction]`** has a rule's shape, and may take more after the caller: `string`, `bool`, `int`,
  `long`, `Guid` or an id, each a parameter of the SQL function after the key. A decimal, a list or a
  nullable value type is not one. With `Shape = AccessFunctionShape.Set` its aggregate's key is one
  column, because the function answers with the keys of the rows it allows.
- **An `[AccessFunctionContract]`** declares nothing, or one question for the generator to implement:
  `static partial bool Allows(TKey key, ...)`, or `static partial AccessSet<TKey> Ids(...)`, which also needs
  `Shape = AccessFunctionShape.Set` if the attribute names a shape.
- **A question of an `[AccessFunctions]` class** is a `static partial` method without a body, in a
  `static partial` class: an `[AccessSet]` returns `AccessSet<T>`, an `[AccessScalar]` returns a value, and
  its parameters are the types above, or a type parameter constrained to `IEntityId`. An `[AccessSet]` or
  `[AccessScalar]` method in a class without `[AccessFunctions]` has nothing to write its body.
- **A function's name** is `schema.name`, `owner/name`, or a name relative to its owner, each part letters,
  digits and underscores, and the owner lower case letters, digits and dashes. A relative name with no owner
  is [DDD00052](#ddd00052).
- **A [column rule](row-level-security.md#column-rules)**, a rule with `Columns`, is for `RowOperations.Change`
  alone: reading, adding and removing are about whole rows. Each name in `Columns` is a property of the
  aggregate, its parent or its template's parent, or a property of a value object it holds, written with a
  dot: `"Planned.From"`. It is not a collection of its entities, whose rows are in a table of their own and
  follow the rules for the aggregate's row; a collection of values, such as a list of strings stored as an
  array, is one column and may be named whole. It is reported on the `Columns` argument. Whether the model
  stores the property in a column of the aggregate's table only the export knows, and it refuses one that it
  does not, naming the rule.
- **A function a column rule calls with `Sql.Call`** names its schema: `"public.is_agent"`, or
  `"pg_catalog.lower"` for one of Postgres's own. A column rule is asked in a trigger whose search path is
  empty, where a name without a schema finds Postgres's own functions alone. It is reported on the name.

```csharp
[RowAccess<Project>(RowOperations.Read | RowOperations.Change, Columns = [nameof(Project.State)])]   // DDD00038: Change alone
[RowAccess<Project>(RowOperations.Change, Columns = ["Stat", nameof(Project.Crew)])]                  // DDD00038: no property, a collection of entities
```

## DDD00039

**A row access rule can only say what the database can check.**

```csharp
public static bool Allows(Order order, Caller caller)
    => order.Team!.StartsWith("north");                           // DDD00039, on the call
```

A rule becomes a condition the database evaluates for every row, so it can use the aggregate's own
properties, constants written in the rule, and the caller: `caller.UserId`, `caller.IsSignedIn`,
`caller.Role` and `caller.Claim("app_metadata.team")`. It can compare them, with `==`, `!=`, `<`, `<=`,
`>` and `>=`, and combine the comparisons with `&&`, `||` and `!`. The time is `DateTimeOffset.UtcNow` or
`DateTime.UtcNow`, which the database answers as `now()`. What only the database knows it asks through an
[access function](row-level-security.md#asking-the-aggregates-entities-access-functions) or a
[question](row-level-security.md#set-shaped-questions) of an `[AccessFunctions]` class, and a set-shaped
question only with `Contains`. A method call, a local variable or another object has no column and no claim
to become, and leaving it out would make the database answer differently from the C# method, so it is an
error on the part that cannot be translated. Store what the rule needs as a property of the aggregate, or
put it in the caller's `app_metadata`.

## DDD00040

**A row access rule guards an aggregate root.**

```csharp
[RowAccess<OrderLine>(RowOperations.Read)]                       // DDD00040
public static partial class LinesOfBigOrders { ... }
```

An aggregate is read and changed as a whole. A rule on one of its entities could hide some of an order's
lines and not the order, and Entity Framework would load half an aggregate whose invariants then check half
the data. Write the rule for the root. The export gives the tables of the aggregate's entities a policy
that follows it: a line is visible exactly when its order is.

A rule about a supporting domain's parent, `[RowAccess<SubscriptionAggregate<SubscriptionId>>]`, reports
this too: the parent is abstract and has no table. Write the rule for the application's class declared
with the parent's template; it reads the parent's properties like its own.

---

## DDD00041

**A row access rule reads the aggregate's entities through an access function.**

```csharp
[RowAccess<Project>(RowOperations.Read)]
public static partial class MembersSeeTheirProjects
{
    public static bool Allows(Project project, Caller caller)
        => project.Members.Any(member => member.UserId == caller.UserId);   // DDD00041
}
```

The tables of an aggregate's entities have policies that ask the aggregate's table whether their row is
visible. A policy on the aggregate's table that read those tables would ask itself, and Postgres stops the
query with infinite recursion. Put the question in an [access function](row-level-security.md#asking-the-aggregates-entities-access-functions),
which runs as its owner and reads the entities without their policies, and call it from the rule:

```csharp
[AccessFunction<Project>("projects.is_member")]
public static partial class ProjectMembership
{
    public static bool Allows(Project project, Caller caller)
        => project.Members.Any(member => member.UserId == caller.UserId);
}

[RowAccess<Project>(RowOperations.Read)]
public static partial class MembersSeeTheirProjects
{
    public static bool Allows(Project project, Caller caller) => ProjectMembership.Allows(project, caller);
}
```

---

## DDD00042

**A parent for entities is an abstract generic class whose first type parameter is the id.**

```csharp
[AggregateRootBase]
public partial class SubscriptionAggregate<TSubscriptionId> { ... }   // DDD00042: not abstract
```

A package ships a parent for the application's own classes to derive from, and the generator writes its
base class the way it does for any aggregate root or entity: `AggregateRoot<TId>` or `Entity<TId>`, closed
over the parent's first type parameter. So the parent is abstract, since only what derives from it is ever
created; the id is its first type parameter; it is not nested in a generic type; and the id is constrained
the way the toolkit's base classes require. Like every entity it is also a `partial class`, which
[DDD00005](#ddd00005) and [DDD00002](#ddd00002) report:

```csharp
[AggregateRootBase]
public abstract partial class SubscriptionAggregate<TSubscriptionId>
    where TSubscriptionId : IEntityId, IEquatable<TSubscriptionId>
{
    public string Plan { get; private set; } = "";
}
```

A parent with more than one problem reports the first; the next is reported once that one is fixed. A
class declared with the template of a parent that reports this gets nothing generated either, and no
diagnostic of its own: the parent is the one to fix. The compiler errors in that class, such as a
`base(...)` call it cannot make or a parent's property it cannot find, come from the missing base class
and go away once the parent is fixed.

---

## DDD00043

**A template's first type argument is an entity id.**

```csharp
[Subscription<Guid>]                                                  // DDD00043
public sealed partial class ShopSubscription;
```

A template attribute names the id of the class it declares, and that class derives from a parent closed
over the id. Unlike `[AggregateRoot<Guid>]`, a template never generates an id from a raw value: the id
belongs to the application, which declares it where every module that refers to it can see it, usually a
contracts project.

```csharp
[EntityId<Guid>]
public readonly partial record struct SubscriptionId;                // in Shop.Contracts

[Subscription<SubscriptionId>]
public sealed partial class ShopSubscription;
```

---

## DDD00044

**A template takes a type from a class nobody declares.**

```csharp
[Invoice<InvoiceId>]                                                  // DDD00044: no [Subscription] class
public sealed partial class ShopInvoice;
```

Some parents need more than the id of the class that derives from them: the parent of an invoice needs the
subscription's id and the class of its lines. The template attribute takes those from the one class
declared with the template it names, so each is declared once and every class agrees on it. It looks in
the project first and, when the project declares none, in the projects it references. Declare the class
the message names, with the attribute it names, in one of them.

A code fix declares it for you, next to the class the error is on: `[Subscription<SubscriptionId>] public
sealed partial class ShopSubscription;`, named after that class (`ShopInvoice` gives the prefix `Shop`) and
declared with the id whose name matches the parent's id parameter (`TSubscriptionId` gives
`SubscriptionId`). When no id has that name, it looks for the template's name with `Id` after it instead,
so `[OrganizationUnit]`, whose parent's parameter is `TUnitId`, finds your `OrganizationUnitId`. It is
offered when exactly one such id is found, in the project or in a project it references. A template of more
than one word gives the new class the prefix and the last word only: an organization called
`ShopOrganization` that has no unit gets `ShopUnit`. A class named for its template alone has no prefix,
and the new class is then named after its template in full: `Organization` gets `OrganizationUnit`.

---

## DDD00045

**A template takes a type from a class declared more than once.**

```csharp
[Subscription<SubscriptionId>] public sealed partial class ShopSubscription;
[Subscription<SubscriptionId>] public sealed partial class TrialSubscription;

[Invoice<InvoiceId>]                                                  // DDD00045: which subscription?
public sealed partial class ShopInvoice;
```

The parent takes a type argument from the one class declared with another template. With two there is no
telling which one is meant, and picking one would bind the parent to it without a word. Keep one.

A [registration closed over your classes](writing-a-supporting-domain.md#a-registration-closed-over-your-classes)
takes its type arguments the same way, and reports this too, on the first class of the project declared with
one of its templates: `'SubscriptionModelBuilderExtensions.AddSubscriptions' is declared with
[TemplateRegistration], which takes 'TSubscription' from the class declared with [Subscription], and there
are several`.

A package can say that a template is meant to be declared more than once, with `AllowSeveral = true` on its
marker: the comments on a subscription and the comments on an invoice. For a registration several classes of
such a template are no mistake. It is written
[once per class, named after the class](writing-a-supporting-domain.md#a-template-declared-more-than-once),
and this is reported only for what still cannot be told apart, with the way out instead of "keep one":

- **Two templates of one method each have several classes.** There is no telling which class of the one goes
  with which of the other. Call the package's generic method for these, with its type arguments written out.
- **Two of the classes have one name,** in two namespaces. The registrations are named after their classes;
  give each a name of its own.
- **Two registrations would be called the same.** A method that
  [says what its registration is called](writing-a-supporting-domain.md#a-registration-named-after-what-it-is-for)
  names each after what its class says of itself, the aggregate it belongs to say, and two classes that say the
  same come to one name: `the registration of each would be called 'AddShopInvoiceDiscounts'`. Let what they
  are named after differ; with Membership, a resource has one member class. The other classes keep their
  registrations.

A parent that takes a type from such a template is still closed over one class, so two are reported there
as for any other template.

---

## DDD00046

**A template attribute fills exactly the type parameters of its parent.**

```csharp
[AggregateRootTemplate(typeof(InvoiceAggregate<,,,>))]
public sealed class InvoiceAttribute<TInvoiceId> : Attribute;        // DDD00046: nothing fills the other three
```

This is a mistake in the package that declares the template attribute. It is reported on the attribute
when the package is built, and on the class that uses it when the package was built without the
generator, because that is where the generator then meets it. The marker names an open parent marked
`[AggregateRootBase]` for `[AggregateRootTemplate]`, or `[EntityBase]` for `[EntityTemplate]`. The
attribute's own type arguments fill the parent's first type parameters, the id first, and every parameter
after them is filled by exactly one `[TemplateArgument]`:

```csharp
[AggregateRootTemplate(typeof(InvoiceAggregate<,,,>))]
[TemplateArgument(1, typeof(SubscriptionAttribute<>))]
[TemplateArgument(2, typeof(InvoiceLineAttribute<>), Take = TemplateArgumentKind.Type)]
[TemplateArgument(3, typeof(InvoiceLineAttribute<>))]
public sealed class InvoiceAttribute<TInvoiceId> : Attribute;
```

A parameter that takes the application's class, with `Take = TemplateArgumentKind.Type`, is not
constrained `new()`: the parameterless constructor the generator writes for that class is never public,
so it could never meet it. A parent creates the application's class through a static abstract factory
instead, as [Writing your own supporting domain](writing-a-supporting-domain.md#creating-the-applications-entities)
shows.

Type arguments the attribute has beyond what its parent takes are no mistake. They are the template's own,
such as the aggregate a class belongs to, and a registration
[takes them by position](writing-a-supporting-domain.md#a-registration-named-after-what-it-is-for).

---

## DDD00047

**A class is declared an entity or aggregate root once.**

```csharp
[AggregateRoot<SubscriptionId>]
[Subscription<SubscriptionId>]                                        // DDD00047
public sealed partial class ShopSubscription;
```

`[AggregateRoot<TId>]`, `[Entity<TId>]`, `[AggregateRootBase]`, `[EntityBase]` and a package's template
attributes each give the class a base class, and a class has only one. Keep the one that describes it:
the template, when the class is meant to extend what the package ships. `[AggregateRoot<TId>]` together
with `[Entity<TId>]` is [DDD00009](#ddd00009).

---

## DDD00048

**A class a template takes meets its parent's constraints.**

```csharp
[InvoiceLine<InvoiceLineId>]
public sealed partial class ShopInvoiceLine;                          // no IInvoiceLineFactory

[Invoice<InvoiceId>]                                                  // DDD00048
public sealed partial class ShopInvoice;
```

A `[TemplateArgument]` with `Take = TemplateArgumentKind.Type` hands one of the application's own classes
to the parent as a type argument, here the class of an invoice's lines. The parent may ask more of that
class than being declared with the right template, such as an interface it creates the class through.
Without it, the parent closed over the class would be a compile error inside generated code, so nothing
is generated and the message names what the class is missing:

```csharp
[InvoiceLine<InvoiceLineId>]
public sealed partial class ShopInvoiceLine : IInvoiceLineFactory<ShopInvoiceLine, InvoiceLineId>
{
    private ShopInvoiceLine(InvoiceLineId id, decimal amount) : base(id, amount) { }

    public static ShopInvoiceLine Create(InvoiceLineId id, decimal amount) => new(id, amount);
}
```

---

## DDD00049

**A template registration needs a class declared with each of its templates.**

```csharp
[Subscription<SubscriptionId>]                                        // DDD00049: no [Invoice] class, nor [InvoiceLine]
public sealed partial class ShopSubscription;

modelBuilder.AddSubscriptions();                                      // closed over the subscription and the invoice
```

A package's `[TemplateRegistration]` method, such as `modelBuilder.AddSubscriptions()`, is written into
every project that declares a class with one of its templates, closed over that project's classes. Each of
its `[TemplateType]` type parameters takes the one class declared with a template, or that class's id,
looked for in the project first and then in the projects it references, as a `[TemplateArgument]` is. This
project declares a class with one of the method's templates, so it is meant to get the registration, and
nobody declares a class with the template the message names:

```
A class declared with [Invoice] is needed by 'SubscriptionModelBuilderExtensions.AddSubscriptions' and
'SubscriptionServiceCollectionExtensions.AddSubscriptions', and neither this project nor a project it
references declares one; declare one, once
```

It is reported once for each missing template, naming every registration that needs it, on the first class
of the project declared with one of their templates, and no registration is written until the class is
declared. The call to it then fails to compile too, because the method it names does not exist; this is the
error to fix. The same code fix as for [DDD00044](#ddd00044) declares the class next to the one the error is
on. Where a class of the project already says the class is missing, because its parent takes a type from
it, that [DDD00044](#ddd00044) is the one report: the registrations stand back behind it, rather than say
the same once more each. Two classes declared with the template are [DDD00045](#ddd00045), and two whose
registrations would be called the same are reported once, however many registrations they share, on the
class to fix.

A module split into projects by layer declares the classes in its domain project and calls the registration
in its infrastructure project, which declares none. That project gets the registration too, closed over the
classes the projects of its module declare, the ones with the same `[assembly: Module]`, and only those.
When those projects declare a class with one of the method's templates and none with another, this is
reported on the infrastructure project's `[assembly: Module]` attribute, with no code fix: the class belongs
in the project that declares the others. [DDD00045](#ddd00045) and [DDD00050](#ddd00050) are reported there
too, for such a project.

---

## DDD00050

**A type a template registration takes meets the method's constraints.**

```csharp
[TemplateRegistration]
public static IServiceCollection AddAudit<
    [TemplateType(typeof(SubscriptionAttribute<>), Take = TemplateArgumentKind.Type)] TSubscription>(this IServiceCollection services)
    where TSubscription : class, IAudited;

[Subscription<SubscriptionId>]
public sealed partial class ShopSubscription;                         // DDD00050: not IAudited
```

A `[TemplateType]` with `Take = TemplateArgumentKind.Type` hands one of your classes to the package's
method as a type argument. The method may ask more of it than being declared with the template, such as an
interface. The registration closed over a class without it would be a compile error inside generated code,
so none is written and the message names what the class is missing. It is judged the way
[DDD00048](#ddd00048) judges a class a template takes: by the parent the generator derives it from, closed
over its own id. Give the class what the message names.

A `[TemplateType]` that takes an id is held to the method's `struct` or `class` constraint the same way:
an id declared as a `record class` where the method says `where TTenantId : struct` is reported on the
class declared with the template, rather than failing inside the registration. Its interfaces are not
judged here: the generator writes them for an entity id, which [DDD00043](#ddd00043) already asks for.

A [later type argument of the template](writing-a-supporting-domain.md#a-template-with-more-than-one-type-argument),
taken with `Argument = 1` and up, is whatever type you wrote there, and is held to the method's constraints
the way [DDD00053](#ddd00053) holds it to the parent's: whether it is a struct or a class always, and an
interface the method asks for when the type is one no generator will still complete.

```csharp
[TemplateRegistration]
public static ModelBuilder AddCommentsByAuthor<
    [TemplateType(typeof(CommentAttribute<,>), Take = TemplateArgumentKind.Type)] TComment,
    [TemplateType(typeof(CommentAttribute<,>))] TCommentId,
    [TemplateType(typeof(CommentAttribute<,>), Argument = 1)] TAuthorId>(this ModelBuilder modelBuilder)
    where TComment : CommentAggregate<TCommentId, TAuthorId>
    where TAuthorId : struct, IEquatable<TAuthorId>, IComparable<TAuthorId>;

public readonly record struct Initials(string Letters);              // equatable, as the parent asks, and without an order

[Comment<InvoiceCommentId, Initials>]                                // DDD00050: 'Initials' as 'TAuthorId', which requires 'IComparable<Initials>'
public sealed partial class ShopInvoiceComment;
```

Two more things a registration can ask of a later type argument are reported the same way, on the class that
wrote it. A type argument [whose id the method takes](writing-a-supporting-domain.md#a-registration-named-after-what-it-is-for),
with `IdOfArgument = true`, is one of your entities or aggregate roots, since its id is read from how it is
declared: `requires an entity or an aggregate root, whose id it takes`. And a type the registration is named
after, with `[TemplateRegistration(Name = ...)]`, has a name of its own, which an array has not. Name the
class the template asks for, such as the aggregate your member class is a member of. A child entity where
the registration asks for an aggregate is reported too, `requires an aggregate root`: a class of your project
is judged by what the generator will make of it.

```csharp
public sealed class Note;                                             // no [AggregateRoot<TId>], so no id

[Member<NoteShareId, UserId, NamedRole, Note>]                       // DDD00050: 'Note' as 'TResourceId'
public sealed partial class NoteShare;
```

---

## DDD00051

**A set-shaped question is asked once per statement, so its arguments do not read the row.**

```csharp
public static bool Allows(Ticket ticket, Caller caller)
    => DeskQuestions.TicketsForTeam(ticket.Team!).Contains(ticket.Id);   // DDD00051, on ticket.Team!
```

A [set-shaped question](row-level-security.md#set-shaped-questions) becomes
`"Id" = ANY (ARRAY(SELECT desk.tickets_for_team(...)))` in the policy. Postgres works the set out once,
before it reads the table, and then finds the rows through the column's index. An argument that reads the
row, a column of it or a question about it, would make the set different for every row, and Postgres would
call the function once per row instead, which is what the set-shaped form is there to avoid. The value
compared with the set, the argument of `Contains`, is the one that reads the row.

Pass the question constants, the caller, or, inside an access function, the function's own parameters.
To ask about the row itself, ask a question about one row: an access function's `Allows`, or an
`[AccessScalar]` question, which may take the row's columns and is then asked per row.

---

## DDD00052

**A function named without its schema belongs to a module.**

```csharp
// an assembly without [assembly: Module(...)]
[AccessFunctions]
public static partial class DeskQuestions
{
    [AccessSet("tickets_i_watch")]                                       // DDD00052
    public static partial AccessSet<TicketId> TicketsIWatch();
}
```

A function name without a schema is [relative to the module](row-level-security.md#names-relative-to-the-module)
that owns it: `tickets_i_watch` in the Desk module is `desk/tickets_i_watch`, and the export writes it as
the function of that name in the schema of the context that defines it, whatever the host calls that
schema. The owner is the module the declaring assembly names with `[assembly: Module]`, or the `Owner` of
the class's `[AccessFunctions]`, which a package that declares no module uses. With neither there is no
owner, and a function of the same name in another module could not be told from it.

Add `[assembly: Module("Desk")]`, give the class `[AccessFunctions(Owner = "desk")]`, or write the name
with its schema, `desk.tickets_i_watch`. A question named with its schema is a function you create
yourself, which rules call as it is.

---

## DDD00053

**A type argument of a template meets its parent's constraints.**

```csharp
[EntityId<Guid>]
public partial record SubscriptionId;                                 // a class

[Subscription<SubscriptionId>]                                        // DDD00053
public sealed partial class ShopSubscription;
```

A class declared with a template derives from the package's parent, closed over the type arguments the
template supplies: the attribute's own, the id first, and the ids a `[TemplateArgument]` takes from other
classes. The parent may ask more of an id than [DDD00043](#ddd00043) does. Most often it asks for a
struct, `where TSubscriptionId : struct`, because it holds ids by value. The parent closed over a
`record class` would be a compile error inside generated code, so nothing is generated for the class, and
the message names the type, the parent's type parameter and what that parameter requires. Declare the id
as a struct:

```csharp
[EntityId<Guid>]
public readonly partial record struct SubscriptionId;
```

A class that takes the id from another class is told so on its own line. An invoice whose parent holds
the subscription's id by value reports the subscription's id, whatever the subscription's own parent
takes, so one id of the wrong kind is reported on every class whose parent refuses it, and is fixed once.

Whether the type is a struct or a class is always judged. The parent's other constraints, an interface
for instance, are judged of a type from a referenced project, such as an id in a contracts project, and
of a type written out in full. A `partial` type declared in the same project may still be completed by a
generator, which is how an `[EntityId<T>]` gets `IEntityId<T>`, so an interface it does not show yet is
left to the compiler. An `[EntityId<T>]` that has an error of its own, [DDD00003](#ddd00003) for
instance, is not completed: it is reported here as missing `IEntityId`, and fixing its own error clears
both. A class the template takes is judged by [DDD00048](#ddd00048).

---

## DDD00054

**Use the row access contributions your references offer.**

Reported in the project that turns the Supabase export on (`<SupabaseMigrationsExport>`), normally the
host, about a package or a module it references:

```csharp
// in the package
[assembly: RowAccessContribution(typeof(AuditRowAccess))]

// in the host, nothing: DDD00054
```

A package or a module can write [row level security of its own](row-level-security.md#policies-a-package-ships):
SQL functions, policies and statements for its tables, written from your model. It offers them with
`[assembly: RowAccessContribution]`, and the export writes them into your migrations only when the project
that runs the export lists the contribution:

```csharp
[assembly: UseRowAccessContribution(typeof(AuditRowAccess))]
```

The migrations run that SQL as the role that owns your tables, so nothing a reference offers gets there
without your say. Left out, the package's tables may have no policies at all, and a rule that asks one of
its functions by its logical name fails the export, naming the rule. List it to use it. A package whose SQL
depends on what only your application knows may offer a class for you to derive from, or a generic one for
you to close with your own types: listing your class counts as using the offer. The export creates what you
list with `new X()`, so the message says what to write for the offer at hand: the attribute alone, the
attribute closed over a type of yours, or, for one whose constructor takes what only you know, a class of
yours that hands it over:

```csharp
// DDD00054 for the Membership package's contribution, which is generic and made with a resource's rules
[assembly: UseRowAccessContribution(typeof(DocumentMembershipFunctions))]

public sealed class DocumentMembershipFunctions() : MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules);
```

If leaving it out is deliberate, because you write those policies yourself, suppress the warning for the
project with `<NoWarn>`.

## DDD00055

**A context's migration files are named after its module.**

Reported in the project that turns the Supabase export on (`<SupabaseMigrationsExport>`), normally the
host, about a factory in a project it references:

```csharp
// no [assembly: Module] in the factory's project, nor in the context's
[SupabaseMigrations]
public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>  // DDD00055
```

The export names every file it writes after the module the context belongs to, as in
`20260922120000_AddOrders.ordering.ddd.sql`, and finds a module's files again by that name. The module is
the one the factory's assembly declares, or else the context's. With neither, the name comes from the
context's class, less its `Context`, and the message says which name that is. It works until somebody
renames the class: the export then looks for files under the new name and recognizes none of those it
wrote. Every migration is reported as `VersionTaken`, because the file under the old name holds its
timestamp, the module's access file is written once more, and the files have to be renamed by hand.

```csharp
[assembly: Module("Ordering")]
```

Declare the module in the project that holds the context, and the files keep their name whatever the
class is called. Files already written under the context's name, `ordering` for `OrderingContext`, stay
recognized when the module has that name, `Module("Ordering")`; under another name, rename them once.

## DDD00056

**A request interface is one a behavior can be written for.**

```csharp
[AccessRequests]
public interface IBillingRequest;                       // DDD00056: does not derive from IRequireAccess

[AccessRequests]
public interface IBillingRequest<TAnswer> : IRequireAccess;   // DDD00056: has type parameters
```

`[AccessRequests]` marks the interface a module's commands and queries implement to say
[what they require of their caller](access-requirements.md). In a project that uses
the Mediator library the generator writes a pipeline behavior for it, beside the interface and named after
it, which asks the module's access checks before every handler. For that the interface has to be one the
behavior can be written for:

| It | Because |
|---|---|
| derives from `IRequireAccess` | that is where a request's requirement is read from, and what `AccessChecks<TRequests>` is closed over |
| has no type parameters | the behavior and the module's set of checks are closed over the interface itself |
| is not declared inside a type, and is not `file`-local | the behavior is written beside it, in a file of its own, and has to name it |
| gives a behavior name no other marked interface of its namespace gives | `IBillingRequest` and `IBilling` both give `BillingAccessBehavior` |

```csharp
[AccessRequests]
public interface IBillingRequest : IRequireAccess;
```

It is an error because no behavior is written for the interface until it has that shape, so nothing asks the
checks for its requests. The interface is judged the same way in a project that does not use the library,
where no behavior is written either way: adding the library later then brings no surprise.

## DDD00057

**The Mediator library's pipeline behavior has the shape the generator writes a behavior for.**

Reported on an `[AccessRequests]` interface, about the version of the Mediator library the project
references:

```
No access behavior is written for 'IBillingRequest': the Mediator library this project references declares
IPipelineBehavior<,> otherwise than the generator knows it, with a Handle that does not take the message, a
cancellation token and the delegate that runs the next step, each once.
```

The generator implements the library's `IPipelineBehavior<TMessage, TResponse>` and reads how from the
library itself: one method, `Handle`, that takes the message, a cancellation token and the delegate that runs
the next step, in whatever order the referenced version declares them, and answers a `ValueTask` or a `Task`
of the response; and a delegate that takes the message and the token and answers the same. So a version that
moves a parameter is followed without a new release of the toolkit. A version that declares the interface
otherwise is one the generator does not know, and it writes nothing rather than guess: a behavior that did
not compile, or one that never asked the checks, would be worse than none.

The same holds for `IStreamPipelineBehavior<TMessage, TResponse>`, the pipeline of the messages that are
answered with a stream, whose `Handle` answers an `IAsyncEnumerable` of the response: where the library has
it, the generator writes a second behavior for it, and the message names the interface it could not read.
Either one unknown stops both, since half of a module's messages held and the other half not would look like
all of them were.

It is an error because without the behavior the requests of the interface reach their handlers unchecked.
Write the behavior by hand against the version you use, and take `[AccessRequests]` off the interface, which
still says which checks a request is held to:

```csharp
public sealed class BillingAccessBehavior<TMessage, TResponse>(AccessChecks<IBillingRequest> checks) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IBillingRequest, IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        await checks.RequireAsync(message, cancellationToken);
        return await next(message, cancellationToken);
    }
}

// Only where the module has messages answered with a stream, which pass this pipeline and not the other
public sealed class BillingAccessStreamBehavior<TMessage, TResponse>(AccessChecks<IBillingRequest> checks) : IStreamPipelineBehavior<TMessage, TResponse>
    where TMessage : IBillingRequest, IStreamMessage
{
    public async IAsyncEnumerable<TResponse> Handle(TMessage message, StreamHandlerDelegate<TMessage, TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await checks.RequireAsync(message, cancellationToken);
        await foreach (var item in next(message, cancellationToken))
        {
            yield return item;
        }
    }
}
```

---

## DDD00058

**A notification implements no request interface.**

```csharp
[AccessRequests]
public interface IBillingRequest : IRequireAccess;

public sealed record InvoiceClosed(InvoiceId Invoice) : INotification, IBillingRequest   // DDD00058
{
    AccessRequirement IRequireAccess.RequiredAccess => new BillingAccess.OnInvoice("billing.view", Invoice);
}
```

The behavior written for an `[AccessRequests]` interface is part of Mediator's pipeline for commands and
queries, and the second one of its pipeline for the messages that are answered with a stream. Mediator
publishes a notification to its handlers through neither. So a notification that implements the interface
declares a requirement nothing ever asks: every handler of it runs for whoever published it, while the
declaration reads as if it were checked.

It is reported on the type where the notification and the interface are first joined, a class, a struct or
an interface of your own that is both, and not again on what implements or derives from that. A partial
type is reported once, at the part that lists what it implements. A project that does not use the library
has no notifications, and reports nothing.

What needs a check before it is handled is sent as a command or a query:

```csharp
public sealed record CloseInvoice(InvoiceId Invoice) : ICommand, IBillingRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new BillingAccess.OnInvoice("billing.close", Invoice);
}

public sealed record InvoiceClosed(InvoiceId Invoice) : INotification;   // says what happened, to whoever listens
```

A notification says that something happened, and whoever publishes it has passed its own check already.
Where the handlers of a notification must not run for every publisher, ask the checks before publishing,
`AccessChecks<IBillingRequest>.RequireAsync(request, cancellationToken)`, with a request of the module's
that declares the requirement.

## DDD00059

**The member list of a resource is written from what the resource declares.**

```csharp
[Member<DocumentShareId, UserId, NamedRole, Document>]
public sealed partial class DocumentShare;

[AggregateRoot<DocumentId>]
public sealed partial class Document                    // DDD00059: two properties of UserId, and no codes
{
    public UserId OwnerId { get; private set; }

    public UserId WrittenBy { get; private set; }

    public partial IReadOnlyList<DocumentShare> Shares { get; }
}
```

For a class declared with [Membership](membership.md)'s member template, the package's generator writes the
member list on the resource the template names: a private property `Members`, which the resource's own
methods change its members through. It writes it from four things the resource declares, and only when each
can be told without a guess:

| The list needs | It is | Not told when |
|---|---|---|
| the members | the one get-only `partial` property of `IReadOnlyList<TMember>`, `IReadOnlyCollection<TMember>` or `IEnumerable<TMember>`, which the toolkit keeps in a list | there is none, or there are two; a set is kept in a `HashSet` |
| the owner | the one property of what a member is known by, the template's second type | there is none, or there are two |
| a new row's id | `CreateSequential` of the member class's own id, an `[EntityId<Guid>]` | the id is over something else than a `Guid` |
| the codes | the one static property or field of `MembershipCodes` on the resource | there is none, or there are two |

It is also not written when the resource has a member called `Members` already, and when two member classes
name the same resource and it keeps a collection of both, or of neither, since both lists would be called the
same. A member class that names no aggregate root, or one that keeps its members as another member class, is
[DDD00060](#ddd00060), on the member class.
Nor when a type of the member class is one the generator cannot see: the id of an `[AggregateRoot<Guid>]` is
written by another generator, and no generator sees what another writes. Declare such an id yourself, with
`[EntityId<Guid>]`, as the member class's own id is. The message names each thing that stands in the way.

Say what is missing, and the list is written:

```csharp
[AggregateRoot<DocumentId>]
public sealed partial class Document
{
    public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

    public UserId OwnerId { get; private set; }

    public partial IReadOnlyList<DocumentShare> Shares { get; }
}
```

Or write it yourself, which is the form for every shape above. A resource that declares a `MemberList` of its
member class, under whatever name, is left alone and hears nothing:

```csharp
private MemberList<DocumentShare, DocumentShareId, UserId, NamedRole> Members
    => new(_shares, OwnerId, DocumentShareId.CreateSequential, DocumentRefusals.Membership);
```

It is a warning because the resource compiles: it has no member list, so its members cannot be changed, and
the first method you write over `Members` says only that the name does not exist. A resource that changes its
members some other way turns the warning off for the project, `<NoWarn>DDD00059</NoWarn>`.

## DDD00060

**A member class names an aggregate root whose members it is.**

```csharp
[AggregateRoot<DocumentId>]
public sealed partial class Document
{
    public partial IReadOnlyList<DocumentShare> Shares { get; }
}

[Member<DocumentShareId, UserId, NamedRole, Document>]
public sealed partial class DocumentShare;

[Member<DocumentWatcherId, UserId, NamedRole, Document>]
public sealed partial class DocumentWatcher;                // DDD00060: the document keeps its members as shares already

[Member<NoteShareId, UserId, NamedRole, DocumentShare>]
public sealed partial class NoteShare;                      // DDD00060: a share is no aggregate root
```

The fourth type of the [member template](membership.md) is the resource the members are of: an aggregate root
of yours, which keeps them in a collection of the member class and gets the member list over it. A resource
has one member class. Of two that name it, the one the resource keeps a collection of is its member class, and
its list is written, so the resource's own methods still compile; the other is reported, on itself, as the
class to fix: name the resource it is for, or remove it. A member class whose resource is no aggregate root,
a child entity, the member class itself or a class that is no entity, is reported on itself too: name the
aggregate root the members belong to.

A project that gets the package's registrations as well, the infrastructure project that holds the context,
hears the same from them, as the errors [DDD00045](#ddd00045) and [DDD00050](#ddd00050), on the same class,
and this warning is not reported beside them. Where the resource keeps a collection of both member classes,
or of neither, there is no telling which is the extra one: that is [DDD00059](#ddd00059), once, on the
resource.

## DDD00061

**A request that declares its access is sent, not handed to its handler.**

```csharp
[AccessRequests]
public interface IBillingRequest : IRequireAccess;

public sealed class InvoiceReminders(CloseInvoiceHandler handler)
{
    public async Task CloseOverdueAsync(InvoiceId invoice, CancellationToken cancellationToken)
        => await handler.Handle(new CloseInvoice(invoice), cancellationToken);   // DDD00061
}
```

The behavior written for an `[AccessRequests]` interface asks the module's checks in Mediator's pipeline, which
a command or a query passes when it is sent. A handler called directly passes no pipeline: it runs with nothing
having asked what the request declares, and nothing at run time notices. Where the database has
[policies](row-level-security.md), they are all that stand in the way, and per table they are coarser than what
one request requires: a policy has to let every caller who may write a project's row write it, to close the
project or to rename it, so it lets a rename through for one who may only close it. So the call is reported
where it is written: `Handle` of one of Mediator's handlers, `ICommandHandler`, `IQueryHandler`,
`IRequestHandler` or one of their stream kinds, or of a class that implements one or derives from one, with a
message that implements a marked interface. A reference to `Handle` that makes a delegate of it is reported too.

Send it instead, and it passes the behavior on its way to the same handler:

```csharp
public sealed class InvoiceReminders(ISender sender)
{
    public async Task CloseOverdueAsync(InvoiceId invoice, CancellationToken cancellationToken)
        => await sender.Send(new CloseInvoice(invoice), cancellationToken);
}
```

A code fix does that where the call can use an `ISender`, or an `IMediator`, in a local, a parameter, a field or a
property: not a field from a static member, say, nor a local of the method around a static lambda. A query
answered with a stream is sent with `CreateStream`, and named arguments are put in the order of the handler's
parameters. Where the call can use none, inject one: the fix does not change a constructor for you.

What is not reported, and why:

- **Constructing a handler, and injecting one.** The call is where the check is skipped, and it is reported
  there, also when it goes through the interface the handler was injected as.
- **Mediator's own dispatch,** which is generated code and calls every handler after the pipeline.
- **`base.Handle`** in a handler that overrides it: that hands on the request that passed on its way in.
- **A decorator,** a handler that wraps another of the same message and hands it the message it was given,
  unchanged. The request passed the pipeline on its way to the decorator, and sending it again would bring it
  back round to the decorator. A message the decorator makes itself is reported.
- **A test project,** one with `IsTestProject` or `IsTestingPlatformApplication` set, as
  `Microsoft.NET.Test.Sdk` and Microsoft.Testing.Platform set them. A test that calls a handler on purpose
  tests the handler alone. A test of what a request may do sends it.
- **A project without Mediator,** where no behavior is written: there you ask the checks yourself,
  [in front of every handler](access-requirements.md#asking-the-checks-without-mediator).

A call that is meant, outside a test project, is suppressed where it is made, with the reason beside it:

```csharp
#pragma warning disable DDD00061 // replayed from the outbox after the behavior let it through once
await handler.Handle(replayed, cancellationToken);
#pragma warning restore DDD00061
```

The build cannot see every way round the behavior. A helper that calls `Handle` on a handler of a type parameter
with no constraint to a marked interface, a dispatcher of a transport's own say, is not reported, and neither is
the call that hands it a handler: such a dispatcher sends through the sender, or asks the checks itself. A
module whose behavior is not in the pipeline is stopped when the host starts, by the
[start-up check](startup-checks.md) `access.behaviors-registered`; see
[When nothing asks the checks](access-requirements.md#when-nothing-asks-the-checks).

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
