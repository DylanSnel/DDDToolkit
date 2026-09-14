# Examples

## The one to read: `ModularMonolith/`

A modular monolith is the architecture this toolkit is aimed at, and it is the only honest way to show
integration events: two modules in one process, each owning its own aggregates, its own database and
its own contract.

```
ModularMonolith/
  Ordering/
    DDDToolkit.Examples.Ordering.Contracts   the published surface: OrderId and OrderPlacedV1
    DDDToolkit.Examples.Ordering             the aggregate, the value object, the domain event, the context
  Shipping/
    DDDToolkit.Examples.Shipping             consumes Ordering's contract and nothing else
  DDDToolkit.Examples.Host                   the composition root and four HTTP endpoints
```

What happens when you place an order:

1. `Order` is constructed with a validated address and at least one line, and raises `OrderPlaced`.
2. `SaveChanges` writes the order, its lines and one outbox row in one transaction.
3. A second later the background service reads the row, converts the domain event into
   `OrderPlacedV1`, and hands it to the module sink.
4. `BookShipment` in Shipping receives the contract, writes a `Shipment`, and the inbox writes the row
   that says it applied that message. Both in one save, in one transaction.
5. Deliver it twice and step 4 happens once.

Shipping references `DDDToolkit.Examples.Ordering.Contracts` and never `DDDToolkit.Examples.Ordering`.
Its project file turns [DDD00022 and DDD00023](../docs/modules.md) into build errors, so that stays
true: the build breaks the day somebody names a type Ordering did not publish, or stores an entity
Ordering owns.

### Running it

```bash
dotnet run --project Examples/ModularMonolith/DDDToolkit.Examples.Host
```

Then work through `DDDToolkit.Examples.Host.http` from the top. It refuses a bad address, places an
order, and shows the shipment Shipping booked from it.

Two warnings on start-up are expected and harmless: SQLite has no schemas, so the `ddd` schema the
outbox and the inbox ask for is dropped. On SQL Server or Postgres you get the schema.

### What each feature is shown by

| Feature | Where |
|---|---|
| Module declaration and the boundary rules | `Ordering/*/Module.cs`, `Shipping/*/Module.cs`, `DDDToolkit.Examples.Shipping.csproj` |
| Published contracts | `Ordering/DDDToolkit.Examples.Ordering.Contracts/OrderingContracts.cs` |
| An explicitly declared identifier, and why | the same file |
| Generated identifiers, and why | `Ordering/.../OrderLine.cs`, `Shipping/.../Shipment.cs` |
| A real invariant | `Ordering/.../Order.cs` |
| Generated read-only collections | the same file |
| A value object, and `TryToValid` at a boundary | `Ordering/.../Address.cs`, `Host/Endpoints.cs` |
| Domain events, stable names, Mediator dispatch | `Ordering/.../OrderPlaced.cs`, `OrderPlacedLog.cs` |
| The outbox, the published contract, the module sink | `Host/Program.cs` |
| The inbox and an idempotent consumer | `Shipping/.../BookShipment.cs`, `ShippingContext.cs` |
| Conventions, generated converters, outbox, inbox | `Ordering/.../OrderingContext.cs`, `Shipping/.../ShippingContext.cs` |
| Optimistic concurrency as a 409 | `Host/Endpoints.cs` |
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

Deliberately outside the solution, pinned to the published 2.x packages. See the
[README](DDDToolkit.NugetApi/README.md) in that folder.
