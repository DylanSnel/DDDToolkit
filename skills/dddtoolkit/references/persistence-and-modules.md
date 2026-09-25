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
        configurationBuilder.AddOrderingConverters();    // one per assembly that declares ids or single value objects
    }
}
```

- `Add{Module}Converters` takes its name from `<DDD_Module>Ordering</DDD_Module>` in the project file,
  or else the assembly name with the dots removed. A contracts project or shared kernel that declares
  identifiers has a method of its own, and the context calls each of them.
- `DDD_Module` only names generated methods. What makes an assembly a module is
  `[assembly: Module("Ordering")]`, below.
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
- Prefer `SaveChangesAsync`. Migrations are ordinary `dotnet ef migrations add`.
- A primary key made of more than the id: `[KeyPart]`, see `composite-keys.md`. Migrations as Supabase
  SQL files: `DDDToolkit.EntityFramework.Supabase`, see `supabase.md`.

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
Give every event that is stored a `[DomainEventName("module.something-happened")]`, so renaming the
class does not orphan stored rows.

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

## Integration events

A domain event is the module's own and uses its types. An integration event is a published contract
other modules deploy against, written in primitives and published ids. Full page:
`https://dylansnel.github.io/DDDToolkit/docs/integration-events.md`.

The contract, in the publishing module's contracts project:

```csharp
[IntegrationEvent("ordering.order-confirmed", Version = 1)]
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
- Never change a published contract. Breaking the payload means a new record and a new version
  (`OrderConfirmedV2`, `Version = 2`), an upcaster from the old one, and keeping the old record.
- Out of the process (pgmq, Wolverine, MassTransit, a custom sink): `transports.md`. To GraphQL
  subscribers: `graphql.md`.
