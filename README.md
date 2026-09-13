# DDDToolkit

Source generators that remove the repetitive parts of domain driven design in .NET. You declare the
intent with an attribute; the generator writes the base type, the equality members, the identifier
plumbing, the persistence mapping and the API conversions.

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;

[AggregateRoot<OrderId>]
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

That is the whole declaration. `OrderId` becomes an allocation-free identifier with parsing,
comparison and JSON support. `Order` gets its base class, an optimistic concurrency version, a domain
event list only it can write to, and a `_lines` backing field that Entity Framework maps directly
while the outside world sees a read-only list.

## Documentation

| Page | What it covers |
|---|---|
| [Getting started](docs/getting-started.md) | Installing the packages, the one MSBuild property you need, your first aggregate |
| [Identifiers](docs/identifiers.md) | `[EntityId<T>]`, struct versus record ids, parsing, prefixes |
| [Value objects](docs/value-objects.md) | `[ValueObject]`, `[SingleValueObject<T>]`, validation and the always-valid twin |
| [Entities and aggregates](docs/entities-and-aggregates.md) | `[Entity<T>]`, `[AggregateRoot<T>]`, read-only collections, versioning |
| [Domain events](docs/domain-events.md) | Raising, draining, stable names, delivery |
| [Entity Framework](docs/entity-framework.md) | Converters and conventions, mapping, event dispatch, the outbox, concurrency, migrations |
| [GraphQL](docs/graphql.md) | `AddDDDToolkitTypes()`, the generated scalar bindings, hiding `[Internal]` members, the `DomainEvent` interface |
| [Diagnostics](docs/diagnostics.md) | Every DDD000xx error and how to fix it |

## Packages

Reference `DDDToolkit` and add the integrations you actually use. Each integration package brings its
own generator, so referencing it is all the configuration there is.

| Package | Use it for |
|---|---|
| `DDDToolkit` | Base types and the core generators. Start here. |
| `DDDToolkit.Abstractions` | The attributes and marker interfaces alone, for projects that must not reference the runtime. |
| `DDDToolkit.EntityFramework` | Value converters, model conventions, domain event dispatch, optimistic concurrency. |
| `DDDToolkit.FluentValidation` | A generated validator per value object. |
| `DDDToolkit.HotChocolate` | GraphQL scalar bindings and converters for typed identifiers. |
| `DDDToolkit.NewtonSoft.Json` | Newtonsoft converters and a contract resolver that honours `[Internal]`. |

```bash
dotnet add package DDDToolkit
```

Requires .NET 10. The generators themselves target `netstandard2.0` and carry no runtime
dependencies, so they load in any recent SDK.

## What the generators produce

| You write | You get |
|---|---|
| `[EntityId<T>]` on a `readonly partial record struct` | `Value`, a constructor, `Empty`/`IsEmpty`, `Parse`/`TryParse`, `IParsable<T>`, `IComparable<T>`, explicit conversions, a JSON converter, and `CreateUnique`/`CreateSequential` for `Guid` |
| `[EntityId<T>]` on a `partial record` | The same identifier surface on a reference type, plus a `Valid` twin |
| `[SingleValueObject<T>]` on a `partial record` | A wrapper with value equality, a `Valid` twin and validation |
| `[ValueObject]` on a `partial record` | Structural equality across the properties you did not exclude, plus a `Valid` twin |
| `[Entity<TId>]` / `[AggregateRoot<TId>]` on a `partial class` | The base type, a persistence constructor, and an implementation for every get-only partial collection property |

Add `DDDToolkit.EntityFramework` and the same declarations also produce value converters, `[Owned]`
and `[ComplexType]` annotations, and a single `Add<Module>Converters` call for your `DbContext`. Add
`DDDToolkit.HotChocolate` and they produce GraphQL type bindings. You never write that code.

## Status

Version 3.0 is a breaking release. Identifiers may be structs, domain events moved from `Entity` to
`AggregateRoot`, `IDomainEvent` carries an id and a timestamp, and misapplied attributes now report a
diagnostic instead of silently generating nothing.

## License

MIT. See [LICENSE](LICENSE).
