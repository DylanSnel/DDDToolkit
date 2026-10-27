# Performance

[Identifiers](identifiers.md) recommends the struct form of an identifier, because of its memory layout. This page
has the measurements behind that advice, including two places where the measurements first disagreed with it. The
[Tenancy section](#tenancy-the-shape-of-the-access-data) measures the database shapes behind the Tenancy
supporting domain.

The benchmarks are in `Benchmarks/DDDToolkit.Benchmarks` and use [BenchmarkDotNet](https://benchmarkdotnet.org).
Run them with:

```
dotnet build Benchmarks/DDDToolkit.Benchmarks/DDDToolkit.Benchmarks.csproj -c Release
Benchmarks/DDDToolkit.Benchmarks/bin/Release/net10.0/DDDToolkit.Benchmarks.exe --filter *
```

Both identifiers wrap a `Guid` and have the same prefix. Only the form differs:

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct StructOrderId;

[EntityId<Guid>("ORD")]
public partial record RecordOrderId
{
    public static RecordOrderId From(Guid value) => new(value);
}
```

## Test machine

Every number below comes from one run on one machine. Compare the ratios. The absolute times will differ on your
machine.

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9445/25H2)
12th Gen Intel Core i9-12900K 3.20GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.301
.NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
GC=Concurrent Workstation
```

The Entity Framework benchmarks run against SQLite in memory, the fastest database a benchmark can use. So the
share of a round trip that Entity Framework and the identifier take is as large as it can be. On a real server
across a network, the same difference is smaller.

## Creating identifiers

The benchmark builds 10,000 identifiers from existing values, into an array. The `Guid` row (`RawGuid` in the
benchmark) is the floor: the same array without a wrapper.

| | Mean | Allocated |
|---|---|---|
| `Guid` | 42.89 μs | 156.29 KB |
| Struct id | 42.67 μs | 156.29 KB |
| Record id | 58.54 μs | 625.02 KB |

- The struct id costs the same as the raw `Guid` array. It allocates the same bytes, and its time is within the
  measurement error.
- The record id costs **4 times the memory**, 64 bytes per identifier against 16. That is an 8-byte reference in
  the array plus a 56-byte object. The object holds the object header, the `Guid`, the prefix reference, and two
  validation fields that it inherits from `ValueObject`.

[Identifiers](identifiers.md#struct-or-record) counts the same 64 bytes. The two validation fields, `_isValid` and
`_validationErrors`, hold the validation state of a value object. An identifier never uses them.

The time difference here (1.37x) is mostly garbage collection. It is the least reliable number on this page. The
record run had a standard deviation of 13.8 μs against a mean of 58.5 μs, because 625 KB per operation makes the
garbage collector run during the measurement.

## Comparing ids in an array

The benchmark compares 10,000 identifiers in an array with a target that matches the last element, so every
comparison runs.

| | Mean | Ratio | Allocated |
|---|---|---|---|
| Struct id, compare each | 3.37 μs | 1.00 | none |
| Record id, compare each | 8.46 μs | 2.51 | none |
| Struct id, read `.Value` | 7.23 μs | 2.15 | none |
| Record id, read `.Value` | 8.63 μs | 2.57 | none |

Reading the value through a reference costs about 20% more. That is the dereference that
[Identifiers](identifiers.md) predicts. Comparing costs 2.5 times as much, for the same dereference twice plus the
call.

**The ratio of the record id used to be 69.6, with 1,440,000 bytes allocated.** A record identifier inherited its
equality from `ValueObject`, which compares the sequences of `GetEqualityComponents()`:

```csharp
return Enumerable.SequenceEqual(GetEqualityComponents(), other.GetEqualityComponents());
```

Every comparison made two iterator objects, boxed two `Guid`s and called LINQ. That fits a value object with
several components. For an identifier that wraps one `Guid`, it was far too expensive, and nobody had measured it.
The generators now write a direct comparison of `Value` for entity ids and single value objects. The answer is
the same, because both types have one component. The numbers above are from after that change.

## Reading a read-only collection

`order.Lines` returns a read-only view over the generated backing field. `List<T>.AsReadOnly()` is
`new ReadOnlyCollection<T>(this)` and caches nothing. So a property written as `=> _lines.AsReadOnly()` builds a
new wrapper on every read. The generator keeps the view in a second field instead.

The benchmark reads one property 16 times:

| | Time | Allocated |
|---|---|---|
| A wrapper per read | 68.2 ns | 384 B |
| The wrapper held in a field | 10.5 ns | 0 B |
| The bare list, no protection | 5.2 ns | 0 B |

384 bytes is 16 wrappers of 24 bytes. The number of elements changes nothing. At 4 elements and at 256 the
numbers are the same, because the wrapper does not copy the list.

The third row is the floor, and no real option. Returning `_lines` allocates nothing and satisfies
`IReadOnlyList<T>`. But a caller can cast it back to `List<T>` and change the aggregate past every invariant it
has. The 5 nanoseconds between the second and the third row are the cost of that protection, now that the
wrapper is built once.

## Dictionary and set lookup

The benchmark makes 1,000 lookups that hit, in a container of 10,000 entries.

| | Mean | Ratio | Allocated |
|---|---|---|---|
| `Dictionary` keyed by struct id | 3.53 μs | 1.00 | none |
| `Dictionary` keyed by record id | 5.35 μs | 1.52 | none |
| `HashSet` of struct ids | 3.55 μs | 1.01 | none |
| `HashSet` of record ids | 4.92 μs | 1.40 | none |

Every lookup computes a hash and makes at least one equality check. So this benchmark showed the same defect as
the comparison above. The ratios of the record id used to be 17.5 and 21.1, with 272,000 bytes allocated. With the
direct comparison, the record form costs about 1.5 times the struct form, for the dereference and the call, and
allocates nothing.

## Equality and hashing

One comparison and one hash, measured alone.

| | Mean | Allocated |
|---|---|---|
| Struct id `==` | 0.20 ns | none |
| Record id `==` | 0.23 ns | none |
| Struct id `GetHashCode` | not measurable | none |
| Record id `GetHashCode` | 0.19 ns | none |

Both forms now cost close to nothing, and these numbers are at the limit of what BenchmarkDotNet can measure. For
the struct hash it reports *"the method duration is indistinguishable from the empty method duration"*, so the
table gives no figure for it.

Before the generators wrote a direct comparison, an equality check of the record form took 30.47 ns and 144 bytes,
and a hash took 23.81 ns and 128 bytes. Those two costs caused the whole gap of 17x to 70x in the two sections
above.

## ToString and Parse

`ToString` and `Parse`, the operations at the edges of the system:

| | Mean | Allocated |
|---|---|---|
| Struct id `ToString()` | 15.52 ns | 104 B |
| Record id `ToString()` | 19.18 ns | **104 B** |
| Struct id `Parse` | 23.62 ns | 96 B |
| Record id `Parse` | 28.11 ns | 152 B |

**This measurement went against the recommendation.** The `ToString` of the struct identifier used to allocate
2.2 times as much as that of the record identifier: 232 bytes against 104.

The cause was the generated code of the struct. The generator used to write:

```csharp
public override string ToString()
    => IdPrefix.Length == 0 ? ValueToString() : IdPrefix + "_" + ValueToString();

private string ValueToString() => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? string.Empty;
```

`Convert.ToString(object, IFormatProvider)` boxed the `Guid` and built the 36-character string. The concatenation
then built a second string of 40 characters. That made three allocations, against one for the record form, which
used an interpolated string. A struct identifier written into a log line on every request cost 232 bytes.

The generator now writes the interpolated form for the struct too, with the invariant culture:

```csharp
public override string ToString()
    => IdPrefix.Length == 0
        ? string.Create(CultureInfo.InvariantCulture, $"{Value}")
        : string.Create(CultureInfo.InvariantCulture, $"{IdPrefix}_{Value}");
```

Nothing is boxed, and one string is built. Both forms now allocate 104 bytes. The table above is from after that
change.

## The Entity Framework round trip

This section measures whether the form of the identifier matters to persistence.
[Identifiers](identifiers.md#struct-or-record) says it does not, based on this measurement. Both aggregates have
the same shape and the same payload column. Only the key differs:

```csharp
[AggregateRoot<StructOrderId>] public partial class StructOrder { ... }
[AggregateRoot<RecordOrderId>] public partial class RecordOrder { ... }
```

### Inserting

The benchmark adds and saves 1,000 aggregates, in a new database for each measured run.

| | Mean | Allocated |
|---|---|---|
| Struct id | 30.26 ms ± 3.68 | 7.38 MB |
| Record id | 31.44 ms ± 4.15 | 7.71 MB |

The times overlap, so neither form wins. The error bars are ±12%, the distribution has two peaks, and a database
write cannot be measured to three significant figures. The allocation is deterministic. The record form allocates
4% more, for the 1,000 identifier objects.

### Reading

The table has 2,000 rows. "Read all" loads every row through a new context. "Find by key" makes 100 lookups of one
row each.

| | Mean | Allocated |
|---|---|---|
| Read all, struct id | 1.52 ms | 1090.03 KB |
| Read all, record id | 1.39 ms | 1293.20 KB |
| Find by key, struct id | 1.11 ms | 888.16 KB |
| Find by key, record id | 1.20 ms | 899.90 KB |

The claim holds, with one correction. **Neither form costs anything in persistence.**

- Both forms go through the same generated value converter to the same column type. The provider test suite
  checks that the two forms produce the same column type.
- The database work is far larger than anything either form adds.
- The record form allocates 19% more when it reads all rows. That is one object per row, small next to the 1 MB
  that loading 2,000 rows costs anyway.

So persistence is no reason to choose either form.

## Summary: struct or record

[Identifiers](identifiers.md) recommends the struct form. The table lists the reasons for that recommendation, and
what the measurements say about each:

| Claim | Verdict |
|---|---|
| The struct is 16 bytes, and the record adds a reference and a header. | True, and the difference is larger: 64 bytes against 16, because the record also carries validation state. |
| A list of ten thousand is one block instead of ten thousand objects. | True. Reading through the reference costs about 20%. |
| The struct costs nothing in persistence. | True. The record costs nothing either, so this is no reason to choose one form. |
| (unstated) Equality and hashing | This was the strongest reason, at 17x to 70x with an allocation on every comparison. The measurements for this page found the cause, and it is fixed. The gap is now 1.4x to 2.5x, with nothing allocated. |
| (unstated) `ToString` | This was the one place where the record form won, because of an inefficiency in the generated struct code. That is fixed too. Both forms allocate 104 bytes. |

Choose the struct form to avoid an object per identifier and a dereference on every read. Persistence is no reason
for either form, and equality is no longer a reason. Two of the five rows describe defects that existed because
nobody had measured. That is why the benchmarks stay in the repository.

## Provider coverage

The benchmarks above run on SQLite only. A separate test suite checks the mapping on other providers.
`Tests/DDDToolkit.EntityFramework.Providers.Tests` runs the mapping, concurrency, outbox and inbox tests against
PostgreSQL 17 and SQL Server 2022 in [Testcontainers](https://dotnet.testcontainers.org).

- Without Docker, every test in the suite skips with a message that names the reason.
- With the environment variable `DDDTOOLKIT_REQUIRE_CONTAINERS` set, a container that does not start fails the
  test instead.

The suite checks three things that SQLite cannot:

- **The `ddd` schema works.** SQLite has no schemas and ignores the schema argument without an error. So the
  default schema of the outbox and the inbox was never tested there. On both servers, `EnsureCreated` creates the
  schema, and creates both tables in it.
- **A struct id and a record id produce the same column.** The column is `uuid` on PostgreSQL and
  `uniqueidentifier` on SQL Server, for the key, for a plain property and for a nullable one. This is the other
  half of the persistence numbers above.
- **The outbox and the inbox keep their timestamps in the provider's own instant type.** That is
  `timestamp with time zone` on PostgreSQL and `datetimeoffset` on SQL Server. The toolkit converts to a UTC
  `DateTime` on SQLite only, because SQLite cannot order by a `DateTimeOffset`. A database made with
  `DomainEventTimestamps.UtcDateTime` keeps its `datetime2` columns on SQL Server, and both shapes read back the
  same instant ([Outbox and inbox timestamps](migrating-to-3.md#outbox-and-inbox-timestamps)).

The suite also shows one difference between the providers. A primitive collection of converted ids becomes a
native `integer[]` on PostgreSQL and a JSON `nvarchar` on SQL Server. Both round trip, and a LINQ query over the
collection works on both providers. A SQL query that reads inside the collection needs a different form for each
provider.

## Tenancy: the shape of the access data

The [Tenancy](tenancy/overview.md) supporting domain makes three choices:

- it stores facts, such as which seat holds which role at which unit;
- it starts every query from the person who asks;
- it keeps the organization tree as a closure table.

The measurements below support these choices. They compare Tenancy's shape with a shape that permission models
often grow into:

- a table of what every person may do on every project, kept up to date by triggers;
- a copy of the organization path on every project;
- row level security (RLS) policies that call a function for every row.

### The data

Both shapes hold the same data, and both give every person the same result:

- one large tenant: 3,111 units in five levels (the root, 10 regions, 100 areas, 1,000 units below the areas and
  2,000 below those), 49,776 projects, 1,000 seats, and a crew of three on every project: a crew lead, a surveyor
  and an observer;
- 20 area managers who hold `projects.edit` at an area, and one who holds it at the root;
- 49 small tenants of 111 units and 999 projects each, 98,727 projects in all.

### Storage and the project list

Measured on PostgreSQL 17, for two people:

- an area manager, who holds the permission at one area, is also on some crews, and sees 643 projects;
- a surveyor, who holds no permission in the organization, and is on the crew of 150 projects.

| | Per-person table, per-row policies | Per-person table, set-shaped policy | Facts, set-shaped |
|---|---:|---:|---:|
| Storage | 411 MB | 411 MB | 60 MB |
| "My projects", area manager | 10,900 ms | 185 ms | 5.2 ms |
| "My projects", surveyor | 10,364 ms | 155 ms | 4.6 ms |
| Buffers touched (shared hits and reads), area manager's list | 1,243,555 | 450,277 | 1,019 |
| 50 projects opened one by one, total, area manager | 27 ms | 9,126 ms | 24 ms |
| 50 projects opened one by one, total, surveyor | 26 ms | 7,356 ms | 26 ms |
| Growth of the connection's memory | +1.0 MB | +0.8 MB | +0.6 MB |
| Cached plans in the connection | 293 KB | 49 KB | none |

Three causes explain the difference:

- **Storing outcomes costs the space.** The table of what every person may do on every project has one row per
  person, permission and project. It is 256 MB on its own. The facts it is computed from, the crews and the
  permissions of each role, take 39 MB.
- **A policy that runs per row reads the whole tenant.** For a list, the policy calls its function once for each
  project of the tenant, whatever the person may see. A set-shaped policy computes the set of permitted projects
  once per statement. That makes the list fast, and makes the other case slow. A single project now pays for the
  whole set, 9 seconds for 50 projects. Starting from the person is fast in both cases, because the set is only as
  large as what that person may do.
- **Memory per connection is multiplied by the number of connections.** PostgreSQL runs every connection in its
  own process, and each process keeps its cached plans and catalogue entries for its lifetime. The per-row
  policies call PL/pgSQL functions, whose plans each connection caches. The set functions of the facts shape are
  SQL functions that run once per statement, and PostgreSQL 17 caches no plan for them. A connection grew by
  0.6 MB instead of 1.0 to 1.1 MB. That is small per connection, and it adds up over hundreds of connections.

### The organization tree

The tree has its own data set: the same tenants, units and projects, without seats or crews. Three people hold
`projects.edit`:

- an area manager at two areas, who sees 992 projects;
- an area manager at the root;
- a crew lead at one unit below an area.

The query asks which projects are below the units where the person holds the permission. The table shows the
median execution time of seven runs on PostgreSQL, for each way to store the tree:

| Who, and how many projects | `ltree` | Text path, a range | Closure table | Path copied on every project, per row |
|---|---:|---:|---:|---:|
| Area manager at two areas, 992 | 0.71 ms | 0.40 ms | 0.33 ms | 21.49 ms |
| Area manager at the root, 49,776 | 10.41 ms | 10.23 ms | 10.31 ms | 16.15 ms |
| Crew lead at one unit, 48 | 0.22 ms | 0.16 ms | 0.14 ms | 15.22 ms |

- The three ways to store the tree are equally fast.
- The path copied onto every project is slower for everyone, the manager at the root included, because the query
  checks every project of the tenant.
- Moving an area with its 30 units below another region takes 0.4 ms with a text path, 0.7 ms with `ltree`, and
  3.3 ms with the closure table. The closure table replaces the 62 rows that link the moved units to the units
  above them that they leave and gain. None of the three changes a project row.

The closure table works on every database. It uses plain joins on ids. It needs no extension and no collation for
a range query, and it has no limit on the length of an index key. The same query on SQL Server 2022, against a
text path with a binary collation, and on SQLite in memory:

| Who | SQL Server, text path | SQL Server, closure table | SQLite, text path | SQLite, closure table |
|---|---:|---:|---:|---:|
| Area manager at two areas | 1.28 ms | 1.27 ms | 0.05 ms | 0.04 ms |
| Area manager at the root | 4.00 ms | 4.01 ms | 2.52 ms | 1.88 ms |
| Crew lead at one unit | 1.24 ms | 1.26 ms | 0.01 ms | 0.01 ms |

Compare the columns within one database, and do not compare the databases. SQL Server's times are taken inside
the server, over 200 executions at a time, because its clock moves in steps of about 4 ms. SQLite runs in the
process, in memory.

### How it was measured

On the test machine above, with PostgreSQL 17 and SQL Server 2022 in Docker, one session at a time, and a warm
cache. This is no load test. The behavior with many people at once is not measured.

The tree comparison is in `Benchmarks/Tenancy`. Its `README.md` says how to run it and how each number is taken.
The script of the storage and list comparison is not in the repository. Its numbers are given as measured.

## Not measured

- **Other providers.** Everything before the Tenancy section runs on SQLite in memory. The suite above checks the
  mapping on PostgreSQL and SQL Server, but does not time it.
- **The generators.** Nothing measures the build-time cost of the source generators.
- **The outbox.** The throughput of `OutboxProcessor` under load, and the effect of the `ProcessedAt` index as the
  table grows, are open questions.
- **JSON and GraphQL.** The generated converters have no benchmarks.
- **Value objects.** `ValueObject` equality still walks `GetEqualityComponents`, with the cost measured above.
  Value objects are compared far less often than identifiers, but nobody has measured them.
