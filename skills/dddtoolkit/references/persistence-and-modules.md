# Persistence, event delivery and modules

The wiring a DDDToolkit solution needs once aggregates are stored and modules talk to each other. Each
section links the docs page with the full story; fetch it when a task goes further than this. A page
named `x.md` below is `https://dylansnel.github.io/DDDToolkit/docs/x.md`.

Namespaces: `AddDDDToolkitEntityFramework` and `UseDDDToolkit` are in `DDDToolkit.EntityFramework`;
`AddDDDToolkitConventions` in `DDDToolkit.EntityFramework.Conventions`; `AddDomainEventOutbox` in
`DDDToolkit.EntityFramework.Outbox` and `AddDomainEventInbox` in `DDDToolkit.EntityFramework.Inbox`;
`IOutboundIntegrationEvent<,>`, `IIntegrationEventHandler<>` and `[IntegrationEventConsumer]` in
`DDDToolkit.EntityFramework.Integration`; `IntegrationEventMessage` in `DDDToolkit.BaseTypes`;
`IDomainEvent` in `DDDToolkit.Interfaces`; `DispatchWithMediator` in `DDDToolkit.Mediator`.

## Entity Framework Core

Package `DDDToolkit.EntityFramework`. It brings its own generator, so referencing it is the only setup
for the generated parts. Full page: `https://dylansnel.github.io/DDDToolkit/docs/entity-framework.md`.

```csharp
using DDDToolkit.EntityFramework;

builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());   // delivery, below

builder.Services.AddDbContext<OrderingContext>((services, options) => options
    .UseNpgsql(connectionString)
    .UseDDDToolkit(services));   // the callback's provider: handlers then get this same context instance
```

```csharp
using DDDToolkit.EntityFramework.Conventions;
using Ordering.Converters;                   // generated: {AssemblyName}.Converters

public sealed class OrderingContext(DbContextOptions<OrderingContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();          // aggregate roots only

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddOrderingConverters();    // one per module, and one per assembly that is no module
    }
}
```

- `Add{Module}Converters` takes its name from `[assembly: Module("Ordering")]` when the project declares
  one, which always wins; otherwise from `<DDD_Module>Shop</DDD_Module>` in the project file or a
  `Directory.Build.props`; otherwise from the assembly name with the dots removed. The same goes for
  `Add{Module}IntegrationEvents`, `Add{Module}GraphQlRuntimeBindings` and `{Module}EventNames`.
- A module and its contracts project share the name, and the module's method calls the contracts'
  one: the module's context calls `AddOrderingConverters()` once, and another module that references
  only the contracts calls the contracts' `AddOrderingConverters()`. Never import both generated
  namespaces in one file. A shared kernel has a method of its own, and the context calls it too.
- A project without Entity Framework, such as a module's domain or contracts project, has no converters of
  its own. The generated `Add{Module}Converters()` of a project of the same `[assembly: Module]` that does
  reference it registers them with `SingleValueConverter<T, TValue>`, through `ISingleValue<TSelf, TValue>`,
  which every generated id and single value object implements; it also registers the `[ModuleContract]`
  ids of other modules that have no converter of their own. So a layered module's domain and contracts
  projects need no Entity Framework, and its infrastructure project gets the method even when it declares
  no ids. A project without a module registers only its own.
- A registration a package closes over the application's classes, such as `modelBuilder.AddTenancy()` and
  `services.AddTenancy<TContext>()`, is written into the project that declares the classes, or, in a layered
  module, into the project of the same `[assembly: Module]` that references the package's registrations and
  declares none: the infrastructure project. A project of the module above that one, such as an API project
  that composes the module, gets none: call the infrastructure project's own public registration from it.
- A module that asks Tenancy inside its own queries maps Tenancy's read model (`AddTenancyReadModel`, or
  `AddTenancyReadFunctions` on Postgres) and nothing else of Tenancy's. Its rows carry access facts: ids,
  keys, periods and statuses, and no name. So the module answers ids (`unitId`, `seatId`, `roleId`), and what
  a seat, a unit or a role is called is asked of Tenancy's directory by id (`SeatsByIdAsync`,
  `UnitsByIdAsync`, `RolesByIdAsync`), by whoever shows it. Never map a type of the module's own onto one of
  Tenancy's tables to read a name; `TenancyModel.ReadsBeyondAccessFacts(model)` in a test finds it.
- `DDD_Module` only names generated code, in a project that is no module. What makes an assembly a module is
  `[assembly: Module("Ordering")]`, below. The `DDDToolkit.Analyzers` package declares the property to
  the compiler; a build that warns DDD00014 is ignoring it, see `diagnostics.md`.
- Mapped with no configuration: identifiers and single value objects as their raw value (generated
  converters), `[Entity<T>]` children as owned types, `[ValueObject]` records inline as complex types,
  partial collections through their backing field, `Version` as the concurrency token. Do not write
  `OwnsMany`, `HasConversion` or `ComplexProperty` for these, and give owned children no `DbSet`.
  Column widths come from `ColumnLength:` on `[EntityId<T>]` and `[SingleValueObject<T>]`.
- `AddDDDToolkitEntityFramework` is required even when nothing raises events: it registers the
  interceptors. Each module may call it again to add its own outbox or contracts; every call
  configures the same options.
- Every save, in this order: domain events are dispatched, invariants are checked on everything the
  save writes (`InvariantViolationException` stops it), the version of each touched aggregate goes up.
  A stale version throws `ConcurrencyConflictException` with `AggregateType` and `AggregateId`. There
  is no automatic retry: reload and reapply a repeatable command, or report the conflict.
- A client that sends the version it read (`If-Match`, or a field of a mutation's input): after the load
  and before the change, `context.ExpectVersion(aggregate, version)`. Another loaded version throws
  `ConcurrencyConflictException` before anything changes; the same version leaves the save to compare.
- Prefer `SaveChangesAsync`. Migrations are ordinary `dotnet ef migrations add`.
- A context pool, for reads that run side by side: `AddPooledDbContextFactory<OrderingContext>(...)`
  (or `AddDbContextPool`) with the same `UseDDDToolkit(services)` in its callback, then
  `services.AddScopedFromPool<OrderingContext>()`. Commands take the scope's context; a read rents one
  per query from `IDbContextFactory<OrderingContext>`. The callback runs once, so it reads no caller,
  request or scope. A context rented from the factory dispatches in process only after
  `context.BindToScope(scope.ServiceProvider)`; the outbox needs no scope.
- A primary key made of more than the id: `[KeyPart]`, see `composite-keys.md`. Migrations as Supabase
  SQL files: `DDDToolkit.EntityFramework.Supabase`, see `supabase.md`. Queries run as the caller, and
  `[RowAccess]` rules as Postgres policies: `DDDToolkit.EntityFramework.Postgres`, see `row-level-security.md`.
- A property that never changes once its row is saved: `builder.Property(order => order.CustomerId).IsFixedAfterInsert()`.
  Entity Framework throws when a save would change it, and a policy script written with
  `RowAccessExport.WriteGrants` leaves its column out of the `UPDATE` privilege. Write the tables'
  privileges from the policies that way rather than by hand; see `row-level-security.md`.
- At start-up, `builder.Services.RunStartupChecks()` runs every check the registrations brought, before the
  server binds its port: `AddDDDToolkitEntityFramework` brings the one that refuses a context built without
  `UseDDDToolkit`, which would otherwise save without invariants, versions or events; row level security, the
  Supabase migrations, Tenancy and Membership on Postgres bring theirs. Write no start-up class of your own for
  them; turn one off with `SkipStartupCheck(name, reason: ...)`. See `startup-checks.md`.

## Delivering domain events

A save with pending domain events throws until a delivery mode is configured, rather than dropping
them. Full page: `https://dylansnel.github.io/DDDToolkit/docs/event-delivery.md`.

| | In-process | Outbox |
|---|---|---|
| Handlers run | inside `SaveChanges`, before the write, in its transaction | after the commit, from a table |
| Survives a crash | no | yes |
| A failing handler | aborts the save | is recorded on the row and retried |
| Handlers must be idempotent | no | yes, keyed on `EventId` |
| Use for | side effects in the same database | anything that leaves the module or the process |

In-process, through [Mediator](https://github.com/martinothamar/Mediator) with `DDDToolkit.Mediator`:

```csharp
using DDDToolkit.Mediator;

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);  // scoped: handlers use the DbContext
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

public interface IOrderingEvent : IDomainEvent, Mediator.INotification;   // every dispatched event must be an INotification
public sealed record OrderCancelled(OrderId OrderId, string Reason) : DomainEvent, IOrderingEvent;
```

- Handlers are Mediator `INotificationHandler<OrderCancelled>`. They must not call `SaveChanges`: the
  save is already running and picks up their changes.
- In-process handlers run before the save checks invariants, so they also run for a save that is then
  refused. Their database changes roll back with it; an email sent or an HTTP call made does not. Keep
  in-process handlers to changes in the same database, and send anything else through the outbox.
- `Mediator.SourceGenerator` is referenced by the composition root (the host) only.
- Without Mediator, `options.DispatchInProcess((services, events, cancellationToken) => ...)` takes a
  delegate returning a `Task`, handed the scope of the saving context and the events in the order they
  were raised. One delegate per process.

The outbox, with the table in the context:

```csharp
services.AddDDDToolkitEntityFramework(options => options.UseOutbox<OrderingContext>(outbox =>
{
    outbox.AddOrderingIntegrationEvents();   // generated: every domain event of the module and every outbound class
    outbox.SendToModules();                  // offer what it publishes to the modules in this process
    outbox.AlsoDispatchInProcess = true;     // optional: also run the in-process handlers at save
}));
services.AddOutboxBackgroundService<OrderingContext>(pollingInterval: TimeSpan.FromSeconds(1));

// OrderingContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
    => modelBuilder.AddDomainEventOutbox(Database, schema: Schema);   // the module's own schema when modules share a database
```

`Add{Module}IntegrationEvents()` is generated into the namespace `{AssemblyName}.IntegrationEvents`.
It also registers the domain events of the module's projects that do not reference Entity Framework, such
as the domain project, under the names they would have had; those events must be `public` (DDD00033).
Each row stores the event's name, which by convention is the module and the class name in kebab case.
Renaming a stored event's class changes that name, so pin the old one with `[DomainEventName(...)]`,
or the rows already written are orphaned.

An event that is also a record, of what happened and who did it, is kept in an event log:
`outbox.KeepEventLog(log => log.Keep<OrderPlaced>())` with `modelBuilder.AddEventLog(Database, keepFor: ...)`
in the context. The row is written with the outbox row in the same save and never updated; on Postgres the
policy scripts guard the table against updates and early deletes. An `IEventLogFields`, or an
`IActedByAccessor` of your own, is a singleton that reads ambient state when asked and takes no scoped
service. See `entity-framework.md`.

## Modules

A module is an assembly: `[assembly: Module("Ordering")]` in any file of the project. Two assemblies
with the same name, such as `Ordering` and `Ordering.Contracts`, are one module. Full pages:
`modules.md`, `module-contracts.md`.

- Everything a module declares is private to it, `public` or not, unless it is marked
  `[ModuleContract]` or is an `[IntegrationEvent]` record (types nested in those are published too).
- Publish identifiers, integration events, and where really needed a read model or an interface. Never
  publish entities; another module holding one is DDD00023 whether published or not.
- Keep published types free of unpublished ones: a published record of primitives and published ids.
- The rules report nothing until both sides are modules. Once a project's list is empty, hold it with
  `<WarningsAsErrors>$(WarningsAsErrors);DDD00022;DDD00023</WarningsAsErrors>`.
- A common layout is a `*.Contracts` project per module holding the published ids and integration
  events; other modules reference only that project.
- A module in layers is a project per layer, every one with the same `[assembly: Module]`: Contracts,
  Domain, Application, Infrastructure (the context, the migrations, the adapters, and the generated
  registrations) and Api (the routes and the module's entry, which the host references alone). Inside a
  project the thing comes first and the kind second, and a root holds only `Module.cs`, `GlobalUsings.cs`
  and the class that registers the project:

  ```
  Projects.Domain/
    Aggregates/Projects/            Project.cs, ProjectRefusals.cs, ProjectFailures.cs + .resx, .nl.resx (their texts)
      Entities/  Events/  Invariants/  ValueObjects/     a type per file
  Projects.Application/
    Access/                         the access check and what it asks
    Crew/
      Commands/AddCrewMember.cs     the request, its handler and what only it answers with: one file
      Queries/AllCrewMembers.cs
    StoredProjects/                 the ports several features share, in a folder named for what it holds:
                                    never a layer's name, and never Persistence, which is the infrastructure's
  Projects.Api/
    Crew/Rest/CrewEndpoints.cs      the same feature names as the application project
  Projects.Infrastructure/
    Persistence/                    by adapter: the context, Migrations/, the adapters of the ports
  ```

  - Domain: a folder per aggregate under `Aggregates`, named in the plural; an event and an invariant
    each in a file of its own, an invariant as a partial of the class it is nested in. What several
    aggregates share goes in `ValueObjects` and `Services` at the root.
  - Shared by several modules: a domain type two or more modules use and none owns goes in a project of
    its own, `Shared.Domain/ValueObjects`, with no `[assembly: Module]` and no reference to a module; a
    module's domain and contracts projects reference it. A type one module owns stays in its contracts.
  - Application: a folder per feature, named by a noun of the domain, with `Commands` and `Queries` in
    it. Never a folder named `Commands`, `Queries`, `Handlers`,
    `Validators`, `Ports` or `Dtos` at a project's root. A port one feature uses lives in that feature's
    folder.
  - Api: the application project's feature names, a feature's routes in `Rest` (its GraphQL types in
    `GraphQL`), composed by the entry.
  - Tests: a folder per module and feature, mirroring what they test.
  - Namespaces: the namespace of a type is its folder, in every project, the domain included
    (`Projects.Domain.Aggregates.Projects.Events`); `GlobalUsings.cs` carries what most files need. An
    invariant's file is the exception, since a partial has its class's namespace. After moving an entity
    class, replace its full name in the EF snapshot and the migrations' designer files by hand (no new
    migration: the tables did not change), and never derive a stored or sent name from a namespace.

  Full page: `modules.md`, "Folders inside the layers".

## Integration events

A domain event is the module's own and uses its types. An integration event is a published contract
other modules deploy against, written in primitives and published ids. Full page:
`https://dylansnel.github.io/DDDToolkit/docs/integration-events.md`.

The contract, in the publishing module's contracts project:

```csharp
[IntegrationEvent]                    // published as "ordering.order-confirmed", version 1 from the V1 suffix
public sealed record OrderConfirmedV1(OrderId OrderId, string City, string PostalCode);
```

How a domain event becomes it, next to the aggregate that raises the domain event:

```csharp
public sealed class PublishOrderConfirmed : IOutboundIntegrationEvent<OrderConfirmed, OrderConfirmedV1>
{
    public ValueTask<OrderConfirmedV1?> CreateAsync(OrderConfirmed confirmed, CancellationToken cancellationToken)
        => new(new OrderConfirmedV1(confirmed.OrderId, confirmed.ShipTo.City, confirmed.ShipTo.PostalCode));
}
```

Returning `null` skips that occurrence. The generated `Add{Module}IntegrationEvents()` on the outbox
registers the class; a domain event with no outbound class is published as it stands.

The consuming module handles the contract, never the other module's domain event:

```csharp
[IntegrationEventConsumer("shipping.booker")]           // the inbox keys on this name: keep it stable
public sealed class BookShipment(ShippingContext context) : IIntegrationEventHandler<OrderConfirmedV1>
{
    public Task HandleAsync(OrderConfirmedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        context.Shipments.Add(new Shipment(ShipmentId.CreateSequential(), contract.OrderId, message.OccurredAt));
        return Task.CompletedTask;                       // no SaveChanges: the inbox saves it with its own row
    }
}
```

```csharp
using Shipping.IntegrationEvents;                        // generated

services.AddDDDToolkitEntityFramework(options =>
    options.MapIntegrationEvents(contracts => contracts.AddShippingIntegrationEvents()));
services.AddModuleIntegrationEvents<ShippingContext>(module => module.AddShippingIntegrationEvents());

// ShippingContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
    => modelBuilder.AddDomainEventInbox(Database, schema: Schema);
```

- Handler constructors take services from the container. The generated registration builds them with
  `new`; one it cannot build is DDD00033.
- Delivery is at least once; the inbox makes a handler run once per message and consumer.
- Never change a published contract. Breaking the payload means a new record whose suffix is the new
  version (`OrderConfirmedV2`, same name, version 2), an upcaster from the old one, and keeping the old
  record.
- Out of the process (pgmq, Wolverine, MassTransit, a custom sink): `transports.md`. To GraphQL
  subscribers: `graphql.md`.
