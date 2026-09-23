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
| `Microservices.MassTransit/` | three services and a gateway | a SQL Server database per service | RabbitMQ, through MassTransit 8 |

```
Modules/
  SharedKernel/        Money: the one type every module means the same thing by. Not a module.
  Catalog/
    DDDToolkit.Examples.Catalog.Contracts    what Catalog publishes
    DDDToolkit.Examples.Catalog              the module, with its Postgres migrations
    DDDToolkit.Examples.Catalog.Migrations.SqlServer   its SQL Server migrations, for hosts on SQL Server
  Ordering/ Inventory/ Payments/ Shipping/   the same shape
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
    Orders/                  one folder per aggregate the application layer works on
      DomainEvents/            in-process handlers of this module's own events (OrderLog)
      IntegrationEvents/
        Inbound/               policies: what the order does when another module says something
        Outbound/              what each of the order's events becomes for the others (PublishOrderPlaced)
    ReadModels/
      CatalogPrices/         a copy of another module's data, and the inbound policies that keep it current
  Infrastructure/
    Persistence/             the DbContext, the design-time factory, Migrations/
  Api/                       the module's HTTP endpoints
    GraphQL/                 its GraphQL types, queries, mutations and lookups
  Module.cs                  [assembly: Module("Ordering")]
  OrderingModule.cs          AddOrderingModule: everything the host calls
```

Namespaces follow the folders and stop at the aggregate: everything under `Aggregates/Orders/` is
`DDDToolkit.Examples.Ordering.Domain.Orders`, and everything under `Application/Orders/` is
`DDDToolkit.Examples.Ordering.Application.Orders`. The building-block folders (`Aggregates/`,
`ReadModels/`) and the kind-and-direction folders below a slice are for the reader, not the namespace. A named invariant is a nested part of its entity, so it has
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

Each module serves a GraphQL **source schema** of its own, from its `Api/GraphQL`: its types as
code-first descriptors (the domain classes carry no GraphQL attribute), its queries and mutations, and
its own part of the types other modules own. Ordering says a line's product is the `Product` with that
SKU (`ProductStub.cs`), Payments and Shipping say what they add to the `Order` with that id
(`OrderStub.cs`), and Catalog marks `productBySku` as the lookup a `Product` is fetched by. No module
knows another's classes; they agree on a type's name and its key.

A Fusion gateway inside the monolith composes the five into the one schema at start-up and answers each
query by calling the modules' schemas directly, in the process, with no HTTP between them. It is the same
Fusion the microservices samples run across processes, and the modules' GraphQL is the same code in
both; see [GraphQL across services](#graphql-across-services). `Shared/DDDToolkit.Examples.GraphQL`
only wires it: the modules' source schemas in the application's container, and the gateway in a
container of its own inside the same application, because HotChocolate and Fusion each claim the one
executor provider of a container. `Tests/Spikes/DDDToolkit.Spikes.FusionInProcess` shows why, apart from
the shop.

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
its outbox poller starts. Each module's SQL Server migrations live in a project next to it,
`DDDToolkit.Examples.{Module}.Migrations.SqlServer`, apart from its Postgres ones: Entity Framework keeps
one model snapshot per context per assembly, and the two providers disagree on every column type. A host
on SQL Server references the migrations of the modules it runs, and `ModuleDatabase.SqlServer` finds them
by name. Scaffold one with:

```bash
dotnet ef migrations add AddGiftWrap --project Examples/Modules/Ordering/DDDToolkit.Examples.Ordering.Migrations.SqlServer --output-dir Migrations
```

### As services

The same five modules, cut into three deployables, with a gateway in front so a client still sees one
shop at one address:

| Service | Runs | Consumes from the others |
|---|---|---|
| `storefront` | Catalog, Ordering | stock reserved or refused, payments taken or refused |
| `payments` | Payments | orders placed or cancelled, stock reserved |
| `fulfilment` | Inventory, Shipping | orders placed, cancelled or confirmed |

Every sample has a project per service, `DDDToolkit.Examples.{Sample}.Storefront`, `.Payments` and
`.Fulfilment`, each with its own `Program.cs`, and each referencing only the modules it runs. Payments
cannot call into Ordering: it does not reference it. What it knows of Ordering is `OrderPlacedV1`, from
Ordering's contracts, the way a service in another repository would. Nothing shared knows the whole
shop, apart from the gateway's route table in its `appsettings.json`.

A service is not a module, though: Storefront runs Catalog and Ordering in one process, so a price Catalog
publishes still reaches Ordering through the module sink, next door, and only a message another service
consumes leaves the process.

Each sample has a gateway of its own, `DDDToolkit.Examples.{Sample}.Gateway`, and a client talks to
nothing else: REST through YARP, with the route table in the gateway's `appsettings.json`, and GraphQL
through Fusion.

#### GraphQL across services

Every service serves the GraphQL of the modules it runs, as a source schema, and the gateway composes the
three into the schema the monoliths serve. A client cannot tell the difference: the scenario tests send
the monoliths' queries to the gateway.

```graphql
{ order(id: "…") { status lines { product { name } } payment { status } shipment { destination } } }
```

Storefront answers `status` and `lines { product }`: Catalog and Ordering run there, so the join from a
line's SKU to the product is made in-process, as in the monolith. Payments and Fulfilment each declare
an `Order` of their own that holds nothing but the order's id, and add the one field they know about,
`payment` or `shipment`. The gateway merges the three `Order` types on the id, asks Storefront for the
order, then asks Payments and Fulfilment for their fields with the id it got back. Those stubs are in
the modules, as `OrderStub.cs` in Payments' and Shipping's `Api/GraphQL`: they are the module's part of
the order's API, and a module that runs in a monolith leaves them out, because there Ordering's `Order`
is in the same schema.

Two things make that work that are not obvious:

- The stub is a Relay node. The gateway hands Payments the order's node id, and Payments can only read an
  `OrderId` back out of it when `Order` is a node there too. The toolkit's serializer for `OrderId` writes
  the same node id in every service.
- `Money` is in Storefront's schema and in Payments', and composition refuses a type two services both
  answer unless it is `@shareable`. A value object has no owner, so the toolkit marks every value object
  `@shareable` in a source schema; see
  [Value objects in a Fusion source schema](../docs/graphql.md#value-objects-in-a-fusion-source-schema).

The AppHost composes: `AddNitroComposition()`, `WithGraphQLHttpEndpoint()` on each service and
`WithNitroComposition(...)` on the gateway. Before the gateway starts, it reads each service's schema from
the running service, composes them and writes `gateway.far` next to the gateway, which serves it; it
composes again when a service restarts. Each service project has a `schema-settings.json` naming its
source schema. No Nitro account is involved.

**`Microservices.Pgmq/`** keeps the one database the monolith on Supabase has, and puts the queues in it.
It routes with pgmq's own topics (pgmq 1.11 and later): the sender sends under the contract's published
name, and every service binds its own queue to the contracts it has to be sent. From Storefront's
`Program.cs`:

```csharp
// sending: by topic, to every queue bound to the contract's name
builder.Services.AddPgmqSink(queues, pgmq => pgmq.UseTopics());

// receiving: this service's own queue, bound at start-up to what its modules handle, into the same modules
builder.Services.AddPgmqConsumer(queues, "storefront", consumer => consumer.BindTopics = true);
```

Nothing names another service or a contract. What Storefront is bound to follows from the handlers of
Catalog and Ordering, less what Storefront publishes itself, which the module sink already delivers.

No broker to run: a queue is a table, and on Supabase it is a Queue you can watch in the dashboard.
`BookShipment` in Shipping is the same class it is in the monolith; it cannot tell that
`OrderConfirmedV1` came through `pgmq.q_fulfilment` rather than from the module next door.

```bash
dotnet run --project Examples/Microservices.Pgmq/DDDToolkit.Examples.Pgmq.AppHost
```

**`Microservices.Wolverine/`** gives every service a database of its own, and not of one kind: Storefront
on SQL Server, Payments and Fulfilment on Postgres. What they share is RabbitMQ, used the way Wolverine
uses it: conventional routing, a fanout exchange per contract type and, per service, a queue for every
contract it handles. The modules are registered first, so Wolverine knows what the service handles; from
Payments':

```csharp
builder.Services.AddPaymentsModule(host);

builder.UseWolverine(wolverine =>
{
    wolverine.UseRabbitMq(rabbitUri)
        .AutoProvision()
        .UseConventionalRouting(conventions => conventions
            .QueueNameForListener(type => $"payments.{type.Name}")
            .ConfigureListeners((listener, _) => listener.ProcessInline())
            .ConfigureSending((sender, _) => sender.SendInline()));

    wolverine.ReceiveIntegrationEvents(builder.Services.IntegrationEventSubscriptions());
    wolverine.Policies.DisableConventionalLocalRouting();
});
```

```bash
dotnet run --project Examples/Microservices.Wolverine/DDDToolkit.Examples.Wolverine.AppHost
```

**`Microservices.MassTransit/`** is the same topology with MassTransit, every service on a SQL Server
database of its own, and RabbitMQ used the way MassTransit uses it: every contract a message type with an
exchange of its own, one receive endpoint per service, bound by MassTransit to the exchanges of the
contracts its consumers take. In each service's `RabbitMq.cs`, called after the modules:

```csharp
bus.AddIntegrationEventConsumers(services.IntegrationEventSubscriptions());
bus.UsingRabbitMq((context, rabbit) =>
{
    rabbit.Host(new Uri(connectionString));
    rabbit.ReceiveEndpoint("payments", endpoint =>
    {
        endpoint.UseMessageRetry(retry => retry.Intervals(250, 1000, 5000));
        endpoint.ConfigureConsumers(context);
    });
});
```

MassTransit 8 is the last version under the Apache 2.0 licence; the package's README says more.

```bash
dotnet run --project Examples/Microservices.MassTransit/DDDToolkit.Examples.MassTransit.AppHost
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

The Supabase monolith also runs against a real Supabase project, in the Supabase Live workflow. It puts
the exported `supabase/migrations` on with `supabase db push`, as a deploy would, and plays the same
scenarios against the project, where the monolith checks on start-up that every migration was applied.
The project exists for these tests alone: each run first drops the module schemas and forgets their
migrations. With the repository variable `SUPABASE_BRANCHING` set to `true`, each run gets a preview
branch of its own instead and deletes it afterwards; branching needs a Supabase Pro organisation. The
workflow connects through Supabase's pooler, because the database's own host has no IPv4 address and
GitHub's runners have no IPv6.

Locally, point the AppHost at a project the same way the workflow does:

```bash
dotnet user-secrets set "ConnectionStrings:Supabase" "Host=<pooler host>;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<password>;SSL Mode=Require" --project Examples/ModularMonolith.Supabase/DDDToolkit.Examples.Supabase.AppHost
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
| Domain events, stable names, Mediator dispatch | `Ordering/.../Domain/Aggregates/Orders/Events/`, `Application/Orders/DomainEvents/OrderLog.cs` |
| Published contracts, one per module | each `*.Contracts` project |
| The outbox per module, the module sink | each `*Module.cs` |
| What a domain event becomes outside the module | each `Application/<slice>/IntegrationEvents/Outbound/` |
| Policies, the inbox, idempotent consumers | each `Application/<slice>/IntegrationEvents/Inbound/` |
| A process across modules, with compensation, and no saga class | `Order.RecordStockReserved`, `RecordPayment`, `Cancel`, and the policies around them |
| Messages that arrive out of order | `Payments/.../PaymentPolicies.cs` (throw and retry), `Ordering/.../ReadModels/CatalogPrices/CatalogPrice.cs` (newest wins) |
| A read model of another module's data | `Ordering/.../Application/ReadModels/CatalogPrices/` |
| Optimistic concurrency as a 409 | `Api/OrderingEndpoints.cs` (cancel), `Catalog/.../Api/CatalogEndpoints.cs` (reprice) |
| A module registering itself, a host that only switches modules on | each `*Module.cs`, each sample's `Program.cs` |
| The same modules on another database, and who applies the migrations | `Shared/DDDToolkit.Examples.Hosting/ModuleDatabase.cs`, the two monoliths' `Program.cs` |
| Migrations per provider in separate assemblies | each module's `Infrastructure/Persistence/Migrations` and its `*.Migrations.SqlServer` project |
| The whole system under test, containers included | `Tests/DDDToolkit.Examples.AppHost.Tests` |
| One GraphQL schema over modules that do not know each other: Fusion in the monolith | `Shared/DDDToolkit.Examples.GraphQL/ShopSchema.cs`, each module's `Api/GraphQL`, `ProductStub.cs`, `OrderStub.cs` |
| The same schema composed across services by a Fusion gateway | each `Microservices.*` AppHost and gateway, `OrderStub.cs` in Payments and Shipping |
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
