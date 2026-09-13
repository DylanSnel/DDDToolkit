# Getting started

## Install

```bash
dotnet add package DDDToolkit
```

`DDDToolkit` brings the base types and the core generators. Add an integration package for each
technology you use; each one carries its own generator and needs no further registration.

```bash
dotnet add package DDDToolkit.EntityFramework
dotnet add package DDDToolkit.FluentValidation
dotnet add package DDDToolkit.HotChocolate
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

## Declare an identifier

```csharp
using DDDToolkit.Abstractions.Attributes;

namespace Ordering.Domain;

[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;
```

Two rules: the type must be `partial` so the generator can add to it, and it must be a record. Use
`readonly partial record struct` unless you have a reason not to; it costs no allocation and the
generator gives it a complete identifier surface. See [Identifiers](identifiers.md).

## Declare an aggregate

```csharp
using DDDToolkit.Abstractions.Attributes;

namespace Ordering.Domain;

[AggregateRoot<OrderId>]
public partial class Order
{
    public Order(OrderId id, CustomerId customer) : base(id)
    {
        Customer = customer;
        RaiseDomainEvent(new OrderPlaced(id, customer));
    }

    public CustomerId Customer { get; private set; }

    public OrderStatus Status { get; private set; } = OrderStatus.Draft;

    public partial IReadOnlyList<OrderLine> Lines { get; }

    public void AddLine(OrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _lines.Add(line);
    }
}
```

The generator supplies the `AggregateRoot<OrderId>` base class, a protected parameterless constructor
for your persistence framework, a private `_lines` list, and the read-only `Lines` implementation.
`RaiseDomainEvent` comes from the base class and is protected, so nothing outside the aggregate can
put an event into it.

Note that `Lines` is declared `partial` and get-only. That is the contract: you describe the property
you want, the generator writes the field and the body. Declaring a setter is an error
([DDD00020](diagnostics.md#ddd00020)).

## Declare a value object

```csharp
[ValueObject]
public partial record Address
{
    public Address(string street, string city, string postalCode)
        => (Street, City, PostalCode) = (street, city, postalCode);

    public string Street { get; protected init; }

    public string City { get; protected init; }

    public string PostalCode { get; protected init; }
}
```

Equality is generated across all three properties. Properties carrying `[DontCompare]` are excluded
from equality, and properties carrying `[Internal]` are excluded from equality, persistence and
serialization. Setters must be `protected init`, which stops callers from using `with` to produce an
invalid copy ([DDD00010](diagnostics.md#ddd00010), [DDD00011](diagnostics.md#ddd00011)).

## Wire up Entity Framework

With `DDDToolkit.EntityFramework` referenced, every identifier and single value object gets a value
converter and every project gets one registration method:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.AddOrderingConverters();
}
```

Call it once per assembly that declares identifiers or value objects. A solution with a shared kernel
and two modules calls three methods.

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
