# Modules

Everything else in this toolkit is tactical: an id that cannot be mixed up, a value object that cannot
be invalid, an aggregate that saves as one unit. None of it stops one part of your system from
reaching into another part and taking whatever it finds. That is a strategic question, and in a
modular monolith it is the only one that decides whether you still have modules a year from now.

This page is about the one strategic rule a compiler can actually check: a module is a boundary, and
you may only use what the module on the other side published.

## What a module is here

**A module is an assembly.** One project, one module, named in its project file:

```xml
<!-- Ordering.csproj, or the Directory.Build.props of a folder of projects -->
<DDD_Module>Ordering</DDD_Module>
```

That is the whole declaration. Everything the assembly declares belongs to the module, and everything
it declares is internal to the module unless it says otherwise. The build turns the property into
`[assembly: Module("Ordering")]` in the compiled assembly, which is how every project that references it
knows the module, and a project may write that attribute in any file itself instead; one in the source
wins. Set in a `Directory.Build.props`, the property declares a whole folder of projects at once: see
[A module named by its folder](#a-module-named-by-its-folder).

Two assemblies may carry the same name, and then they are one module. That is how you split a module
into `Ordering.Domain` and `Ordering.Infrastructure` without inventing a boundary between them; see
[A module in layers](#a-module-in-layers).

An assembly with no module is never reported for, and never reported against. The framework, your
NuGet packages, a shared kernel and every project you have not got round to yet all stay out of the
way. Nothing changes in a codebase until somebody sets `DDD_Module` or adds the attribute.

## What it publishes

Once there are two modules, each one decides what the other may use. It marks those types
`[ModuleContract]`, and every integration event it declares is published without being marked:

```csharp
[ModuleContract]
[EntityId<Guid>("CUS")]
public readonly partial record struct CustomerId;

[ModuleContract]
public sealed record CustomerSummary(CustomerId Id, string Name);
```

Everything else in the assembly, `public` or not, is the module's own business. Why a module would
want that, what belongs in a contract and where to keep it is a page of its own:
[Module contracts](module-contracts.md).

A project that holds nothing but what its module publishes, a contracts project, says so once instead of on
every type: `<DDD_ModuleContracts>true</DDD_ModuleContracts>` in its project file, or
`[assembly: ModuleContracts]` in any file of it, and every public type of it is published. See
[A contracts project](#a-contracts-project).

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
| Several modules inside one project | A module is an assembly here. See [below](#why-not-namespaces) |
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

A consuming module also says who its handlers run as, next to them:
`module.Around((services, message, contract) => Callers.Begin(Caller.SystemIn("shipping")))`. Under row
level security that is what its policies see, for the handler's work and its inbox row alike; see
[Who the handlers run as](integration-events.md#who-the-handlers-run-as).

## One set of modules, any host

A module registers everything it needs itself, so a host only chooses which modules it runs and how
their messages travel. The example shop runs the same five modules as one process and as three
services:

```mermaid
flowchart TB
    subgraph monolith ["ModularMonolith: one host, messages in process or through one queue"]
        direction LR
        M1["Catalog"] ~~~ M2["Ordering"] ~~~ M3["Inventory"] ~~~ M4["Payments"] ~~~ M5["Shipping"]
    end
    subgraph services ["Microservices: three hosts, messages over pgmq, Wolverine or MassTransit"]
        direction LR
        Gateway["Gateway: one GraphQL schema"] --> Storefront["Storefront: Catalog, Ordering"]
        Gateway --> PaymentsService["Payments: Payments"]
        Gateway --> Fulfilment["Fulfilment: Inventory, Shipping"]
    end
    monolith ~~~ services
```

<details>
<summary>Show the code: two hosts over the same modules</summary>

The monolith runs all five, and hands each module's messages to the others in process:

```csharp
var host = ModuleHost.InProcess(database);

builder.Services.AddCatalogModule(host);
builder.Services.AddOrderingModule(host);
builder.Services.AddInventoryModule(host);
builder.Services.AddPaymentsModule(host);
builder.Services.AddShippingModule(host);
```

*[`ModularMonolith.Supabase/Examples.Webshop.Host/Program.cs`](../Examples/ModularMonolith.Supabase/Examples.Webshop.Host/Program.cs)*

The storefront service runs two of them, and sends what the others need through pgmq:

```csharp
var host = new ModuleHost(
    ModuleDatabase.Postgres(connectionString),
    outbox =>
    {
        outbox.SendToModules();   // Catalog to Ordering, next door
        outbox.SendToPgmq();      // everything the other services handle
    });

builder.Services.AddCatalogModule(host);
builder.Services.AddOrderingModule(host);
```

*[`Microservices.Pgmq/Examples.Webshop.Pgmq.Storefront/Program.cs`](../Examples/Microservices.Pgmq/Examples.Webshop.Pgmq.Storefront/Program.cs)*

</details>

A module does not know which of the two it is in. What changes is the host's `Program.cs`, and the
modules' boundaries are what make that possible: nothing crosses between them except contracts, and a
contract travels as well over a queue as through a method call.

## A module in layers

A module can be as many projects as its layers: a contracts project with what it publishes, a domain project
with its aggregates and events, an application project with its use cases, an infrastructure project with
its context and migrations, and an API project with its routes and the module's entry. Every one of them
declares the same module, Ordering, so they are one module to the analyzer, and the domain, application and
contracts projects reference neither Entity Framework nor ASP.NET Core. The folder they are in says so for all
of them, with `DDD_Module` in its `Directory.Build.props`
([A module named by its folder](#a-module-named-by-its-folder)), or each says it with
`[assembly: Module("Ordering")]`.

The generators treat the projects of one module as one module too. What Entity Framework needs is written
where Entity Framework is, into the infrastructure project, from what the other projects declare:

```mermaid
flowchart TB
    subgraph ordering ["module Ordering"]
        Api["Ordering.Api<br/>the module's entry, routes, GraphQL schema<br/>generated: AddOrderingGraphQlRuntimeBindings(),<br/>when it references DDDToolkit.HotChocolate"] --> Application["Ordering.Application<br/>use cases, ports"]
        Api --> Infrastructure
        Infrastructure["Ordering.Infrastructure<br/>context, migrations, adapters<br/>generated: AddOrderingConverters(),<br/>AddOrderingIntegrationEvents(),<br/>a package's registrations"] --> Application
        Application --> Domain["Ordering.Domain<br/>aggregates, domain events"]
        Domain --> Contracts["Ordering.Contracts<br/>published ids"]
    end
    Host["the host"] --> Api
    Shipping["Shipping.Infrastructure<br/>generated: AddShippingConverters()<br/>stores OrderId too"] --> Contracts
```

<details>
<summary>Show the code: the module's entry and what it calls</summary>

```csharp
// Ordering.Api: the entry, the one type of the project that names the infrastructure project
public static class OrderingModule
{
    public static IServiceCollection AddOrderingModule(this IServiceCollection services, ModuleHost host)
        => services.AddOrderingInfrastructure(host).AddOrderingApplication();

    public static IEndpointRouteBuilder MapOrderingModule(this IEndpointRouteBuilder routes)
        => routes.MapOrderingEndpoints();
}

// Ordering.Infrastructure: the context, the adapters of the ports, and what the generators wrote here
public static IServiceCollection AddOrderingInfrastructure(this IServiceCollection services, ModuleHost host)
{
    host.Database.AddContext<OrderingContext>(services, OrderingContext.Schema);
    services.AddScoped<IOrderStore, EfOrderStore>();
    services.AddDDDToolkitEntityFramework(options => options.UseOutbox<OrderingContext>(outbox =>
    {
        outbox.AddOrderingIntegrationEvents();
        host.Publish(outbox);
    }));
    return services;
}

// The host: one reference and one call per module
builder.Services.AddOrderingModule(host);
```

</details>

- **Converters.** Every id and single value object implements `ISingleValue<TSelf, TValue>`. The
  infrastructure project's `Add{Module}Converters()` registers `SingleValueConverter<T, TValue>` for the ids of
  its module's other projects that have no converter of their own, and registers the published ids of other
  modules it references, with their own converter where their project references Entity Framework. It is
  written even when the project declares no id. See
  [Identifiers](identifiers.md#stored-by-a-project-that-does-not-declare-it).
- **Domain events.** Its `Add{Module}IntegrationEvents()` names the domain events of its module's projects
  that do not reference Entity Framework, under the names they would have had in their own registration.
  Those events must be `public`; one the infrastructure project cannot see is
  [DDD00033](diagnostics.md#ddd00033).
- **A package's registrations.** A registration closed over the classes a module declares with a package's
  templates, such as `modelBuilder.AddTenancy()`, is written into the infrastructure project, which references
  the package's Entity Framework registrations, closed over the classes of the domain project. A project of
  the module above it, such as the API project, gets none: it calls the infrastructure project's own public
  registration. See
  [Writing your own supporting domain](writing-a-supporting-domain.md#a-registration-closed-over-your-classes).

- **GraphQL bindings.** An API project that references `DDDToolkit.HotChocolate` holds the module's GraphQL
  source schema, and gets `Add{Module}GraphQlRuntimeBindings()`: the ids of its module's other projects, and
  the published ids of the modules it names, as scalars and node ids. See
  [Bound by a project that does not declare it](graphql.md#bound-by-a-project-that-does-not-declare-it).

Only projects that declare the same module are taken together. A project of another module gets none
of this but the converters for the ids this module publishes, as `Shipping.Infrastructure` stores `OrderId`
above. A project that declares no module, such as the host, registers only what it declares itself, and a
package that declares no module is never taken for one of your projects.

The module's entry, `AddOrderingModule(host)`, goes in the API project, which is what a host serves. It calls
the infrastructure project's own registration, `AddOrderingInfrastructure(host)`, for the context, the
adapters of the ports and whatever the generators wrote there, then the application project's, and it maps
the routes. So the host references the API project and nothing else of the module, and still makes one call
per module. The API project references the infrastructure project for that one call: the entry is the only
type in it that names the infrastructure project, and no route names it, Entity Framework or a port. The
[Tenancy sample](../Examples/README.md#the-tenancy-sample) is laid out this way, with tests that hold
each project to it. Its modules' reads each take a context of their own, so its infrastructure registrations
take their contexts from a pool, with `AddScopedFromPool` for the request's own
([Contexts from a pool](entity-framework.md#contexts-from-a-pool)): two pools for each context, one on the
host's connections for requests and one on those for background work.

### A module named by its folder

A module in layers is several projects that all say the same thing. Rather than a `Module.cs` in each of them,
the `Directory.Build.props` of the module's folder says it once, for every project below it:

```xml
<DDD_Module>Ordering</DDD_Module>
```

The name names the module's code, its stored events and its functions, so it holds no `"` and no `\`: the build
stops with the reason if it does.

A project there is the module's exactly as if it declared `[assembly: Module("Ordering")]`: the analyzer holds it
to the boundary, the generators take it together with the module's other projects, the runtime stores its events
as `ordering.order-placed`, and a project that references it sees the module, because the compiled assembly
carries the attribute. A project added to the folder is the module's from its first build. The
[Tenancy sample](../Examples/README.md#the-tenancy-sample) declares its three modules this way, each named after
its folder: [`Examples/Tenancy/Modules/Directory.Build.props`](../Examples/Tenancy/Modules/Directory.Build.props).

The build takes two steps, because a source generator never sees what another generator writes. First the
build writes the property into a file of the project, as assembly metadata, and every generator reads the
module from there. Then the toolkit's generator writes `[assembly: Module("Ordering")]` from it, for what
reads the compiled assembly: the analyzer, the runtime and every project that references it.

```mermaid
flowchart LR
    Props["Directory.Build.props<br/>DDD_Module"] --> Build["the build<br/>writes it into obj/"]
    Build --> Generators["every generator<br/>of the project"]
    Build --> Module["the toolkit's generator<br/>writes [assembly: Module]"]
    Module --> Dll["Ordering.Domain.dll"]
    Dll --> Readers["the analyzer, the runtime,<br/>every project that references it"]
```

<details>
<summary>Show the code: a folder that names its modules, and what the build and the generator write</summary>

```xml
<!-- Modules/Directory.Build.props: Modules/Ordering/Ordering.Domain is a project of module Ordering -->
<Project>
  <!-- MSBuild reads the nearest Directory.Build.props only, so import the one above. -->
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />

  <PropertyGroup>
    <!-- The name of the folder the project's folder is in. -->
    <DDD_Module>$([System.IO.Path]::GetFileName($([System.IO.Path]::GetDirectoryName($(MSBuildProjectDirectory)))))</DDD_Module>
  </PropertyGroup>
</Project>
```

```csharp
// obj/Debug/net10.0/Ordering.Domain.DDDToolkitModule.g.cs, written by the build
[assembly: global::System.Reflection.AssemblyMetadata("DDD_Module", "Ordering")]

// Module.g.cs, written by the toolkit's generator, in a project that declares no module itself
[assembly: global::DDDToolkit.Abstractions.Attributes.ModuleAttribute("Ordering")]
```

The build step is in the `DDDToolkit.Analyzers` package, as `build/` and `buildTransitive/` targets beside its
props file, so it arrives wherever the generators do. A project that references the generators as a bare
analyzer assembly, or as a project with `OutputItemType="Analyzer"`, gets no build step: import the targets file
as you import the props file, or declare its module with the attribute.
[DDD00064](diagnostics.md#ddd00064) says so where `DDD_Module` reaches the generators and nothing declared it.

</details>

The generator writes the attribute only where the project declares no module itself, which only a generator
can tell: it is handed every file the compiler compiles, the project's own and the `AssemblyInfo.cs` the build
writes into `obj/`, and the build cannot see what the source says. So the attribute is never declared twice,
and there is no CS0579 to fix:

| The project | What is written | Its module |
|---|---|---|
| has a `Module.cs` with `[assembly: Module("Sales")]` | nothing | Sales: the attribute always wins |
| has an `<AssemblyAttribute>` item that writes the attribute | nothing | the item's |
| declares no module | `[assembly: Module("Ordering")]` | Ordering, from `DDD_Module` |

A project that kept its `Module.cs` when the folder got its `Directory.Build.props` builds as before, and the
file can go when convenient. So does a folder that declares its modules with an `<AssemblyAttribute>` item that
writes `[assembly: Module]` from `DDD_Module`, and the item can go as well: the property declares the same
module.

**A project that is no module.** Two kinds of project set `DDD_Module` and are not declared a module by the
build; the property only names their generated code. One is a test project, which tests the modules from
outside and names what they do not publish: the build knows it by the mark its test SDK sets,
`IsTestProject` or `IsTestingPlatformApplication`. The other is rare: a project whose generated code wants a
name of its own, `Add{Name}Converters` and the rest, and that is meant to be no module, such as a shared
kernel every module uses without either side publishing anything, or a package of templates that becomes part
of whichever module declares a class with them. It says so beside the property:

```xml
<!-- SharedKernel.csproj: names its generated code, and is no module -->
<DDD_Module>SharedKernel</DDD_Module>
<DDD_DeclareModule>false</DDD_DeclareModule>
```

Declared a module, such a project would have every module that names one of its unpublished types hear
[DDD00022](diagnostics.md#ddd00022), and its domain events stored under its name.
[`DDDToolkit.ExampleLibrary`](../Examples/DDDToolkit.ExampleLibrary/DDDToolkit.ExampleLibrary.csproj), whose
`AddCommonConverters` the example API calls, is such a project, and so are Tenancy's and Membership's packages.
A project that wants neither the module nor the name sets no `DDD_Module` at all, and its generated code is
named after its assembly, as the
[webshop's shared kernel](../Examples/Modules/SharedKernel/Examples.Webshop.SharedKernel/Examples.Webshop.SharedKernel.csproj)
does; one that inherits the property from a folder's `Directory.Build.props` clears it with `<DDD_Module />`.
A host that sets `DDD_Module` to name its own generated code is that module too, and its domain events carry the
module's name; the samples' hosts set none, since the composition root is no module.

A diagnostic about the module that has no line of code to point at, such as
[DDD00049](diagnostics.md#ddd00049) on a module whose projects declare no class for a template, points at the
project file. A severity in an `.editorconfig` section for `*.cs` files does not reach a diagnostic there; set it
with `<NoWarn>` or `<WarningsAsErrors>`, as the sample does for DDD00022 and DDD00023, or in a global analyzer
config, a `.globalconfig` file with `is_global = true`. That includes DDD00033, DDD00045, DDD00049 and
DDD00050 in a project whose module an `<AssemblyAttribute>` item declares: they point at the project file, not
at the `AssemblyInfo.cs` that item writes into `obj/`, where a `[*.cs]` section did reach them.

### A contracts project

A module's contracts project exists to be named by the other modules: its ids, its read models, its keys and the
interfaces they ask it through. Marking each of those `[ModuleContract]` says the same thing once per type, and a
type added to the project without the mark is [DDD00022](diagnostics.md#ddd00022) in every module that names it.
So the project says it once, in its project file:

```xml
<!-- Ordering.Contracts.csproj -->
<DDD_ModuleContracts>true</DDD_ModuleContracts>
```

or with `[assembly: ModuleContracts]` in any file of it. Every public type of the project is then part of the
module's contract, and a type it keeps to itself is `internal`, which C# already holds every other project to. A
type's own `[ModuleContract]` keeps meaning what it means, in this project and in any other project of the module.
Publishing an entity this way does not make it holdable: [DDD00023](diagnostics.md#ddd00023) still fires on a
module that stores it.

**Always the project's own choice.** The toolkit never takes a project for its module's contracts because of its
name. A module may be about contracts of another kind, legal ones say, and its `Legal.Contracts` project its domain;
a toolkit that published everything in it for that name would open the module up without anybody deciding it. Only
the property or the attribute makes a project a contracts project.

**Tip: one line for a folder of modules.** A codebase whose contracts projects all end in `.Contracts` can say so once,
in the `Directory.Build.props` of its modules' folder. That condition on the name is the codebase's own convention,
written in its own props, and not something the toolkit reads:

```xml
<!-- Directory.Build.props: every project whose name ends in .Contracts is its module's contracts -->
<PropertyGroup Condition="$(MSBuildProjectName.EndsWith('.Contracts'))">
  <DDD_ModuleContracts>true</DDD_ModuleContracts>
</PropertyGroup>
```

The [Tenancy sample](../Examples/README.md#the-tenancy-sample) does this in
[`Examples/Tenancy/Modules/Directory.Build.props`](../Examples/Tenancy/Modules/Directory.Build.props), beside the
`DDD_Module` it names each module with, and none of its contracts types carries `[ModuleContract]`. A project the
condition reaches that is not meant to publish everything sets `<DDD_ModuleContracts>false</DDD_ModuleContracts>` in
its own project file, which MSBuild reads after the props.

From the property, the toolkit's generator writes `[assembly: ModuleContracts]` into the project, which is where
the other modules read it: the module boundary analyzer of a module that names one of the project's types, and
the converters and GraphQL bindings a module writes for the published ids of the modules it references.

```mermaid
flowchart LR
    Props["Directory.Build.props<br/>or the project file:<br/>DDD_ModuleContracts"] --> Generator["the toolkit's generator<br/>writes [assembly: ModuleContracts]"]
    Generator --> Dll["Ordering.Contracts.dll"]
    Dll --> Analyzer["another module's analyzer:<br/>every public type may be named"]
    Dll --> Registrations["another module's converters<br/>and GraphQL bindings of its ids"]
```

Unlike `DDD_Module`, the property needs no build step that writes it into the project first. That step is there
because every generator of a project asks which module it is in, and a generator never sees what another one writes.
Whether a type is published matters to the other modules, and they read it from the compiled assembly, where the
attribute is. The one generator of the project itself that asks, the one that writes `{Module}EventNames` and marks
it `[ModuleContract]` when every event it names is published, reads the property as well, so the property and the
attribute in source give the same class. The attribute is written only where the project declares none itself, in a
file of its own or through an `<AssemblyAttribute>` item, so it is never declared twice.

<details>
<summary>Show the code: a contracts project that says so, and what the generator writes</summary>

```xml
<!-- Ordering.Contracts.csproj, or a Directory.Build.props above it -->
<PropertyGroup>
  <DDD_ModuleContracts>true</DDD_ModuleContracts>
</PropertyGroup>
```

```csharp
// Ordering.Contracts: published, and nothing on the types says so
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;

public sealed record OrderSummary(OrderId Id, decimal Total);

public interface IOrderLookup
{
    Task<OrderSummary?> FindAsync(OrderId id, CancellationToken cancellationToken);
}

// ModuleContracts.g.cs, written by the toolkit's generator, in a project that declares no [assembly: ModuleContracts]
[assembly: global::DDDToolkit.Abstractions.Attributes.ModuleContractsAttribute]
```

Only `true` counts, in any case, as MSBuild compares it; `false`, an empty value and anything else make no
contracts project. The property reaches the generator through the props file of the `DDDToolkit.Analyzers` package,
as `DDD_Module` does ([How the property reaches the generators](#how-the-property-reaches-the-generators)). A project
whose generators arrive without that file, as a bare analyzer assembly, gets nothing from the property: it declares
the attribute in its source instead. A project that sets the property and references a `DDDToolkit.Abstractions`
older than the attribute, or none, has nothing to write it with, and hears [DDD00068](diagnostics.md#ddd00068) at
its project file rather than find out from the other modules' DDD00022.

</details>

### Folders inside the layers

Inside a layer project the thing comes first and the kind second. A folder is named for what its classes are
about: an aggregate in the domain project, a feature in the application and the API project, an adapter in the
infrastructure project. Only inside it do `Events`, `Commands` or `Rest` say what kind of class a file holds.
A folder named `Commands` at a project's root would keep every command of the module together and spread what
belongs to a crew over five folders; a folder named `Crew` keeps the crew together. The Projects module of the
[Tenancy sample](../Examples/README.md#the-tenancy-sample):

```
Directory.Build.props             every project of a module's folder declares that module, and each *.Contracts
                                  project is its module's contracts; see above
Projects/
  Examples.Tenancy.Projects.Contracts/
    ValueObjects/                 ProjectId.cs
    Keys/                         ProjectKeys.cs
    Gate/                         IProjectGate.cs, ProjectAnswer.cs
    RowAccess/                    ProjectsISee.cs, ProjectsWhereIHold.cs: what another module's row rules may ask
  Examples.Tenancy.Projects.Domain/
    GlobalUsings.cs
    Aggregates/
      Projects/
        Project.cs                the aggregate root
        ProjectRefusals.cs        its refusals, beside it
        ProjectFailures.cs        the marker of ProjectFailures.resx and .nl.resx: the refusals' texts, in two languages
        Entities/                 CrewMember.cs, on the Membership package's member template
        Events/                   ProjectOpened.cs, CrewRoleGiven.cs and the rest, an event per file
        Invariants/               NameIsValid.cs, OneMembershipPerSeat.cs and the rest, a rule per file
        ValueObjects/             ProjectState.cs, CrewMemberId.cs
      ProjectRoles/
        ProjectRole.cs            a project role of a tenant's, on the Membership package's kept-role template
        Events/                   ProjectRoleMade.cs and the rest
        ValueObjects/             ProjectRoleId.cs
  Examples.Tenancy.Projects.Application/
    GlobalUsings.cs, ProjectsApplicationServices.cs
    Access/                       the access check, the rules it asks, the keys, and what it reads of a project
      Queries/                    KeyOnProject.cs, KeysOnProjects.cs, KeysHeldAtRoot.cs
    Crew/
      Commands/                   AddCrewMember.cs, GiveCrewRole.cs, TakeCrewRole.cs, RemoveCrewMember.cs
      Queries/                    AllCrewMembers.cs
      CrewOverview.cs             what more than one use case answers with, beside them
    Lifecycle/
      Commands/                   OpenProject.cs, ChangeProjectName.cs, PlanProject.cs, MoveProjectToUnit.cs, CloseProject.cs, ReopenProject.cs
    Operators/
      Queries/                    TenantProjects.cs: what the application's own staff read
      TenantProject.cs            what it answers with
    Overview/
      Queries/                    VisibleProjects.cs, ProjectDetail.cs, and three that are asked about a page of projects
      ProjectOverview.cs, ProjectListFilter.cs
    Ownership/
      Commands/                   ChangeProjectOwner.cs
    ProjectRoles/
      Commands/                   MakeProjectRole.cs, RenameProjectRole.cs, SetProjectRoleKeys.cs, ArchiveProjectRole.cs, SetUpProjectRoles.cs
      Queries/                    TenantProjectRoles.cs, ProjectRolesById.cs
      ProjectRoleListing.cs       what both answer with
    StoredProjects/               IProjectStore.cs, IProjectReads.cs, IProjectReading.cs: the ports several features share
  Examples.Tenancy.Projects.Infrastructure/
    GlobalUsings.cs, ProjectsInfrastructure.cs
    Persistence/                  ProjectsContext.cs, marked [SupabaseMigrations], beside which the build
                                  writes the factory dotnet ef and the export build it with;
                                  EfProjectStore.cs, EfProjectReads.cs; Migrations/
    Access/                       the row rules, a class per file, column rules among them;
                                  UnitChangesWithItsKeys.cs, a rule Postgres holds beyond the
                                  policies, as a trigger of the module's own
  Examples.Tenancy.Projects.Api/
    Module.cs, GlobalUsings.cs, ProjectsModule.cs
    Access/Rest/                  AccessEndpoints.cs
    Access/GraphQL/               AccessQueries.cs, ProjectKeySetType.cs; AccessDataLoaders.cs, which a rule on a
                                  field asks through
    Crew/Rest/                    CrewEndpoints.cs
    Crew/GraphQL/                 CrewMutations.cs; CrewMemberType.cs and CrewRoleHoldType.cs, types over the
                                  application's records; CrewFieldKeys.cs, which answers the permission key a
                                  field of a crew member asks for
    Lifecycle/Rest/               LifecycleEndpoints.cs
    Lifecycle/GraphQL/            LifecycleMutations.cs
    Operators/Rest/               OperatorsEndpoints.cs
    Operators/GraphQL/            OperatorsQueries.cs, TenantProjectType.cs
    Overview/Rest/                OverviewEndpoints.cs
    Overview/GraphQL/             OverviewQueries.cs, OverviewPagedQueries.cs, ProjectType.cs, OverviewDataLoaders.cs, ChangedProject.cs
    Ownership/Rest/               OwnershipEndpoints.cs
    Ownership/GraphQL/            OwnershipMutations.cs
    ProjectRoles/Rest/            ProjectRolesEndpoints.cs
    ProjectRoles/GraphQL/         ProjectRolesQueries.cs, ProjectRolesMutations.cs, ProjectRoleType.cs,
                                  ProjectRolesDataLoaders.cs, ProjectRoleFieldKeys.cs
    Rest/                         ProjectVersions.cs: what the routes of several features share, here If-Match and ETag
    GraphQL/                      ProjectsGraphQL.cs, which registers the module's schema; ReferencedSeat.cs and
                                  the other entities of Tenancy's as this module names them; schema.graphql
```

- **A project's root** holds its entry points and nothing else: `GlobalUsings.cs`, and the class that registers
  the project, which in the API project is the module's entry, beside a `Module.cs` with HotChocolate's
  assembly attributes. No project declares the toolkit's module: the folder it is in does
  ([A module named by its folder](#a-module-named-by-its-folder)).
- **A file declares one type**, and is named after it: an event, an invariant, an entity and a value object
  each have a file of their own. A use case is the exception. Its request, its handler and what only it
  answers with are one file, because they change together.
- **The namespace of a type is its folder**, in every project: the project's name, then the folders its file
  is in, so `Projects.Domain.Aggregates.Projects.Events.ProjectOpened` and
  `Projects.Application.Crew.Queries.AllCrewMembers`. A type is found from its name, and a using says which
  part of a project a file leans on. `GlobalUsings.cs` carries the namespaces most files of a project need,
  so a file names only what is particular to it.
- **The domain project** has a folder per aggregate under `Aggregates`, named in the plural. The aggregate
  root and its refusals are in it, with `Entities`, `Events`, `Invariants` and `ValueObjects` beside them.
  An invariant is nested in the class it is about, so its file declares a partial of that class around the
  one rule, and is the one file whose namespace is not its folder: a partial has its class's namespace. What
  several aggregates share goes in `ValueObjects` and `Services` at the project's root.
- **What several modules share** and none owns is in a project of its own beside the modules,
  `Shared/Examples.Tenancy.Shared.Domain`, with the same folders by kind; it declares no module
  and references none, and only a module's domain and contracts projects reference it. The sample's example
  is `ValueObjects/DateRange.cs`, a range of calendar days: Projects plans a project with one, Inspections
  says which days an inspection covers with one, and the rule between them, that those days lie within the
  project's planned range, is held by Inspections with what Projects' gate answers. A type one module owns
  is not shared: it stays in that module's contracts, as `ProjectId` does. What the modules' infrastructure
  projects do the same way has a project beside it, `Shared/Examples.Tenancy.Shared.Infrastructure`, which
  only they reference: the check a paged read makes of the marker it is asked with, `Paging/ListCursors.cs`.
  And what their application projects do the same way has one too,
  `Shared/Examples.Tenancy.Shared.Application`, which only they reference: the check a paged query makes of
  the page it is asked for, `Paging/PageSizes.cs`.
- **The application project** has a folder per feature, named by a noun of the domain, and in it `Commands`
  and `Queries`; a feature with commands only has no `Queries` folder. A feature may be named for who it is
  for: what the application's own staff read is `Operators`. A port that one feature uses lives in that
  feature's folder, as Inspections' two do. The ports that several features share sit in a folder at the root
  named for what it holds: `StoredProjects` holds what the use cases read and save projects through. It holds
  the ports and nothing else, since what a port answers with is a feature's, and lives there. No folder in
  this project carries the name of a layer, nor `Persistence`, the name the infrastructure project gives its
  storage: a layer is a project. The access check is a feature of its own, `Access`.
- **The API project** uses the application project's feature names. A feature's routes are in `Rest`, in one
  class named after the feature, and they send that feature's requests. Its GraphQL is in `GraphQL` beside it:
  the fields in `{Feature}Queries` and `{Feature}Mutations`, which send that feature's requests too, and the
  types they answer. A field is a method marked `[Query]` or `[Mutation]`. The one exception is a field that
  pages: HotChocolate writes its connection type only for a class it generates the type of, so the paged
  fields of a feature are in a class marked `[QueryType]`, `{Feature}PagedQueries`, which holds nothing else.
  What the features share is at the project's root: in `GraphQL` the registration of the module's source
  schema, another module's entities as this one names them, and the committed schema; in `Rest` what the
  routes of several features read or write the same way. The entry maps the routes, feature by feature, and
  registers the schema.
- **The infrastructure project** is grouped by adapter, because one adapter serves many features:
  `Persistence` holds the context, its migrations and the adapters of the ports. It knows how the module is
  stored and nothing of how it is served: its project names no HotChocolate package and its code uses none, a
  list is paged by the paging library alone (`GreenDonut.Data.EntityFramework`), and no context is registered
  with a schema, because a field only sends a query and every query that reads takes a context of its own.
- **The tests** mirror it: a folder per module with a folder per feature in it, next to `Architecture` for
  the tests that hold the layout and `Infrastructure` for the fixtures.

Nothing but convention keeps a folder in step with what is in it. So the sample's tests ask:
`FeatureFolderTests` reads the requests from the host's container and holds each to its feature's `Commands`
or `Queries`, each route and each GraphQL field to the feature whose requests it sends, and every project's
root to having no folder named after a kind of class or after a layer; `LayerReferenceTests` holds a project
that knows storage to referencing no HotChocolate; `SourceTreeTests` reads the source tree and holds every aggregate, entity, event
and invariant to its folder, every file to declaring one type, and every namespace to its folder. Both are in
[`Tests/Examples.Tenancy.Tests/Architecture`](../Tests/Examples.Tenancy.Tests/Architecture).

Moving a class to another namespace changes its full name, and two things name a class as text. An Entity
Framework snapshot and each migration's designer file name every entity by its class: the model differ compares
tables, so it sees no change and asks for no migration, and the text is replaced by hand. And a name that is
stored or sent must not move with a namespace. The outbox stores a domain event under its module's name and
its class's (`projects.project-opened`), with nothing of its namespace, and the sample's models have no
discriminator column. `MigrationTests` and `StoredNameTests` hold both.
The shop under [`Examples/Modules`](../Examples/Modules) keeps a module in one project and has the same
folders inside it, with the layers as its top folders: `Domain/Aggregates/Orders/Events`,
`Application/Orders/IntegrationEvents`.

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

## DDD_Module, and the package that brings it

A module's name is also the name in the code the generators write: `{Module}EventNames`,
`Add{Module}Converters`, `Add{Module}IntegrationEvents` and `Add{Module}GraphQlRuntimeBindings`. The
generators look for that name in three places, and the first one that answers wins:

| Where | Who it is for |
|---|---|
| `[assembly: Module("Ordering")]` | A module that says so in its source. It always wins. |
| `<DDD_Module>Ordering</DDD_Module>` | A module: the build writes the attribute above from it. |
| `<DDD_Module>Tests</DDD_Module>` in a test project, or beside `<DDD_DeclareModule>false</DDD_DeclareModule>` | A project that is no module and names its generated code: a test project, a shared kernel. |
| The assembly name, with the dots removed | A project with neither. |

`DDD_Module` is an MSBuild property, so a `Directory.Build.props` can set it for every project in a
folder. The name is also the module the project declares, and the build writes `[assembly: Module]` for it, so
a `Directory.Build.props` that sets it makes every project below it that module: put it in the folder of one
module's projects, not above a host or a shared project. In a test project, and in one that sets
`DDD_DeclareModule` to false, it only names the generated code.

```xml
<!-- Modules/Ordering/Directory.Build.props: every project below is module Ordering -->
<PropertyGroup>
  <DDD_Module>Ordering</DDD_Module>
</PropertyGroup>
```

The attribute is what one assembly says about itself, which is why it wins: a project below that folder that
declares `[assembly: Module("Sales")]` is module Sales. A folder of several modules names each after its own
folder instead: see [A module named by its folder](#a-module-named-by-its-folder).

### Two assemblies, one module

Ordering and its contracts project both declare `[assembly: Module("Ordering")]`, so both generate
`AddOrderingConverters` and `AddOrderingGraphQlRuntimeBindings`. They do not collide. An assembly's
method calls the ones of the module's other assemblies it references, so one call registers the module,
and it leaves to them what they registered. A project of the module without Entity Framework or
HotChocolate has no method to call: its identifiers are registered by the method of the project that
references it ([A module in layers](#a-module-in-layers)).

```csharp
// in Ordering's context: OrderLineId from Ordering, and OrderId from Ordering.Contracts with it
configurationBuilder.AddOrderingConverters();

// in Shipping's context, which references only Ordering.Contracts and stores an OrderId: Shipping's
// registration covers the OrderId it stores, so it is the one call
configurationBuilder.AddShippingConverters();
```

Import the generated namespace of the assembly you are in, or of the one assembly of another module
you reference, and the name is never ambiguous. `Add{Module}IntegrationEvents` is the one method that
does not call the others. A contracts project has none to call; if two assemblies of one module both
generate it, call the second as an ordinary static method.

### How the property reaches the generators

A generator can only read an MSBuild property the project
declares as visible to the compiler. **The `DDDToolkit.Analyzers` package declares `DDD_Module`**, and
`DDD_ModuleContracts` beside it ([A contracts project](#a-contracts-project)). The package
that holds the generators also holds a props file declaring the properties they read, and NuGet imports
that file into each project the generators run in: one that references the package itself, one that
gets it as a dependency of `DDDToolkit`, and one that gets it through a project reference. There is
nothing to add to a project file. The build step that declares the module the property names arrives beside it,
as a targets file in the same package.

So there are two supported ways to reference the toolkit, and a module with a
[contracts project](module-contracts.md#a-project-of-its-own) uses both:

| Project | References | Gets |
|---|---|---|
| The module | `DDDToolkit`, and the integrations it uses | The base types, and through them the attributes and the generators |
| Its contracts | `DDDToolkit.Abstractions` and `DDDToolkit.Analyzers`, without `DDDToolkit` | The attributes and the generators, and nothing that runs |

The second row is for a project that should carry no runtime. A record struct identifier, a read model
and an integration event compile against the attributes alone. Entities, aggregate roots, value objects
and domain events derive from base types in `DDDToolkit`, so the project that declares those references
`DDDToolkit`.

```mermaid
flowchart LR
    Host["a project referencing the module"] --> Module["the module's project"]
    Module --> Core["DDDToolkit"]
    Core --> Analyzers
    Contracts["its contracts project"] --> Abstractions["DDDToolkit.Abstractions"]
    Contracts --> Analyzers
    subgraph Analyzers ["DDDToolkit.Analyzers"]
        direction TB
        Generators["the generators"] ~~~ Props["props: declares DDD_Module<br/>and DDD_ModuleContracts"] ~~~ Targets["targets: declares the module"]
    end
```

<details>
<summary>Show the code: a contracts project without the runtime</summary>

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <!-- the version of every other Temp.DDDToolkit.* package: the attribute, the property and the step are 3.2.0's -->
    <PackageReference Include="Temp.DDDToolkit.Abstractions" Version="3.2.0-preview.4" />
    <PackageReference Include="Temp.DDDToolkit.Analyzers" Version="3.2.0-preview.4" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

```csharp
[assembly: Module("Billing")]
[assembly: ModuleContracts]

[EntityId<Guid>("INV")]
public readonly partial record struct InvoiceId;

public sealed record InvoiceSummary(InvoiceId Id, decimal Total);

[IntegrationEvent]
public sealed record InvoiceSent(InvoiceId InvoiceId);
```

The generators write `InvoiceId` and `BillingEventNames.InvoiceSent` here, as they would in the module, and every
public type is published ([A contracts project](#a-contracts-project)).
`<DDD_Module>Billing</DDD_Module>` in the project file declares the same module as the attribute, through the
build step that arrives with the package, and `<DDD_ModuleContracts>true</DDD_ModuleContracts>` says the same as
the second attribute. Both are how
[`build/package-consumers/ContractsOnly`](../build/package-consumers/ContractsOnly/Acme.Billing.Contracts.csproj)
says it, to prove the properties and the step arrive.

</details>

Both rows are built against the packed packages on every pull request, together with a project that
gets the toolkit only through a project reference, and the build fails if `DDD_Module` did not name
the generated class, or did not declare the module, in any of them.

If the generators do arrive and the props file does not, because a reference excludes the package's
build assets, `DDD_Module` is ignored, and the build step that would declare the module, excluded with
it, declares nothing. The build says so with [DDD00014](diagnostics.md#ddd00014) rather than naming
everything after the assembly without a word. A module that declares itself with `[assembly: Module]` is
not affected: its name comes from the attribute, and the property is not read. If the property arrives and the
build step does not, because the props file is imported without the targets file, or the property is listed by
hand as DDD00014 suggests, `DDD_Module` names the code and declares no module, and the build says that with
[DDD00064](diagnostics.md#ddd00064).

## Adopting this on an existing codebase

1. Pick the module with the fewest things pointing at it and set `DDD_Module` in its project file, or
   add `[assembly: Module]` to it. Nothing happens yet, because nothing else is a module.
2. Do the same for one of its callers. Now you get a list.
3. Work the list. Most entries are a published id that was never marked, or a query that should be a
   published read model.
4. Turn `DDD00023` into an error for those two projects when its list is empty, then `DDD00022`.
5. Repeat with the next module. The rules stay silent about every project you have not reached.

## Design choices

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

### Why the property becomes an attribute

`DDD_Module` is an MSBuild property, which means it reaches the compiler of the project that sets it and
travels no further. The compiler building `Ordering` cannot read what `Billing.csproj` set, so a property alone
could name generated code and could not be a boundary. That is why the build writes the attribute from it:
what travels is the attribute in the compiled assembly, and the boundary is the attribute. The property is the
way to write it once, for a project or for a whole folder
([A module named by its folder](#a-module-named-by-its-folder)), and the attribute in a project's source wins
over it. What the two name, and how the property reaches the generators, is
[above](#ddd_module-and-the-package-that-brings-it).

Naming a project's generated code after a module and declaring the module are one property, because they are one
fact: a project whose events, converters and registrations carry a module's name is that module's. A separate
switch for the declaration would let a project carry a module's name and be left out of the module, which the
build would then have to warn about. The rare project that wants the name and not the module, a shared kernel or
a package, says so with `DDD_DeclareModule` set to false, and a test project is told by its own marks. The one
way left to carry the name and not the module by accident is to leave the package's build step behind, and
[DDD00064](diagnostics.md#ddd00064) is that warning.

## Related

- [Module contracts](module-contracts.md), what a module publishes and why.
- [Integration events](integration-events.md), the supported way across a boundary.
- [One schema over a modular monolith](graphql.md#one-schema-over-a-modular-monolith), the modules'
  GraphQL composed without a module knowing another.
- [Entities and aggregates](entities-and-aggregates.md#reference-other-aggregates-by-id), the same
  argument one scale down, inside a single module.
- [Diagnostics](diagnostics.md#ddd00022), the reference entries for both rules.
