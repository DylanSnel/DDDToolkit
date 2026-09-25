# Entity Framework

`DDDToolkit.EntityFramework` is the persistence half of the toolkit. It teaches Entity Framework Core
about the types the generators produce, so a domain model written the way the other pages describe
maps to tables without hand-written configuration. It also carries the behaviours that need a save to
hang off: domain event delivery, the invariant check and optimistic concurrency.

The package does five things:

| | What it gives you |
|---|---|
| Generated converters | A `ValueConverter` per identifier and single value object, and one registration call per assembly |
| Conventions | Read-only collections mapped, `Version` made a concurrency token, `[Internal]` members ignored |
| Domain event delivery | In-process dispatch during `SaveChanges`, or a transactional outbox |
| Invariants at the save | Every entity a save adds or changes is asked for its invariants first; see [Invariants](invariants.md#at-the-save) |
| Optimistic concurrency | `Version` incremented per save, stale writes turned into `ConcurrencyConflictException` |

It does not give you a repository abstraction or a message bus. `DbContext` is already the unit of
work, and the delegate that hands events to your publisher is one you write. If your publisher is
[Mediator](https://github.com/martinothamar/Mediator), the companion package `DDDToolkit.Mediator`
writes that delegate for you; see [In-process dispatch](event-delivery.md#in-process-dispatch). If the
events have to leave the process, the outbox delivers to a sink you implement; see
[Integration events](integration-events.md).

## Install

```bash
dotnet add package DDDToolkit.EntityFramework
```

The package brings its own source generator, so referencing it is all the configuration there is. It
targets .NET 10 and Entity Framework Core 10.

## Wiring it up

Three calls. One in your service registration, one on the `DbContextOptionsBuilder`, and one or more
in `ConfigureConventions`.

```csharp
using DDDToolkit.EntityFramework;

builder.Services.AddDDDToolkitEntityFramework();

builder.Services.AddDbContext<OrderingContext>((services, options) => options
    .UseSqlite(connectionString)
    .UseDDDToolkit(services));
```

```csharp
using DDDToolkit.EntityFramework.Conventions;
using Ordering.Domain.Converters;

public class OrderingContext(DbContextOptions<OrderingContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddOrderingConverters();
    }
}
```

That stores and loads aggregates. It does not yet save an aggregate that raised a domain event: with
events pending, `SaveChanges` throws an `InvalidOperationException` that names the aggregate, says that
no delivery mode is configured and names the two calls that would configure one. Dropping the events
quietly would be worse, because each of them is a promise that something will happen.

The argument to `AddDDDToolkitEntityFramework` says how events are delivered. Handing them to
[Mediator](https://github.com/martinothamar/Mediator) handlers in the same process takes a second
package, besides Mediator itself:

```bash
dotnet add package DDDToolkit.Mediator
```

```csharp
using DDDToolkit.Mediator;

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());
```

That package is optional, because delivery is a delegate you can write yourself against any library or
none, and because the outbox is the other way to deliver. [Delivering domain events](event-delivery.md)
explains both and when to use which.

### `UseDDDToolkit`

Adds the toolkit's interceptors to the context: the one that delivers domain events, the one that
checks invariants and the one that raises the version. Pass the `IServiceProvider` that the
`AddDbContext` callback gives you, not the root provider. That provider belongs to the same scope as
the context, so a handler that injects `OrderingContext` receives the very instance that is saving.
[The interceptors](#the-interceptors) lists them in the order they run.

### `AddDDDToolkitConventions` and `Add{Module}Converters`

`AddDDDToolkitConventions` adds the four toolkit conventions to the model. It is the same for every
context, so it takes no arguments. What each one does is under
[What is generated and what is a convention](#what-is-generated-and-what-is-a-convention).

`Add{Module}Converters` is generated, one per assembly that declares identifiers or single value
objects. It registers the value converter the generator wrote for each of them, so Entity Framework
stores an `OrderId` as the `Guid` inside it:

```csharp title="ConverterExtensions.g.cs, shortened"
namespace Ordering.Domain.Converters;

public static class ConverterExtensions
{
    public static ModelConfigurationBuilder AddOrderingConverters(this ModelConfigurationBuilder modelConfigurationBuilder)
    {
        // ...
        modelConfigurationBuilder.Properties<OrderId>().HaveConversion<OrderId.OrderIdConverter>();
        modelConfigurationBuilder.DefaultTypeMapping<OrderId>().HasConversion<OrderId.OrderIdConverter>();
        // ...
        return modelConfigurationBuilder;
    }
}
```

The method lives in a `Converters` namespace under the assembly name, in a static class called
`ConverterExtensions`. Its name comes from the `DDD_Module` property that
[Getting started](getting-started.md#store-it-with-entity-framework) sets in the project file, so a
project with `<DDD_Module>Ordering</DDD_Module>` gets `AddOrderingConverters`. Without it the
generators use the assembly name with the dots removed.

`DDD_Module` is only a name for generated methods. It is not what makes a project a module: that is
`[assembly: Module("Ordering")]`, the boundary the analyzer checks, described in [Modules](modules.md).
[Why not the DDD_Module MSBuild property](modules.md#why-not-the-ddd_module-msbuild-property) explains
why the two are separate.

Call one per assembly. A solution with a shared kernel and two modules calls three:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.AddDDDToolkitConventions();
    configurationBuilder.AddSharedKernelConverters();
    configurationBuilder.AddOrderingConverters();
    configurationBuilder.AddBillingConverters();
}
```

An assembly that declares no identifier and no single value object produces no method, because there
would be nothing for it to register.

### `AddDDDToolkitEntityFramework`

Registers the delivery configuration and the interceptors `UseDDDToolkit` adds. The argument
configures delivery; see [Delivering domain events](event-delivery.md). If your aggregates never raise
events you can call it with no argument at all, but you still need it, because it is what registers the
interceptors.

It can be called more than once, which is what lets each module of a modular monolith register its own
outbox next to its own context. See [An outbox per context](event-delivery.md#an-outbox-per-context).

## Mapping

Everything in this section works with no `OnModelCreating` code at all. The mapping tests in
`Tests/DDDToolkit.EntityFramework.Tests/MappingTests.cs` cover each case against SQLite.

### Identifiers

A struct identifier works as a primary key, as an ordinary property, and as a nullable property:

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;

[AggregateRoot<OrderId>]
public partial class Order
{
    public CustomerId Customer { get; private set; }

    public CourierId? Courier { get; private set; }
}
```

The generator writes a converter into each identifier. It stores the underlying value, so the column is
a `Guid`, an `int` or a `string`, not a serialized object:

```csharp title="OrderId.Converter.g.cs"
readonly partial record struct OrderId
{
    public sealed class OrderIdConverter : ValueConverter<OrderId, Guid>
    {
        public OrderIdConverter() : base(static v => v.Value, static v => new OrderId(v))
        {
        }
    }
}
```

and the identifier is usable in a query predicate:

```csharp
var order = context.Orders.Single(o => o.Id == orderId);          // the key
var theirs = context.Orders.Count(o => o.Customer == customerId); // a plain property
var unassigned = context.Orders.Count(o => o.Courier == null);    // a nullable property
```

Class identifiers, declared as `partial record` rather than `readonly partial record struct`, map the
same way and get a converter for their always-valid twin as well.

### Value objects

A `[ValueObject]` record is annotated `[ComplexType]`, so its properties are stored inline in the
owning table:

```csharp
[ValueObject]
public partial record PersonName(string FirstName, string LastName);
```

```csharp title="PersonName.EntityFramework.g.cs"
[ComplexType]
partial record PersonName
{
}

[ComplexType]
partial record ValidPersonName
{
}
```

An order's `Recipient`, a `PersonName`, becomes `Recipient_FirstName` and `Recipient_LastName` columns
on the `Orders` table, not a table of its own.

A `[SingleValueObject<T>]` is a converted scalar, so it becomes one column, and a nullable one stores
and reads `null`. Its converter is written the same way as an identifier's, with a second one for the
always-valid twin:

```csharp
[SingleValueObject<string>]
public partial record EmailAddress
{
    public static EmailAddress Create(string value) => new(value);
}
```

```csharp title="EmailAddress.Converter.g.cs"
partial record EmailAddress
{
    public sealed class EmailAddressConverter : ValueConverter<EmailAddress, string>
    {
        public EmailAddressConverter() : base(static v => v.Value, static v => new EmailAddress(v))
        {
        }
    }
}

partial record ValidEmailAddress
{
    public sealed class ValidEmailAddressConverter : ValueConverter<ValidEmailAddress, string>
    {
        public ValidEmailAddressConverter() : base(static v => v.Value, static v => new ValidEmailAddress(v))
        {
        }
    }
}
```

See [Value objects](value-objects.md#entity-framework).

### Child entities and owned collections

`[Entity<TId>]` produces `[Owned]`, so a child entity is loaded and saved with its aggregate:

```csharp
[Entity<Guid>("LINE")]
public partial class OrderLine
{
    // Sku and Quantity
}
```

```csharp title="OrderLine.EntityFramework.g.cs"
[Owned]
partial class OrderLine
{
}
```

A generated `partial IReadOnlyList<OrderLine> Lines { get; }` on the aggregate is discovered as an owned
collection. The `[BackingField]` annotation on the generated property points Entity Framework at the
private list, so it reads and writes the field and never tries to write through the read-only view:

```csharp title="Order.g.cs, shortened"
partial class Order : AggregateRoot<OrderId>
{
    protected Order()
    {
    }

    // ...

    private readonly List<OrderLine> _lines = new();

    private IReadOnlyList<OrderLine>? __linesView;

    [BackingField(nameof(_lines))]
    public partial IReadOnlyList<OrderLine> Lines => __linesView ??= _lines.AsReadOnly();

    // ...
}
```

The protected constructor is the one Entity Framework uses to materialize a row. The core generator
writes `[BackingField]` only when the project references Entity Framework, so a domain project without
it gets the same property without the annotation.

Removing an element removes the row. Callers still cannot cast `Lines` back to `List<OrderLine>` and
mutate it.

### Composite keys

A property marked `[KeyPart]` joins the primary key ahead of `Id`, and the foreign key of every owned
child carries it too, so `Order` keyed `(RegionId, Id)` owns `OrderLine` rows keyed
`(RegionId, OrderId, Id)`. The child needs a property of the same name; without one the model refuses
to build. [Composite keys](composite-keys.md) has the rules, the order of several parts, and how to
override it.

### Read-only collections of primitives

A generated `IReadOnlyList<T>` where `T` is a primitive, a converted identifier or a single value
object is mapped as an Entity Framework primitive collection, which on a relational provider is a
JSON column. The elements are stored as their converted values:

```csharp
[EntityId<int>("TAG")]
public readonly partial record struct TagId;

public partial IReadOnlyList<TagId> Tags { get; }
```

Two tags produce the column value `[1,2]`, not a pair of objects.

This needs the convention. Entity Framework discovers primitive properties only when they have a
setter, and a generated collection property is get-only, so without `AddDDDToolkitConventions` the
property would be skipped silently and the data would never reach the database. The convention finds
it, sets the element type, and copies the converter, max length, unicode, precision and scale from
the type mapping the generated registration installed.

The convention only considers public get-only properties. A `protected partial IReadOnlyList<T>` is
not mapped as a primitive collection.

"A JSON column" is true on SQL Server and SQLite and not on PostgreSQL, which stores the same
collection as a native array. LINQ queries port across that difference and hand-written SQL does not.
See [Primitive collections do not port below LINQ](#primitive-collections-do-not-port-below-linq)
before you write a report against one of these columns.

### What is not mappable

Entity Framework Core 10 accepts only arrays and `IList<T>` implementations as primitive collections.
A generated `IReadOnlySet<T>` is backed by a `HashSet<T>`, so a set of primitives cannot be a
primitive collection.

Rather than leave the property silently unmapped, `ReadOnlyCollectionConvention` throws while the
model is built. The message names the type and property, says that the backing field is a `HashSet`
and that only arrays and `IList<T>` qualify, and tells you to declare `IReadOnlyList<T>` instead or
exclude the property with `[NotMapped]` or `Ignore()`. You see it on first use of the context, not at
save time.

```csharp
public partial IReadOnlySet<string> Keywords { get; }   // throws while building the model
public partial IReadOnlyList<string> Keywords { get; }  // maps
```

An `IReadOnlySet<T>` of entities is fine. That is a navigation, not a primitive collection, so
relationship discovery handles it and the children round-trip with their aggregate.

## Optimistic concurrency

Every aggregate root carries `long Version`. It is 0 on a new instance, becomes 1 when the aggregate
is inserted, and increases by exactly one per save that touches the aggregate. `AddDDDToolkitConventions`
maps it as a concurrency token, so the `UPDATE` carries the loaded version in its `WHERE` clause.

"Touches the aggregate" includes more than the root's own properties. `AggregateVersionInterceptor`
walks from each changed entry to the aggregate root it belongs to, through ownership or through a
foreign key whose principal is an aggregate root, and bumps that root. A change to an owned child, an
element added to or removed from an owned collection, or a change to a primitive collection on a
child, all version the root. Each root is bumped at most once per save, however many entries changed,
and a save that changes nothing does not bump anything.

Deleting an aggregate does not bump its version, but the token is still checked, so deleting an
aggregate somebody else has changed in the meantime conflicts like any other stale write.

The version is what makes the aggregate a unit of consistency: two people editing different lines of
the same order still conflict, which is the point.

When a save writes a stale version, you get a `ConcurrencyConflictException` naming the aggregate:

```csharp
try
{
    await context.SaveChangesAsync(cancellationToken);
}
catch (ConcurrencyConflictException conflict)
{
    // conflict.AggregateType is typeof(Order), conflict.AggregateId is the OrderId.
    // Reload the aggregate, reapply the change and save again, or report it to the user.
    return Conflict(conflict.Message);
}
```

There is no safe generic answer for the catch block, which is why the toolkit does not retry for you.
Reloading and reapplying is right when the change is a command you can repeat, such as adding a line.
Reporting the conflict is right when the user needs to see what changed underneath them. Blindly
retrying a computed value is wrong.

`conflict.InnerException` is the original `DbUpdateConcurrencyException` if you need the entries.
[Why the exception is rethrown from `SaveChangesFailed`](#why-the-exception-is-rethrown-from-savechangesfailed)
explains how it reaches your catch block unwrapped.

## Migrations

There is nothing special to do. The toolkit adds types and configuration to your own `DbContext`
model, so everything it maps appears in your ordinary migrations:

```bash
dotnet ef migrations add AddOutbox
dotnet ef database update
```

The outbox table is part of the model as soon as `AddDomainEventOutbox(Database)` is in `OnModelCreating`, so
the next migration you scaffold contains it, `ddd` schema and all. Opting in is that one call. There
is no separate package, no separate migration history table and no separate command. The same goes for
`AddDomainEventInbox(Database)` on the consuming side. See [The table](event-delivery.md#the-table).

If you write migrations by hand rather than scaffolding them, `migrationBuilder.CreateDomainEventOutbox()`
and its inbox and drop counterparts write the same tables. See
[Integration events](integration-events.md#tables-schema-and-migrations).

The same applies to everything else on this page. Struct identifier columns, complex type columns,
primitive collection columns and the `Version` column are all just columns in your model, so a
`migrations add` after changing an aggregate produces the diff you would expect.

If you map the outbox table before you need it, as the example context does, switching from
in-process dispatch to the outbox later costs no migration at all.

On Supabase the CLI applies migrations, not Entity Framework; see [Supabase](#supabase).

## What is generated and what is a convention

The split is not arbitrary. A value converter is specific to one type, so it has to be written per
type and registered per assembly, which is work for a generator. A convention is a rule about the
shape of a model, identical in every context, so it lives in the runtime package.

The generator emits, for each type:

| You wrote | Generated |
|---|---|
| `[EntityId<T>]` | A nested `ValueConverter`, for example `OrderId.OrderIdConverter`, storing `Value` |
| `[SingleValueObject<T>]` | The same, plus a converter for the always-valid twin |
| `[ValueObject]` | `[ComplexType]` on the record and on its twin |
| `[Entity<TId>]` | `[Owned]` on the class |

and once per assembly, the registration method, which registers every converter twice:

```csharp title="ConverterExtensions.g.cs, shortened"
modelConfigurationBuilder.Properties<OrderId>().HaveConversion<OrderId.OrderIdConverter>();
modelConfigurationBuilder.DefaultTypeMapping<OrderId>().HasConversion<OrderId.OrderIdConverter>();
```

Both lines are needed and neither covers the other's ground. `Properties<T>()` configures properties
of that type, which is most of what a model contains. `DefaultTypeMapping<T>()` configures the type
where no property is involved: query parameters, constants, and the element type of a primitive
collection. The read-only collection convention reads that default type mapping to find the converter
for the elements of a generated `IReadOnlyList<T>`, because Entity Framework does not carry property
configuration down to collection elements.

`ColumnLength` on an identifier or single value object becomes `HaveMaxLength` on the property
registration and `HasMaxLength` on the type mapping. See [Identifiers](identifiers.md#column-length).

The conventions, added by `AddDDDToolkitConventions`, are:

| Convention | What it does |
|---|---|
| `InternalMemberConvention` | Ignores every member carrying `[Internal]`, on entity types and complex types alike |
| `ReadOnlyCollectionConvention` | Maps generated get-only collections of primitives and converted types as primitive collections, applying the element converter |
| `AggregateRootVersionConvention` | Makes `Version` on every aggregate root a concurrency token |
| `KeyPartConvention` | Puts `[KeyPart]` properties into the primary key ahead of `Id`, and into the foreign key of every owned type below; see [Composite keys](composite-keys.md) |

`InternalMemberConvention` is why `DomainEvents` never reaches your tables. The `AggregateRoot<TId>`
base class marks it `[Internal]`, so the convention ignores it exactly as `[NotMapped]` would.

Explicit configuration in `OnModelCreating` still wins over any of this. The conventions fill in what
you did not say.

## The interceptors

`UseDDDToolkit` adds three interceptors, in the order they run:

| | Interceptor | Why there |
|---|---|---|
| 1 | `PublishDomainEventsInterceptor` | Delivers domain events first, so whatever the handlers change is part of the same save |
| 2 | `InvariantInterceptor` | Sees whatever those handlers changed |
| 3 | `AggregateVersionInterceptor` | Comes last, so a save the invariants reject leaves no version bumped |

`AddDDDToolkitEntityFramework` registers them. The options object is a singleton,
`PublishDomainEventsInterceptor` is scoped so that it can hand handlers the scope that owns the
`DbContext` being saved, and `InvariantInterceptor` and `AggregateVersionInterceptor` are singletons
because they hold no state.

## Why the exception is rethrown from `SaveChangesFailed`

The interceptor translates the conflict in `ThrowingConcurrencyException`, but the update pipeline
wraps whatever that throws in a `DbUpdateException`, so callers would have to dig through inner
exceptions to find it. The interceptor therefore also handles `SaveChangesFailed`, unwraps the
`ConcurrencyConflictException` and rethrows it, which is what lets you write
`catch (ConcurrencyConflictException)` directly. It does the same for a provider that raised the
conflict without passing through `ThrowingConcurrencyException`.

Both the synchronous and the asynchronous save path behave the same.
`Tests/DDDToolkit.EntityFramework.Tests/ConcurrencyTests.cs` covers the version arithmetic, child-only
changes, deletes and both paths.

## Providers

Everything on this page is tested on SQLite, PostgreSQL and SQL Server. SQLite is where the fast suite
runs; the other two run in containers, in their own CI workflow, against the same aggregates and the
same context. Most of the mapping is identical on all three. This section is the part that is not, so
that none of it is a surprise in production.

### What differs, and what to do about it

| | SQLite | PostgreSQL | SQL Server |
|---|---|---|---|
| `Guid` identifier | `TEXT` | `uuid` | `uniqueidentifier` |
| Outbox timestamps | UTC `DateTime` | `timestamptz` | `datetimeoffset` |
| Primitive collection | JSON `TEXT` | `integer[]` | JSON `nvarchar` |
| Schemas | ignored | yes | yes |

Timestamps are covered under [Timestamps](event-delivery.md#timestamps). Schemas are ignored by SQLite,
which drops the schema when it writes an identifier, so `ddd.OutboxMessages` is plain `OutboxMessages`
there and nothing else changes. The primitive collection is the one that needs a paragraph.

### Primitive collections do not port below LINQ

A generated `IReadOnlyList<TagId>` is mapped as an Entity Framework primitive collection. What that
becomes in the database is the provider's decision, not the toolkit's, and the providers decide
differently:

- **PostgreSQL** stores it as a real `integer[]`. Npgsql maps .NET collections of a primitive onto
  PostgreSQL's own array types, which is the better mapping and the reason it does it.
- **SQL Server and SQLite** store it as a JSON document in a string column, `[1,2]`.

**This is inherent to Entity Framework Core, not something the toolkit chose or can sensibly undo.**
The mapping is supplied by each provider's type mapping source. The toolkit's convention only finds
the get-only property, sets its element type and copies the element converter across; the store type
is chosen after that, by the provider, exactly as it would be for a hand-written
`modelBuilder.PrimitiveCollection(...)`. Forcing every provider onto the same store type would mean
overriding Npgsql's native array mapping with a worse one for the sake of a portability nobody asked
for, and would break every PostgreSQL index and query already written against the array.

So the practical rule is:

**LINQ ports.** `Contains`, `Count` and the rest translate on both providers, to `= ANY(...)` on
PostgreSQL and to `OPENJSON` on SQL Server. If your query goes through `IQueryable`, you can move
providers and it keeps working.

```csharp
// Translates on both.
var tagged = await context.Shelves
    .SelectMany(shelf => shelf.Books)
    .Where(book => book.Tags.Contains(new TagId(7)))
    .ToListAsync();
```

**SQL does not port.** Anything that reaches inside the column in hand-written SQL is provider
specific, and there is no spelling that runs on both:

```sql
-- PostgreSQL
SELECT COUNT(*) FROM "Book" WHERE 7 = ANY("Tags");

-- SQL Server
SELECT COUNT(*) FROM "Book" WHERE EXISTS (SELECT 1 FROM OPENJSON("Tags") WHERE CAST([value] AS int) = 7);
```

That matters for reports, ad hoc queries, data migrations and anything a DBA writes. Indexing differs
too: a PostgreSQL array takes a GIN index, a JSON string column does not.

If you need a query like that to port, the answer is not to fight the mapping. Model the collection as
a child entity with its own table, which is one row per tag and identical on every provider. You lose
the single-column read and gain a join. That is a design decision, so make it because you need the
portability, not by accident.

Both halves are asserted against real servers in
`Tests/DDDToolkit.EntityFramework.Providers.Tests/Providers/ProviderMappingTests.cs`.

## Domain event delivery

A raised domain event is a promise that something will happen, and this package keeps it when the
context saves: by running your handlers inside `SaveChanges`, or by writing the events to an outbox
table in the same transaction and delivering them afterwards. [Delivering domain events](event-delivery.md)
explains both modes, how to choose between them, and the outbox's table, processor and retries.

## Supabase

The Supabase CLI applies migrations from SQL files in `supabase/migrations`, not from Entity Framework.
`DDDToolkit.EntityFramework.Supabase` writes one such file per Entity Framework migration as part of the
build, and can check at start-up that Supabase applied them. See [Supabase](supabase.md).

## Where to look next

- [Getting started](getting-started.md) for the module name and your first aggregate.
- [Delivering domain events](event-delivery.md) for in-process dispatch, Mediator and the outbox.
- [Identifiers](identifiers.md) for what a struct identifier actually is, and `ColumnLength`.
- [Value objects](value-objects.md) for `[ValueObject]` and `[SingleValueObject<T>]`.
- [Entities and aggregates](entities-and-aggregates.md) for read-only collections and `[BackingField]`.
- [Invariants](invariants.md) for the check every save runs.
- [Domain events](domain-events.md) for raising, draining and stable names.
- [Integration events](integration-events.md) for sinks, published contracts and the inbox.
- [Supabase](supabase.md) for exporting migrations to the Supabase CLI.
- [Diagnostics](diagnostics.md) for the build errors the generators report.

The runnable version of everything here is the shop in
[`Examples/ModularMonolith.Supabase`](../Examples/ModularMonolith.Supabase). The host's
[`Program.cs`](../Examples/ModularMonolith.Supabase/DDDToolkit.Examples.Host/Program.cs) shows the
registration,
[`OrderingContext.cs`](../Examples/Modules/Ordering/DDDToolkit.Examples.Ordering/Infrastructure/Persistence/OrderingContext.cs)
shows the conventions and the generated converters,
[`OrderingModule.cs`](../Examples/Modules/Ordering/DDDToolkit.Examples.Ordering/OrderingModule.cs) shows
the outbox, and
[`OrderingEndpoints.cs`](../Examples/Modules/Ordering/DDDToolkit.Examples.Ordering/Api/OrderingEndpoints.cs)
shows the conflict catch block. The example's `supabase/` folder and each module's `Migrations` folder
show the Supabase export.

## For contributors: running the provider tests

```bash
dotnet test Tests/DDDToolkit.EntityFramework.Providers.Tests --filter "Provider=Postgres"
dotnet test Tests/DDDToolkit.EntityFramework.Providers.Tests --filter "Provider=SqlServer"
```

They need Docker. Without it they skip themselves, naming what could not be started, because a machine
without Docker is a normal machine and should not go red.

That skip is a problem in CI, where a green run would then say PostgreSQL and SQL Server pass without
either having started. Setting `DDDTOOLKIT_REQUIRE_CONTAINERS=1` turns every such skip into a failure
that says what was missing. Both workflows set it, and the provider workflow also counts the tests that
ran, so a filter that matched nothing cannot pass either.

The images are pinned exactly, in
`Tests/DDDToolkit.EntityFramework.Providers.Tests/Infrastructure/ContainerImages.cs`, with the reason
for each next to it. No floating tag, so a red build is always something we changed. PostgreSQL matches
the major of the pgmq image the Postgres package is tested against; SQL Server is the older release
still in mainstream support, which is the weaker of the two and therefore the one worth testing.
