# Modules

Everything else in this toolkit is tactical: an id that cannot be mixed up, a value object that cannot
be invalid, an aggregate that saves as one unit. None of it stops one part of your system from
reaching into another part and taking whatever it finds. That is a strategic question, and in a
modular monolith it is the only one that decides whether you still have modules a year from now.

This page is about the one strategic rule a compiler can actually check: a module is a boundary, and
you may only use what the module on the other side published.

## What a module is here

**A module is an assembly.** One project, one module.

```csharp
// Ordering/AssemblyInfo.cs, or any file in the project
[assembly: Module("Ordering")]
```

That is the whole declaration. Everything the assembly declares belongs to the module, and everything
it declares is internal to the module unless it says otherwise.

Two assemblies may carry the same name, and then they are one module. That is how you split a module
into `Ordering.Domain` and `Ordering.Infrastructure` without inventing a boundary between them.

An assembly with no `[Module]` is not a module. It is never reported for, and never reported against.
The framework, your NuGet packages, a shared kernel and every project you have not got round to yet
all stay out of the way. Nothing changes in a codebase until somebody adds the attribute.

### Why not namespaces

Several modules inside one project is the other common layout, and this analyzer does not support it.

The reason is what an analyzer can see. It gets this compilation plus the *metadata* of everything the
compilation references. When code in `Ordering` names a type from `Billing`, the analyzer has to ask
that type which module it belongs to, and the only answer available is whatever survived into
`Billing.dll`. An assembly attribute survives. A namespace cannot carry an attribute at all, so a
namespace layout would have to be described by a convention that the other side cannot confirm, and a
boundary you cannot confirm is not a boundary.

There is a second reason to prefer a project per module, and it is the better one: the compiler
already enforces `internal` at the assembly boundary. Put a module in its own project and half the job
is done by C# itself. What the analyzer adds is the other half, which C# has no word for: *public, but
not for you*.

### Why not the DDD_Module MSBuild property

`DDD_Module` already exists in this toolkit and it is not this. It names the generated
`Add{Module}Converters` and `Add{Module}GraphQlRuntimeBindings` methods, and it is an MSBuild property,
which means it reaches the compiler of the project that sets it and travels no further. The compiler
building `Ordering` cannot read what `Billing.csproj` set. Leave `DDD_Module` where it is; it is a
naming knob, not a boundary.

## The published contract

A module says what it publishes type by type.

```csharp
using DDDToolkit.Abstractions.Attributes;

[ModuleContract]
[EntityId<Guid>("CUS")]
public readonly partial record struct CustomerId;

[ModuleContract]
public sealed record CustomerSummary(CustomerId Id, string Name);

[IntegrationEvent("crm.customer-registered")]
public sealed record CustomerRegistered(Guid CustomerId, string Name);
```

Three things are published there. `[ModuleContract]` publishes a type. An
[integration event](integration-events.md) is published without a second attribute, because a type
whose whole job is to be read by somebody else is already a contract. A type nested inside a published
type is published with it.

Everything else in the assembly, `public` or not, is the module's own business.

What belongs in a contract, in rough order of how often you will want it:

| Publish | Why |
|---|---|
| Integration events | The supported way for another module to learn that something happened |
| Identifiers | Another module has to be able to point at your things |
| Value objects and read models | A copy of data, with no behaviour and no navigation |
| Interfaces your module implements | A front door with a signature |
| Entities and aggregate roots | Almost never. See below |

## What the analyzer catches

### DDD00022, using what is not published

```csharp
// in module Sales
public sealed class OrderReport
{
    public string Describe(Customer customer) => customer.Name;   // DDD00022
}
```

`Customer` belongs to module `Crm` and `Crm` does not publish it. The rule reports wherever you *name*
another module's unpublished type: a parameter, a field, a base type, a generic argument, an
attribute, a `typeof`, a `new`, a static call, a `using` alias. One name, one warning.

Two ways out. If the type really is part of the contract, mark it `[ModuleContract]` in the module that
owns it, which is a decision taken by the team that owns it. If it is not, go through something that
is: a published read model, an interface, an integration event.

### DDD00023, holding another module's entity

```csharp
// in module Sales
[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public Customer Buyer { get; private set; }   // DDD00023
}
```

This is the one that quietly ends a modular monolith, so it gets a rule of its own.

A property typed as another module's entity is a navigation. Entity Framework will map it, a query in
`Sales` will load rows belonging to `Crm`, and one `SaveChanges` will write into both modules inside
one transaction. From that point on the two modules cannot be tested apart, migrated apart, or pulled
into separate services without unpicking every query that crossed over. Nothing about the code looks
wrong; it is one property.

Hold the identifier instead, and let an integration event tell you when the other side changes:

```csharp
[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public CustomerId Buyer { get; private set; }

    public void PlaceFor(CustomerId customer) => Buyer = customer;
}
```

**Publishing the entity does not help**, and the rule fires whether or not the entity is published.
That is deliberate. `[ModuleContract]` says "you may name this type"; it cannot say "you may make this
type part of your own transaction", because that is not the owner's to give.

Like [DDD00021](diagnostics.md#ddd00021), this rule reads stored state only: fields and properties.
Passing another module's entity into a method and reading it is not reported, because nothing is
stored and Entity Framework builds nothing from it. It is still usually a sign that the call belongs
on the other side of the boundary, but it is not what this rule is about.

### Both rules are warnings

A module boundary is a design decision. The code compiles either way, nothing stops being generated,
and a codebase adopting modules wants to see the list before it is forced to fix it. Adding
`[assembly: Module]` to one project and getting forty errors would teach exactly one lesson: take the
attribute off again.

Once the list is empty, hold it:

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);DDD00022;DDD00023</WarningsAsErrors>
</PropertyGroup>
```

Or drop a rule entirely, per project:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);DDD00023</NoWarn>
</PropertyGroup>
```

Unlike the generator diagnostics, these two come from a real analyzer, so `#pragma warning disable
DDD00022` and `[SuppressMessage]` both work on a single line or member. Use them where you mean it and
leave a reason next to them.

## What the analyzer cannot catch

Read this list before you trust the rule, because the gaps are real.

| Not caught | Why |
|---|---|
| Several modules inside one project | A module is an assembly here. See [above](#why-not-namespaces) |
| A type you never name, such as `var buyer = summary.Owner;` | The rule reports names in your source, and there is no name in that line |
| An extension method called on an instance, `customer.Deactivate()` | You named the method, not the class that declares it |
| A member inherited from an unpublished base type | Reporting it would fire on types the author never saw |
| Reflection, DI by string, `dynamic`, serialization | Nothing about them is visible at compile time |
| Generated code | Skipped on purpose; you cannot fix a file you do not write |
| A published type that hands you an unpublished one | That is the publishing module's bug, and the rule is not clever enough to call it |

The last one is worth designing against rather than hoping for: if a published type exposes an
unpublished one, the contract is not really a contract. A published record of primitives and published
ids has no such hole.

## How this fits with integration events

The two halves are meant to be read together. [Integration events](integration-events.md) explain how
a message gets from one place to another and how to consume it once. This page is why you would bother
instead of adding a project reference and a navigation property.

The shape a module ends up with is small:

- It publishes identifiers, so other modules can point at its things.
- It publishes integration events, so other modules can react to its things.
- It publishes a read model or an interface where somebody genuinely needs to ask it a question.
- It keeps its entities, its aggregates, its repositories and its `DbContext` to itself.

Two modules that share only that can be deployed together forever, and can be pulled apart on the day
that stops being true. Two modules that share a navigation property cannot.

## One API over the modules: GraphQL

A module's boundary holds in its API as well. Rather than one GraphQL schema that knows every module,
each module can serve a schema of its own: its types, its queries, and its part of the types other
modules own, keyed on a name and a key they agree on. Catalog declares `Product` with its name and
price; Inventory declares its own `Product`, keyed on the same SKU, with the stock; Ordering says a line's
product is the `Product` with that SKU. No module references another's classes, and a client still sees
one `Product`.

`DDDToolkit.HotChocolate.Fusion.InMemory` composes those schemas inside the monolith with HotChocolate
Fusion, and calls the modules in memory. The same schemas compose across processes when a module becomes
a service, so its GraphQL does not change on that day either. See
[One schema over a modular monolith](graphql.md#one-schema-over-a-modular-monolith), and
`Examples/ModularMonolith.*` for five modules doing it.

## Adopting this on an existing codebase

1. Pick the module with the fewest things pointing at it and add `[assembly: Module]` to it. Nothing
   happens yet, because nothing else is a module.
2. Add `[assembly: Module]` to one of its callers. Now you get a list.
3. Work the list. Most entries are a published id that was never marked, or a query that should be a
   published read model.
4. Turn `DDD00023` into an error for those two projects when its list is empty, then `DDD00022`.
5. Repeat with the next module. The rules stay silent about every project you have not reached.

## Related

- [Integration events](integration-events.md), the supported way across a boundary.
- [One schema over a modular monolith](graphql.md#one-schema-over-a-modular-monolith), the modules'
  GraphQL composed without a module knowing another.
- [Entities and aggregates](entities-and-aggregates.md#reference-other-aggregates-by-id), the same
  argument one scale down, inside a single module.
- [Diagnostics](diagnostics.md#ddd00022), the reference entries for both rules.
