# Performance

[Identifiers](identifiers.md) tells you to prefer the struct form of an identifier, and argues it from
memory layout. This page is the measurement behind that advice, including the two places where the
measurement does not agree with it. The [Tenancy section](#tenancy-the-shape-of-the-access-data) measures
something else: the database shapes behind the Tenancy supporting domain.

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

That is the same 64 bytes [Identifiers](identifiers.md#struct-or-record) counts. The two fields at the
end are easy to forget: the record form carries the value object validation state, two fields per
identifier that an identifier never uses.

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

## Reading a read-only collection

`order.Lines` hands out a read-only view over the generated backing field. `List<T>.AsReadOnly()` is
`new ReadOnlyCollection<T>(this)` and caches nothing, so a property written as `=> _lines.AsReadOnly()`
builds a wrapper on every read. The generator holds the view in a second field instead.

Sixteen reads of one property:

| | Time | Allocated |
|---|---|---|
| A wrapper per read | 68.2 ns | 384 B |
| The wrapper held in a field | 10.5 ns | 0 B |
| The bare list, no protection | 5.2 ns | 0 B |

384 bytes is exactly 16 wrappers of 24. The element count does not change any of it: at 4 elements and
at 256 the numbers are the same, because the wrapper wraps rather than copies.

The third row is not an option, only the floor. Returning `_lines` allocates nothing and satisfies
`IReadOnlyList<T>`, but a caller can cast it back to `List<T>` and mutate the aggregate around every
invariant it has. The 5 nanoseconds between the second row and the third are what that protection
costs once it is no longer rebuilt per read.

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

The question is whether the form of the identifier matters to persistence at all.
[Identifiers](identifiers.md#struct-or-record) says it does not, on the strength of this measurement.
Both aggregates are the same shape, with the same payload column, keyed differently:

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

[Identifiers](identifiers.md) says to prefer the struct form. These are the reasons that
recommendation has rested on, and what the measurements say about each:

| Claim | Verdict |
|---|---|
| The struct is 16 bytes and the record adds a reference and a header | True, and more than it sounds: 64 bytes against 16, because the record also carries validation state. |
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

## Tenancy: the shape of the access data

The [Tenancy](tenancy.md) supporting domain stores who holds what as facts, starts every question from the
person asking, and keeps the organization as a closure table. These are the measurements those choices
rest on. They compare it with the shape a permission model tends to grow into: a table of what every
person may do on every project, kept up to date by triggers, a copy of the organization path on every
project, and row level security policies that call a function for every row.

### The data

The same data both ways, and both give every person the same answer:

- one large tenant: 3,111 units in five levels (a root, 10 regions, 100 branches, 1,000 team units and
  2,000 sub-team units), 49,776 projects, 1,000 seats, and a project team of three on every project, a
  lead, an inspector and a viewer;
- 20 regional managers who hold `project.update` at a branch, and a director who holds it at the root;
- 49 small tenants of 111 units and 999 projects each, 98,727 projects in all.

### Storage, and the list of my projects

Measured on PostgreSQL 17 for two people: a regional manager, who holds the key at one branch and is on
some project teams as well, and sees 643 projects; and a field worker, who holds no key in the
organization and is on the project team of 150 projects.

| | Per-person table, per-row policies | Per-person table, set-shaped policy | Facts, set-shaped |
|---|---:|---:|---:|
| Storage | 411 MB | 411 MB | 60 MB |
| "My projects", regional manager | 10,900 ms | 185 ms | 5.2 ms |
| "My projects", field worker | 10,364 ms | 155 ms | 4.6 ms |
| Buffers touched (shared hits and reads), regional manager's list | 1,243,555 | 450,277 | 1,019 |
| 50 projects opened one by one, total, regional manager | 27 ms | 9,126 ms | 24 ms |
| 50 projects opened one by one, total, field worker | 26 ms | 7,356 ms | 26 ms |
| Growth of the connection's memory | +1.0 MB | +0.8 MB | +0.6 MB |
| Cached plans in the connection | 293 KB | 49 KB | none |

Three things account for the difference:

- **Storing outcomes is what costs the space.** The table of what every person may do on every project,
  one row per person, key and project, is 256 MB on its own. The facts it is computed from, the teams and
  the keys of each role, are 39 MB.
- **A policy that runs per row reads the whole tenant.** Asked for a list, it runs its function once for
  each of the tenant's projects, whatever the person may see. Rewritten set-shaped, the policy computes
  the set of permitted projects once per statement, which fixes the list and breaks the other case: a
  single project now pays for the whole set, 9 seconds for 50 of them. Starting from the person is cheap
  both ways, because the set is only as large as what that person may do.
- **What a connection keeps is multiplied by the connections.** PostgreSQL gives every connection a
  process of its own, and each keeps its cached plans and catalogue entries for as long as it lives. The
  per-row policies call PL/pgSQL functions, whose plans are cached per connection. The facts version's
  set functions are SQL functions that run once per statement, for which PostgreSQL 17 keeps no cached
  plan. A connection grew by 0.6 MB instead of 1.0 to 1.1 MB: small per connection, and it adds up with
  hundreds of them.

### The organization tree

The tree was measured on data of its own: the same tenants, units and projects, no seats or project teams,
and three people who hold `project.update`: a regional manager at two branches, who sees 992 projects,
the director at the root, and the lead of one team unit. On PostgreSQL, the median execution time of seven
runs, per way to ask "which projects are under the units where I hold the key":

| Who, and how many projects | `ltree` | Text path, a range | Closure table | Path copied on every project, per row |
|---|---:|---:|---:|---:|
| Regional manager, 992 | 0.71 ms | 0.40 ms | 0.33 ms | 21.49 ms |
| Director at the root, 49,776 | 10.41 ms | 10.23 ms | 10.31 ms | 16.15 ms |
| Team lead of one team unit, 48 | 0.22 ms | 0.16 ms | 0.14 ms | 15.22 ms |

The three ways of storing the tree are equally fast. The path copied onto every project is slower for
everyone, the director included, because it checks every project of the tenant whatever the person may
see. Moving a branch with its 30 units under another region takes 0.4 ms with a text path, 0.7 ms with
`ltree` and 3.3 ms with the closure table, which replaces the 62 rows that link the moved units to the
ancestors they leave and gain. No project is touched in any of them.

The closure table is the one that works everywhere. It is plain joins on ids: no extension, no collation
that decides whether a range query is right, and no limit on the length of an index key. The same query
on SQL Server 2022, against a text path with a binary collation, and on SQLite in memory:

| Who | SQL Server, text path | SQL Server, closure table | SQLite, text path | SQLite, closure table |
|---|---:|---:|---:|---:|
| Regional manager | 1.28 ms | 1.27 ms | 0.05 ms | 0.04 ms |
| Director at the root | 4.00 ms | 4.01 ms | 2.52 ms | 1.88 ms |
| Team lead of one team unit | 1.24 ms | 1.26 ms | 0.01 ms | 0.01 ms |

Compare the columns of one database, not the databases: SQL Server's times are taken inside the server,
200 executions at a time because its clock moves in steps of about 4 ms, and SQLite runs in the process,
in memory.

### How it was measured

On the machine above, with PostgreSQL 17 and SQL Server 2022 in Docker, one session at a time and a warm
cache. This is not a load test: how the shapes behave with many people at once is measured when the
Postgres layer is built.

The tree comparison is in `Benchmarks/Tenancy`, with a `README.md` that says how to run it and how each
number is taken. The storage and list comparison has no script in the repository: its per-person table
reproduces a production schema that is not the toolkit's to publish, so its numbers are given as measured
while Tenancy was designed.

## Things this page does not measure

- **Other providers.** Everything before the Tenancy section is SQLite in memory. Mapping behaviour on
  PostgreSQL and SQL Server is covered by the suite above, but nothing times it.
- **The generators.** Build-time cost of the source generators is not measured anywhere.
- **The outbox.** Throughput of `OutboxProcessor` under load, and what the `ProcessedAt` index does as
  the table grows, are open questions.
- **JSON and GraphQL.** The generated converters are not benchmarked.
- **Value objects.** `ValueObject` equality has the same `GetEqualityComponents` cost measured above,
  and value objects are compared far less often than identifiers, but nobody has checked.
