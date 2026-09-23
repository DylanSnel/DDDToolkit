# Examples

## The one to read: a shop in five modules

A modular monolith is the architecture this toolkit is aimed at. The example is a small shop, split the
way its business splits:

| Module | Owns | Publishes | Listens to |
|---|---|---|---|
| **Catalog** | products and their prices | `ProductListedV1`, `ProductPriceChangedV1` | nothing |
| **Ordering** | orders, and its own copy of the prices | `OrderPlacedV1`, `OrderConfirmedV1`, `OrderCancelledV1` | Catalog, Inventory, Payments |
| **Inventory** | stock per SKU, reservations per order | `StockReservedV1`, `StockReservationFailedV1` | `OrderPlacedV1`, `OrderCancelledV1` |
| **Payments** | a payment per order, a payment provider behind a port | `PaymentSucceededV1`, `PaymentFailedV1` | `OrderPlacedV1`, `StockReservedV1`, `OrderCancelledV1` |
| **Shipping** | shipments | nothing | `OrderConfirmedV1` |

The modules live in `Modules/`, apart from any host, because they are the domain and a host is only one
way to run it. Each sample is a different way, over the very same modules:

| Sample | Topology | Database | Messages travel by |
|---|---|---|---|
| `ModularMonolith.Supabase/` | one process | SQLite, or Postgres/Supabase | the module sink, in process |
| `ModularMonolith.SqlServer/` | one process | SQL Server | the module sink, in process |
| `Microservices.Pgmq/` | three services and a gateway | one Postgres, a schema per module | pgmq: a queue per service, in the same database |
| `Microservices.Wolverine/` | three services and a gateway | a database per service: SQL Server and Postgres | RabbitMQ, through Wolverine |

```
Modules/
  SharedKernel/        Money: the one type every module means the same thing by. Not a module.
  Catalog/
    DDDToolkit.Examples.Catalog.Contracts    what Catalog publishes
    DDDToolkit.Examples.Catalog              the module
  Ordering/ Inventory/ Payments/ Shipping/   the same shape
  Migrations.SqlServer/                      every module's SQL Server migrations, in one assembly
Shared/
  DDDToolkit.Examples.GraphQL                the shop's GraphQL schema over every module, and the joins between them
  DDDToolkit.Examples.Hosting                ModuleDatabase and ModuleHost: the host's two decisions
  DDDToolkit.Examples.ServiceDefaults        Aspire's service defaults: telemetry, health, discovery
ModularMonolith.Supabase/
  DDDToolkit.Examples.Host                   all five modules in one process; its build exports
  DDDToolkit.Examples.Supabase.AppHost       Aspire: Postgres seeded from supabase/migrations, or a live project
  supabase/
    config.toml                              a local Supabase project, from supabase init
    migrations/                              every module's migrations, written by the host's build
ModularMonolith.SqlServer/
  DDDToolkit.Examples.SqlServer.Host         the same five modules on SQL Server, migrating on start-up
  DDDToolkit.Examples.SqlServer.AppHost      Aspire: SQL Server in Docker
```

A host makes exactly two decisions for a module, and hands them over as a `ModuleHost`: where its
tables live (`ModuleDatabase.Sqlite`, `.Supabase`, `.Postgres`, `.SqlServer`), and where what it
publishes goes (`ModuleHost.InProcess` sends it to the other modules in the process). Everything else,
its context, its outbox, what it publishes as what, the policies it follows, the module registers
itself in its `Add{Module}Module`.

Inside a module the folders say what kind of thing a file is:

```
DDDToolkit.Examples.Ordering/
  Domain/
    Aggregates/Orders/       Order.cs and everything that belongs to it:
      Entities/                child entities (OrderLine)
      Events/                  domain events it raises
      Invariants/              one file per named rule, each another part of the entity
    ValueObjects/            Address
    Services/                domain services (OrderPricer)
  Application/
    IntegrationEvents/       policies: what this module does when another one says something
    DomainEvents/            in-process handlers of this module's own events
    ReadModels/              copies of other modules' data, kept current by the policies
  Infrastructure/
    Persistence/             the DbContext, the design-time factory, Migrations/
  Api/                       the module's HTTP endpoints
    GraphQL/                 its GraphQL types, queries, mutations and lookups
  Module.cs                  [assembly: Module("Ordering")]
  OrderingModule.cs          AddOrderingModule: everything the host calls
```

Namespaces follow the folders and stop at the aggregate: everything under `Aggregates/Orders/` is
`DDDToolkit.Examples.Ordering.Domain.Orders`. A named invariant is a nested part of its entity, so it has
to share the entity's namespace wherever its file lives. Each module's `GlobalUsings.cs` imports its
own namespaces, so the `using` lines above the code name what comes from outside the module.

### What happens when you place an order

1. The endpoint prices the lines with `OrderPricer`, from Ordering's copy of the catalog, and constructs
   the `Order`. It raises `OrderPlaced`. `SaveChanges` writes the order and one outbox row in one
   transaction.
2. A second later Ordering's outbox poller publishes `OrderPlacedV1`, and the module sink offers it to
   every module. Inventory's `ReserveStock` asks `StockAllocator` to set every line aside, all or
   nothing. Payments' `OpenPayment` opens a pending payment for the total.
3. Inventory publishes `StockReservedV1`. Payments' `TakePayment` charges through `IPaymentProvider`
   and publishes `PaymentSucceededV1`. Ordering hears both and the order confirms itself, whichever
   answer came first. Shipping books a van on `OrderConfirmedV1`.
4. When Payments is refused, Ordering cancels, Inventory hears `OrderCancelledV1` and puts the stock
   back. When Inventory is short, Ordering cancels and Payments voids the pending payment.
5. Deliver any message twice and its handler runs once: every handler runs inside its module's inbox.

No module names another anywhere. Each says what it publishes and what it listens to, in its own
`*Module.cs`, and reads the others' contracts projects and never their domains. Every module's project
file turns [DDD00022 and DDD00023](../docs/modules.md) into build errors, so that stays true.

### One GraphQL schema over five modules

Next to the REST endpoints, both monoliths serve GraphQL at `/graphql`, and it reads as one shop:

```graphql
{
  order(id: "T3JkZXI6…") {
    status
    lines { quantity product { name price { amount currency } } }   # Catalog
    payment { status }                                                # Payments
    shipment { destination }                                          # Shipping
  }
}
```

Each module publishes its own part in `Api/GraphQL`: its types as code-first descriptors (the domain
classes carry no GraphQL attribute), its queries and mutations, and a **lookup** for what others may
want from it: `ProductBySkuDataLoader`, `PaymentByOrderDataLoader`, `ShipmentByOrderDataLoader`. The
fields that cross a boundary, `OrderLine.product`, `Order.payment` and `Order.shipment`, are not in any
module. `Shared/DDDToolkit.Examples.GraphQL` adds them, on those lookups, because it composes the schema
and is allowed to see every module the way a host is. Ordering still knows a line's SKU and nothing more.
When the modules run as services, a Fusion gateway makes the same joins over the same lookups, and the
query above does not change.

Every entity a client can refetch is a Relay node: `node(id:)` finds orders, products, payments,
shipments and stock items, and the ids are the toolkit's identifiers written into HotChocolate's own node
id format (see [Relay node ids](../docs/graphql.md#relay-node-ids)). A module that points at an order it
does not own publishes the reference as an `Order` node id, `payment { order }`, without knowing the
`Order` type.

A broken rule is a GraphQL error whose `extensions.code` is the rule's code, `ORDER_ALREADY_CONFIRMED`,
exactly what the REST endpoint's 422 carries; an invalid address is one error per field. And
`orderConfirmed(id:)` and `orderCancelled(id:)` push an order to a client the moment it settles: the
outbox sends Ordering's contracts to `GraphQlSubscriptionSink` as well as to the other modules.

### Running it

```bash
dotnet run --project Examples/ModularMonolith.Supabase/DDDToolkit.Examples.Host
```

Then work through `DDDToolkit.Examples.Host.http` from the top. It lists the products and the stock,
refuses a bad address, an unknown SKU, a blank SKU and a duplicate SKU, places an order that is
confirmed and shipped, refuses to cancel it, and then places one the payment provider declines and
one there are not enough mugs for, and shows how each module undid its part. Every refusal by a rule
of the aggregate is a 422 carrying the code of the rule that broke.

This runs on SQLite, one file per module next to the built binary. Each module creates its own file from
its model on start-up, before its outbox poller starts, and Catalog and Inventory put the starting range
on the shelves. SQLite has no schemas, so the modules' schemas are dropped there, and the modules turn
off the warning that would say so on every start.

### On Supabase

The same host runs on Postgres when it is given `ConnectionStrings:Supabase`. The modules then share
one database, as they would on one Supabase project, each in its own schema with its own migration
history, outbox and inbox. The migrations are Supabase's to apply: each module registers its
migrations, and the host checks over all of them that none is pending and refuses to start otherwise.

Three ways to give it one:

```bash
# Aspire: a Postgres container seeded from supabase/migrations, the host, and the dashboard.
dotnet run --project Examples/ModularMonolith.Supabase/DDDToolkit.Examples.Supabase.AppHost

# The same against a real Supabase project or one of its branches: set the connection string on the
# AppHost once, and it starts no container.
dotnet user-secrets set ConnectionStrings:Supabase "Host=...;Database=postgres;..." --project Examples/ModularMonolith.Supabase/DDDToolkit.Examples.Supabase.AppHost

# The Supabase CLI's local stack, without Aspire.
cd Examples/ModularMonolith.Supabase
supabase start          # a local Supabase in Docker; applies supabase/migrations
dotnet run --project DDDToolkit.Examples.Host --launch-profile supabase
```

The AppHost's container is plain Postgres: it runs every file in `supabase/migrations` on its first
start, in name order, which is what Supabase does with them, so the host's start-up check finds every
migration applied. The `supabase` launch profile points at the CLI's local database on port 54322.

After changing a model,
scaffold the migration in the module that owns it, and build:

```bash
dotnet ef migrations add AddGiftWrap --project ../Modules/Ordering/DDDToolkit.Examples.Ordering --startup-project DDDToolkit.Examples.Host --output-dir Infrastructure/Persistence/Migrations
dotnet build DDDToolkit.Examples.Host   # writes 2026…_AddGiftWrap.ordering.ddd.sql
supabase migration up                   # or: supabase db reset, to start over
```

There is no export step to remember. Each module's design-time factory is marked `[SupabaseMigrations]`,
and the host's project file sets `SupabaseMigrationsExport`: `Write` locally, so every build writes the
files a new migration needs, and `Check` in CI, so a pull request that adds a migration without its file
fails. The files are committed, because Supabase branching reads them from the repository. See
[Entity Framework → Supabase](../docs/entity-framework.md#supabase) for what the build writes and how.

### On SQL Server

```bash
dotnet run --project Examples/ModularMonolith.SqlServer/DDDToolkit.Examples.SqlServer.AppHost
```

The same five modules, the same endpoints and the same `.http` walk-through (on port 5090 when the
host runs outside Aspire), on SQL Server in Docker. Compare the two hosts' `Program.cs`: the one line
that differs in substance is `ModuleDatabase.SqlServer(...)` for `ModuleDatabase.Supabase(...)`.

One thing follows from that line. On Supabase somebody else applies the migrations and the application
only checks; on SQL Server nobody else will, so each module migrates its own schema on start-up, before
its outbox poller starts. The SQL Server migrations live in `Modules/Migrations.SqlServer`, apart from
the Postgres ones in each module: Entity Framework keeps one model snapshot per context per assembly,
and the two providers disagree on every column type. Scaffold one with:

```bash
dotnet ef migrations add AddGiftWrap --project Examples/Modules/Migrations.SqlServer/DDDToolkit.Examples.Migrations.SqlServer --context OrderingContext --output-dir Ordering
```

### As services

The same five modules, cut into three deployables, with a gateway in front so a client still sees one
shop at one address:

| Service | Runs | Reads a queue of messages for |
|---|---|---|
| `storefront` | Catalog, Ordering | stock reserved or refused, payments taken or refused |
| `payments` | Payments | orders placed or cancelled, stock reserved |
| `fulfilment` | Inventory, Shipping | orders placed, cancelled or confirmed |

`Shared/DDDToolkit.Examples.Microservices` decides that once for every microservices sample: which
modules a service runs (`AddShopService`), which paths it answers (`ShopServices.Routes`, what the
gateway routes on), and which services each published contract goes to (`ShopServices.ConsumersOf`). A
service is not a module: Storefront runs Catalog and Ordering in one process, so a price Catalog publishes
still reaches Ordering through the module sink, next door, and only a message another service consumes
leaves the process.

What each sample adds is the transport, in one service project that its AppHost starts three times.

**`Microservices.Pgmq/`** keeps the one database the monolith on Supabase has, and puts the queues in it:

```csharp
// sending: to the modules next door, and onto the queue of every other service that consumes it
builder.Services.AddPgmqSink(queues, pgmq => pgmq.UseQueues(message =>
    ShopServices.RecipientsOf(message.Name, service).Select(ShopServices.NameOf)));
var host = new ModuleHost(ModuleDatabase.Postgres(connectionString), outbox =>
{
    outbox.SendToModules();
    outbox.SendToPgmq();
});

// receiving: this service's own queue, into the same modules
builder.Services.AddPgmqConsumer(queues, ShopServices.NameOf(service));
```

No broker to run: a queue is a table, and on Supabase it is a Queue you can watch in the dashboard.
`BookShipment` in Shipping is the same class it is in the monolith; it cannot tell that
`OrderConfirmedV1` came through `pgmq.q_fulfilment` rather than from the module next door.

```bash
dotnet run --project Examples/Microservices.Pgmq/DDDToolkit.Examples.Pgmq.AppHost
```

**`Microservices.Wolverine/`** gives every service a database of its own, and not of one kind: Storefront
on SQL Server, Payments and Fulfilment on Postgres. What they share is RabbitMQ, and Wolverine carries the
envelopes: a topic exchange keyed on the contract's name, and a queue per service bound to the contracts
`ShopServices.ContractsFor` names.

```csharp
wolverine.PublishMessagesToRabbitMqExchange<IntegrationEventEnvelope>("integration-events", envelope => envelope.Name)
    .ExchangeType(ExchangeType.Topic).SendInline();
wolverine.ListenToRabbitQueue(name, queue =>
    {
        foreach (var contract in ShopServices.ContractsFor(service)) queue.BindExchange("integration-events", contract);
    })
    .ProcessInline();
wolverine.ReceiveIntegrationEvents();
```

```bash
dotnet run --project Examples/Microservices.Wolverine/DDDToolkit.Examples.Wolverine.AppHost
```

### Testing the samples end to end

`Tests/DDDToolkit.Examples.AppHost.Tests` starts each sample's AppHost, containers and all, and plays
the same scenarios against every one of them over HTTP: an order confirmed and shipped, a payment
refused and the stock put back, an order there is no stock for and its payment voided, a confirmed
order that cannot be cancelled. The answers must not depend on how the shop is hosted, and these tests
are what says so. They need Docker, skip themselves without it, and run in CI in the Sample Tests
workflow, one job per sample:

```bash
dotnet test Tests/DDDToolkit.Examples.AppHost.Tests --filter "Sample=ModularMonolith.SqlServer"
```

### Which building block is where

Paths are under `Modules/`.

| Building block | Where |
|---|---|
| Aggregate root, child entity, generated read-only collection | `Ordering/.../Domain/Aggregates/Orders/Order.cs`, `Entities/OrderLine.cs` |
| An explicitly declared identifier, and generated ones | `Ordering/...Contracts/OrderingContracts.cs`; every other `[AggregateRoot<Guid>("…")]` |
| Value object, always-valid twin, `TryToValid` at a boundary | `Ordering/.../Domain/ValueObjects/Address.cs`, `Api/OrderingEndpoints.cs` |
| A positional value object in a shared kernel | `SharedKernel/.../Money.cs` |
| A named invariant, a seam rule, a child's own rule | `Ordering/.../Domain/Aggregates/Orders/Invariants/`, the bottom of `Order.cs` |
| A rule about state rather than about a call | `Invariants/MustNotCancelAConfirmedOrder.cs` |
| A rule several aggregates in one save are held to | `Inventory/.../Domain/Aggregates/StockItems/Invariants/MustNotOverReserve.cs` |
| Asking an aggregate what is broken, before any save | `Ordering/.../Api/OrderingEndpoints.cs`, the `Broken` helper |
| Domain services, pure and without I/O | `Ordering/.../Domain/Services/OrderPricer.cs`, `Inventory/.../Domain/Services/StockAllocator.cs` |
| A factory that decides the state it creates | `StockReservation.Reserved` / `.Refused`, called by `StockAllocator` |
| An anti-corruption layer | `Payments/.../Domain/Services/IPaymentProvider.cs`, `Infrastructure/PaymentProvider/FakePaymentProvider.cs` |
| Domain events, stable names, Mediator dispatch | `Ordering/.../Domain/Aggregates/Orders/Events/`, `Application/DomainEvents/OrderLog.cs` |
| Published contracts, one per module | each `*.Contracts` project |
| The outbox per module, `PublishAs`, the module sink | each `*Module.cs` |
| Policies, the inbox, idempotent consumers | each `Application/IntegrationEvents/` |
| A process across modules, with compensation, and no saga class | `Order.RecordStockReserved`, `RecordPayment`, `Cancel`, and the policies around them |
| Messages that arrive out of order | `Payments/.../PaymentPolicies.cs` (throw and retry), `Ordering/.../ReadModels/CatalogPrice.cs` (newest wins) |
| A read model of another module's data | `Ordering/.../Application/ReadModels/CatalogPrice.cs` |
| Optimistic concurrency as a 409 | `Api/OrderingEndpoints.cs` (cancel), `Catalog/.../Api/CatalogEndpoints.cs` (reprice) |
| A module registering itself, a host that only switches modules on | each `*Module.cs`, each sample's `Program.cs` |
| The same modules on another database, and who applies the migrations | `Shared/DDDToolkit.Examples.Hosting/ModuleDatabase.cs`, the two monoliths' `Program.cs` |
| Migrations per provider in separate assemblies | `Modules/Migrations.SqlServer`, each module's `Infrastructure/Persistence/Migrations` |
| The whole system under test, containers included | `Tests/DDDToolkit.Examples.AppHost.Tests` |
| One GraphQL schema over modules that do not know each other | `Shared/DDDToolkit.Examples.GraphQL/ShopSchema.cs`, each module's `Api/GraphQL` |
| Relay node ids from the toolkit's identifiers, and references to another module's node | each `Api/GraphQL/*Type.cs`, `.ID("Order")` in Payments, Inventory and Shipping |
| Rules and invalid values as GraphQL errors with their codes | `AddDDDToolkitErrors()` in `ShopSchema.cs`, `Ordering/.../Api/GraphQL/OrderingOperations.cs` |
| Live updates from the outbox | `OrderingSubscriptions`, `GraphQlSubscriptionSink` in each monolith's `Program.cs` |
| A schema, a migration history, an outbox and an inbox per module in one database | each `Infrastructure/Persistence/*Context.cs` |
| Entity Framework migrations applied by Supabase | `supabase/migrations`, `[SupabaseMigrations]` on each factory, the host's `.csproj` |
| The testing kit and `DomainEventClock` | `Tests/DDDToolkit.Examples.Tests` |

There are no repositories. A module's `DbContext` is its repository and unit of work, used directly by
the endpoints and the policies. The toolkit has no repository abstraction to show, and a wrapper
around a `DbContext` in a sample would only hide what the toolkit does to it.

What it does not show: Newtonsoft, FluentValidation validators, pgmq, and upcasting an older
payload. Those have runnable coverage in `Tests/` and a page each in [docs](../docs).

## `DDDToolkit.ExampleApi` and `DDDToolkit.ExampleLibrary`

The example that predates the one above. They survive because four test projects use their types as
fixtures: `DDDToolkit.Tests`, `DDDToolkit.EntityFramework.Tests`,
`DDDToolkit.EntityFramework.Providers.Tests` and `DDDToolkit.HotChocolate.Tests` all reference
`EmailAddress`, `PersonName`, `UserId` and `ExampleContext`. Deleting them is therefore a change to the
test suite, not to this folder.

They still work, and `ExampleLibrary` is the only place FluentValidation validators and the
HotChocolate attributes are demonstrated. Read `ModularMonolith.Supabase/` first.

## `DDDToolkit.NugetApi`

Deliberately outside the solution, and the only project here that consumes the toolkit as packages
rather than as project references. It names every generated member it expects, including all four
members of the invariant check, a nested rule on the root and one on a child, so a generator that did
not arrive fails the build. See the
[README](DDDToolkit.NugetApi/README.md) in that folder.
