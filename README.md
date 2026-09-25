# DDDToolkit

Source generators that remove the repetitive parts of domain driven design in .NET. You declare the
intent with an attribute; the generator writes the base type, the equality members, the identifier
plumbing, the persistence mapping and the API conversions.

**Documentation:** [dylansnel.github.io/DDDToolkit](https://dylansnel.github.io/DDDToolkit/), the
[`docs/`](docs) folder as a site.

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
| [Getting started](docs/getting-started.md) | One module built step by step: an id, a value object, an aggregate, a test, a database, a second module |
| [What the generator writes](docs/generated-code.md) | The generated code for one small aggregate, file by file, and why it is generated |
| [Identifiers](docs/identifiers.md) | `[EntityId<T>]`, struct versus record ids, parsing, prefixes |
| [Value objects](docs/value-objects.md) | `[ValueObject]`, `[SingleValueObject<T>]`, validation and the always-valid twin |
| [Entities and aggregates](docs/entities-and-aggregates.md) | `[Entity<T>]`, `[AggregateRoot<T>]`, child entities, read-only collections, referencing by id |
| [Invariants](docs/invariants.md) | Named `IInvariant<T>` rules and the `CheckInvariants()` seam, the two stages, and the save that runs them |
| [Domain events](docs/domain-events.md) | Declaring, raising and draining events, and their stable names |
| [Designing aggregates](docs/aggregate-design.md) | The four rules of aggregate design, and how the toolkit holds each one |
| [Testing](docs/testing.md) | The aggregate testing kit: acting on an aggregate and asserting on what it raised |
| [Failure handling](docs/value-objects.md#failure-handling) | Validating without exceptions, and when to throw anyway |
| [Entity Framework](docs/entity-framework.md) | Wiring, the generated converters, mapping, concurrency and migrations |
| [Composite keys](docs/composite-keys.md) | `[KeyPart]`: keying a table on more than the id, and carrying that into every owned table |
| [Delivering domain events](docs/event-delivery.md) | In-process dispatch or the outbox, and how to choose |
| [Supabase](docs/supabase.md) | Exporting each module's migrations for `supabase db push`, as part of the build |
| [Modules](docs/modules.md) | `[assembly: Module]` and the boundary the analyzer checks |
| [Module contracts](docs/module-contracts.md) | What a module publishes, why, and where to keep it |
| [Integration events](docs/integration-events.md) | Contracts between modules, the outbox and the inbox, versioning |
| [Transports](docs/transports.md) | Carrying integration events out of the process: pgmq, Wolverine, MassTransit, or a sink of your own |
| [GraphQL](docs/graphql.md) | `AddDDDToolkitTypes()`, the generated scalar bindings, errors with codes, Relay node ids, one schema over the modules |
| [Localization](docs/localization.md) | Validation errors and invariant violations in the reader's language, looked up by code |
| [Performance](docs/performance.md) | The benchmarks behind the struct-versus-record advice, including where they disagree with it |
| [Diagnostics](docs/diagnostics.md) | Every DDD000xx diagnostic and how to fix it |
| [Migrating to 3.0](docs/migrating-to-3.md) | Every 2.x break, with the before and the after |

## With an AI coding agent

An agent that has never seen the toolkit writes the base class the generator already writes, a
settable collection, a sealed value object. Three things teach it otherwise.

**A skill.** [`skills/dddtoolkit`](skills/dddtoolkit) holds the declarations, the rules the generators
enforce, the wiring for Entity Framework and modules, and the fix for every DDD diagnostic. It is an
[Agent Skill](https://agentskills.io), the format Claude Code, Codex, Cursor and GitHub Copilot read.
This repository is also a Claude Code plugin marketplace, so in Claude Code:

```text
/plugin marketplace add DylanSnel/DDDToolkit
/plugin install dddtoolkit@dddtoolkit
```

For another agent, `npx skills add DylanSnel/DDDToolkit` installs it, or copy the folder into the
agent's skills directory.

**The docs as text.** The site serves [`llms.txt`](https://dylansnel.github.io/DDDToolkit/llms.txt), an
index of the pages with what each covers, and
[`llms-full.txt`](https://dylansnel.github.io/DDDToolkit/llms-full.txt), all of them in one file. Every
page is also plain Markdown at its own address with `.md` added, such as
[`docs/invariants.md`](https://dylansnel.github.io/DDDToolkit/docs/invariants.md).

**The diagnostics.** Every DDD diagnostic carries a link to its entry in [Diagnostics](docs/diagnostics.md),
so an IDE opens it from the error list and an agent that reads the build output can follow it.

## Packages

Reference `DDDToolkit` and add the integrations you actually use. Each integration package brings its
own generator, so referencing it is all the configuration there is.

| Package | Use it for |
|---|---|
| `DDDToolkit` | Base types and the core generators. Start here. |
| `DDDToolkit.Abstractions` | The attributes and marker interfaces alone, for projects that must not reference the runtime. |
| `DDDToolkit.EntityFramework` | Value converters, model conventions, domain event dispatch, invariant checks, optimistic concurrency, the outbox and the inbox. |
| `DDDToolkit.Messaging.Postgres` | A [pgmq](https://github.com/pgmq/pgmq) sink, so the outbox enqueues inside the same Postgres transaction that writes the aggregate, and a consumer that reads a queue into the modules' inboxes. |
| `DDDToolkit.Messaging.Wolverine` | [Wolverine](https://wolverinefx.net/) as the transport between one process's outbox and another's inbox. |
| `DDDToolkit.Messaging.MassTransit` | [MassTransit](https://masstransit.io/) 8 as that transport, for those already on it. |
| `DDDToolkit.EntityFramework.Supabase` | Your Entity Framework migrations written as Supabase migration files by the build, so `supabase db push` and branching apply them, and a CI build that fails when one is missing. |
| `DDDToolkit.Mediator` | One call that dispatches domain events through [Mediator](https://github.com/martinothamar/Mediator) instead of a hand-written delegate. |
| `DDDToolkit.FluentValidation` | A generated validator per value object. |
| `DDDToolkit.Localization` | Validation errors and invariant violations phrased in the reader's language, through `IStringLocalizer`. |
| `DDDToolkit.HotChocolate` | GraphQL scalar bindings and converters for typed identifiers, plus a subscription sink. |
| `DDDToolkit.HotChocolate.Fusion.InMemory` | One GraphQL schema over a modular monolith: each module a source schema, composed by a Fusion gateway in the process. Needs HotChocolate Fusion 16.6.6 or later. |
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
[Delivering domain events](docs/event-delivery.md#the-delegate) shows the delegate to write for it.

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
