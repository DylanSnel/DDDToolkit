# Identifiers

A strongly typed identifier stops you passing a customer id where an order id belongs. `[EntityId<T>]`
generates one from a single declaration.

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;
```

## Struct or record

Both are supported. They behave the same and they do not cost the same.

| | `readonly partial record struct` | `partial record` |
|---|---|---|
| Allocation | None. The id is its value. | One object per id on the heap, 64 bytes. |
| Base type | None. Implements `IEntityId<TValue>`. | Derives from `EntityId<TValue>`. |
| Equality and hashing | Written by the compiler. Free. | Through `GetEqualityComponents`. 144 bytes a comparison. |
| Absent value | `default`, exposed as `Empty`/`IsEmpty` | `null` |
| Always-valid twin | No | Yes, `Valid<Name>` |
| Inheritance | Not possible | Possible |

**Prefer the struct.** An identifier is a value, and the struct form is what `readonly record struct`
exists for. All of this is measured in [Performance](performance.md); the short version follows.

A `Guid`-based struct id is sixteen bytes, the same as the `Guid` it wraps. The record form costs
sixty-four: an eight-byte reference, an object header, the `Guid`, the prefix and the two validation
fields every `ValueObject` carries and no identifier ever reads. In a list of ten thousand ids that is
one contiguous block of 156 KB versus 625 KB spread over ten thousand small objects, and every read
pays a dereference, which measures at about 20%.

The larger difference is equality. A struct id compares with two instructions; the compiler writes
them and a benchmark cannot separate the call from an empty method. A record id inherits equality from
`ValueObject`, which walks `GetEqualityComponents()` through LINQ, so every comparison allocates two
iterators and boxes the value: 144 bytes and around 30 nanoseconds. A dictionary keyed by a record id
is 17 times slower than one keyed by a struct id, a `HashSet` 21 times, and scanning a list of ten
thousand comparing each one is 70 times. If you index anything by identifier in memory, this is the
reason to choose the struct, not the memory layout.

Entity Framework is **not** a reason either way. Both forms map through a generated value converter to
the same provider column, and both round trip at the same speed: the database work is orders of
magnitude larger than the difference between them. The struct form saves one object per row, which
disappears into what materialising a row costs anyway.

One measurement runs the other way. `ToString()` on a struct id currently allocates 232 bytes against
the record form's 104, because the generated struct formats its value through
`Convert.ToString(object, IFormatProvider)` and then concatenates. It is faster in time and worse in
allocation, it is a fixable detail of the generator rather than anything to do with structs, and it is
written down here because a page that argues from performance should own the number that disagrees
with it.

Reach for `partial record` only when you need inheritance or the always-valid twin. Neither is common
for identifiers: validation belongs to value objects, and an id is either well-formed or it is not.

## What the struct form generates

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;
```

```csharp
public const string IdPrefix = "ORD";
public Guid Value { get; }
public OrderId(Guid value);

public static OrderId Empty { get; }          // default
public bool IsEmpty { get; }

public static OrderId CreateUnique();         // Guid only
public static OrderId CreateSequential();     // Guid only, version 7, index friendly

public override string ToString();            // "ORD_2f1c..."
public static OrderId Parse(string input);    // prefix optional
public static bool TryParse(string? input, out OrderId result);

public int CompareTo(OrderId other);
public static explicit operator Guid(OrderId id);
public static explicit operator OrderId(Guid value);
```

The type implements `IEntityId<Guid>`, `IComparable<OrderId>` and `IParsable<OrderId>`, and carries a
`[JsonConverter]` pointing at a generated nested converter, so `System.Text.Json` writes it as the
bare value and reads it back. Record struct equality comes from the language.

Conversions are explicit on purpose. An implicit conversion would undo the type safety you asked for
by letting a raw `Guid` flow in wherever an `OrderId` is expected.

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

Because the struct form implements `IParsable<T>`, it also works with generic code and with ASP.NET
Core minimal API route and query binding, which binds through `IParsable<T>` and `TryParse`. MVC
controllers bind through `TypeConverter` instead, so a route parameter typed as an identifier there
still needs a converter of its own.

## Creating identifiers

```csharp
var id = OrderId.CreateUnique();      // Guid.NewGuid()
var id = OrderId.CreateSequential();  // Guid.CreateVersion7(), .NET 9 and later
```

Prefer `CreateSequential` for anything you store. Version 7 identifiers embed a timestamp and sort
roughly in creation order, which keeps database indexes from fragmenting the way random identifiers
do. Both are generated only for `Guid`; for other value types you construct the id yourself.

## The empty value

A struct has no null, so `default(OrderId)` exists and wraps `Guid.Empty`. The generator makes that
explicit rather than leaving it as a trap:

```csharp
public static OrderId Empty { get; }
public bool IsEmpty { get; }
```

Guard on `IsEmpty` where a reference id would have been null-checked. Where you genuinely want an
optional id, use `OrderId?`; it stays off the heap.

## The record form

```csharp
[EntityId<Guid>("CUST")]
public partial record CustomerId
{
    public static CustomerId Create(Guid value) => new(value);
}
```

This derives from `EntityId<Guid>`, which supplies `Value`, equality over the value, and the prefixed
`ToString`. The generator adds the constructors, `Parse`/`TryParse`, `CreateUnique` for `Guid`, and a
`ValidCustomerId` twin reachable through `ToValid()`.

The constructors are `protected`, so a public factory like `Create` above is the usual pattern.

Equality comes from `ValueObject`, which compares `GetEqualityComponents()`. That is a fine default for
a value object with several parts and an expensive one for an identifier with one: see
[Struct or record](#struct-or-record) above and [Performance](performance.md#dictionary-and-set-lookup)
for what it costs when the id is a dictionary key.

## Column length

```csharp
[EntityId<string>(Prefix: "SKU", ColumnLength: 32)]
public readonly partial record struct Sku;
```

`ColumnLength` flows into the generated Entity Framework configuration as `HaveMaxLength`. It has no
effect on validation and none at all without the Entity Framework package.

## Letting the entity declare the id

Most identifiers exist only to identify one entity, and declaring them separately says the same thing
twice. Name the raw value on the entity instead and the toolkit generates the identifier too:

```csharp
[AggregateRoot<Guid>("ORD")]
public partial class Order { }          // also generates OrderId
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

`Prefix` and `ColumnLength` mean what they mean on `[EntityId<T>]`, and can be passed by name:

```csharp
[AggregateRoot<string>(Prefix: "SKU", ColumnLength: 32)]
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

## Requirements

The declaration must be `partial` and must be a record. A plain `class` or `struct` carrying
`[EntityId<T>]` reports [DDD00003](diagnostics.md#ddd00003). A record struct that is not `readonly`
reports [DDD00004](diagnostics.md#ddd00004) as a warning and still generates; making it readonly
prevents mutation and avoids defensive copies. A sealed record reports
[DDD00013](diagnostics.md#ddd00013), because the always-valid twin has to derive from it.
