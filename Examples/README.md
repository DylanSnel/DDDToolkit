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
| `ModularMonolith.Supabase/` | one process | SQLite, or Postgres/Supabase | the module sink, in process; or Supabase Queues, with `Messaging=pgmq` |
| `ModularMonolith.SqlServer/` | one process | SQL Server | the module sink, in process |
| `Microservices.Pgmq/` | three services and a gateway | one Postgres, a schema per module | pgmq: a queue per service, in the same database |
| `Microservices.Wolverine/` | three services and a gateway | a database per service: SQL Server and Postgres | RabbitMQ, through Wolverine |
| `Microservices.MassTransit/` | three services and a gateway | a SQL Server database per service | RabbitMQ, through MassTransit 8 |

`Tenancy/` is not one of them: it is a second, smaller application with modules of its own, on the Tenancy
supporting domain. See [The Tenancy sample](#the-tenancy-sample).

```
Modules/
  SharedKernel/        Money: the one type every module means the same thing by. Not a module.
  Catalog/
    Examples.Webshop.Catalog.Contracts    what Catalog publishes
    Examples.Webshop.Catalog              the module, with its Postgres migrations
    Examples.Webshop.Catalog.Migrations.SqlServer   its SQL Server migrations, for hosts on SQL Server
  Ordering/ Inventory/ Payments/ Shipping/   the same shape
Shared/
  Examples.Hosting                ModuleDatabase and ModuleHost: the host's two decisions
  Examples.ServiceDefaults        Aspire's service defaults: telemetry, health, discovery
ModularMonolith.Supabase/
  Examples.Webshop.Host                      all five modules in one process; its build exports
  Examples.Webshop.Supabase.AppHost          Aspire: Postgres seeded from supabase/migrations, or a live project
  supabase/
    config.toml                              a local Supabase project, from supabase init
    migrations/                              every module's migrations, written by the host's build
ModularMonolith.SqlServer/
  Examples.Webshop.SqlServer.Host            the same five modules on SQL Server, migrating on start-up
  Examples.Webshop.SqlServer.AppHost         Aspire: SQL Server in Docker
Tenancy/                                     the second application, on Tenancy: its own modules, API, UI and AppHost
```

A host makes exactly two decisions for a module, and hands them over as a `ModuleHost`: where its
tables live (`ModuleDatabase.Sqlite`, `.Supabase`, `.Postgres`, `.SqlServer`), and where
what it publishes goes (`ModuleHost.InProcess` sends it to the other modules in the process). Everything
else, its context, its outbox, what it publishes as what, the policies it follows, the module registers
itself in its `Add{Module}Module`.

Inside a module the folders say what kind of thing a file is:

```
Examples.Webshop.Ordering/
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
    Persistence/             the DbContext, marked [SupabaseMigrations], Migrations/
  Api/                       the module's HTTP endpoints
    GraphQL/                 its GraphQL types, queries, mutations and lookups
  Module.cs                  [assembly: Module("Ordering")]
  OrderingModule.cs          AddOrderingModule: everything the host calls
```

Namespaces follow the folders and stop at the aggregate: everything under `Aggregates/Orders/` is
`Examples.Webshop.Ordering.Domain.Orders`, and everything under `Application/Orders/` is
`Examples.Webshop.Ordering.Application.Orders`. The building-block folders (`Aggregates/`,
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
(`OrderStub.cs`), Inventory adds a product's stock (`ProductStock.cs`), and Catalog marks
`productBySku` as the lookup a `Product` is fetched by. So `lines { product { name stock { available } } }`
is answered by three modules, the name by Catalog and the stock by Inventory, merged on the SKU. No module
knows another's classes; they agree on a type's name and its key. The shop's types, data loaders and errors
are still written by hand, so for how to declare a schema, read the
[Tenancy sample](../docs/tenancy.md#graphql-in-the-sample), which is the reference for GraphQL.

A Fusion gateway inside the monolith composes the five into the one schema at start-up and answers each
query by calling the modules' schemas directly, in the process, with no HTTP between them. It is the same
Fusion the microservices samples run across processes, and the modules' GraphQL is the same code in
both; see [GraphQL across services](#graphql-across-services).

A module registers its source schema with the rest of itself, in `Add{Module}Module`, when the host
serves GraphQL; the host says so once, and chooses what is its to choose, the subscriptions' transport:

```csharp
var host = ModuleHost.InProcess(database)
    .AlsoSendTo<GraphQlSubscriptionSink>()
    .WithGraphQL(graphql => graphql.AddInMemorySubscriptions());

builder.Services.AddCatalogModule(host);         // ... and the other four
builder.Services.AddInMemoryFusionGateway();     // composes whatever source schemas the modules registered

app.MapInMemoryFusionGateway();                  // /graphql
```

The gateway is the toolkit's `DDDToolkit.HotChocolate.Fusion.InMemory`, and it knows no module; see
[One schema over a modular monolith](../docs/graphql.md#one-schema-over-a-modular-monolith).

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
dotnet run --project Examples/ModularMonolith.Supabase/Examples.Webshop.Host
```

In Visual Studio or Rider, start one of two kinds of project. A monolith's `Host` runs on its own, on
SQLite, and serves REST and GraphQL on port 5080 (5090 for SQL Server); it opens no browser, so use the
`.http` file or a GraphQL client against it. An `AppHost` runs a whole sample under Aspire, containers
included, and opens the Aspire dashboard, from which every resource's endpoint and logs are one click
away. The microservices' services and gateways are not meant to be started on their own: they get their
databases and brokers from their AppHost.

Then work through `Examples.Webshop.Host.http` from the top. It lists the products and the stock,
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
dotnet run --project Examples/ModularMonolith.Supabase/Examples.Webshop.Supabase.AppHost

# The same against a real Supabase project or one of its branches: set the connection string on the
# AppHost once, and it starts no container.
dotnet user-secrets set ConnectionStrings:Supabase "Host=...;Database=postgres;..." --project Examples/ModularMonolith.Supabase/Examples.Webshop.Supabase.AppHost

# The Supabase CLI's local stack, without Aspire.
cd Examples/ModularMonolith.Supabase
supabase start          # a local Supabase in Docker; applies supabase/migrations
dotnet run --project Examples.Webshop.Host --launch-profile supabase
```

The AppHost's container is Postgres 17 with pgmq 1.5.1, the versions a Supabase project has. It runs
every file in `supabase/migrations` on its first start, in name order, which is what Supabase does with
them, so the host's start-up check finds every migration applied. The `supabase` launch profile points
at the CLI's local database on port 54322.

After changing a model,
scaffold the migration in the module that owns it, and build:

```bash
dotnet ef migrations add AddGiftWrap --project ../Modules/Ordering/Examples.Webshop.Ordering --startup-project Examples.Webshop.Host --output-dir Infrastructure/Persistence/Migrations
dotnet build Examples.Webshop.Host   # writes 2026…_AddGiftWrap.ordering.ddd.sql
supabase migration up                   # or: supabase db reset, to start over
```

There is no export step to remember. Each module's context is marked `[SupabaseMigrations]`, which also has
the build write the design-time factory `dotnet ef` uses beside it, and the host's project file sets
`SupabaseMigrationsExport`: `Write` locally, so every build writes the files a new migration needs, and
`Check` in CI, so a pull request that adds a migration without its file fails. On Supabase the host checks
every module's migrations with one `AddSupabaseMigrations()` in its `Program.cs`. The files are committed, because Supabase branching reads them from the repository. See
[Entity Framework → Supabase](../docs/supabase.md) for what the build writes and how.

#### Signed-in customers and row level security

With `Supabase:Url` set as well, a request may carry the access token Supabase Auth gives a signed-in
user, and the host runs every module's queries for that request as that user, the way PostgREST runs the
Data API's. Supabase's own policies then decide what each caller sees. Here an order is its customer's:
an order knows who placed it, `Order.PlacedBy`, and two rules written in C# next to it,
[`Domain/Aggregates/Orders/Access/OrderAccess.cs`](Modules/Ordering/Examples.Webshop.Ordering/Domain/Aggregates/Orders/Access/OrderAccess.cs),
say that a customer sees and changes their own orders and nobody places one for somebody else. The build
writes them into `supabase/migrations` as `…_access.ordering.ddd.sql`, with policies for the order lines
that follow the order: read with it, and written as its rules allow. A request without a token runs as
`anon` and places a guest's order, which anybody with its id can follow, as every other scenario does.
The outbox pollers run outside any request, as the role the host logged in as, so checkout goes on
regardless. On SQLite and SQL Server nobody signs in, every order is a guest's, and nothing enforces the
rules.

The AppHost's container gets the roles and `auth` functions every Supabase project has from
`Examples.Webshop.Supabase.AppHost/database/`, and the host is given the CLI's local JWT secret, as the
`supabase` launch profile gives it against `supabase start`. With it the host takes two kinds of token: one
signed with that secret, and one signed with a key Auth publishes, which is how the Auth server of
`supabase start` signs a user's. Against a project, set `Supabase:Url` on the AppHost next to the
connection string, and the host checks tokens against the keys the project publishes:

```bash
dotnet user-secrets set Supabase:Url "https://<ref>.supabase.co" --project Examples/ModularMonolith.Supabase/Examples.Webshop.Supabase.AppHost
```

```http
GET http://localhost:5080/orders/{id}
Authorization: Bearer <access token from supabase.auth.getSession()>
```

`supabase/migrations/20260925150000_grant_modules_to_callers.sql` gives `anon` and `authenticated` the
module schemas, which the Data API does not expose, so only the application reaches them. The host's project
file keeps those grants by setting `SupabaseRowAccessGrants` to `None`, since a guest reprices products in
Catalog, which has no rules, and privileges written from the policies would refuse that; the access files
still force Ordering's policies on the tables' owner, `postgres`, which may bypass them. Its
`SupabaseRowAccessRoles` says `system=none`, so the work outside a request, the outbox pollers, runs as that
owner on purpose rather than as the bookkeeping role `ddd_system`, which no access file of this host makes; the
build records it in the host, and `AddSupabaseRowLevelSecurity` in `Program.cs` reads it. See
[Row level security for your own queries](../docs/supabase.md#row-level-security-for-your-own-queries).

#### Through Supabase Queues

The same host, with its modules talking through Supabase Queues instead of in process. Every module's
outbox sends to one pgmq queue, `shop`, in the same database, and the host reads it back into the modules
through the same inboxes the module sink uses. No module and no handler changes; `OverSupabaseQueues` at
the bottom of the host's `Program.cs` is the whole difference.

```bash
# Aspire: the same container, the host with Messaging=pgmq
dotnet run --project Examples/ModularMonolith.Supabase/Examples.Webshop.Supabase.AppHost --launch-profile pgmq

# Against the Supabase CLI's local stack
dotnet run --project Examples/ModularMonolith.Supabase/Examples.Webshop.Host --launch-profile supabase-pgmq
```

One queue rather than one per module, because Supabase ships pgmq 1.5.1, and the topic routing
`Microservices.Pgmq` uses to give every service a queue of its own came in pgmq 1.11. The extension is
turned on by `supabase/migrations/20260925090000_enable_queues.sql`, a migration written by hand next to
the exported ones; the build's export leaves a file it did not write alone. In the Supabase dashboard the
queue is under Integrations, Queues, where its messages can be watched as the scenarios run. See
[Transports](../docs/transports.md#when-a-module-becomes-its-own-deployable-pgmq) for what each pgmq
version supports.

### On SQL Server

```bash
dotnet run --project Examples/ModularMonolith.SqlServer/Examples.Webshop.SqlServer.AppHost
```

The same five modules, the same endpoints and the same `.http` walk-through (on port 5090 when the
host runs outside Aspire), on SQL Server in Docker. Compare the two hosts' `Program.cs`: what differs in
substance is the database, `ModuleDatabase.SqlServer(...)` for `ModuleDatabase.Supabase(...)`, and the one call
that follows from it on Supabase, `AddSupabaseMigrations()`.

On Supabase somebody else applies the migrations and the application only checks them, with that one call; on
SQL Server nobody else will, so each module migrates its own schema on start-up, before its outbox poller
starts. Each module's SQL Server migrations live in a project next to it,
`Examples.Webshop.{Module}.Migrations.SqlServer`, apart from its Postgres ones: Entity Framework keeps
one model snapshot per context per assembly, and the two providers disagree on every column type. A host
on SQL Server references the migrations of the modules it runs, and `ModuleDatabase.SqlServer` finds them
by name. Scaffold one with:

```bash
dotnet ef migrations add AddGiftWrap --project Examples/Modules/Ordering/Examples.Webshop.Ordering.Migrations.SqlServer --output-dir Migrations
```

### As services

The same five modules, cut into three deployables, with a gateway in front so a client still sees one
shop at one address:

| Service | Runs | Consumes from the others |
|---|---|---|
| `storefront` | Catalog, Ordering | stock reserved or refused, payments taken or refused |
| `payments` | Payments | orders placed or cancelled, stock reserved |
| `fulfilment` | Inventory, Shipping | orders placed, cancelled or confirmed |

Every sample has a project per service, `Examples.Webshop.{Sample}.Storefront`, `.Payments` and
`.Fulfilment`, each with its own `Program.cs`, and each referencing only the modules it runs. Payments
cannot call into Ordering: it does not reference it. What it knows of Ordering is `OrderPlacedV1`, from
Ordering's contracts, the way a service in another repository would. Nothing shared knows the whole
shop, apart from the gateway's route table in its `appsettings.json`.

A service is not a module, though: Storefront runs Catalog and Ordering in one process, so a price Catalog
publishes still reaches Ordering through the module sink, next door, and only a message another service
consumes leaves the process.

Each sample has a gateway of its own, `Examples.Webshop.{Sample}.Gateway`, and a client talks to
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
line's SKU to the product is made in-process, as in the monolith: a field the service adds to Ordering's
`OrderLine`, in a static partial class with `[ObjectType<OrderLine>]`, `GraphQL/OrderLineProduct.cs` in each
storefront project. Payments and Fulfilment each declare
an `Order` of their own that holds nothing but the order's id, and add the one field they know about,
`payment` or `shipment`. The gateway merges the three `Order` types on the id, asks Storefront for the
order, then asks Payments and Fulfilment for their fields with the id it got back. Those stubs are in
the modules, as `OrderStub.cs` in Payments' and Shipping's `Api/GraphQL`: they are the module's part of
the order's API, in every schema the module is part of.

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

No broker to run: a queue is a table in the database the outbox is already in. Topic routing needs pgmq
1.11 or later, and Supabase ships 1.5.1 today, so this sample runs on Postgres with pgmq 1.13; on
Supabase, the monolith [through Supabase Queues](#through-supabase-queues) shows the shape that works
there.
`BookShipment` in Shipping is the same class it is in the monolith; it cannot tell that
`OrderConfirmedV1` came through `pgmq.q_fulfilment` rather than from the module next door.

```bash
dotnet run --project Examples/Microservices.Pgmq/Examples.Webshop.Pgmq.AppHost
```

**`Microservices.Wolverine/`** gives every service a database of its own, and not of one kind: Storefront
on SQL Server, Payments and Fulfilment on Postgres. What they share is RabbitMQ, used the way Wolverine
uses it: conventional routing, a fanout exchange per contract type and, per service, a queue for every
contract it handles. `UseIntegrationEventNames()` names each exchange after the contract's published name
and version, `ordering.order-placed.v1`, rather than its CLR type. The modules are registered first, so Wolverine knows what the service handles; from
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
dotnet run --project Examples/Microservices.Wolverine/Examples.Webshop.Wolverine.AppHost
```

**`Microservices.MassTransit/`** is the same topology with MassTransit, every service on a SQL Server
database of its own, and RabbitMQ used the way MassTransit uses it: every contract a message type with an
exchange of its own, named `ordering.order-placed.v1` by `UseIntegrationEventNames()`, one receive
endpoint per service, bound by MassTransit to the exchanges of the contracts its consumers take. In each service's `RabbitMq.cs`, called after the modules:

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
dotnet run --project Examples/Microservices.MassTransit/Examples.Webshop.MassTransit.AppHost
```

### Testing the samples end to end

`Tests/Examples.AppHost.Tests` starts each sample's AppHost, containers and all, and plays
the same scenarios against every one of them over HTTP: an order confirmed and shipped, a payment
refused and the stock put back, an order there is no stock for and its payment voided, a confirmed
order that cannot be cancelled. The answers must not depend on how the shop is hosted, and these tests
are what says so. They need Docker, skip themselves without it, and run in CI in the Sample Tests
workflow, one job per sample:

```bash
dotnet test Tests/Examples.AppHost.Tests --filter "Sample=ModularMonolith.SqlServer"
dotnet test Tests/Examples.AppHost.Tests --filter "Sample=ModularMonolith.Supabase.Pgmq"   # through Supabase Queues
```

The Supabase samples add two scenarios of their own, with signed-in customers: one customer's order is
not another's to see or cancel, and a customer's order still goes through checkout.

The Supabase monolith also runs against a real Supabase project, in the Supabase Live workflow, twice: in
process and through Supabase Queues. It puts the exported `supabase/migrations` on with `supabase db push`,
as a deploy would, and plays the same scenarios against the project, where the monolith checks on
start-up that every migration was applied. Its customers are real users of the project's Auth, made with
its admin API for each run and deleted afterwards, so their tokens are the project's own. The project exists for these tests alone: each run first
drops the module schemas and the shop's queue and forgets their migrations. With the repository variable `SUPABASE_BRANCHING` set to `true`, each run gets a preview
branch of its own instead and deletes it afterwards; branching needs a Supabase Pro organisation. The
workflow connects through Supabase's pooler, because the database's own host has no IPv4 address and
GitHub's runners have no IPv6.

Locally, point the AppHost at a project the same way the workflow does:

```bash
dotnet user-secrets set "ConnectionStrings:Supabase" "Host=<pooler host>;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<password>;SSL Mode=Require" --project Examples/ModularMonolith.Supabase/Examples.Webshop.Supabase.AppHost
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
| The same modules on another database, and who applies the migrations | `Shared/Examples.Hosting/ModuleDatabase.cs`, the two monoliths' `Program.cs` |
| Migrations per provider in separate assemblies | each module's `Infrastructure/Persistence/Migrations` and its `*.Migrations.SqlServer` project |
| The whole system under test, containers included | `Tests/Examples.AppHost.Tests` |
| One GraphQL schema over modules that do not know each other: Fusion in the monolith | each module's `Api/GraphQL` (`Add{Module}SourceSchema`, `ProductStub.cs`, `ProductStock.cs`, `OrderStub.cs`), `DDDToolkit.HotChocolate.Fusion.InMemory` |
| The same schema composed across services by a Fusion gateway | each `Microservices.*` AppHost and gateway, `OrderStub.cs` in Payments and Shipping |
| Relay node ids from the toolkit's identifiers, and references to another module's node | each `Api/GraphQL/*Type.cs`, `.ID("Order")` in Payments, Inventory and Shipping |
| Rules and invalid values as GraphQL errors with their codes | `AddDDDToolkitErrors()` in each module's `Add{Module}SourceSchema`, `Ordering/.../Api/GraphQL/OrderingOperations.cs` |
| Live updates from the outbox | `OrderingSubscriptions`, `GraphQlSubscriptionSink` in each monolith's `Program.cs` |
| A schema, a migration history, an outbox and an inbox per module in one database | each `Infrastructure/Persistence/*Context.cs` |
| Entity Framework migrations applied by Supabase | `supabase/migrations`, `[SupabaseMigrations]` on each context, the host's `.csproj`, `AddSupabaseMigrations()` in its `Program.cs` |
| Modules talking through Supabase Queues | `OverSupabaseQueues` in `ModularMonolith.Supabase/Examples.Webshop.Host/Program.cs`, `supabase/migrations/20260925090000_enable_queues.sql` |
| The testing kit and `DomainEventClock` | `Tests/Examples.Webshop.Tests` |

The shop has no repositories. Each of its modules' `DbContext` is its repository and unit of work, used
directly by the endpoints and the policies. The toolkit has no repository abstraction to show, and a
wrapper around a `DbContext` would only hide what the toolkit does to it. The Tenancy sample, below, does put its
storage behind ports, to show a module whose use cases know no Entity Framework, and says why there.

What it does not show: Newtonsoft, FluentValidation validators, and upcasting an older payload. Those have runnable coverage in `Tests/` and a page each in [docs](../docs).

## The Tenancy sample

A second application, smaller than the shop and apart from it: crews, the people who work on a project,
in two tenants, on the [Tenancy](../docs/tenancy.md) supporting domain, and on the
[Membership](../docs/membership.md) one for the crews. It is there to show who may see and do what, and to let
you try it: a dev login with seeded people, a UI that calls the API with each person's token and tenant, and
calls that deliberately break a rule and show the refusal. How the access works is explained on the Tenancy
page, in [Who may do what, in the sample](../docs/tenancy.md#who-may-do-what-in-the-sample) and
[Try it](../docs/tenancy.md#try-it).

A crew is the Membership package's: its members are seats, and the roles they hold are project roles, which
each tenant keeps for its crews, starting from three starter roles made when the tenant is set up, and makes,
renames, re-keys and archives on the UI's Crew roles page. A role of the organization goes on no crew, and
the Tenants module says nothing of crews any more. The four functions the database asks about a crew keep
their names, and the package writes them, with the lock that holds a crew's rows and a project's owner to the
keys their commands ask.

| Module | Owns | Asks | Layers |
|---|---|---|---|
| **Tenants** | the application's tenant, organization, unit, seat and role classes, on the package; its context and migrations | nothing: it is what the others ask | Contracts, Domain, Application, Infrastructure, Api |
| **Projects** | projects, their crews and the tenants' project roles, on the Membership package, and who may see and change a project | Tenancy: where the caller holds a key, and whether a seat is active | Contracts, Domain, Application, Infrastructure, Api |
| **Inspections** | inspections recorded on a project | Projects, through `IProjectGate`: may the caller do this to that project | Domain, Application, Infrastructure, Api |

| Project | What it is |
|---|---|
| `Examples.Tenancy.Host` | the API: the three modules, the dev login, the tenant a request works in, refusals as problem+json, and the demonstration data |
| `Examples.Tenancy.Catalogue` | the application's permission catalogue, which the host runs with and the exported policies are written from |
| `Examples.Tenancy.Exporter` | a program without routes whose build writes `Tenancy/supabase/migrations` |
| `Examples.Tenancy.Ui` | a Blazor Web App, interactive on the server, that knows the API only over HTTP |
| `Examples.Tenancy.AppHost` | Aspire: Supabase's own Postgres and Auth images and a mail catcher as containers, the files of `Tenancy/supabase/migrations` applied to that database, and the API and the UI as processes |

```
Tenancy/
  Modules/
    Directory.Build.props                          declares each project's module, named after its folder, with DDD_Module: no Module.cs per project; and makes each *.Contracts project its module's contracts, with DDD_ModuleContracts: no [ModuleContract] per type
    Tenants/
      Examples.Tenancy.Tenants.Contracts           the ids, and the operators' token role
      Examples.Tenancy.Tenants.Domain              the application's classes on the package, a folder per aggregate it adds to; Module.cs with Tenancy's switch, which writes the organization and the role; and TenantsTenancy, which the generator writes here and the use cases are named through
      Examples.Tenancy.Tenants.Application         a command or query per use case, in a folder per feature; the request interface its access behavior is generated from; the port ITenancyReads
      Examples.Tenancy.Tenants.Infrastructure      the context, marked [SupabaseMigrations], its migrations, EfTenancyReads, the column rule on the seat's name in Access/, AddTenantsInfrastructure
      Examples.Tenancy.Tenants.Api                 the module's entry, TenantsModule, and per feature the routes (Rest) and the GraphQL fields and types (GraphQL)
    Projects/
      Examples.Tenancy.Projects.Contracts          ProjectId, the keys and IProjectGate: all Inspections may name
      Examples.Tenancy.Projects.Domain             a project and its crew, and a tenant's project roles, on the Membership package's templates; their rules, events and refusals
        Aggregates/Projects/                                Project.cs and ProjectRefusals.cs, with Entities/, Events/, Invariants/ and ValueObjects/ beside them
        Aggregates/ProjectRoles/                            ProjectRole.cs, with Events/ and ValueObjects/
      Examples.Tenancy.Projects.Application        a command or query per use case, the access rules and check, the ports IProjectStore and IProjectReads
        Access/                                             the check, the rules, the keys; Queries/KeyOnProject.cs
        Crew/                                               Commands/ to change a crew, Queries/AllCrewMembers.cs to read one
        Lifecycle/                                          Commands/ to open, rename, plan, move, close and reopen a project
        Operators/                                          Queries/TenantProjects.cs, for the application's own staff; each module has the feature
        Overview/                                           Queries/VisibleProjects.cs and ProjectDetail.cs
        Ownership/                                          Commands/ChangeProjectOwner.cs
        ProjectRoles/                                       Commands/ to make, rename, re-key and archive a project role, and to set a tenant up; Queries/
        StoredProjects/                                     the ports the features read and save projects through
      Examples.Tenancy.Projects.Infrastructure     the context with Tenancy's read model, marked [SupabaseMigrations], its migrations, EfProjectStore, EfProjectReads, the row rules in Access/, AddProjectsInfrastructure
      Examples.Tenancy.Projects.Api                the module's entry, ProjectsModule, the routes and the GraphQL schema
        Access/Rest/, Crew/Rest/, Lifecycle/Rest/, ...      a feature's routes, under the name the application project gives the feature
    Inspections/                                            no contracts, since no module names its types
      Examples.Tenancy.Inspections.Domain          an inspection, its id, event and refusals
      Examples.Tenancy.Inspections.Application     a command and its queries in the one feature Recording, with the ports IInspectionStore and IInspectionReads beside them; the access check that asks Projects' gate
      Examples.Tenancy.Inspections.Infrastructure  the context, marked [SupabaseMigrations], its migrations, EfInspectionStore, EfInspectionReads, the row rules in Access/, AddInspectionsInfrastructure
      Examples.Tenancy.Inspections.Api             the module's entry, InspectionsModule, the routes and the GraphQL schema
  Shared/
    Examples.Tenancy.Shared.Application            what the modules' application projects do the same way: PageSizes, which holds a paged query to one end of its list and to the list's own sizes
    Examples.Tenancy.Shared.Domain                 what several modules share and none owns: DateRange, a project's planned range and the days an inspection covers
    Examples.Tenancy.Shared.Infrastructure         what the modules' infrastructure projects do the same way: ListCursors, which holds a paged read to the cursors of its own list
  Examples.Tenancy.Host                            the API, REST, GraphQL at /graphql and the administration's gateway at /admin/graphql, and Examples.Tenancy.Host.http to walk through it
  Examples.Tenancy.Catalogue                       SampleCatalogue, and what Tenancy writes into the exported access files for it
  Examples.Tenancy.Exporter                        the export, as a build step of a program of its own
  supabase/                                                 config.toml and migrations/: what the export wrote, the login role's file among them, and the earlier one written by hand, which stays
  Examples.Tenancy.Ui                              the UI
  Examples.Tenancy.AppHost                         Aspire: the containers of Supabase's own images, the migrations applied to them, and api and ui
```

### Running it

1. Start Docker. The sample runs on Supabase's own images and on no other database.
2. From the repository's root, run `dotnet run --project Examples/Tenancy/Examples.Tenancy.AppHost`.
3. Open the dashboard with the login link the terminal prints, `http://localhost:15105/login?t=...`. It lists
   `db`, `roles`, `mail`, `auth`, `migrate`, `api` and `ui`.
4. Open `ui` from there, `http://localhost:5091`, and sign in: with a card of the dev login, or with
   `<name>@example.test` and the password the dashboard shows under Parameters, as `demo-password`, masked
   until its eye is clicked.
5. Read a mail Auth sends in `mail`, from the dashboard: it is empty until somebody is invited by address on
   the UI's Invitations page. The link in the mail opens as it is written.

Nothing is configured. The API (`api`, on `http://localhost:5090`) and the UI (`ui`) run as processes, on
containers of Supabase's own Postgres and Auth images (the tags in `SupabaseImages.cs`, the set the Supabase
CLI starts) and a mail catcher. They are Supabase's builds and not a plain Postgres made to look like one,
because the roles the image ships and what each may do is exactly what the sample leans on. Storage and
Realtime are not started. `roles` and `migrate` run once and exit.

- **`migrate`** applies every file of `Tenancy/supabase/migrations` as `postgres`, which on this image is no
  superuser, and then turns `tenancy_api`'s login on with a password of this run. The API gets a connection
  string for `tenancy_api` and nothing else.
- **`auth`** is Supabase's Auth server, with no gateway in front of it: `Supabase:AuthUrl` tells the API
  and the UI where it answers. With `Sample:SeedAuthUsers` the API makes the nine people users there, through
  the Auth admin client, each under the fixed id their seats are found by (`Host/Seeding/DemoAuthUsers.cs`,
  Development only). Auth is given a signing key made up for the run (`AuthSigningKeys.cs`), as the Auth
  server of the stack the Supabase CLI starts has one: it signs a person's token with it, and the API checks
  that token with the public half Auth publishes, fetched from `Supabase:AuthUrl`.
- **The login page shows both logins**: an e-mail address and a password, checked by Auth
  (`rhea@example.test`, and the password of `demo-password`), and the dev login's cards below it. The API
  takes either token, and finds the same seat by its subject. The form and what it tells a person who is
  refused read in the UI's language, as the headings, labels and explanations of every page do, and the
  actions of the try-it form. What the API answers as data is shown as it came, in English: the description
  on each card of the dev login, the titles of the presets on Try it, and every status, key and name of a
  tenant, a unit or a role. A refusal's text is the API's as well, in the language the UI asked for.
- **The mail of an invitation leads to the UI's page that accepts it.** The AppHost tells the API where the
  page is (`Sample:Invitations:AcceptPage`, the UI's endpoint and `/invitations/accept`) and gives Auth the UI
  as its site, both under `localhost`, the one host name Auth takes for its site here. The link in the mail
  names Auth's own address on this machine and the path Auth answers itself, `/verify`, so it needs no
  gateway.

The demonstration has nine people: an administrator who also runs the work (ada), an area manager (rhea), a
project's owner (leo), two crew members (juno, vic), a suspended seat (seth), someone seated in both tenants
(tove), someone who only gives people their roles (hana) and an access admin, who runs who may do what and
does none of the work (maud). A tenth, orla, is an operator, one of the application's own staff with no seat:
the UI has no card for her and no page for the operators' lists, which are reached with a token from the dev
login, as `Examples.Tenancy.Host.http` shows.

The three modules share one database, each in a schema of its own: `tenancy`, `projects` and `inspections`.
They have to share it: Projects asks Tenancy inside its own queries, through Tenancy's read functions, and
that needs both in one database. Each module has migrations of its own, in its infrastructure project, and
keeps its history table in its schema. The API seeds Harbor Works and Meadow Gardens on its first start. The
containers are new every run, so to start over, stop the AppHost and start it again. Ctrl+C in its terminal
stops it, with the API and the UI, and removes its containers; an AppHost whose process is killed leaves
them behind, to be removed by hand.

Without Aspire, the way to run the sample is [the stack the Supabase CLI starts](#on-the-stack-the-supabase-cli-starts).
The host has no other database to fall back on: started without `ConnectionStrings:Supabase`, it stops and
says how to get one.

### On Postgres

The host runs on Postgres, as Supabase runs it, at the connection string it is given in
`ConnectionStrings:Supabase`, and it runs there the strict way:

- **The host logs in as `tenancy_api`, a role that owns nothing** and holds no privilege on any table. Every
  command runs as its caller: a signed-in user, system work in a tenant, or the toolkit's bookkeeping, each a
  database role with exactly the privileges written from its policies. Every table forces its policies on
  its owner as well.
- **The database is made by whoever owns it**, from the files under `Tenancy/supabase/migrations`. There
  are three kinds: a file for each migration of a module, with its tables; a module's access file, with the
  policies, functions, triggers and privileges, written again under a later name whenever a rule, the
  catalogue or the model changed; and `*_login_role.tenancy_api.ddd.sql`, which makes the login role and
  grants it the roles its callers run as, written from the exporter's `SupabaseLoginRole` and written again
  under a later name whenever those roles change. They are applied in the order of their names, each of
  which begins with a timestamp. Before the build wrote the login role's file, it was written by hand:
  `*_tenancy_login_role.sql` says the same, and stays, since a database that applied it keeps its version
  in the history the Supabase CLI compares the directory with. Both come after an access file, which makes
  three of the roles they grant (`ddd_system_in`, `ddd_system` and `tenancy_operator`); the one written by
  hand needs that, and the build's makes them where they are missing all the same. The host applies none of
  the files. It checks at start-up that none is missing and that the database is set up as the policies
  rely on, and does not start otherwise.
- **The roles are the defaults but one**, said where the export runs: `Examples.Tenancy.Exporter.csproj` maps the
  operators' token role, `token:tenancy_operator=tenancy_operator`, and a signed-in user, an anonymous caller,
  system work in a tenant and the toolkit's bookkeeping run as `authenticated`, `anon`, `ddd_system_in` and
  `ddd_system` without a word. The host is a project of its own, so it maps the same token role in code, in
  `Host/Storage/SampleStorage.cs`, and sets no other role. Every access file records the roles it was written
  for on the `ddd` schema, and the host's start-up check `supabase.roles-match-access-files` stops it where its
  own differ, naming the line to change on either side.
- **The files are written by a build**, of `Examples.Tenancy.Exporter`: every migration of the
  modules' infrastructure projects, and the access files whenever a rule, the catalogue or the model
  changed. In CI the same build only compares. An access file names the project a row access contribution of
  the application's is in with its version, and is written anew when that version changes, so the project that
  holds the sample's own, `Examples.Tenancy.Projects.Infrastructure`, has a version of its own and does not
  follow the toolkit's. Tenancy's and Membership's SQL comes with the exporter's references to the modules
  that use their Postgres packages, made in `DDDToolkit.RowAccessContributionsOfPackages.g.cs` among the
  exporter's generated files from what the sample marks, and is named by the package's class and assembly,
  without a version.
- **A module has one set of migrations**, beside its context in its infrastructure project. The context is marked
  `[SupabaseMigrations]`, and the build writes the design-time factory beside it that `dotnet ef`, the export and the
  host's start-up check all build the context with; the host checks every marked context with one
  `AddSupabaseMigrations()`. After a change to a model, `dotnet ef migrations add` in that project and a build of the
  exporter write the new file; the context's remarks have both commands.
- **Connections are budgeted per purpose**: one data source for requests and one for background work, each
  with its own maximum (`Sample:Pools:Requests`, 16, and `Sample:Pools:Background`, 4).
- **GraphQL answers as the routes do.** A field only sends, so what it reads goes through the same
  contexts, on the connections for requests, as its caller: `SampleOnPostgresTests` adds a policy the
  application knows nothing of, and a query is withheld the rows by the database.
- **One save is the application's work for a seat.** A seat that takes its own role on a crew, or itself off
  it, gives up the right the change was allowed by, and the database, which judges each statement of a save
  by the rows as they are then, would refuse the seat the rest of that save. It is that only for the command
  whose check let it through, the request in hand; a handler reached around its check saves as the caller
  (`Crew/OwnPlaceOnTheCrew.cs` in the Projects application project).

`SampleOnPostgresTests` proves it on Supabase's Postgres image: it applies the files in order as the role that
owns the database, turns the login on and starts the host. The next section runs it by hand, on the stack the
Supabase CLI starts.

### On the stack the Supabase CLI starts

`Examples/Tenancy` is a Supabase project as the CLI reads one: `supabase/config.toml`, and the files under
`supabase/migrations`. It needs Docker and the Supabase CLI, installed or through `npx`: every `supabase`
below is `npx --yes supabase@2.119.0` just as well, which is the release these steps were run with.

```bash
cd Examples/Tenancy
supabase start                         # or: npx --yes supabase@2.119.0 start
```

The CLI starts the database on port 54322, the gateway with Auth behind it on 54321, Studio on 54323 and a
mail catcher on 54324, and applies every file of `supabase/migrations` in the order of their names. It does
so as `postgres`, the role that owns the database and on Supabase's image no superuser, which is the role
the tests apply them as:

```
Applying migration 20261001215449_Initial.tenants.ddd.sql...
...
Applying migration 20261006170419_access.inspections.ddd.sql...
Started supabase local development setup.
```

Then, once, as the database's owner, the login role gets a password. No file gives it one: a migration is
kept in a repository, and a password is not.

```bash
docker exec supabase_db_examples-tenancy psql -U postgres -c "alter role tenancy_api with login password '<a password>'"
```

It answers `ALTER ROLE`. The host is then started with that role's connection string, and nothing else of
the database:

```bash
dotnet run --project Examples.Tenancy.Host -- --ConnectionStrings:Supabase "Host=127.0.0.1;Port=54322;Database=postgres;Username=tenancy_api;Password=<a password>"
```

```
info: Examples.Tenancy.Host.Seeding.DemoSeeder[0]
      Seeded the demonstration: harbor and meadow.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5090
```

A host that listens passed every start-up check: each file was found applied, `tenancy_api` owns nothing and
holds no privilege in a module's schema, the functions that run as their owner are let through the forced
policies, and Tenancy's second lock is in place. The dev login works here as under the AppHost, so
`Examples.Tenancy.Host.http` walks through it from here. The Data API the stack serves beside the
host reaches no module's schema: `config.toml` exposes none of them, and Supabase's own roles hold nothing
there.

**The real login** takes four more settings, and the UI told where the project is:

```bash
dotnet run --project Examples.Tenancy.Host -- --ConnectionStrings:Supabase "Host=127.0.0.1;Port=54322;Database=postgres;Username=tenancy_api;Password=<a password>" --Sample:SeedAuthUsers true --Supabase:SecretKey "<the Secret key>" --Sample:DemoPassword "<twelve characters or more>" --Sample:Invitations:AcceptPage http://localhost:5091/invitations/accept
dotnet run --project Examples.Tenancy.Ui -- --Supabase:Url http://127.0.0.1:54321
```

The Secret key is under Authentication Keys in what `supabase status` shows. The host makes the nine people
users of the stack's Auth, through the Auth admin client, each under the fixed id their seats are found by,
and says so:

```
info: Examples.Tenancy.Host.Seeding.DemoAuthUsers[0]
      The demonstration people can sign in at Supabase Auth with the password of Sample:DemoPassword; made just now: ada, rhea, leo, juno, vic, seth, tove, hana, maud.
```

The login page, on `http://localhost:5091`, then shows an e-mail address and a password above the dev login's
cards. `rhea@example.test` with the demonstration password signs in at Auth, and the header reads "Signed in
as rhea@example.test". The API answers her token with the two projects of the north, Pier 7 and Inland depot,
through the routes and through GraphQL alike, and refuses it in meadow, where she has no seat. Three things
about this stack are worth knowing:

- **Auth signs a person's token with a key it publishes**, where the dev login signs with the local stack's
  secret. The host takes both, through the toolkit's bearer scheme (`Host/Auth/SampleAuthentication.cs`): a
  token signed with the secret is checked with the secret, and one signed with a key with the keys Auth
  publishes, fetched when the first one arrives. The Auth server the AppHost starts signs the same way.
- **`[auth.email] enable_signup` is on in `config.toml`**, though nobody signs up: in the local stack that
  setting is what lets a person sign in with a password at all, and `[auth] enable_signup = false` is what
  closes signing up.
- **The mail of an invitation leads to the UI's page that accepts it**, with the last setting above. The
  mail is in the catcher on port 54324. Auth sends a browser on only to an address of its site, which
  `config.toml` says is the UI (`site_url`), and it tells the site by host name: `localhost` is not
  `127.0.0.1`. A page given under the other name is not refused. Auth sends the person to the site's own
  address instead, without the invitation's token
  ([Inviting a person by address](../docs/tenancy.md#inviting-a-person-by-address)).

`supabase stop` keeps the data for the next `supabase start`, and `supabase stop --no-backup` removes it.
`supabase db reset` applies the files again to an empty database: the login role is there again with no
password and Auth has no users, so the password and the host's first start come again.

```bash
DDDTOOLKIT_REQUIRE_SUPABASE_CLI=1 dotnet test Tests/Examples.Tenancy.Tests --filter "Sample=Tenancy.SupabaseCli"
```

`SampleOnTheCliStackTests` takes these steps against a stack that is running, from the repository's root: the
files the CLI recorded and who owns what they made, the host started as the login role, a person signing in
at the stack's Auth and reading her projects and nothing of the other tenant, a stranger who is refused an
account, and a change the database refuses with its own code. It starts no container, and it runs only where
`DDDTOOLKIT_REQUIRE_SUPABASE_CLI=1` says it is meant, where a stack that is not running fails it: without the
variable its tests are skipped and touch no stack, so a run of every test leaves a stack you have up alone. The
Sample Tests workflow starts the stack and runs it as `Tenancy.SupabaseCli`. It
works in the stack's own database and changes two passwords there unless its environment holds them:
`tenancy_api` gets one of its own unless `ConnectionStrings__Supabase` is set, and the nine people get one
unless `Sample__DemoPassword` is set. A host started before it is given the role's password again
afterwards, and gives the people theirs again when it starts.

### How it is built, and what is where

The Tenancy page explains the sample, with diagrams and the code beside them, and this page does not say it
again:

- [Who may do what, in the sample](../docs/tenancy.md#who-may-do-what-in-the-sample): a module's layers and
  its entry, which registers the module and maps its routes for callers with a seat and for operators (Tenants
  also for a signed-in person who has no seat yet); what every request declares and the pipeline it passes;
  the ports; the routes of a crew, the paged lists and a project's version; and how a module answers ids and
  a screen asks the directory what they are called.
- [GraphQL in the sample](../docs/tenancy.md#graphql-in-the-sample): one schema over the three modules, as
  types over the application's own records with generated data loaders. A token is needed as for every route,
  and a seat for every field but `seatsOfMine` and `invitationAccept`, which need none, and the operators'
  four, which ask for an operator.
- [Inviting a person by address](../docs/tenancy.md#inviting-a-person-by-address) and
  [An operator, who changed a row, and the history as a list](../docs/tenancy.md#an-operator-who-changed-a-row-and-the-history-as-a-list).
- [Design choices and where to see them](../docs/tenancy.md#design-choices-and-where-to-see-them): every choice
  the sample makes, with its code, something to try and its test.
- [Folders inside the layers](../docs/modules.md#folders-inside-the-layers): the tree of one module and the
  reason for each folder. `FeatureFolderTests` and `SourceTreeTests` hold the sample to it, and
  `LayerReferenceTests` to which project references which.

A browser application on another origin is let in only when the host's `Sample:Cors:Origins` lists its
origin; with none listed there is no CORS at all. Such a client sends the token and the tenant itself:

```js
const response = await fetch(`${api}/projects?size=20`, {
  headers: {
    Authorization: `Bearer ${session.access_token}`,
    Tenant: 'harbor',
  },
});
const { items, next } = await response.json();   // next: send it as ?after= for the page after this one
```

`Tests/Examples.Tenancy.Tests` has a class per scenario: for every rule the demonstration shows, for every
preset of the try-it page, and for the UI's client against the real API. A class that needs the host with a
database never makes one. It takes its hosts from one fixture, `SampleHosts`, which starts Supabase's own
images through Testcontainers, once for the run, and gives every host a database of its own there: made by
the exported files, seeded once and copied, with the host logged in as `tenancy_api`. So every answer a
scenario reads went through the exported policies and privileges as well as the application's own checks.
With Docker running:

```bash
DDDTOOLKIT_REQUIRE_CONTAINERS=1 dotnet test Tests/Examples.Tenancy.Tests --filter "Sample=Tenancy.Supabase"
```

The first run pulls the images; the containers stop with the run, and nothing is installed. Without the
variable the tests skip on a machine that has no Docker. These classes carry the samples' traits, and CI
runs them in the Sample Tests workflow: as `Tenancy.Supabase`, and as `Tenancy.Supabase.Floor` against the
oldest dependency versions the packages allow. A scenario in which a period has to run out
(`CrewMembershipOverTimeScenarios`) gives it an end a few seconds ahead and waits for it: the database
compares periods with its own clock, which no test moves.

In the same run, `SampleOnPostgresTests` and `SampleOnSupabaseTests` ask the database beside the host: every
table forces its policies, the login role reads no row it has no seat for, and a person signs in at the Auth
server with a password and reads her projects and nothing of the other tenant.
`InvitationWithSupabaseAuthTests` invites an address Auth has never seen: Auth's mail arrives in the mail
catcher, its link signs the person in, and they accept the invitation with its token. `InvitedByMailTests`
does it as the UI does: with `Sample:Invitations:AcceptPage` set, the mail's link lands on the UI's page with
the token and the sign-in after the `#`, the person chooses a password, accepts, and signs in again with it.
`TenancyOnSupabase`, in the samples' AppHost tests, starts the AppHost itself, signs in through it and
follows the mail of an invitation, as it is written, to the UI's page; it runs as `Tenancy.AppHost`.

What starts no database has no trait and stays in the main build: the architecture tests, which read the
host's registrations and its GraphQL schemas from a host that connects to nothing (`SampleWithoutDatabase`),
`MigrationTests`, which builds each context with its design-time factory, and the UI's classes over a stub.
`ContainerTraitTests` keeps the two apart: a class that takes a fixture on containers carries the traits,
and only the fixtures make a host on a database.

Four classes hold the modules to answering ids: `ModuleModelTests` (no module's model maps more of Tenancy's
than the read model), `StrictAnswersTests` (no answer of Projects or Inspections carries a name of
Tenancy's), `DirectoryScenarios` (the directory by id, over HTTP) and `DirectoryNamesTests` (the UI's names,
over a stub).

| What | Where |
|---|---|
| A supporting domain extended by the application | `Tenancy/Modules/Tenants/...Tenants.Domain/Aggregates/`: the unit's own rule, the seat's job title |
| The classes the application adds nothing to, written by the generator: Tenancy's switch writes the organization and the role, and the classes the module declares win. The written role is the package's whole, and follows its pack when the host syncs | `...Tenants.Domain/Module.cs`, `[assembly: GenerateTenancyClasses]`; `SourceTreeTests`, `MigrationTests`, `RolePackSyncScenarios` |
| A second supporting domain beside Tenancy: a resource's members and the roles a customer keeps for them | `Entities/CrewMember.cs` on `[Member]` and `...Projects.Domain/Aggregates/ProjectRoles/ProjectRole.cs` on `[KeptRole]`, the rules in `...Projects.Application/Access/ProjectMembership.cs` with the starter roles in `Catalogue/SampleCatalogue.cs`, `AddProjectMembershipWithTenancy` and `AddProjectMemberAccess` in `...Projects.Infrastructure/ProjectsInfrastructure.cs`; `CrewMembershipScenarios`, `ProjectRoleScenarios` |
| An entity with entities of its own: crew members, each with dated roles | `...Projects.Domain/Aggregates/Projects/Project.cs`, with `Entities/CrewMember.cs` beside it, the nested `OwnsMany` that `HasMembers` maps in `...Projects.Infrastructure/Persistence/ProjectsContext.cs`; `ProjectCrewTests`, `CrewMembershipScenarios` |
| Row rules that ask a resource's members, for a database that checks rows | `...Projects.Infrastructure/Access/SeatsSeeTheProjectsTheyReach.cs`, which asks the functions the Membership package writes from the projects' rules, marked `[MembershipRules<CrewMember>]` in `Catalogue/SampleCatalogue.cs`; `ProjectRowRulesTests`, `SampleOnPostgresTests` |
| A rule of one module asking another's, through a contract | `ProjectsISee` and `ProjectsWhereIHold` in `...Projects.Contracts/RowAccess/`, a line each that names the project's id and no function, answered by the functions the Membership package writes for the projects under the names `ProjectMembership.Functions` keeps, asked by `...Inspections.Infrastructure/Access/` and by Projects' own rules; `ProjectRowRulesTests`, `InspectionRowRulesTests`, `SampleOnPostgresTests` |
| A login role that owns nothing, forced policies, privileges from the policies | `Tenancy/supabase/migrations/*_login_role.tenancy_api.ddd.sql`, the two properties in `Examples.Tenancy.Exporter.csproj`, which leaves the privileges and the forced policies to the export's defaults, `Host/Storage/SampleStorage.cs`, `Host/Program.cs`, which runs the start-up checks the registrations bring; `SampleOnPostgresTests`, `LoginRoleFileTests`, `PostgresCompositionTests` |
| The roles said once where they differ from the defaults, and the host held to the ones the database's access files were written for | `SupabaseRowAccessRoles` in `Examples.Tenancy.Exporter.csproj`, the token role in `Host/Storage/SampleStorage.cs`, the record at the end of every `*_access.*.ddd.sql`, `supabase.roles-match-access-files` among the checks `Host/Program.cs` runs; `PostgresCompositionTests`, `SampleOnPostgresTests` |
| The export as a build step of a program of its own | `Tenancy/Examples.Tenancy.Exporter`, which references each module's infrastructure project and the catalogue; `PostgresCompositionTests` |
| A module's migrations beside its context, and one factory that `dotnet ef`, the export and the host's start-up check build the context with: the one the build writes beside the context marked `[SupabaseMigrations]` | `...Tenants.Infrastructure/Persistence/Migrations/` and `TenantsContext.cs`; the same in Projects and Inspections; `AddSupabaseMigrations()` in the host's `Program.cs`; `MigrationTests` |
| A rule Postgres holds beyond a module's policies: a project's unit changes only with its keys, by a trigger of the module's own, and its owner and its crew's rows with theirs, by the Membership package's lock | `UnitChangesWithItsKeys` in `...Projects.Infrastructure/Access/`, listed by `Tenancy/Examples.Tenancy.Exporter/Program.cs`, and the lock the Membership package writes by itself from the rules `Catalogue/SampleCatalogue.cs` marks; `SampleOnPostgresTests` |
| Column rules: a column whose command asks a stricter key than changing the row does, a project's name and planned days the key to edit it, its state the key to close it | `NameAndPlanChangeWithTheEditKey` and `StateChangesWithTheCloseKey` in `...Projects.Infrastructure/Access/`, written as triggers into the module's access files; `SampleOnPostgresTests` |
| A trigger that refuses as the toolkit's access guards do, so a save it refuses is `access.refused`, a 403, and not a failure of the server | `Refusal` in `UnitChangesWithItsKeys`, which writes its refusal with `RowAccessModel.Refusal`, as the column rules and the Membership lock write theirs; `SampleOnPostgresTests` |
| Connections per purpose: requests and background | `PostgresPools`, `PostgresPoolBudget` and `ContextsByPurpose` in `Shared/Examples.Hosting`, `ModuleHost.OnPostgres` in `Host/Storage/SampleStorage.cs`, and `host.RequirePostgres()` in each `Add{Module}Infrastructure`; `SampleOnPostgresTests`, `PostgresCompositionTests` |
| Who wrote a row and who changed it, and an access history that only grows | `RecordsWhoChanged` in `ProjectsContext.cs` and `InspectionsContext.cs`, `AddTenancyEventLogTable` in `TenantsContext.cs` with `KeepEventLog(log => log.AddTenancyEventLog())` in `TenantsInfrastructure.cs`, which keeps what changes access with who made the change; `PeopleOfficeScenarios`, `SampleOnPostgresTests`, `MigrationTests` |
| Who changed a row, in an answer | `changedBy` on a project and an inspection: the value object `ChangedBy` in `...Tenants.Contracts/ValueObjects/`, declared once for both modules, read in `EfProjectReads.cs` and `EfInspectionReads.cs`, and one GraphQL type in both schemas, shareable because it is a value object; `WhoChangedScenarios` |
| An operator, who reads across tenants and changes nothing | `Host/Access/OperatorRequirement.cs` and `OperatorRequirementHandler.cs`, `Host/DevLogin/DevOperators.cs`, the feature `Operators` in each module's application and API project, with a route and a GraphQL field for each read, `OperatorsSeeEveryProject.cs` and `OperatorsSeeEveryInspection.cs` in the infrastructure projects' `Access/`; `OperatorScenarios`, `OperatorFieldScenarios`, `TenantProjectsFieldScenarios`, `TenantProjectInspectionsFieldScenarios`, `SampleOnPostgresTests` |
| The access history as a list, paged with `PagingArguments` and `ToPageAsync` | `...Tenants.Application/History/Queries/AccessHistory.cs`, `HistoryAsync` in `...Tenants.Infrastructure/Persistence/EfTenancyReads.cs`, `...Tenants.Api/History/Rest/HistoryEndpoints.cs` and `History/GraphQL/HistoryPagedQueries.cs`, `Ui/Components/Pages/History.razor`; `AccessHistoryScenarios`, `AccessHistoryFieldScenarios` |
| Inviting a person by address: the package's invitations, an account through the identity port, whose id the invitation keeps so it can be mailed again or deleted unused, a seat when they accept | `Invitation` in `...Tenants.Domain/Aggregates/Invitations/`, the feature `Invitations` in `...Tenants.Application` and `...Tenants.Api`, with a route and a GraphQL field for each use case, `AddTenancyInvitations` in `TenantsContext.cs` and `TenantsInfrastructure.cs`, `Host/Auth/SampleIdentityAccounts.cs` and `Host/DevLogin/DevIdentityAccounts.cs`, `Ui/Components/Pages/Invitations.razor` and `AcceptInvitation.razor`, and for the link in Auth's mail `InvitationPage.cs` in the feature, `Sample:Invitations:AcceptPage`, `Ui/Auth/AuthLink.cs` and `Ui/Auth/LinkSignIn.cs`, which asks before a link's sign-in replaces a tab's; `InvitationScenarios`, `LinkSignInTests`, `InvitationFieldScenarios`, `InvitationOverTimeScenarios`, `InvitationWithSupabaseAuthTests`, `InvitedByMailTests` |
| A column that never changes once its row is saved | `IsFixedAfterInsert()` on a project's number in `ProjectsContext.cs` and on an inspection's project in `InspectionsContext.cs`; `MigrationTests` |
| An administrators' pack that lists its keys, and the marks pinned by a test | `Tenancy/Examples.Tenancy.Catalogue/SampleCatalogue.cs`; `AccessAdminScenarios`, `ApplicationRuleScenarios` |
| A role made from a pack follows its pack: a key a module brings later, or a key added to a pack, reaches the tenants' roles at the host's next start, what a tenant changed itself stays, and the change is in the access history | `SyncRolePacks()` after `RunStartupChecks()` in `Host/Program.cs`, the `KeysFromPack` migration in `...Tenants.Infrastructure/Persistence/Migrations/` and its exported `*_KeysFromPack.tenants.ddd.sql`; `RolePackSyncScenarios`, `MigrationTests` |
| Tenancy's use cases named through `TenantsTenancy`, a class the toolkit's generator writes into the Tenants domain project, which declares the classes, so no project of the sample writes the nine types | `TenantsTenancy.SeatCommands` in `...Tenants.Application/Seats/Commands/SuspendTenantSeat.cs`, `TenantsTenancy.KeyReach` in `...Tenants.Api/Seats/GraphQL/KeyOfMineType.cs`, `TenantsTenancy.TenantCommands` in `Host/Seeding/DemoSeeder.cs`; `SourceTreeTests`, which holds every file of the sample and of its tests to closing the use cases nowhere |
| Tenancy's ids named once: system work and the current caller closed over them wherever the Tenants module's classes are seen, the registrations in the module's own infrastructure project, and tenant selection without them | `TenantsTenancy.BeginSystem()` in `Host/Seeding/DemoSeeder.cs`, `TenantsTenancy.CurrentCaller()` in `Host/Access/SeatRequirementHandler.cs`, `ITenantSelection` in `Host/Access/TenantHeader.cs`, `outbox.AddTenancyDomainEvents()` in `TenantsInfrastructure.cs`; the ids written out in `ProjectsInfrastructure.cs`, whose module sees only them, and inferred in `OwnPlaceOnTheCrew.cs`; `SourceTreeTests`, which holds every project that sees the classes to naming no id where a closed form exists |
| A module's permission keys stated once, on a list marked `[TenancyPermissions]`, which the host and the export both find without naming a module | `...Projects.Application/Access/ProjectCatalogue.cs` and `...Inspections.Application/Access/InspectionCatalogue.cs`, `AddTenancyPermissionsOfModules()` in `Host/Program.cs`, and for the export the same marked lists, found by the exporter's build and written into its `DDDToolkit.RowAccessContributionsOfPackages.g.cs` beside the part of the catalogue `Catalogue/SampleCatalogue.cs` marks `[TenancyCatalogue]`; `ModuleKeysTests` |
| A unit's kind, kept by the application on its own unit class since Tenancy keeps none | `UnitKind` and `OrganizationUnit.Kind` in `...Tenants.Domain/Aggregates/Organizations/`, set in the use case's callback by `...Tenants.Application/Organization/Commands/AddOrganizationUnit.cs` and by the seeder's `ConfigureRoot`, stored by `UnitKindKeyConverter`; `GraphQLMutationScenarios`, `RequestBodyTests` |
| A seat's name, kept per tenant by the application on its own seat class since Tenancy keeps none, with a rule of its own, and renamed by a command of its own | `Seat.DisplayName` and its rule `DisplayNameIsValid` in `...Tenants.Domain/Aggregates/Seats/`, set in the use cases' callbacks by the seeder and by `...Tenants.Application/Invitations/Commands/AcceptInvitation.cs`, selected from the seats the directory answers whole into `SeatListing`, the tenant picker's `SeatOfMine` and the overview's `SeatOverviewListing` in `...Tenants.Application/Seats/`, renamed by `Seats/Commands/RenameSeat.cs`; `SeatNameScenarios`, `InvitationScenarios`, `MigrationTests` |
| A field of the application's own on the package's table, guarded in the database by the application: Tenancy holds only its own columns of a seat (id, identity, tenant, status), so a column rule holds the name to the rule of `RenameSeat`, and the job title, with no command, is as writable as the row | `...Tenants.Infrastructure/Access/NameChangesByTheSeatOrWithTheSeatsKey.cs`, written by the exporter into the newest `*_access.tenants.ddd.sql`; `SampleOnPostgresTests` |
| Asking Tenancy inside a module's own query | `...Projects.Application/Access/ProjectAccess.cs`, which asks the projects' rules for a reach, `...Projects.Infrastructure/Persistence/EfProjectReads.cs`, and `AddTenancyReadFunctions` and `ScopeToTenant` in `ProjectsContext.cs` next to it; `AccessStatementTests` counts the statements |
| A write port and a read port in the application, implemented by the infrastructure | `...Projects.Application/StoredProjects/IProjectStore.cs` and `IProjectReads.cs`, `...Projects.Infrastructure/Persistence/EfProjectStore.cs` and `EfProjectReads.cs` |
| A module asking another through a contract | `IProjectGate` in `...Projects.Contracts/Gate/`, asked by `...Inspections.Application/Access/InspectionsAccessCheck.cs` before a handler runs, and by `Recording/Queries/ProjectInspections.cs` for what the caller may do |
| The tenant of a request, from a header and the caller's own seats | `Host/Access/TenantHeader.cs` |
| A seat in that tenant as an authorization policy, answered with the refusal's code | `Host/Access/SamplePolicies.cs`, `SeatRequirement.cs`, `SeatRequirementHandler.cs` and `SeatRefusalResults.cs`; `SeatPolicyScenarios` |
| A paged list that asks Tenancy, paged by GreenDonut's `PagingArguments` and `Page<T>` under REST and GraphQL alike | `...Projects.Application/Overview/Queries/VisibleProjects.cs`, `PageAsync` in `...Projects.Infrastructure/Persistence/EfProjectReads.cs`; `ProjectListScenarios`, `GraphQLProjectScenarios` |
| A marker that is not a cursor of the list it is sent to, refused by one check under every paged read | `ListCursors` in `Tenancy/Shared/Examples.Tenancy.Shared.Infrastructure/Paging/`, called in `EfProjectReads.cs`, `EfInspectionReads.cs` and `EfTenancyReads.cs`; `ListCursorsTests`, `ProjectListScenarios`, `InspectionListScenarios`, `AccessHistoryScenarios` |
| A page asked for from both ends, or in a size that is none of the list's, refused by one check in front of every paged query | `PageSizes` in `Tenancy/Shared/Examples.Tenancy.Shared.Application/Paging/`, called in `VisibleProjects.cs`, `InspectionPages.cs` and `HistoryRefusals.cs`; `PageSizesTests`, `ProjectListScenarios`, `InspectionListScenarios`, `AccessHistoryFieldScenarios` |
| GraphQL types over the application's records, generated data loaders and a permission key on a field, whose rule the query holds so the route answers what the field answers | `...Projects.Api/Overview/GraphQL/ProjectType.cs` and `OverviewDataLoaders.cs`, `Crew/GraphQL/CrewMemberType.cs` and `CrewFieldKeys.cs`, `...Projects.Application/Crew/CrewOverviews.cs`, `...Tenants.Application/Roles/RoleListing.cs`; `GraphQLProjectScenarios`, `CrewRoleScenarios`, `RoleKeysScenarios`, and `GraphQLDeclarationTests` for all three modules |
| Two GraphQL gateways, the user's and the tenant's administration's, the second with a field `/graphql` offers nobody; both endpoints that require authorization, whose schemas a tool reads with a key | `[GraphQLSchema("admin", OperationType.Query)]` on `...Tenants.Api/Seats/GraphQL/SeatsAdminQueries.cs`, both of Tenancy's schemas in `...Tenants.Api/GraphQL/TenantsGraphQL.cs`, the gateways in `Host/GraphQL/SampleGateways.cs` and `Host/Program.cs`, `Host/schema.graphql` and `Host/admin.graphql`; `AdministrationSchemaScenarios`, `GraphQLSchemaTests`, `GraphQLSchemaKeyTests` |
| A page within what a request may cost: HotChocolate's page sizes, and a weight on a field behind a data loader | `[UseConnection]` and `[Cost]` in `...Projects.Api/Overview/GraphQL/OverviewPagedQueries.cs`, `OverviewQueries.cs` and `ProjectType.cs`, `...Inspections.Api/Recording/GraphQL/ProjectType.cs` and `InspectionsConnection.cs`, `...Tenants.Api/Directory/GraphQL/DirectoryQueries.cs`; `GraphQLProjectScenarios`, `GraphQLDeclarationTests` |
| The keys a caller holds on a page of projects, in one statement | `...Projects.Application/Access/Queries/KeysOnProjects.cs` and `KeysHeldAtRoot.cs`; `KeySetScenarios` |
| A version a client sends back with a change | `ETag` and `If-Match` in `...Projects.Api/Rest/ProjectVersions.cs`, compared by the Membership package's check, `MemberAccessCheck`; `VersionScenarios` |
| A browser client on another origin | `Host/Requests/BrowserCors.cs`; `CorsTests` |
| Coded refusals as problem+json | `Host/Requests/RefusalProblems.cs` |
| Refusals and pages in English and Dutch, by the request's `Accept-Language` | `...Projects.Domain/Aggregates/Projects/ProjectFailures.resx` and `.nl.resx` beside the refusals, added to the localizer with the module (`...Projects.Application/ProjectsApplicationServices.cs`); `Host/Languages/RequestLanguages.cs`; `Ui/Languages/UiTexts.cs`, and the `en` and `nl` switch in the UI's top bar; `LanguageScenarios`, `TranslationTests` |
| Start-up checks: the catalogue, every module's access behavior, the wiring of every context, the migrations, the login role and the second lock, brought by the packages' registrations and run by one call, with no class of the sample's own | `Host/Program.cs` (`RunStartupChecks`), `...Projects.Infrastructure/ProjectsInfrastructure.cs` (`AddMembershipPostgres`); `StartupTests`, `SampleOnPostgresTests`, `SampleWithoutDatabaseTests` |
| A module's entry in its API project, and its registration in the infrastructure project | `...Tenants.Api/TenantsModule.cs`, `...Tenants.Infrastructure/TenantsInfrastructure.cs` |
| A command or a query per use case, with its feature, and routes that only send | `...Projects.Application/Crew/Commands/` and `Crew/Queries/`, `...Projects.Api/Crew/Rest/CrewEndpoints.cs`; the same for every feature, in Inspections, and in Tenants, wrapping the package's use cases; `FeatureFolderTests` |
| What a request requires of its caller, checked in the pipeline by a behavior the toolkit generates | `[AccessRequests]` on `...Projects.Application/Access/IProjectsRequest.cs`, the module's own case and its check in `ProjectsRequirement.cs` and `ProjectsAccessCheck.cs`, `AddAccessCheck` and `AddProjectsAccessBehavior` in `ProjectsApplicationServices.cs`, `AddTenancyAccess` and `AddProjectMemberAccess` in `...Projects.Infrastructure/ProjectsInfrastructure.cs`; the same in `...Tenants.Application/Access/` and `...Inspections.Application/Access/`; `AccessDeclarationTests`, and `AccessBehaviorRegistrationTests` for the start-up check that every module's behavior is in the pipeline |
| A handler that takes nothing from its check: it loads what its command names, with the version its caller named held at the load, and the save, the project's rules and the database hold the write | `...Projects.Application/Lifecycle/Commands/CloseProject.cs`, `IProjectStore.LoadAsync` and `...Projects.Infrastructure/Persistence/EfProjectStore.cs`; `AccessHoldScenarios`, `RequestPipelineTests` |
| The expert hold, which ties every save of a project to the version its request's check read with one line, `UseMemberHolds`: the sample ships without it, and its tests switch it on and play the races both ways | `HoldsOnProjects` in the tests' infrastructure; `AccessHoldScenarios` |
| A save the database refuses after the caller's rights changed, told from a rule C# and the policies hold differently by asking the request in hand's check again: an information line, not a warning | the comment on the log filter in `Host/Program.cs`, with nothing written for it; `RequestPipelineTests` (a key taken from a crew role between the check and the save), `SampleOnPostgresTests` (an owner named by a seat that lost the key, refused by a guard) |
| A query on a context of its own, so queries can run side by side | `...Projects.Application/StoredProjects/IProjectReads.cs`, `...Tenants.Application/StoredTenancy/ITenancyReads.cs`, `...Inspections.Application/Recording/IInspectionReads.cs` and their adapters; `RequestPipelineTests` |
| One call per context: `UseDDDToolkit` adds the toolkit's interceptors, then what the host's registrations brought, row level security on Postgres and Tenancy's save check last, and the log says once per context what it was given | `.UseDDDToolkit(application)` in each module's infrastructure registration, such as `...Projects.Infrastructure/ProjectsInfrastructure.cs`, with `AddSupabaseRowLevelSecurity` in `Host/Storage/SampleStorage.cs` and `AddTenancy` in `...Tenants.Infrastructure/TenantsInfrastructure.cs`; `StartupTests` |
| Contexts from a pool: a read's for its one query, and the request's own | `PostgresPools.AddContext` in `Shared/Examples.Hosting`, called by each module's infrastructure registration; `StartupTests`, `PooledContextScenarios` |
| A domain laid out per aggregate, and one type per file | `...Projects.Domain/Aggregates/Projects/` with `Entities/`, `Events/`, `Invariants/` and `ValueObjects/`; `SourceTreeTests` |
| The mediator and the first step of every request's pipeline | `Host/Program.cs`, `Host/Requests/RequestTracingBehavior.cs`, registered by `RequestTracingServices.cs` |
| A dev login that issues Supabase access tokens, and the guard that keeps it local | `Host/DevLogin/`, `Host/Auth/DevLoginGuard.cs` |
| The real login beside the dev login: Supabase Auth's own sign-in, and the demonstration people as its users | `Tenancy/Examples.Tenancy.Ui/Auth/SupabaseLoginClient.cs` and `Components/Shared/PasswordLogin.razor`, `Host/Seeding/DemoAuthUsers.cs`, `Tenancy/Examples.Tenancy.AppHost/Program.cs`; `SupabaseLoginClientTests`, `DemoAuthUsersTests`, `SampleOnSupabaseTests` |
| Two kinds of token under one issuer: the dev login's, signed with the secret, and Auth's, signed with a key it publishes | The toolkit's bearer scheme, registered in `Host/Auth/SampleAuthentication.cs`; the signing key the images' Auth is given, `Tenancy/Examples.Tenancy.AppHost/AuthSigningKeys.cs`; `PublishedKeyTokenTests`, `SupabaseStackTests`, `SampleOnSupabaseTests`, `SampleOnTheCliStackTests` |
| The sample on the stack the Supabase CLI starts | `Tenancy/supabase/config.toml`; `SampleOnTheCliStackTests` on its fixture `SupabaseCliStack`, and `Tenancy.SupabaseCli` in `.github/workflows/SampleTests.yml` |
| Seeding as system work in a tenant | `Host/Seeding/DemoSeeder.cs` |
| A UI that only speaks HTTP, and never retries | `Tenancy/Examples.Tenancy.Ui/Api/SampleApi.cs` |
| Answers that carry ids, and names asked of their owner by id | `...Projects.Application/Overview/ProjectOverview.cs`, `...Tenants.Application/Directory/Queries/SeatsById.cs`, `OrganizationUnitsById.cs` and `RolesById.cs`, `...Tenants.Api/Directory/Rest/DirectoryEndpoints.cs`; `StrictAnswersTests`, `DirectoryScenarios` |
| A model check: a module maps no more of Tenancy than access facts | `TenancyModel.ReadsBeyondAccessFacts`; `ModuleModelTests` |
| A screen that resolves the names of the ids it was answered | `Tenancy/Examples.Tenancy.Ui/Api/DirectoryNames.cs`, `Components/Pages/MyProjects.razor` and `ProjectDetail.razor`; `DirectoryNamesTests` |

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
