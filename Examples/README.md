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
way to run it. `ModularMonolith.Supabase/` is the host that runs all five in one process; more hosts
over the same modules follow.

```
Modules/
  SharedKernel/        Money: the one type every module means the same thing by. Not a module.
  Catalog/
    DDDToolkit.Examples.Catalog.Contracts    what Catalog publishes
    DDDToolkit.Examples.Catalog              the module
  Ordering/ Inventory/ Payments/ Shipping/   the same shape
ModularMonolith.Supabase/
  DDDToolkit.Examples.Host   switches the five modules on and maps their endpoints; its build exports
  supabase/
    config.toml              a local Supabase project, from supabase init
    migrations/              every module's migrations, written by the host's build
```

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

```bash
cd Examples/ModularMonolith.Supabase
supabase start          # a local Supabase in Docker; applies supabase/migrations
dotnet run --project DDDToolkit.Examples.Host --launch-profile supabase
```

The `supabase` launch profile points at the local database on port 54322. After changing a model,
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
| A module registering itself, a host that only switches modules on | each `*Module.cs`, `ModularMonolith.Supabase/.../Program.cs` |
| A schema, a migration history, an outbox and an inbox per module in one database | each `Infrastructure/Persistence/*Context.cs` |
| Entity Framework migrations applied by Supabase | `supabase/migrations`, `[SupabaseMigrations]` on each factory, the host's `.csproj` |
| The testing kit and `DomainEventClock` | `Tests/DDDToolkit.Examples.Tests` |

There are no repositories. A module's `DbContext` is its repository and unit of work, used directly by
the endpoints and the policies. The toolkit has no repository abstraction to show, and a wrapper
around a `DbContext` in a sample would only hide what the toolkit does to it.

What it does not show: GraphQL, Newtonsoft, FluentValidation validators, pgmq, and upcasting an older
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
