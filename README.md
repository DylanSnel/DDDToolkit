# DDDToolkit

Source generators that remove the repetitive parts of domain driven design in .NET. You declare the
intent with an attribute; the generator writes the base type, the equality members, the identifier
plumbing, the persistence mapping and the API conversions.

```csharp
[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public Order(OrderId id, CustomerId customer) : base(id)
    {
        Customer = customer;
        RaiseDomainEvent(new OrderPlaced(id, customer));
    }

    public CustomerId Customer { get; private set; }

    public partial IReadOnlyList<OrderLine> Lines { get; }

    public void AddLine(OrderLine line) => _lines.Add(line);
}
```

That is the whole declaration. `OrderId` did not have to be written at all: naming the raw value
generates it as an allocation-free identifier with parsing, comparison and JSON support. `Order` gets
its base class, an optimistic concurrency version, a domain event list only it can write to, and a
`_lines` backing field that Entity Framework maps directly while the outside world sees a read-only
list.

Declare the identifier yourself when other aggregates, DTOs or API contracts refer to it, which gives
it a file of its own to navigate to:

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;

[AggregateRoot<OrderId>]
public partial class Order { }
```

## Documentation

| Page | What it covers |
|---|---|
| [Getting started](docs/getting-started.md) | Installing the packages, the one MSBuild property you need, your first aggregate |
| [Identifiers](docs/identifiers.md) | `[EntityId<T>]`, struct versus record ids, parsing, prefixes |
| [Value objects](docs/value-objects.md) | `[ValueObject]`, `[SingleValueObject<T>]`, validation and the always-valid twin |
| [Entities and aggregates](docs/entities-and-aggregates.md) | `[Entity<T>]`, `[AggregateRoot<T>]`, read-only collections, versioning |
| [Invariants](docs/invariants.md) | The two stages, named `IInvariant<T>` rules and the `CheckInvariants()` seam, and the interceptor that runs them at every save |
| [Domain events](docs/domain-events.md) | Raising, draining, stable names, delivery, deterministic time in tests |
| [Integration events](docs/integration-events.md) | Publishing outside the process: integration events, sinks, versioning and the inbox |
| [Modules](docs/modules.md) | `[assembly: Module]`, `[ModuleContract]`, and the boundary the analyzer checks |
| [Entity Framework](docs/entity-framework.md) | Converters and conventions, mapping, event dispatch, the outbox, concurrency, migrations, and exporting them for Supabase |
| [Composite keys](docs/composite-keys.md) | `[KeyPart]`: keying a table on more than the id, and carrying that into every owned table |
| [GraphQL](docs/graphql.md) | `AddDDDToolkitTypes()`, the generated scalar bindings, hiding `[Internal]` members, the `DomainEvent` interface |
| [Testing](docs/testing.md) | The aggregate testing kit: acting on an aggregate and asserting on what it raised |
| [Failure handling](docs/value-objects.md#failure-handling) | Validating without exceptions, and when to throw anyway |
| [Performance](docs/performance.md) | The benchmarks behind the struct-versus-record advice, including where they disagree with it |
| [Diagnostics](docs/diagnostics.md) | Every DDD000xx error and how to fix it |
| [Migrating to 3.0](docs/migrating-to-3.md) | Every 2.x break, with the before and the after |

## Packages

Reference `DDDToolkit` and add the integrations you actually use. Each integration package brings its
own generator, so referencing it is all the configuration there is.

| Package | Use it for |
|---|---|
| `DDDToolkit` | Base types and the core generators. Start here. |
| `DDDToolkit.Abstractions` | The attributes and marker interfaces alone, for projects that must not reference the runtime. |
| `DDDToolkit.EntityFramework` | Value converters, model conventions, domain event dispatch, invariant checks, optimistic concurrency, the outbox and the inbox. |
| `DDDToolkit.Messaging.Postgres` | A [pgmq](https://github.com/pgmq/pgmq) sink, so the outbox enqueues inside the same Postgres transaction that writes the aggregate. |
| `DDDToolkit.EntityFramework.Supabase` | Your Entity Framework migrations written as Supabase migration files by the build, so `supabase db push` and branching apply them, and a CI build that fails when one is missing. |
| `DDDToolkit.Mediator` | One call that dispatches domain events through [Mediator](https://github.com/martinothamar/Mediator) instead of a hand-written delegate. |
| `DDDToolkit.FluentValidation` | A generated validator per value object. |
| `DDDToolkit.HotChocolate` | GraphQL scalar bindings and converters for typed identifiers, plus a subscription sink. |
| `DDDToolkit.NewtonSoft.Json` | Newtonsoft converters and a contract resolver that honours `[Internal]`. |
| `DDDToolkit.Testing` | The aggregate testing kit. A test-only reference; it brings no test framework of its own. |

```bash
dotnet add package DDDToolkit
```

There are five more packages you never reference directly: `DDDToolkit.Analyzers` and the
`.EntityFramework`, `.EntityFramework.Supabase`, `.FluentValidation` and `.HotChocolate` analyzer
packages beside it. Each one
carries the generators for its integration and arrives as a dependency of the package above, so
adding `DDDToolkit.HotChocolate` is all it takes to get the GraphQL generator.

Requires .NET 10. The generators themselves target `netstandard2.0` and carry no runtime
dependencies, so they load in any recent SDK.

The core has no mediator dependency and does not need one: in-process event delivery is a delegate you
write, and `DDDToolkit.Mediator` only saves you writing it. The examples publish through Mediator
rather than MediatR because MediatR is commercially licensed from version 13, and this repository
prefers dependencies its users can take for free. MediatR still works perfectly well with the toolkit;
[Entity Framework](docs/entity-framework.md#the-delegate) shows the delegate to write for it.

## What the generators produce

| You write | You get |
|---|---|
| `[EntityId<T>]` on a `readonly partial record struct` | `Value`, a constructor, `Empty`/`IsEmpty`, `Parse`/`TryParse`, `IParsable<T>`, `IComparable<T>`, explicit conversions, a JSON converter, and `CreateUnique`/`CreateSequential` for `Guid` |
| `[EntityId<T>]` on a `partial record` | A reference type identifier: `Value`, equality over it, `Parse`/`TryParse`, `CreateUnique`/`CreateSequential` for `Guid`, and a `Valid` twin. It has `null` rather than `Empty`, and no conversion operators |
| `[SingleValueObject<T>]` on a `partial record` | A wrapper with value equality, a `Valid` twin and validation |
| `[ValueObject]` on a `partial record`, positional or with `{ get; protected init; }` properties | Structural equality across the properties you did not exclude, a `With(...)` for changed copies, and a `Valid` twin |
| `[Entity<TId>]` / `[AggregateRoot<TId>]` on a `partial class` | The base type, a persistence constructor, a `CheckInvariants()` seam, `GetInvariantViolations()` and `EnsureInvariants()` over it, over any nested `IInvariant<T>` rules and over every child entity it holds, an `EnsureOwnInvariants()` / `GetOwnInvariantViolations()` pair that stops at this object, and an implementation for every get-only partial collection property |

Add `DDDToolkit.EntityFramework` and the same declarations also produce value converters, `[Owned]`
and `[ComplexType]` annotations, and a single `Add<Module>Converters` call for your `DbContext`. Add
`DDDToolkit.HotChocolate` and they produce GraphQL type bindings. You never write that code.

## Beyond the declarations

Three things the toolkit does that are not a generated member.

**[Invariants](docs/invariants.md).** State a rule in the generated `partial void CheckInvariants()`
seam, or as a nested `IInvariant<T>` when it deserves a name, a code and a test of its own. There are
two moments to ask. `GetInvariantViolations()` answers with a list and never throws, for the
application that wants to handle "not consistent yet"; `EnsureInvariants()` throws, and an interceptor
runs the same check before every `SaveChanges` that writes the entity, child entities included, so a
broken rule stops the save. Asking the aggregate root asks the whole aggregate, its children too, each
violation naming the entity that reported it, so a handler can act on an aggregate and ask what that
broke without a `DbContext` taking part. State nothing and the compiler erases the seam, so an
aggregate with no invariants pays nothing.

**[Integration events](docs/integration-events.md).** The outbox writes one row per event in the same
transaction as the aggregate. A sink delivers it afterwards: to another module in this process, to a
pgmq queue, to a GraphQL subscription, or to one you wrote. Versioning and upcasting keep last year's
payload readable, and an inbox keyed by message id and consumer makes the receiving side idempotent.

**[Modules](docs/modules.md).** Mark an assembly `[assembly: Module("Ordering")]` and name what it
publishes with `[ModuleContract]`. An analyzer then reports where another module reaches past the
contract. It says nothing at all until both sides opt in.

## Status

Version 3.0 is a breaking release. Identifiers may be structs, domain events moved from `Entity` to
`AggregateRoot`, `IDomainEvent` carries an id and a timestamp, aggregates gained an invariant seam and
a concurrency version, and misapplied attributes now report a diagnostic instead of silently
generating nothing.

Coming from 2.0.22, read [Migrating to 3.0](docs/migrating-to-3.md): it lists every break with the
code you have and the code you need. The [changelog](CHANGELOG.md) has the rest.

## License

MIT. See [LICENSE](LICENSE).
