# Identifiers

An order has an id, and so does the customer who placed it. If both ids are a `Guid`, the compiler sees one type. A
method that takes an order id also accepts a customer id. A call with its arguments in the wrong order compiles,
runs, and finds nothing.

```csharp
public Task Assign(Guid orderId, Guid customerId);          // Assign(customer.Id, order.Id) compiles
public Task Assign(OrderId orderId, CustomerId customerId); // the same mistake does not
```

A strongly typed identifier stops you from passing a customer id where an order id belongs. Written by hand, each
identifier needs a constructor, formatting, parsing, comparison, and a converter for every serializer and database it
meets. `[EntityId<T>]` generates all of that from one declaration:

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;
```

The declaration must be `partial`, so the generator can add to it, and it must be a record. Use
`readonly partial record struct` unless you have a reason not to. There is also a class form, and
[Struct or record](#struct-or-record) says when it is worth using.

This page starts with what the struct form gives you. Then it covers creating, printing and parsing identifiers, and
letting an entity declare its identifier. It ends with the class form, storage, JSON and GraphQL, and the rules a
declaration must follow.

## What the struct form generates

The generator writes the other half of the type while the project compiles. For `OrderId` it writes:

**OrderId.g.cs, shortened**

```csharp
[JsonConverter(typeof(OrderId.SystemTextJsonConverter))]
readonly partial record struct OrderId : IEntityId<Guid>, IComparable<OrderId>, IParsable<OrderId>, ICreatableEntityId<OrderId>
{
    public const string IdPrefix = "ORD";

    public Guid Value { get; }

    public OrderId(Guid value)
    {
        Value = value;
    }

    public static OrderId Empty => default;

    public bool IsEmpty => EqualityComparer<Guid>.Default.Equals(Value, default);

    public static OrderId CreateUnique() => new(Guid.NewGuid());
#if NET9_0_OR_GREATER

    public static OrderId CreateSequential() => new(Guid.CreateVersion7());

    public static OrderId Create() => CreateSequential();
#endif

    public override string ToString()
        => IdPrefix.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Value}")
            : string.Create(CultureInfo.InvariantCulture, $"{IdPrefix}_{Value}");

    public int CompareTo(OrderId other) => Comparer<Guid>.Default.Compare(Value, other.Value);

    public static explicit operator Guid(OrderId id) => id.Value;

    public static explicit operator OrderId(Guid value) => new(value);

    public static OrderId Parse(string input) { /* ... */ }

    public static bool TryParse([NotNullWhen(true)] string? input, out OrderId result) { /* ... */ }

    // ...

    public sealed class SystemTextJsonConverter : JsonConverter<OrderId> { /* ... */ }
}
```

Your half carries the accessibility and the attribute. The generated half carries the members. So `public` appears
once, on the declaration you write, and your declaration stays one line.

The type implements four interfaces: `IEntityId<Guid>`, `IComparable<OrderId>`, `IParsable<OrderId>` and
`ICreatableEntityId<OrderId>` ([Creating identifiers](#creating-identifiers)). It carries a `[JsonConverter]` that
points at a generated nested converter. So `System.Text.Json` writes the id as the bare value and reads it back (see
[JSON and GraphQL](#json-and-graphql)). The language provides the equality of a record struct.

The conversions are explicit on purpose. An implicit conversion would undo the type safety you asked for, because a
raw `Guid` could then go wherever an `OrderId` is expected.

[See the generated code](getting-started.md#see-the-generated-code) says how to open these files in your own project.

## Creating identifiers

```csharp
var id = OrderId.Create();            // a new id, made the way this id type makes ids: CreateSequential() for a Guid
var id = OrderId.CreateUnique();      // Guid.NewGuid()
var id = OrderId.CreateSequential();  // Guid.CreateVersion7(), .NET 9 and later
```

Code makes an id before the save. The database never makes an id. So a change and all of its events know the id from
the start, and Entity Framework stores the id as it is given
([Ids made in code](entity-framework.md#ids-made-in-code)). `Create()` makes a new id. For a
`Guid`, the generator writes `Create()`, and it makes what `CreateSequential` makes. Version 7 identifiers contain a
timestamp and sort roughly in creation order. So they keep database indexes from fragmenting the way random
identifiers do. Where a random id matters, `CreateUnique` makes one.

`Create()` also implements `ICreatableEntityId<TSelf>`. Code that is generic over ids calls this interface's static
member, `TId.Create()`, to make an id. A supporting domain such as
[Tenancy](tenancy/your-module.md#how-a-new-id-is-made) makes the ids of the tenants, seats and roles it creates this
way.

An id over another type than `Guid` has no `Create()` until you write one in its partial declaration. The generator
then implements the interface with your method. Your own `Create()` also replaces the generator's `Create()` for a
`Guid`:

```csharp
[EntityId<long>]
public readonly partial record struct TicketNumber
{
    public static TicketNumber Create() => new(TicketNumbers.Next());   // a snowflake, or a number from a HiLo block
}
```

Only a package that makes ids of a type requires that type's `Create()` ([DDD00067](diagnostics.md#ddd00067)). An id
over a `long` that you create yourself, with the public constructor, needs no `Create()`. The interface is in the
.NET 10 build of DDDToolkit.Abstractions.

## Prefixes

The optional first argument adds a prefix to the text form:

```csharp
[EntityId<Guid>("ORD")]     // ToString() → "ORD_2f1c8e9a-..."
[EntityId<Guid>]            // ToString() → "2f1c8e9a-..."
```

`Parse` and `TryParse` accept the text with or without the prefix. So an id that crossed a system boundary in either
form still round trips. The `IdPrefix` constant holds the prefix.

Prefixes are for people who read logs, URLs and support tickets. The database stores the underlying `Guid`, without
the prefix.

## Parsing

The generator writes `Parse` and `TryParse` when the wrapped type can be parsed. That is `string`, or any type with a
static `TryParse(string, IFormatProvider, out T)`. This covers the numeric types, `Guid`, `DateTime`, `DateOnly` and
similar types. Parsing uses the invariant culture, so an id written on one machine reads back on another.

If the wrapped type has no such method, the generator still writes the identifier, without the parsing members and
without `IParsable<T>`.

```csharp
var id = OrderId.Parse("ORD_2f1c8e9a-...");        // throws FormatException when malformed
if (OrderId.TryParse(candidate, out var parsed))   // false when malformed or null
{
}
```

`TryParse` does the work, and `Parse` calls it. The generated body handles the optional prefix and the invariant
culture:

**OrderId.g.cs, shortened**

```csharp
public static bool TryParse([NotNullWhen(true)] string? input, out OrderId result)
{
    result = default;
    if (input is null)
    {
        return false;
    }

    if (IdPrefix.Length != 0 && input.StartsWith(IdPrefix + "_", StringComparison.Ordinal))
    {
        input = input.Substring(IdPrefix.Length + 1);
    }

    if (!Guid.TryParse(input, CultureInfo.InvariantCulture, out var value))
    {
        return false;
    }

    result = new(value);
    return true;
}
```

The struct form implements `IParsable<T>`. So it works with generic code, and with route and query binding in ASP.NET
Core minimal APIs, which bind through `IParsable<T>` and `TryParse`. MVC controllers bind a route or query parameter of
an identifier type through the same static `TryParse`, so they need nothing extra either.

## The empty value

A struct cannot be null, so `default(OrderId)` exists and wraps `Guid.Empty`. The generator makes this value explicit
with the `Empty` and `IsEmpty` members shown above:

```csharp
OrderId id = default;
id.IsEmpty;               // true
id == OrderId.Empty;      // true
```

Check `IsEmpty` where you would check a reference id for null. Where an id is optional, use `OrderId?`. A nullable
struct stays off the heap.

## Letting the entity declare the id

Most identifiers only identify one entity. A separate declaration for such an identifier repeats what the entity
already says. Name the raw value type on the entity instead, and the toolkit generates the identifier too:

```csharp
[AggregateRoot<Guid>("ORD")]
public partial class Order { }          // also generates OrderId
```

The generator gives the entity its base class, typed on an identifier that it also writes:

**Order.g.cs, shortened**

```csharp
partial class Order : AggregateRoot<OrderId>
{
    protected Order()
    {
    }

    // ...
}
```

**OrderId.g.cs, shortened**

```csharp
[JsonConverter(typeof(OrderId.SystemTextJsonConverter))]
public readonly partial record struct OrderId : IEntityId<Guid>, IComparable<OrderId>, IParsable<OrderId>, ICreatableEntityId<OrderId>
{
    public const string IdPrefix = "ORD";

    public Guid Value { get; }

    // ...
}
```

This is the same as writing both of these declarations:

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;

[AggregateRoot<OrderId>]
public partial class Order { }
```

The generated identifier has the entity's name with `Id` appended. So `Order` gets `OrderId`, and `OrderLine` gets
`OrderLineId`. It is a `readonly partial record struct`, written by the same code as an explicit struct identifier.
So it has the same members, the same interfaces, the same JSON converter, the same Entity Framework value converter
and the same GraphQL binding. It is placed in the entity's namespace, or inside the same containing type when the
entity is nested. `[Entity<T>]` works the same way.

`Prefix` means the same as on `[EntityId<T>]`, and you can pass it by name:

```csharp
[AggregateRoot<string>(Prefix: "SKU")]
public partial class Product { }
```

There is no default prefix. An identifier without a prefix prints its bare value, like `[EntityId<Guid>]` without a
prefix. The toolkit does not make up a prefix from the type name. A prefix appears in logs, URLs and support tickets.
So you choose it once and keep it, and renaming the class must not change it.

The identifier is `partial`, so you can still add members to it in a file of your own, and attributes such as
`[GraphQLType<T>]`:

```csharp
public readonly partial record struct OrderId
{
    public string Short => Value.ToString("N")[..8];
}
```

Your part needs no accessibility modifier. It takes the modifier from the generated part. Your part must be a
`partial record struct`, and it must not carry `[EntityId<T>]`, because that attribute would declare a second
identifier with the same name. Each of these mistakes reports [DDD00007](diagnostics.md#ddd00007).

### When to declare the identifier separately

A generated identifier has no declaration of its own. You have no line to put the cursor on, no "go to definition"
target except generated code, and no place for XML documentation. That is acceptable for an identifier that only its
own aggregate uses. It is not acceptable for an identifier that other aggregates, DTOs, API contracts or message
schemas use. Such an identifier is read by people who never open the aggregate, and it needs a declaration they can
find and document.

So use the short form for an identifier that only its own aggregate uses, and the explicit form for an identifier that
other code uses. Moving between the two forms is a two-line change in either direction, and the generated identifier
stays the same.

One limit applies to both forms. The type argument must be a value type or a `string`. Any other type reports
[DDD00008](diagnostics.md#ddd00008).

## Struct or record

The toolkit supports both forms. They behave the same, and they cost different amounts.

| | `readonly partial record struct` | `partial record` |
|---|---|---|
| Allocation | None. The id is its value. | One object per id on the heap, 64 bytes. |
| Base type | None. Implements `IEntityId<TValue>`. | Derives from `EntityId<TValue>`. |
| Equality and hashing | Written by the compiler, at no cost. | Compares `Value` directly. No allocation, plus a dereference. |
| Absent value | `default`, as `Empty`/`IsEmpty` | `null` |
| Always-valid twin | No | Yes, `Valid<Name>` |
| Inheritance | Not possible | Possible |

**Prefer the struct.** An identifier is a value, and `readonly record struct` exists for values.

A struct id over a `Guid` is sixteen bytes, the same as the `Guid` it wraps. The record form costs sixty-four bytes: an
eight-byte reference, an object header, the `Guid`, the prefix, and two validation fields. The record inherits these
fields as a [value object](value-objects.md#validation), and no identifier reads them. Every read of a record id goes
through a reference, which costs about 20%. A dictionary lookup costs about half as much again. Neither form allocates
when it compares or hashes. Entity Framework is no reason to choose either form (see
[Entity Framework](#entity-framework) below). [Performance](performance.md) has the measurements.

Use `partial record` only when you need inheritance or the
[always-valid twin](value-objects.md#the-always-valid-twin). Identifiers rarely need either. Validation belongs to
value objects, and an id is either well formed or not.

The struct form cannot have a twin anyway. A twin derives from the type it validates, and a struct cannot be derived
from. A struct id needs no twin either. It does not derive from `ValueObject` and has no rules to run, so it is well
formed by construction. It also has no `TryToValid` or `TryValidate`.

## The record form

```csharp
[EntityId<Guid>("CUST")]
public partial record CustomerId
{
    public static CustomerId Create(Guid value) => new(value);
}
```

This type derives from `EntityId<Guid>`, which supplies `Value` and the prefixed `ToString`. The generator adds:

- the constructors;
- equality over `Value`;
- `Parse` and `TryParse`;
- `CreateUnique`, `CreateSequential`, and for a `Guid`, `Create()` as an overload next to the `Create(Guid)` above;
- a `ValidCustomerId` twin, which `ToValid()` returns.

Every value object gets the same twin. [Value objects](value-objects.md#the-always-valid-twin) explains its purpose.

**CustomerId.g.cs, shortened**

```csharp
partial record CustomerId : EntityId<Guid>, IValidatable<ValidCustomerId>
{
    public const string IdPrefix = "CUST";

    protected CustomerId(Guid value) : base(value, IdPrefix)
    {
    }

    [JsonConstructor]
    protected CustomerId() : base(IdPrefix)
    {
    }

    public virtual bool Equals(CustomerId? other)
    {
        // ...

        return EqualityComparer<Guid>.Default.Equals(Value, other.Value);
    }

    public override int GetHashCode() => EqualityComparer<Guid>.Default.GetHashCode(Value);

    // CreateUnique, CreateSequential, Create, Parse and TryParse, as on the struct form

    public ValidCustomerId ToValid() => new(this);
}

public partial record ValidCustomerId : CustomerId, IAlwaysValid
{
    // ...
}
```

The constructors are `protected`, so a public factory such as `Create` above is the usual pattern.

Compared with the struct form, the record form has no `Empty`/`IsEmpty`, no explicit conversion operators and no
`IComparable<T>`. A reference type has `null` for the absent value, and a cast already does the conversion.

The generator writes the equality as a direct comparison of `Value`, instead of inheriting it from `ValueObject`. So
the equality allocates nothing and boxes nothing. It still reads `Value` through a reference, which costs about half
as much again as the struct form. See [Struct or record](#struct-or-record) above and
[Performance](performance.md#dictionary-and-set-lookup) for the measurement.

## Entity Framework

Everything above works without a database. With `DDDToolkit.EntityFramework` referenced, its generator adds a value
converter to every identifier. The converter stores the identifier as its plain value, so an `OrderId` is a `Guid`
column and not a serialized object:

**OrderId.Converter.g.cs**

```csharp
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

The generator also writes one method per project that registers every converter in the project. Call that method from
`ConfigureConventions`. It is named after the project, here `Shop`:

**ConverterExtensions.g.cs, shortened**

```csharp
public static ModelConfigurationBuilder AddShopConverters(this ModelConfigurationBuilder modelConfigurationBuilder)
{
    modelConfigurationBuilder.Properties<OrderId>().HaveConversion<OrderId.OrderIdConverter>();
    modelConfigurationBuilder.DefaultTypeMapping<OrderId>().HasConversion<OrderId.OrderIdConverter>();
    // ...
    return modelConfigurationBuilder;
}
```

[Entity Framework](entity-framework.md) explains how the name is chosen, why each converter is registered twice, and
how to call the method from a context.

The record form gets the same converter, and one more for its always-valid twin. Both forms map to the same column
type, and both round trip at the same speed. The database work is orders of magnitude larger than the difference
between the two forms. The struct form saves one object per row, which is small next to the cost of reading a row.
See [Performance](performance.md#the-entity-framework-round-trip).

### An identifier stored by another project

The generator writes the nested converter into the project that declares the identifier, and only when that project
references Entity Framework. A module split into projects by layer declares its identifiers in a domain or contracts
project without Entity Framework. The project with the context stores those identifiers instead.

Every identifier, single value object and always-valid twin implements `ISingleValue<TSelf, TValue>` from
`DDDToolkit.Interfaces`. This interface names the value type and the way back from a stored value:

**OrderId.g.cs, shortened**

```csharp
readonly partial record struct OrderId : IEntityId<Guid>, ..., ISingleValue<OrderId, Guid>
{
    public Guid Value { get; }

    static OrderId ISingleValue<OrderId, Guid>.FromValue(Guid value) => new(value);
}
```

`DDDToolkit.EntityFramework` has one converter for all of these types, `SingleValueConverter<T, TValue>`. The
generated `Add{Module}Converters()` of the project with the context registers that converter for two groups of
identifiers:

- every identifier of the module's other projects that has no converter of its own;
- the published identifiers of other modules: those marked `[ModuleContract]`, and every public identifier of a
  [contracts project](modules.md#a-contracts-project).

So the context still calls one method for all of them:

**ConverterExtensions.g.cs of Ordering.Infrastructure, shortened**

```csharp
modelConfigurationBuilder.Properties<OrderId>().HaveConversion<SingleValueConverter<OrderId, Guid>>();
modelConfigurationBuilder.DefaultTypeMapping<OrderId>().HasConversion<SingleValueConverter<OrderId, Guid>>();
```

`[assembly: Module]` decides which projects belong to the module. See
[Modules](modules.md#a-module-in-layers).

- A project of the module that references Entity Framework itself keeps its nested converters and its own
  registration method. A project of the same module that references it calls that method, and does not register the
  same identifiers again.
- A published identifier of another module, whose project references Entity Framework, is registered as well, with its
  nested converter. The other module's registration method has another name, and your context's one call must cover
  every identifier the context may store.

`FromValue` is implemented explicitly. So it adds nothing to the members a caller sees, and it only serves to read
back stored values. A generic constraint makes it callable by any code, and that causes no harm. For the plain type,
`FromValue` builds a value that is not yet checked, as reading a column always did. For the always-valid twin,
`FromValue` goes through the twin's public constructor, which validates.

### Column length

```csharp
[EntityId<string>(Prefix: "SKU", ColumnLength: 32)]
public readonly partial record struct Sku;
```

`ColumnLength` is for Entity Framework only. Without the `DDDToolkit.EntityFramework` package it does nothing. It
never validates. A maximum length that a value must respect is a rule you state yourself. With the package,
`ColumnLength` becomes `HaveMaxLength` in the generated configuration, on the registration of the property and of the
type mapping:

**ConverterExtensions.g.cs, shortened**

```csharp
modelConfigurationBuilder.Properties<Sku>().HaveConversion<Sku.SkuConverter>().HaveMaxLength(32);
modelConfigurationBuilder.DefaultTypeMapping<Sku>().HasConversion<Sku.SkuConverter>().HasMaxLength(32);
```

An entity that [declares its own id](#letting-the-entity-declare-the-id) takes `ColumnLength` by name too:

```csharp
[AggregateRoot<string>(Prefix: "SKU", ColumnLength: 32)]
public partial class Product { }
```

## JSON and GraphQL

The JSON converter needs no package. It is part of the struct form's generated code, and the `[JsonConverter]`
attribute on the type attaches it. It writes the identifier as its bare value. So an `OrderId` in a response body is a
plain string that holds the `Guid`, with no prefix and no wrapping object. As a dictionary key, the converter writes
the identifier with `ToString()`, prefix included, and reads it back with `Parse`:

**OrderId.g.cs, shortened**

```csharp
[JsonConverter(typeof(OrderId.SystemTextJsonConverter))]
readonly partial record struct OrderId : IEntityId<Guid>, IComparable<OrderId>, IParsable<OrderId>
{
    // ...

    public sealed class SystemTextJsonConverter : JsonConverter<OrderId>
    {
        public override OrderId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = JsonSerializer.Deserialize<Guid>(ref reader, options);
            return new(value);
        }

        public override void Write(Utf8JsonWriter writer, OrderId value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value.Value, options);

        public override OrderId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Parse(reader.GetString() ?? throw new JsonException("Cannot convert null to OrderId."));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, OrderId value, JsonSerializerOptions options)
            => writer.WritePropertyName(value.ToString());
    }
}
```

With `DDDToolkit.HotChocolate` referenced, its generator nests two more classes in every identifier:

- a `ChangeTypeProvider`, which converts between the identifier and the value it wraps;
- a `NodeIdValueSerializer`, which lets the identifier be the key inside a Relay node id.

One generated method per project registers them:

**BindingExtensions.g.cs, shortened**

```csharp
public static IRequestExecutorBuilder AddShopGraphQlRuntimeBindings(this IRequestExecutorBuilder builder)
{
    // ...
    builder.BindRuntimeType<OrderId, UuidType>();
    builder.AddTypeConverter<OrderId.ChangeTypeProvider>();
    builder.AddNodeIdValueSerializer<OrderId.NodeIdValueSerializer>();
    return builder;
}
```

An `OrderId` is then a `UUID` in the schema. Like the JSON form, it carries no prefix. [GraphQL](graphql.md) covers
the bindings, input objects and node ids.

## Requirements

The declaration must be `partial`, and it must be a record. A plain `class` or `struct` with `[EntityId<T>]` reports
[DDD00003](diagnostics.md#ddd00003). A record struct that is not `readonly` reports
[DDD00004](diagnostics.md#ddd00004) as a warning, and the generator still writes it. Making the struct `readonly`
prevents changes and avoids defensive copies. A sealed record reports [DDD00013](diagnostics.md#ddd00013), because the
always-valid twin must derive from it.
