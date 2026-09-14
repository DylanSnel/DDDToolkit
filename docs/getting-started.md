# Getting started

Every piece of code on this page is taken from a project that builds and runs:
[`Examples/ModularMonolith`](../Examples/ModularMonolith). It is two modules, `Ordering` and
`Shipping`, in one host, and it is small enough to read in a sitting. Paths in this page point at the
real file, so you can go and look at the rest of it.

## Install

```bash
dotnet add package DDDToolkit
```

`DDDToolkit` brings the base types and the core generators. Add an integration package for each
technology you use; each one carries its own generator and needs no further registration.

```bash
dotnet add package DDDToolkit.EntityFramework
dotnet add package DDDToolkit.Testing
```

The libraries target .NET 10. The generators target `netstandard2.0` and reference nothing at run
time, so they load in any recent SDK without version conflicts.

## Name your module

Several generators emit one registration method per project, and they need a name for it. Set
`DDD_Module` in the project file:

```xml
<PropertyGroup>
  <DDD_Module>Ordering</DDD_Module>
</PropertyGroup>
```

That produces `AddOrderingConverters` for Entity Framework and `AddOrderingGraphQlRuntimeBindings`
for GraphQL. Without it the generators fall back to the assembly name with the dots removed, which
works but reads poorly at the call site. The property is made visible to the compiler by a props file
inside the `DDDToolkit` package, so setting it is all you have to do.

This is a naming knob and nothing more. Saying that an assembly is a *module*, with a boundary
something checks, is a separate declaration; see [below](#draw-the-module-boundary).

## Declare an identifier

```csharp
[ModuleContract]
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;
```

*[`Ordering.Contracts/OrderingContracts.cs`](../Examples/ModularMonolith/Ordering/DDDToolkit.Examples.Ordering.Contracts/OrderingContracts.cs)*

Two rules: the type must be `partial` so the generator can add to it, and it must be a record. Use
`readonly partial record struct` unless you have a reason not to; it costs no allocation and the
generator gives it a complete identifier surface, including `Parse`, `TryParse` and `IParsable<T>`.

Most identifiers do not need a declaration at all. Name the raw value on the entity and the toolkit
generates the identifier with it:

```csharp
[Entity<Guid>("LINE")]
public partial class OrderLine    // also generates OrderLineId
```

*[`Ordering/OrderLine.cs`](../Examples/ModularMonolith/Ordering/DDDToolkit.Examples.Ordering/OrderLine.cs)*

Use the short form for the identifier nobody outside the aggregate mentions, and the explicit form for
the identifier everybody does. `OrderId` is written out because Shipping stores one, the HTTP API
parses one and the integration event carries one, so it deserves a file to navigate to.
See [Identifiers](identifiers.md).

## Declare an aggregate

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public Order(OrderId id, ValidAddress shipTo, IEnumerable<OrderLine> lines) : base(id)
    {
        ShipTo = shipTo;
        _lines.AddRange(lines);

        RaiseDomainEvent(new OrderPlaced(id, shipTo, _lines.Count));
    }

    public Address ShipTo { get; private set; }

    public partial IReadOnlyList<OrderLine> Lines { get; }

    public void AddLine(string sku, int quantity)
        => _lines.Add(new OrderLine(OrderLineId.CreateSequential(), sku, quantity));
}
```

*[`Ordering/Order.cs`](../Examples/ModularMonolith/Ordering/DDDToolkit.Examples.Ordering/Order.cs)*

The generator supplies the `AggregateRoot<OrderId>` base class, a constructor for your persistence
framework, a private `_lines` list, the read-only `Lines` implementation, and a `Version` for
optimistic concurrency. `RaiseDomainEvent` is protected, so nothing outside the aggregate can put an
event into it.

`Lines` is declared `partial` and get-only. That is the contract: you describe the property you want,
the generator writes the field and the body. Declaring a setter is an error
([DDD00020](diagnostics.md#ddd00020)).

## State an invariant

An invariant is a rule about a whole aggregate that must hold every time anyone can look at it. A
one-liner goes in the generated seam:

```csharp
partial void CheckInvariants()
{
    if (Lines.Select(line => line.Sku).Distinct().Count() != Lines.Count)
    {
        throw InvariantViolation("An order may not name the same SKU on two lines.");
    }
}
```

*[`Ordering/Order.cs`](../Examples/ModularMonolith/Ordering/DDDToolkit.Examples.Ordering/Order.cs)*

A rule that deserves a name, or a code a caller can branch on, becomes a type of its own, nested
inside the entity it is about so that it can read private state and so the generator can find it:

```csharp
public partial class Order
{
    public sealed class MustHaveLines : IInvariant<Order>
    {
        public string Code => "ORDER_HAS_NO_LINES";

        public string? Check(Order order)
            => order.Lines.Count == 0 ? "An order must have at least one line." : null;
    }
}
```

*[`Ordering/Invariants/MustHaveLines.cs`](../Examples/ModularMonolith/Ordering/DDDToolkit.Examples.Ordering/Invariants/MustHaveLines.cs)*

An interceptor runs both before every save that writes the entity, child entities included, so they
are a guarantee rather than a check somebody remembered to call. `GetInvariantViolations()` asks the
same question without throwing, for the moment before the save where "not consistent yet" is an
answer you want to handle, and asking the root answers for the whole aggregate: its own rules and
every line's, each violation naming the entity that reported it. An entity that states nothing pays
nothing: the compiler erases an unimplemented `partial void` and every call to it. See
[Invariants](invariants.md).

## Declare a value object

```csharp
[ValueObject]
public partial record Address
{
    public Address(string street, string city, string postalCode)
        => (Street, City, PostalCode) = (street, city, postalCode);

    [JsonInclude] public string Street { get; protected init; }
    [JsonInclude] public string City { get; protected init; }
    [JsonInclude] public string PostalCode { get; protected init; }

    protected override void Validate(ValidationErrorBuilder errors)
    {
        if (string.IsNullOrWhiteSpace(Street))
        {
            errors.Add("A street is required.", nameof(Street), "Required", Street);
        }
        // ...
    }
}
```

*[`Ordering/Address.cs`](../Examples/ModularMonolith/Ordering/DDDToolkit.Examples.Ordering/Address.cs)*

Equality is generated across the properties. Setters must be `protected init`, which stops callers
using `with` to produce an invalid copy ([DDD00010](diagnostics.md#ddd00010),
[DDD00011](diagnostics.md#ddd00011)).

Every value object also gets an always-valid twin, `ValidAddress`. Notice that `Order` takes one in its
constructor: the signature is the check, so the aggregate never re-validates an address. Getting one
is where the validation happens.

## Refuse bad input without throwing

`ToValid()` throws, which is right when an invalid value is a bug. At an API boundary it is an ordinary
answer to an ordinary request, so use `TryToValid` and hand back the failures:

```csharp
app.MapPost("/orders", async (PlaceOrder body, OrderingContext orders, CancellationToken cancellationToken) =>
{
    var errors = new List<ValidationError>();

    if (!new Address(body.Street, body.City, body.PostalCode).TryToValid(out var shipTo, out var addressErrors))
    {
        errors.AddRange(addressErrors.Prefixed("shipTo"));
    }

    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors.ToErrorDictionary());
    }
    // ...
});
```

*[`Host/Endpoints.cs`](../Examples/ModularMonolith/DDDToolkit.Examples.Host/Endpoints.cs)*

The caller gets a 400 it can read field by field, with `shipTo.Street` and `shipTo.PostalCode` naming
the fields they filled in. Nothing was thrown. See
[Failure handling](value-objects.md#failure-handling).

## Wire up Entity Framework

Three calls. One on the `DbContextOptionsBuilder`, and two kinds in `ConfigureConventions`:

```csharp
public sealed class OrderingContext(DbContextOptions<OrderingContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.AddDomainEventOutbox();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddOrderingContractsConverters();
        configurationBuilder.AddOrderingConverters();
    }
}
```

*[`Ordering/OrderingContext.cs`](../Examples/ModularMonolith/Ordering/DDDToolkit.Examples.Ordering/OrderingContext.cs)*

`AddDDDToolkitConventions` is the same in every context. `Add{Module}Converters` is generated once per
assembly that declares identifiers or single value objects, so call one per assembly: this context maps
`OrderId` from the contracts project and `OrderLineId` from the domain project, hence two.

There is no configuration for the domain model itself. `OrderLine` is owned because `[Entity]`
generated `[Owned]`, `Lines` is discovered through the generated backing field, `Address` is stored
inline because `[ValueObject]` generated `[ComplexType]`, and `Version` is a concurrency token.

```csharp
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

builder.Services.AddDbContext<OrderingContext>((services, options) => options
    .UseSqlite(connectionString)
    .UseDDDToolkit(services));
```

Pass the provider the `AddDbContext` callback gives you, not the root provider: it belongs to the same
scope as the context, so a handler that injects `OrderingContext` receives the very instance that is
saving. See [Entity Framework](entity-framework.md).

## Handle a concurrency conflict

`Version` is incremented on every save that touches the aggregate and checked in the `WHERE` clause, so
a stale write becomes a `ConcurrencyConflictException` naming the aggregate:

```csharp
try
{
    await orders.SaveChangesAsync(cancellationToken);
}
catch (ConcurrencyConflictException conflict)
{
    return Results.Conflict(conflict.Message);
}
```

*[`Host/Endpoints.cs`](../Examples/ModularMonolith/DDDToolkit.Examples.Host/Endpoints.cs)*

There is no safe generic answer for that catch block, which is why the toolkit does not retry for you.

## Draw the module boundary

One assembly, one module:

```csharp
[assembly: Module("Ordering")]
```

*[`Ordering/Module.cs`](../Examples/ModularMonolith/Ordering/DDDToolkit.Examples.Ordering/Module.cs)*

Nothing happens until a second assembly says it is a module too. From then on, everything an assembly
declares is its own business unless it is marked `[ModuleContract]` or is an integration event, and the
analyzer reports another module naming an unpublished type ([DDD00022](diagnostics.md#ddd00022)) or
storing another module's entity ([DDD00023](diagnostics.md#ddd00023)).

Both are warnings, so that a codebase adopting modules can see the list before it has to fix it. Once
the list is empty, hold it:

```xml
<WarningsAsErrors>$(WarningsAsErrors);DDD00022;DDD00023</WarningsAsErrors>
```

*[`DDDToolkit.Examples.Shipping.csproj`](../Examples/ModularMonolith/Shipping/DDDToolkit.Examples.Shipping/DDDToolkit.Examples.Shipping.csproj)*

See [Modules](modules.md).

## Tell another module something happened

Publish a contract. It is a separate record from the domain event, so the two can change at different
speeds:

```csharp
[IntegrationEvent("ordering.order-placed", Version = 1)]
public sealed record OrderPlacedV1(OrderId OrderId, string City, string PostalCode, int LineCount);
```

*[`Ordering.Contracts/OrderingContracts.cs`](../Examples/ModularMonolith/Ordering/DDDToolkit.Examples.Ordering.Contracts/OrderingContracts.cs)*

Say how the one becomes the other, and where it goes:

```csharp
builder.Services.AddDDDToolkitEntityFramework(options =>
{
    options.MapIntegrationEvents(contracts => contracts.RegisterFromAssemblyContaining<OrderPlacedV1>());
    options.DispatchWithMediator();

    options.UseOutbox(outbox =>
    {
        outbox.RegisterEventsFromAssemblyContaining<Order>();
        outbox.PublishAs<OrderPlaced, OrderPlacedV1>(placed => new OrderPlacedV1(
            placed.OrderId, placed.ShipTo.City, placed.ShipTo.PostalCode, placed.LineCount));
        outbox.SendToModules<ShippingContext>();
        outbox.AlsoDispatchInProcess = true;
    });
});

builder.Services.AddModuleIntegrationEvents<ShippingContext>();
builder.Services.AddIntegrationEventHandler<OrderPlacedV1, BookShipment>();
builder.Services.AddOutboxBackgroundService<OrderingContext>(pollingInterval: TimeSpan.FromSeconds(1));
```

*[`Host/Program.cs`](../Examples/ModularMonolith/DDDToolkit.Examples.Host/Program.cs)*

`SaveChanges` now writes the order and one outbox row in one transaction. The background service reads
the row afterwards, converts it, and hands it to the other modules. Ordering has already committed by
then, which is why a failing consumer cannot refuse an order.

## Consume it once

```csharp
[IntegrationEventConsumer("shipping.booker")]
public sealed class BookShipment(ShippingContext context) : IIntegrationEventHandler<OrderPlacedV1>
{
    public Task HandleAsync(OrderPlacedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        context.Shipments.Add(new Shipment(
            ShipmentId.CreateSequential(), contract.OrderId, $"{contract.PostalCode}, {contract.City}", message.OccurredAt));

        return Task.CompletedTask;
    }
}
```

*[`Shipping/BookShipment.cs`](../Examples/ModularMonolith/Shipping/DDDToolkit.Examples.Shipping/BookShipment.cs)*

Three things there are the point. It is typed on the contract, never on Ordering's domain event, which
is what keeps Shipping free of a reference to Ordering's domain. It does not call `SaveChanges`: the
sink runs it inside the inbox, so the shipment and the row that says this consumer applied this message
are written by one save in one transaction. And the consumer name is what the inbox keys on, so
delivery twice does the work once.

Map the table in the consuming context:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddDomainEventInbox();
```

*[`Shipping/ShippingContext.cs`](../Examples/ModularMonolith/Shipping/DDDToolkit.Examples.Shipping/ShippingContext.cs)*

See [Integration events](integration-events.md).

## Test the aggregate

`DDDToolkit.Testing` acts on an aggregate and asserts on what it raised, with no database anywhere:

```csharp
AggregateScenario.Given(Place())
    .WhenThrows<ArgumentOutOfRangeException>(order => order.AddLine("MUG", 0));
```

`WhenThrows` asserts both halves: the exception came out, and nothing was raised on the way out.

The aggregate is what calls `new OrderPlaced(...)`, so a test cannot pass an initialiser for the
timestamp. `DomainEventClock` replaces the clock the event reads, for the current asynchronous flow
only:

```csharp
var moment = new DateTimeOffset(2026, 9, 14, 9, 30, 0, TimeSpan.Zero);
using var scope = DomainEventClock.Use(new FixedClock(moment));

var order = Place();

order.PendingEvents().Should().OnlyContain(raised => raised.OccurredAt == moment);
```

*[`Tests/DDDToolkit.Examples.Tests/OrderTests.cs`](../Tests/DDDToolkit.Examples.Tests/OrderTests.cs)*

See [Testing](testing.md).

## See the generated code

Nothing here is magic, and reading the output is the fastest way to understand it:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(MSBuildProjectDirectory)\Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Build, then look in `Generated/`. Add that folder to `.gitignore`.

## When something does not generate

Every misuse reports an error with an identifier starting `DDD`. If a type you annotated produced no
code, check the build output first: the generator tells you what is wrong and which line to fix. See
[Diagnostics](diagnostics.md) for the full list.

## Run the example

```bash
dotnet run --project Examples/ModularMonolith/DDDToolkit.Examples.Host
```

Then work through
[`DDDToolkit.Examples.Host.http`](../Examples/ModularMonolith/DDDToolkit.Examples.Host/DDDToolkit.Examples.Host.http)
from the top. [`Examples/README.md`](../Examples/README.md) is the map of the folder and says which
file shows what.
