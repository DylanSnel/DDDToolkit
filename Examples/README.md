# Examples

## The one to read: `ModularMonolith/`

A modular monolith is the architecture this toolkit is aimed at, and it is the only honest way to show
integration events: two modules in one process, each owning its own aggregates, its own database and
its own contract.

```
ModularMonolith/
  Ordering/
    DDDToolkit.Examples.Ordering.Contracts   the published surface: OrderId and OrderPlacedV1
    DDDToolkit.Examples.Ordering             the aggregate, the value object, the domain event, the context,
                                             and OrderingModule: everything Ordering registers
      Invariants/                            a file per named rule, each another part of the entity
      Migrations/                            Ordering's Entity Framework migrations, for Postgres
  Shipping/
    DDDToolkit.Examples.Shipping             consumes Ordering's contract and nothing else; ShippingModule
                                             registers it as a consumer
      Migrations/                            Shipping's Entity Framework migrations, for Postgres
  DDDToolkit.Examples.Host                   switches the modules on, four HTTP endpoints; its build exports
  supabase/
    config.toml                              a local Supabase project, from supabase init
    migrations/                              both modules' migrations, written by the host's build
```

What happens when you place an order:

1. `Order` is constructed with a validated address and at least one line, and raises `OrderPlaced`.
2. `SaveChanges` writes the order, its lines and one outbox row in one transaction.
3. A second later the background service reads the row, converts the domain event into
   `OrderPlacedV1`, and hands it to the module sink.
4. `BookShipment` in Shipping receives the contract, writes a `Shipment`, and the inbox writes the row
   that says it applied that message. Both in one save, in one transaction.
5. Deliver it twice and step 4 happens once.

Each module registers itself. `AddOrderingModule` registers Ordering's context, its outbox and what it
publishes, and says the messages go to the other modules without naming any. `AddShippingModule`
registers Shipping's context and signs it up as a consumer of `OrderPlacedV1`, under its own inbox. The
host calls the two and sets the one process-wide thing, publishing domain events through Mediator.

Shipping references `DDDToolkit.Examples.Ordering.Contracts` and never `DDDToolkit.Examples.Ordering`.
Its project file turns [DDD00022 and DDD00023](../docs/modules.md) into build errors, so that stays
true: the build breaks the day somebody names a type Ordering did not publish, or stores an entity
Ordering owns.

### Running it

```bash
dotnet run --project Examples/ModularMonolith/DDDToolkit.Examples.Host
```

Then work through `DDDToolkit.Examples.Host.http` from the top. It refuses a bad address, refuses an
order whose line names no SKU, places a good one, shows the shipment Shipping booked from it, and then
refuses two amendments: one that breaks a rule of the line, and one that breaks a rule of the order.
Every refusal is a 422 carrying the code of the rule that broke, which is what a rule with a name of
its own buys you, and each of them is one question asked of the order: the root is the consistency
boundary, so it answers for its lines as well. No `DbContext` is involved in the asking.

This runs on SQLite, one file per module next to the built binary. Each module creates its own file from
its model on start-up, before the outbox poller starts. SQLite has no schemas, so the `ordering`,
`shipping` and `ddd` schemas are dropped there, and the modules turn off the warning that would say so on
every start.

### On Supabase

The same host runs on Postgres when it is given `ConnectionStrings:Supabase`. The modules then share
one database, as they would on one Supabase project, each in its own schema with its own migration
history. The migrations are Supabase's to apply: each module registers its migrations, and the host
checks over all of them that none is pending and refuses to start otherwise.

```bash
cd Examples/ModularMonolith
supabase start          # a local Supabase in Docker; applies supabase/migrations
dotnet run --project DDDToolkit.Examples.Host --launch-profile supabase
```

The `supabase` launch profile points at the local database on port 54322. After changing a model,
scaffold the migration in the module that owns it, and build:

```bash
dotnet ef migrations add AddGiftWrap --project Ordering/DDDToolkit.Examples.Ordering --startup-project DDDToolkit.Examples.Host --output-dir Migrations
dotnet build DDDToolkit.Examples.Host   # writes 2026…_AddGiftWrap.ordering.ddd.sql
supabase migration up                   # or: supabase db reset, to start over
```

There is no export step to remember. Each module's design-time factory is marked `[SupabaseMigrations]`,
and the host's project file sets `SupabaseMigrationsExport`: `Write` locally, so every build writes the
files a new migration needs, and `Check` in CI, so a pull request that adds a migration without its file
fails. The files are committed, because Supabase branching reads them from the repository. See
[Entity Framework → Supabase](../docs/entity-framework.md#supabase) for what the build writes and how.

### What each feature is shown by

| Feature | Where |
|---|---|
| Module declaration and the boundary rules | `Ordering/*/Module.cs`, `Shipping/*/Module.cs`, `DDDToolkit.Examples.Shipping.csproj` |
| Published contracts | `Ordering/DDDToolkit.Examples.Ordering.Contracts/OrderingContracts.cs` |
| An explicitly declared identifier, and why | the same file |
| Generated identifiers, and why | `Ordering/.../OrderLine.cs`, `Shipping/.../Shipment.cs` |
| A named `IInvariant<T>`, and what earned it a name | `Ordering/.../Invariants/MustHaveLines.cs` |
| A rule that stayed a `CheckInvariants()` seam, and why | `Ordering/.../Order.cs` |
| A child entity's own invariant, reported by its root | `Ordering/.../Invariants/MustNameASku.cs` |
| Asking an aggregate what is broken, before any save | `Host/Endpoints.cs`, the `Broken` helper |
| Branching on a violation's code, and naming the child it came from | the same helper |
| Generated read-only collections | `Ordering/.../Order.cs` |
| A value object, and `TryToValid` at a boundary | `Ordering/.../Address.cs`, `Host/Endpoints.cs` |
| Domain events, stable names, Mediator dispatch | `Ordering/.../OrderPlaced.cs`, `OrderPlacedLog.cs` |
| A module registering itself, and a host that only switches modules on | `Ordering/.../OrderingModule.cs`, `Shipping/.../ShippingModule.cs`, `Host/Program.cs` |
| The outbox per module, the published contract, the module sink | `Ordering/.../OrderingModule.cs` |
| A consuming module, its inbox and an idempotent consumer | `Shipping/.../ShippingModule.cs`, `BookShipment.cs`, `ShippingContext.cs` |
| Conventions, generated converters, outbox, inbox | `Ordering/.../OrderingContext.cs`, `Shipping/.../ShippingContext.cs` |
| Optimistic concurrency as a 409 | `Host/Endpoints.cs` |
| A schema and a migration history per module in one database | `OrderingContext.cs`, `ShippingContext.cs` |
| Entity Framework migrations applied by Supabase | `supabase/migrations`, `[SupabaseMigrations]` on each module's factory, the host's `.csproj` (the export), `Host/Program.cs` and each `*Module.cs` (the start-up check) |
| The testing kit and `DomainEventClock` | `Tests/DDDToolkit.Examples.Tests` |

What it does not show: GraphQL, Newtonsoft, FluentValidation validators, pgmq, and upcasting an older
payload. Those have runnable coverage in `Tests/` and a page each in [docs](../docs).

## `DDDToolkit.ExampleApi` and `DDDToolkit.ExampleLibrary`

The example that predates the one above. They survive because four test projects use their types as
fixtures: `DDDToolkit.Tests`, `DDDToolkit.EntityFramework.Tests`,
`DDDToolkit.EntityFramework.Providers.Tests` and `DDDToolkit.HotChocolate.Tests` all reference
`EmailAddress`, `PersonName`, `UserId` and `ExampleContext`. Deleting them is therefore a change to the
test suite, not to this folder.

They still work, and `ExampleLibrary` is the only place FluentValidation validators and the
HotChocolate attributes are demonstrated. Read `ModularMonolith/` first.

## `DDDToolkit.NugetApi`

Deliberately outside the solution, and the only project here that consumes the toolkit as packages
rather than as project references. It names every generated member it expects, including all four
members of the invariant check, a nested rule on the root and one on a child, so a generator that did
not arrive fails the build. See the
[README](DDDToolkit.NugetApi/README.md) in that folder.
