# Performance

[Identifiers](identifiers.md) tells you to prefer the struct form of an identifier, and argues it from
memory layout. This page is the measurement behind that advice, including the two places where the
measurement does not agree with it.

The benchmarks live in `Benchmarks/DDDToolkit.Benchmarks` and use
[BenchmarkDotNet](https://benchmarkdotnet.org). Run them yourself with:

```
dotnet build Benchmarks/DDDToolkit.Benchmarks/DDDToolkit.Benchmarks.csproj -c Release
Benchmarks/DDDToolkit.Benchmarks/bin/Release/net10.0/DDDToolkit.Benchmarks.exe --filter *
```

Both identifiers wrap a `Guid` and carry the same prefix, so nothing but the form differs:

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct StructOrderId;

[EntityId<Guid>("ORD")]
public partial record RecordOrderId
{
    public static RecordOrderId From(Guid value) => new(value);
}
```

## The machine

Every number below came from one run on one machine. Treat the ratios as the result and the absolute
times as trivia: yours will differ.

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9445/25H2)
12th Gen Intel Core i9-12900K 3.20GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.301
.NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
GC=Concurrent Workstation
```

The Entity Framework benchmarks run against SQLite in memory. That is the fastest database a benchmark
can talk to, which is the point: it makes the share of a round trip that belongs to Entity Framework
and to the identifier as large as it will ever be. On a real server across a network the same
difference is smaller, never larger.

## Creating identifiers

10,000 identifiers built from values already in hand, into an array. `RawGuid` is the floor: the same
array with no wrapper on it.

| | Mean | Allocated |
|---|---|---|
| `Guid` | 42.89 μs | 156.29 KB |
| Struct id | 42.67 μs | 156.29 KB |
| Record id | 58.54 μs | 625.02 KB |

The struct id is the raw `Guid` array, to the byte and inside the measurement error. The record id
costs **4 times the memory**: 64 bytes per identifier against 16, which is an 8-byte reference in the
array plus a 56-byte object holding the object header, the `Guid`, the prefix reference and the two
validation bookkeeping fields it inherits from `ValueObject`.

Note that 4x is worse than [Identifiers](identifiers.md#struct-or-record) implies. That page counts a
reference and an object header; it does not mention that the record form also carries the value object
validation state, which is two more fields per identifier that an identifier never uses.

The time difference here (1.37x) is mostly garbage collection, and it is the least reliable number on
this page: the record run had a standard deviation of 13.8 μs against a mean of 58.5 μs, because 625 KB
per operation puts the GC in the middle of the measurement.

## Walking a collection

10,000 identifiers in an array, compared against a target that matches the last element, so every
comparison runs.

| | Mean | Ratio | Allocated |
|---|---|---|---|
| Struct id, compare each | 3.37 μs | 1.00 | none |
| Record id, compare each | 8.46 μs | 2.51 | none |
| Struct id, read `.Value` | 7.23 μs | 2.15 | none |
| Record id, read `.Value` | 8.63 μs | 2.57 | none |

Reading the value through a reference costs about 20%, which is the dereference the page predicts.
Comparing costs 2.5 times, which is the same dereference paid twice plus the call.

**This measurement used to read 69.6, with 1,440,000 bytes allocated.** A record identifier inherited
equality from `ValueObject`, which compares `GetEqualityComponents()` sequences:

```csharp
return Enumerable.SequenceEqual(GetEqualityComponents(), other.GetEqualityComponents());
```

Two iterator objects, two boxed `Guid`s and a LINQ call, for every comparison. That is a reasonable
default for a value object with several components and absurd for an identifier wrapping one `Guid`,
and nobody had measured it. The generators now emit a direct comparison of `Value` for entity ids and
single value objects, which yields the same answer because both types have exactly one component. The
numbers above are from after that change.

## Dictionary and set lookup

1,000 hits against a container holding 10,000 entries.

| | Mean | Ratio | Allocated |
|---|---|---|---|
| `Dictionary` keyed by struct id | 3.53 μs | 1.00 | none |
| `Dictionary` keyed by record id | 5.35 μs | 1.52 | none |
| `HashSet` of struct ids | 3.55 μs | 1.01 | none |
| `HashSet` of record ids | 4.92 μs | 1.40 | none |

Every probe pays for a hash and at least one equality check, so this tracked the defect above
exactly: it used to read 17.5 and 21.1, with 272,000 bytes allocated by something whose whole job is
to be a fast index. With the direct comparison in place the record form costs about half as much
again as the struct form, which is the dereference and the call, and allocates nothing.

## Equality and hashing on their own

One comparison and one hash, undiluted.

| | Mean | Allocated |
|---|---|---|
| Struct id `==` | 0.20 ns | none |
| Record id `==` | 0.23 ns | none |
| Struct id `GetHashCode` | not measurable | none |
| Record id `GetHashCode` | 0.19 ns | none |

Both forms are now effectively free, and the numbers here are at the edge of what the harness can
resolve: BenchmarkDotNet reports the struct hash as *"the method duration is indistinguishable from
the empty method duration"*, so there is no honest figure to quote for it.

Before the generators emitted a direct comparison, the record form measured 30.47 ns and 144 bytes for
an equality check and 23.81 ns and 128 bytes for a hash. Those two lines were the whole of the
17x-to-70x gap in the two sections above.

## Text

`ToString` and `Parse`, the operations at the edges of the system.

| | Mean | Allocated |
|---|---|---|
| Struct id `ToString()` | 15.52 ns | 104 B |
| Record id `ToString()` | 19.18 ns | **104 B** |
| Struct id `Parse` | 23.62 ns | 96 B |
| Record id `Parse` | 28.11 ns | 152 B |

**This one goes against the recommendation, and it is a real finding.** The struct identifier's
`ToString` allocates 2.2 times what the record identifier's does.

It is not the struct's fault, it is the generator's. The struct form emits:

```csharp
public override string ToString()
    => IdPrefix.Length == 0 ? ValueToString() : IdPrefix + "_" + ValueToString();

private string ValueToString() => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? string.Empty;
```

`Convert.ToString(object, IFormatProvider)` boxed the `Guid` and built the 36-character string, and the
concatenation then built a second 40-character string: three allocations against the record form's one,
which went through an interpolated string. A struct identifier written into a log line on every request
paid 232 bytes for it.

The generator now writes the interpolated form too, pinned to the invariant culture:

```csharp
public override string ToString()
    => IdPrefix.Length == 0
        ? string.Create(CultureInfo.InvariantCulture, $"{Value}")
        : string.Create(CultureInfo.InvariantCulture, $"{IdPrefix}_{Value}");
```

Nothing is boxed, one string comes out, and both forms now allocate 104 bytes. The table above is from
after that change.

## The Entity Framework round trip

The claim under test is the last paragraph of
[Identifiers](identifiers.md#struct-or-record): *"the struct form costs nothing in persistence"*. Both
aggregates are the same shape, with the same payload column, keyed differently:

```csharp
[AggregateRoot<StructOrderId>] public partial class StructOrder { ... }
[AggregateRoot<RecordOrderId>] public partial class RecordOrder { ... }
```

### Inserting

1,000 aggregates added and saved, against a database created fresh for each measured invocation.

| | Mean | Allocated |
|---|---|---|
| Struct id | 30.26 ms ± 3.68 | 7.38 MB |
| Record id | 31.44 ms ± 4.15 | 7.71 MB |

The times overlap. Do not read a winner into them: the error bars are ±12%, the distribution is
bimodal, and a database write is not a thing you measure to three significant figures. The allocation
figure is deterministic and says what there is to say: 4% more, which is the 1,000 identifier objects.

### Reading

2,000 rows in the table. "Read all" materialises every row through a new context; "find by key" does
100 single-row lookups.

| | Mean | Allocated |
|---|---|---|
| Read all, struct id | 1.52 ms | 1090.03 KB |
| Read all, record id | 1.39 ms | 1293.20 KB |
| Find by key, struct id | 1.11 ms | 888.16 KB |
| Find by key, record id | 1.20 ms | 899.90 KB |

The claim holds, with one correction to how it is phrased. **The struct identifier costs nothing in
persistence, and neither does the record identifier.** Both go through the same generated value
converter to the same provider column (the provider test suite asserts that the two produce the same
column type), and the database work swamps everything either of them does. The 19% extra allocation on
the record read is one object per row, and it disappears into the 1 MB that materialising 2,000 rows
costs anyway.

If you were choosing between the two forms on persistence alone, there would be nothing to choose.

## What this means for the recommendation

[Identifiers](identifiers.md) says to prefer the struct form. The measurements support that, but not
always for the reasons the page gives:

| Claim | Verdict |
|---|---|
| The struct is 16 bytes and the record adds a reference and a header | True, and understated: 64 bytes against 16, because the record also carries validation state. |
| A list of ten thousand is one block instead of ten thousand objects | True. Reading through the reference costs about 20%. |
| The struct costs nothing in persistence | True. So does the record: this is not a reason to choose either. |
| (unstated) Equality and hashing | Was the strongest reason by far, at 17x to 70x with allocation on every comparison. Writing this page found the cause and it is fixed; the gap is now 1.4x to 2.5x with nothing allocated. |
| (unstated) `ToString` | Was the one place the record form won, because of a fixable inefficiency in the generated struct. Also fixed; both allocate 104 bytes. |

The short version: choose the struct form to avoid an object per identifier and a dereference on every
read, not for the database, and no longer because equality is catastrophic. Two of the five rows in
this table describe defects that existed only because nobody had measured, which is the case for
keeping the benchmarks in the repository rather than running them once.

## Provider coverage

The benchmarks above are SQLite only. Correctness on other providers is a separate question, and it is
answered by a separate suite: `Tests/DDDToolkit.EntityFramework.Providers.Tests` runs the mapping,
concurrency, outbox and inbox tests against real PostgreSQL 17 and SQL Server 2022 in
[Testcontainers](https://dotnet.testcontainers.org). Without Docker every test in it skips with a
message naming the reason, so a machine without Docker stays green and stays honest about it.

Three things that suite pins down and SQLite never could:

- **The `ddd` schema is real.** SQLite has no schemas and silently drops the argument, so the default
  the outbox and inbox ship with was never exercised. It works: `EnsureCreated` creates the schema and
  both tables land in it on both servers.
- **A struct id and a record id produce the same column.** `uuid` on PostgreSQL,
  `uniqueidentifier` on SQL Server, for the key, for a plain property and for a nullable one. This is
  what the persistence numbers above are measuring the other half of.
- **The outbox timestamp conversion costs SQL Server and not PostgreSQL.** The outbox stores every
  timestamp as a UTC `DateTime` because SQLite cannot order by `DateTimeOffset`. On PostgreSQL both
  land in `timestamp with time zone`, so the workaround is free. On SQL Server the column is
  `datetime2` where a `DateTimeOffset` would have been `datetimeoffset`: the offset is not stored,
  which is harmless while everything is UTC and is still a column shape SQL Server users did not
  choose.

A fourth is a difference rather than a cost: a primitive collection of converted ids becomes a native
`integer[]` on PostgreSQL and a JSON `nvarchar` on SQL Server. Both round trip, and a query that
reaches inside one will not port.

## Things this page does not measure

- **Other providers.** Everything here is SQLite in memory. Mapping behaviour on PostgreSQL and SQL
  Server is covered by the suite above, but nothing times it.
- **The generators.** Build-time cost of the source generators is not measured anywhere.
- **The outbox.** Throughput of `OutboxProcessor` under load, and what the `ProcessedAt` index does as
  the table grows, are open questions.
- **JSON and GraphQL.** The generated converters are not benchmarked.
- **Value objects.** `ValueObject` equality has the same `GetEqualityComponents` cost measured above,
  and value objects are compared far less often than identifiers, but nobody has checked.
