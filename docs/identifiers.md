# Identifiers

An order has an id, and so does the customer who placed it. If both are a `Guid`, they are the same
type to the compiler: a method that takes an order id accepts a customer id just as happily, and a
call with its arguments the wrong way round compiles, runs, and finds nothing.

```csharp
public Task Assign(Guid orderId, Guid customerId);          // Assign(customer.Id, order.Id) compiles
public Task Assign(OrderId orderId, CustomerId customerId); // the same mistake does not
```

A strongly typed identifier stops you passing a customer id where an order id belongs. Written by hand,
each one needs a constructor, formatting, parsing, comparison and a converter for every serializer and
database it meets. `[EntityId<T>]` generates all of that from a single declaration:

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;
```

The declaration has to be `partial`, so the generator can add to it, and a record. Use
`readonly partial record struct` unless you have a reason not to; there is a class form as well, and
[Struct or record](#struct-or-record) says when it is worth it. This page starts with what the struct
form gives you, then creating, printing and parsing identifiers, then letting an entity declare its
own, and ends with the class form, storage, JSON and GraphQL, and the rules a declaration must follow.

## What the struct form generates

The generator writes the other half of the type while the project compiles. For `OrderId` it writes:

```csharp title="OrderId.g.cs, shortened"
[JsonConverter(typeof(OrderId.SystemTextJsonConverter))]
readonly partial record struct OrderId : IEntityId<Guid>, IComparable<OrderId>, IParsable<OrderId>
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

Your half carries the accessibility and the attribute; the generated half carries the members. That is
why `public` appears once, on the declaration you write, and why that declaration stays one line.

The type implements `IEntityId<Guid>`, `IComparable<OrderId>` and `IParsable<OrderId>`, and carries a
`[JsonConverter]` pointing at a generated nested converter, so `System.Text.Json` writes it as the
bare value and reads it back; see [JSON and GraphQL](#json-and-graphql). Record struct equality comes
from the language.

Conversions are explicit on purpose. An implicit conversion would undo the type safety you asked for
by letting a raw `Guid` flow in wherever an `OrderId` is expected.

[See the generated code](getting-started.md#see-the-generated-code) says how to open these files in
your own project.

## Creating identifiers

```csharp
var id = OrderId.CreateUnique();      // Guid.NewGuid()
var id = OrderId.CreateSequential();  // Guid.CreateVersion7(), .NET 9 and later
```

Prefer `CreateSequential` for anything you store. Version 7 identifiers embed a timestamp and sort
roughly in creation order, which keeps database indexes from fragmenting the way random identifiers
do. Both are generated only for `Guid`; for other value types you construct the id yourself, with the
public constructor.

## Prefixes

The optional first argument prefixes the textual form:

```csharp
[EntityId<Guid>("ORD")]     // ToString() → "ORD_2f1c8e9a-..."
[EntityId<Guid>]            // ToString() → "2f1c8e9a-..."
```

`Parse` and `TryParse` accept the text with or without the prefix, so an id that crossed a system
boundary in either shape still round trips. The prefix is exposed as the `IdPrefix` constant.

Prefixes are for humans reading logs, URLs and support tickets. The stored value is the underlying
`Guid`; the prefix is not persisted.

## Parsing

`Parse` and `TryParse` are generated whenever the wrapped type can be parsed: `string`, or any type
with a static `TryParse(string, IFormatProvider, out T)`. That covers the numeric types, `Guid`,
`DateTime`, `DateOnly` and friends. Parsing uses the invariant culture, so an id written on one
machine reads back on another.

Wrap a type without such a method and the identifier still generates, just without the parsing
members and without `IParsable<T>`.

```csharp
var id = OrderId.Parse("ORD_2f1c8e9a-...");        // throws FormatException when malformed
if (OrderId.TryParse(candidate, out var parsed))   // false when malformed or null
{
}
```

`TryParse` does the work and `Parse` calls it. The generated body is where the optional prefix and the
invariant culture come from:

```csharp title="OrderId.g.cs, shortened"
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

Because the struct form implements `IParsable<T>`, it also works with generic code and with ASP.NET
Core minimal API route and query binding, which binds through `IParsable<T>` and `TryParse`. MVC
controllers bind a route or query parameter typed as an identifier through the same static
`TryParse`, so they need nothing extra either.

## The empty value

A struct has no null, so `default(OrderId)` exists and wraps `Guid.Empty`. The generator makes that
explicit rather than leaving it as a trap, with the `Empty` and `IsEmpty` members shown above:

```csharp
OrderId id = default;
id.IsEmpty;               // true
id == OrderId.Empty;      // true
```

Guard on `IsEmpty` where a reference id would have been null-checked. Where you genuinely want an
optional id, use `OrderId?`; it stays off the heap.

## Letting the entity declare the id

Most identifiers exist only to identify one entity, and declaring them separately says the same thing
twice. Name the raw value on the entity instead and the toolkit generates the identifier too:

```csharp
[AggregateRoot<Guid>("ORD")]
public partial class Order { }          // also generates OrderId
```

The generator gives the entity its base class, typed on an identifier it writes as well:

```csharp title="Order.g.cs, shortened"
partial class Order : AggregateRoot<OrderId>
{
    protected Order()
    {
    }

    // ...
}
```

```csharp title="OrderId.g.cs, shortened"
[JsonConverter(typeof(OrderId.SystemTextJsonConverter))]
public readonly partial record struct OrderId : IEntityId<Guid>, IComparable<OrderId>, IParsable<OrderId>
{
    public const string IdPrefix = "ORD";

    public Guid Value { get; }

    // ...
}
```

That is the same as writing both of these:

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;

[AggregateRoot<OrderId>]
public partial class Order { }
```

The generated identifier is named after the entity with `Id` appended, so `Order` gets `OrderId` and
`OrderLine` gets `OrderLineId`. It is a `readonly partial record struct` written by the same emitter
as an explicit struct identifier, so it has the same members, the same interfaces, the same JSON
converter, the same Entity Framework value converter and the same GraphQL binding. It lands in the
same namespace as the entity, or inside the same containing type when the entity is nested.
`[Entity<T>]` works the same way.

`Prefix` means what it means on `[EntityId<T>]`, and can be passed by name:

```csharp
[AggregateRoot<string>(Prefix: "SKU")]
public partial class Product { }
```

There is no default prefix. An identifier without one prints its bare value, exactly as
`[EntityId<Guid>]` without a prefix does. The toolkit does not invent one from the type name: a
prefix ends up in logs, URLs and support tickets, so it is a decision to make once and keep, not
something that should change the day the class is renamed.

The identifier is `partial`, so you can still add members to it from a file of your own, and
attributes such as `[GraphQLType<T>]` with them:

```csharp
public readonly partial record struct OrderId
{
    public string Short => Value.ToString("N")[..8];
}
```

Your part needs no accessibility modifier; it takes the one the generated part states. It must be a
`partial record struct`, and it must not carry `[EntityId<T>]`, because that would declare a second
identifier of the same name. Either of those reports [DDD00007](diagnostics.md#ddd00007).

### When not to use it

The identifier has no declaration site of its own. There is no line to put the cursor on, nothing to
"go to definition" on except generated code, and nothing to hang XML documentation from. That is a
fair trade for an identifier only its own aggregate ever mentions, and a bad one for an identifier
that other aggregates, DTOs, API contracts or message schemas refer to. Those are types in their own
right, read by people who never open the aggregate, and they deserve a declaration you can find and
comment on.

So: the short form for the identifier nobody talks about, and the explicit form for the identifier
everybody does. Moving from one to the other is a two-line change in either direction, and nothing
about the generated identifier changes with it.

One limit either way: the type argument must be a value type or a `string`. Anything else reports
[DDD00008](diagnostics.md#ddd00008).

## Struct or record

Both are supported. They behave the same and they do not cost the same.

| | `readonly partial record struct` | `partial record` |
|---|---|---|
| Allocation | None. The id is its value. | One object per id on the heap, 64 bytes. |
| Base type | None. Implements `IEntityId<TValue>`. | Derives from `EntityId<TValue>`. |
| Equality and hashing | Written by the compiler. Free. | Compares `Value` directly. Free, plus a dereference. |
| Absent value | `default`, exposed as `Empty`/`IsEmpty` | `null` |
| Always-valid twin | No | Yes, `Valid<Name>` |
| Inheritance | Not possible | Possible |

**Prefer the struct.** An identifier is a value, and the struct form is what `readonly record struct`
exists for.

A `Guid`-based struct id is sixteen bytes, the same as the `Guid` it wraps. The record form costs
sixty-four: an eight-byte reference, an object header, the `Guid`, the prefix, and the two validation
fields it inherits as a [value object](value-objects.md#validation) and no identifier ever reads.
Every read of a record id pays a dereference, which measures at about 20%, and a dictionary lookup
costs about half as much again. Neither form allocates when it compares or hashes. Entity Framework is
not a reason either way; see [Entity Framework](#entity-framework) below.
[Performance](performance.md) has the measurements behind all of this.

Reach for `partial record` only when you need inheritance or the
[always-valid twin](value-objects.md#the-always-valid-twin). Neither is common for identifiers:
validation belongs to value objects, and an id is either well-formed or it is not.

## The record form

```csharp
[EntityId<Guid>("CUST")]
public partial record CustomerId
{
    public static CustomerId Create(Guid value) => new(value);
}
```

This derives from `EntityId<Guid>`, which supplies `Value` and the prefixed `ToString`. The generator
adds the constructors, equality over `Value`, `Parse`/`TryParse`, `CreateUnique` and
`CreateSequential` for `Guid`, and a `ValidCustomerId` twin reachable through `ToValid()`. The twin is
the same one every value object gets; [Value objects](value-objects.md#the-always-valid-twin) explains
what it is for.

```csharp title="CustomerId.g.cs, shortened"
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

    // CreateUnique, CreateSequential, Parse and TryParse, as on the struct form

    public ValidCustomerId ToValid() => new(this);
}

public partial record ValidCustomerId : CustomerId, IAlwaysValid
{
    // ...
}
```

The constructors are `protected`, so a public factory like `Create` above is the usual pattern.

What you do not get, compared with the struct form, is `Empty`/`IsEmpty`, the explicit conversion
operators and `IComparable<T>`. A reference type has `null` for the absent value and no need for a
conversion that a cast already expresses.

Equality is generated as a direct comparison of `Value`, not inherited from `ValueObject`, so it
allocates nothing and boxes nothing. It is still a call through a reference, which costs about half as
much again as the struct form; see [Struct or record](#struct-or-record) above and
[Performance](performance.md#dictionary-and-set-lookup) for the measurement.

## Entity Framework

Everything above works without a database. Reference `DDDToolkit.EntityFramework` and its generator
adds a value converter to every identifier, which stores the identifier as its plain value, so an
`OrderId` is a `Guid` column and not a serialized object:

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

and one method per project that registers every converter in it. You call that method from
`ConfigureConventions`, and it is named after the project, here `Shop`:

```csharp title="ConverterExtensions.g.cs, shortened"
public static ModelConfigurationBuilder AddShopConverters(this ModelConfigurationBuilder modelConfigurationBuilder)
{
    modelConfigurationBuilder.Properties<OrderId>().HaveConversion<OrderId.OrderIdConverter>();
    modelConfigurationBuilder.DefaultTypeMapping<OrderId>().HasConversion<OrderId.OrderIdConverter>();
    // ...
    return modelConfigurationBuilder;
}
```

[Entity Framework](entity-framework.md) says how the name is chosen, why each converter is registered
twice, and how to wire the method into a context.

The record form gets the same converter, and one for its always-valid twin. Both forms map to the
same provider column, and both round trip at the same speed: the database work is orders of magnitude
larger than the difference between them. The struct form saves one object per row, which disappears
into what materialising a row costs anyway. See
[Performance](performance.md#the-entity-framework-round-trip).

### Column length

```csharp
[EntityId<string>(Prefix: "SKU", ColumnLength: 32)]
public readonly partial record struct Sku;
```

`ColumnLength` is for Entity Framework only. Without the `DDDToolkit.EntityFramework` package it does
nothing, and it never validates: a maximum length a value must respect is a rule you state yourself.
With the package, it flows into the generated configuration as `HaveMaxLength`, on the registration of
the property and of the type mapping:

```csharp title="ConverterExtensions.g.cs, shortened"
modelConfigurationBuilder.Properties<Sku>().HaveConversion<Sku.SkuConverter>().HaveMaxLength(32);
modelConfigurationBuilder.DefaultTypeMapping<Sku>().HasConversion<Sku.SkuConverter>().HasMaxLength(32);
```

An entity that [declares its own id](#letting-the-entity-declare-the-id) takes it by name too:

```csharp
[AggregateRoot<string>(Prefix: "SKU", ColumnLength: 32)]
public partial class Product { }
```

## JSON and GraphQL

The JSON converter needs no package: it is part of the struct form's own generated code, attached by
the `[JsonConverter]` attribute on the type. It writes the identifier as its bare value, so an
`OrderId` in a response body is a plain string holding the `Guid`, with no prefix and no wrapping
object. As a dictionary key it is written through `ToString()`, prefix included, and read back through
`Parse`:

```csharp title="OrderId.g.cs, shortened"
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

With `DDDToolkit.HotChocolate` referenced, its generator nests two more classes in every identifier: a
`ChangeTypeProvider`, which converts between the identifier and the value it wraps, and a
`NodeIdValueSerializer`, which lets the identifier be the key inside a Relay node id. One generated
method per project registers them:

```csharp title="BindingExtensions.g.cs, shortened"
public static IRequestExecutorBuilder AddShopGraphQlRuntimeBindings(this IRequestExecutorBuilder builder)
{
    // ...
    builder.BindRuntimeType<OrderId, UuidType>();
    builder.AddTypeConverter<OrderId.ChangeTypeProvider>();
    builder.AddNodeIdValueSerializer<OrderId.NodeIdValueSerializer>();
    return builder;
}
```

An `OrderId` is then a `UUID` in the schema, and like the JSON form it carries no prefix.
[GraphQL](graphql.md) covers the bindings, input objects and node ids in full.

## Requirements

The declaration must be `partial` and must be a record. A plain `class` or `struct` carrying
`[EntityId<T>]` reports [DDD00003](diagnostics.md#ddd00003). A record struct that is not `readonly`
reports [DDD00004](diagnostics.md#ddd00004) as a warning and still generates; making it readonly
prevents mutation and avoids defensive copies. A sealed record reports
[DDD00013](diagnostics.md#ddd00013), because the always-valid twin has to derive from it.
