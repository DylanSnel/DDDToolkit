# What the generator writes

Every attribute in DDDToolkit is a request to a Roslyn source generator. The generator reads your
declaration while the project compiles and adds C# files to the same compilation. There is no runtime
library looking at your types, no reflection over them and nothing woven into the IL. What runs is the
code on this page, and you can open it, read it and step through it like your own.

This page walks through that output for one small aggregate. The code comes from
[`website/sample`](../website/sample), which the docs site compiles to show the same files on its
homepage, so it is the generators' real output. The only change is that `global::` prefixes have been
taken out and long files are shortened. Four files go in:

```csharp
[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public Order(OrderId id, Address shipTo) : base(id)
    {
        ShipTo = shipTo;
        RaiseDomainEvent(new OrderPlaced(id));
    }

    public Address ShipTo { get; private set; }

    public partial IReadOnlyList<OrderLine> Lines { get; }

    public sealed class MustHaveLines : IInvariant<Order>
    {
        public string Code => "ORDER_HAS_NO_LINES";

        public InvariantFailure? Check(Order order)
            => order.Lines.Count == 0
                ? "An order has at least one line."
                : null;
    }
}

[Entity<Guid>("LINE")]
public partial class OrderLine { /* Sku and Quantity */ }

[ValueObject]
public partial record Address(string Street, string City)
{
    protected override bool Validate() => Street.Length > 0 && City.Length > 0;
}

[DomainEventName("shop.order-placed")]
public sealed record OrderPlaced(OrderId Order) : DomainEvent;
```

58 lines in, 963 lines out, in 15 files.

## Why generate it

- **It is there, and it is current.** Add a property to a value object and its equality, its `With()`
  and its mapping follow at the next build. There is no second file that has to be kept in step, and
  no reviewer has to check that it was.
- **It costs what hand-written code costs.** Equality, conversions and invariant checks call your
  members directly. [Performance](performance.md) measures a struct id against the raw `Guid` it
  wraps, and they come out the same.
- **You pay only for what you use.** An entity without rules gets no list of rules and no check to
  run. An unimplemented `partial void CheckInvariants()` is removed by the compiler, with every call
  to it.
- **Misuse is a compile error.** When a declaration cannot be generated, or does something other than
  it looks like, you get a [diagnostic](diagnostics.md) in the editor instead of a type that quietly
  behaves like a plain class.
- **One declaration reaches every integration.** Reference `DDDToolkit.EntityFramework` or
  `DDDToolkit.HotChocolate` and their generators add converters and bindings for the same types. Your
  declaration does not change.

## `[AggregateRoot]` and `[Entity]`

`Order.g.cs` gives the class its base type, with the identifier type the attribute asked for, and the
constructor Entity Framework needs to materialize a row:

```csharp title="Order.g.cs, shortened"
partial class Order : DDDToolkit.BaseTypes.AggregateRoot<Shop.OrderId>
{
    /// <summary>Parameterless constructor for persistence frameworks and serializers.</summary>
    protected Order()
    {
    }
```

Every rule nested in the class is created once and kept in a static array. The checks walk it, then
the `CheckInvariants()` seam, then every child entity the aggregate holds in a collection:

```csharp title="Order.g.cs, shortened"
    private static readonly DDDToolkit.Invariants.IInvariant<Shop.Order>[] __invariants =
    [
        new Shop.Order.MustHaveLines(),
    ];

    public override void EnsureInvariants()
    {
        System.Collections.Generic.List<DDDToolkit.Invariants.InvariantViolation>? violations = null;
        CollectInvariantViolations(ref violations, out var seamFailure);

        CollectChildInvariantViolations(ref violations);

        if (violations is null)
        {
            return;
        }

        ThrowInvariantViolations(violations, seamFailure);
    }
```

The list of violations is created on the first failure, so a consistent aggregate allocates nothing.
[Invariants](invariants.md) explains the two stages, `GetInvariantViolations()` to ask and
`EnsureInvariants()` to throw, and when the save runs them.

The `partial` collection property gets a field behind it:

```csharp title="Order.g.cs, shortened"
    private readonly System.Collections.Generic.List<Shop.OrderLine> _lines = new();

    private System.Collections.Generic.IReadOnlyList<Shop.OrderLine>? __linesView;

    /// <summary>Read-only view over <see cref="_lines"/>. Mutate the collection through the field.</summary>
    [Microsoft.EntityFrameworkCore.BackingField(nameof(_lines))]
    public partial System.Collections.Generic.IReadOnlyList<Shop.OrderLine> Lines => __linesView ??= _lines.AsReadOnly();
}
```

In practice: code inside `Order` adds a line with `_lines.Add(...)`, and code outside it can read
`Lines` but cannot change it. Entity Framework loads and saves the field. This is the usual
hand-written pattern for keeping a collection inside its aggregate, minus the writing.

`OrderLine.g.cs` is the same for the child entity, with `Entity<OrderLineId>` as the base class.

## The identifier

`[AggregateRoot<Guid>("ORD")]` names the id type after the class, so the generator writes `OrderId`.
No `OrderId.cs` exists anywhere. It is a `readonly record struct` around the `Guid`:

```csharp title="OrderId.g.cs, shortened"
[System.Text.Json.Serialization.JsonConverter(typeof(OrderId.SystemTextJsonConverter))]
public readonly partial record struct OrderId : DDDToolkit.Abstractions.Interfaces.IEntityId<System.Guid>, System.IComparable<OrderId>, System.IParsable<OrderId>
{
    public const string IdPrefix = "ORD";

    public System.Guid Value { get; }

    public static OrderId CreateUnique() => new(System.Guid.NewGuid());

    public static OrderId CreateSequential() => new(System.Guid.CreateVersion7());

    public override string ToString() => /* ORD_1b4e28ba-2fa1-11d2-883f-0016d3cca427 */;

    public static OrderId Parse(string input) { /* the prefix is optional */ }
    public static bool TryParse(string? input, out OrderId result) { /* ... */ }

    public sealed class SystemTextJsonConverter : System.Text.Json.Serialization.JsonConverter<OrderId> { /* ... */ }
}
```

`CreateSequential()` makes a version 7 `Guid`, which is ordered by time and friendlier to a database
index than a random one. [Identifiers](identifiers.md) covers the other value types, the record form
and the prefix.

## `[ValueObject]`

`Address.g.cs` gives the record its base type and equality over its components, in declaration order:

```csharp title="Address.g.cs, shortened"
partial record Address : DDDToolkit.BaseTypes.ValueObject, DDDToolkit.Validation.IValidatable<ValidAddress>
{
    [System.Text.Json.Serialization.JsonInclude]
    public string Street { get; protected init; } = Street;

    [System.Text.Json.Serialization.JsonInclude]
    public string City { get; protected init; } = City;

    protected override System.Collections.Generic.IEnumerable<object?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
    }
```

The positional parameters become properties with a `protected init` setter, so nobody outside the
record can make a changed copy with `with` and skip validation. What it offers instead is `With()`,
which copies with changes and judges the copy afresh:

```csharp title="Address.g.cs, shortened"
    public virtual Address With(DDDToolkit.BaseTypes.Optional<string> street = default, DDDToolkit.BaseTypes.Optional<string> city = default)
        => this with { Street = street.Or(Street), City = city.Or(City) };
}
```

It also writes `ValidAddress`, the always-valid twin. Its only constructor validates, so a method that
takes a `ValidAddress` never has to check one again:

```csharp title="Address.g.cs, shortened"
public partial record ValidAddress : Address, DDDToolkit.Abstractions.Interfaces.IAlwaysValid
{
    public ValidAddress(Address value) : base(value)
    {
        value.EnsureValidated();
        _isValid = true;
    }
```

[Value objects](value-objects.md) explains validation, the twin and `With()` in full.

## The event names

Every name the project's events are stored or published under becomes a constant, in a class named after
the module:

```csharp title="EventNames.g.cs, shortened"
public static class ShopEventNames
{
    /// <summary><c>shop.order-placed</c>: <see cref="Shop.OrderPlaced"/> (version 1).</summary>
    public const string OrderPlaced = "shop.order-placed";
}
```

`OrderPlaced` pins its name with `[DomainEventName]`. Without it the name would be the module and the class
name in kebab case; [Stable names](domain-events.md#stable-names) has the rule, and the checks the same
pass runs on it.

## `DDDToolkit.EntityFramework`

With the Entity Framework package referenced, its generator adds the mapping. Each id gets a value
converter that stores it as its plain value:

```csharp title="OrderId.Converter.g.cs"
public readonly partial record struct OrderId
{
    public sealed class OrderIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<Shop.OrderId, System.Guid>
    {
        public OrderIdConverter() : base(static v => v.Value, static v => new Shop.OrderId(v))
        {
        }
    }
}
```

One method registers every converter in the project. It is named after the module, set with
`<DDD_Module>Shop</DDD_Module>` in the project file, and you call it from `ConfigureConventions`:

```csharp title="ConverterExtensions.g.cs, shortened"
public static Microsoft.EntityFrameworkCore.ModelConfigurationBuilder AddShopConverters(this Microsoft.EntityFrameworkCore.ModelConfigurationBuilder modelConfigurationBuilder)
{
    modelConfigurationBuilder.Properties<Shop.OrderId>().HaveConversion<Shop.OrderId.OrderIdConverter>();
    modelConfigurationBuilder.DefaultTypeMapping<Shop.OrderId>().HasConversion<Shop.OrderId.OrderIdConverter>();
    // the same for OrderLineId
    return modelConfigurationBuilder;
}
```

Value objects are marked `[ComplexType]`, so their fields become columns of the table that holds
them. Child entities are marked `[Owned]`, so they are saved with their aggregate and never alone.

`AddShopIntegrationEvents()` registers every domain event of the module with the outbox, under its stable
name, here the one from `[DomainEventName]`:

```csharp title="IntegrationEventExtensions.g.cs, shortened"
outbox.RegisterEvent<Shop.OrderPlaced>("shop.order-placed", 1);
```

The name is what the outbox stores, so renaming the class does not orphan rows already written. See
[Entity Framework](entity-framework.md) and [Integration events](integration-events.md).

## `DDDToolkit.HotChocolate`

With the HotChocolate package referenced, each id gets a type converter and a Relay node id
serializer, and one method binds them all:

```csharp title="BindingExtensions.g.cs, shortened"
public static HotChocolate.Execution.Configuration.IRequestExecutorBuilder AddShopGraphQlRuntimeBindings(this HotChocolate.Execution.Configuration.IRequestExecutorBuilder builder)
{
    builder.BindRuntimeType<Shop.OrderId, HotChocolate.Types.UuidType>();
    builder.AddTypeConverter<Shop.OrderId.ChangeTypeProvider>();
    builder.AddNodeIdValueSerializer<Shop.OrderId.NodeIdValueSerializer>();
    // the same for OrderLineId
    return builder;
}
```

An `OrderId` is a `UUID` in the schema and can be the key inside a Relay node id. See
[GraphQL](graphql.md).

## See it in your own project

Go to definition on a generated member opens the generated file, and in Visual Studio the files are
also listed under **Dependencies → Analyzers**. To have them written to disk:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(MSBuildProjectDirectory)\Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Build, then look in `Generated/`, and add that folder to `.gitignore`. If a type you annotated
produced nothing, the build output has a `DDD` diagnostic that says why; see [Diagnostics](diagnostics.md).
