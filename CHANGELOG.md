# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Releases before 3.0.0 have no changelog entry. Their history is in the
[commit log](https://github.com/DylanSnel/DDDToolkit/commits/main) and the
[git tags](https://github.com/DylanSnel/DDDToolkit/tags).

## [Unreleased]

### Added

- `DDDToolkit.EntityFramework.Supabase`, a new package that depends on nothing but Entity
  Framework's relational layer. Its `SupabaseMigrations` exports Entity Framework migrations as
  files in `supabase/migrations`, one per migration and named after it. `supabase db push`,
  `db reset` and branching then apply the same changes `dotnet ef database update` would. The files
  have no transaction statements of their own, keep the `__EFMigrationsHistory` insert and turn on
  row level security for new tables in `public`. `Export` writes only missing files and never
  rewrites one. `EnsureInSync` fails a test when a migration was not exported, when an exported file
  was changed, or when a file's migration was removed. Several contexts can export into one
  directory, and `FindDirectory()` finds `supabase/migrations` the way the CLI finds its project. See
  [Entity Framework → Supabase](docs/entity-framework.md#supabase).
- `Examples/ModularMonolith` runs on a local Supabase as well as on SQLite. Each module has its own
  schema, migration history and Entity Framework migrations, exported into one `supabase/` project by
  the host's `export-supabase` command. On Postgres the host refuses to start while a migration is
  pending, rather than applying it itself.
- `[KeyPart]` on a property of an `[AggregateRoot<T>]` or `[Entity<T>]` puts it into the primary key
  ahead of `Id`, and the new `KeyPartConvention`, added by `AddDDDToolkitConventions()`, carries it
  into the foreign key of every owned type below: a root keyed `(RegionId, Id)` owns rows keyed
  `(RegionId, RootId, Id)` whose foreign key is `(RegionId, RootId)`. Several key parts follow
  declaration order. The property stays an ordinary domain property that the toolkit never assigns or
  interprets, and it plays no part in equality. Explicit configuration in `OnModelCreating` wins. See
  [Composite keys](docs/composite-keys.md).
- `DDDToolkit.Interfaces.IHasKeyParts`, generated on every type with key parts to hand their
  declaration order to the convention. You do not implement it yourself.
- DDD00028 (error): `[KeyPart]` on a type that is neither an entity nor an aggregate root.
- DDD00029 (warning): a key part with a public setter.
- DDD00030 (error): key parts of one type spread over several files of a partial class, where there is
  no declaration order to follow.
- Building the model now fails, with an exception naming the owner, the child and the property, when
  an owned type has no property for one of its owner's key parts.

A model without `[KeyPart]` is mapped exactly as before; the tests compare it with and without the
convention.

- `[ValueObject]` works on a positional record: `public partial record Money(decimal Amount,
  string Currency);`. The compiler would make each parameter a `public init` property, so the
  generator declares the properties itself as `protected init`; the constructor, `Deconstruct` and
  equality stay, and `with` from outside the type no longer compiles. `[property: ...]` attributes on
  a parameter are carried over to the generated property, and the CS0657 warning that they would be
  ignored is suppressed. Before, every parameter reported DDD00010 and the generated constructor did
  not compile. See [Positional records](docs/value-objects.md#positional-records).
- A generated `With(...)` on every `[ValueObject]`, the toolkit's own `with`: `money.With(amount: 5)`
  replaces what is passed and keeps the rest, `null` included. On the always-valid twin it validates
  the copy and throws `InvalidValueObjectException` right there, and because it is virtual that holds
  even when the twin is held as its base type, which a `with` expression cannot guarantee. The
  parameters are the new `DDDToolkit.BaseTypes.Optional<T>`, which a value converts to implicitly.
  `With` is `[Internal]`, and `[GraphQLIgnore]` when HotChocolate is referenced. A value object that
  declares its own `With` gets none. See [Changing a value](docs/value-objects.md#changing-a-value-with).
- A code fix for DDD00010 and DDD00011 that makes the setter `protected init` (`private protected
  init` on an `internal` property). It ships in the DDDToolkit.Analyzers package as
  `DDDToolkit.Analyzers.CodeFixes.dll`, next to the generators.

### Fixed

- `ToValid()` could hand back a twin holding a different value from the one it had just validated.
  The twin started from an empty object and copied the settable properties one by one, so a get-only
  property or a private field stayed at its default without any warning, and a `protected` property
  failed to compile with CS1540. The twin is now built from the record's copy constructor, which
  copies every field. This applies to `[ValueObject]`, `[SingleValueObject<T>]` and record
  identifiers alike. An `[Internal]` property now travels with the twin as well; it still takes no
  part in equality.
- A project a few folders deep could fail to load in Visual Studio with "exceeds the OS max path
  limit". Visual Studio places every generated file at
  `{project}\Generated\{generator assembly}\{generator type}\{hint name}`, and the toolkit spelled
  the namespace out in both of the last two: `DDDToolkit.EntityFramework.Analyzers.Generators.EntityGenerator\`
  and `Company.Module.Domain.Aggregates.Feature.Type.EntityFramework.g.cs`. A generated file is now
  named after the type alone plus a short hash of its full name, such as
  `Type.EntityFramework.1f3a9c2e.g.cs`, and the generators moved out of the `.Generators`
  namespace. The path from the report went from 293 to 239 characters. Only file names changed; if
  you check `EmitCompilerGeneratedFiles` output into source control, expect it to be renamed.
- [Entities and aggregates](docs/entities-and-aggregates.md) said a base class of your own between an
  aggregate and `AggregateRoot<TId>` works. It does not: the generator writes the base class itself,
  and a class that also names one fails with CS0263. The page now says so and suggests an interface
  instead, and a test pins the behaviour down.

### Changed

- The packages ask for the oldest dependency versions they work with instead of the newest.
  3.0.0 declared the patch this repository was built with, so installing it moved a consumer's
  Entity Framework Core to at least 10.0.12, HotChocolate to 16.6.6 and FluentValidation to 12.1.1.
  The minimums are now:

  | Dependency | 3.0.0 required | Now requires |
  |---|---|---|
  | Microsoft.EntityFrameworkCore, .Relational | 10.0.12 | 10.0.0 |
  | Microsoft.Extensions.*.Abstractions | 10.0.12 | 10.0.0 |
  | Npgsql | 10.0.3 | 10.0.0 |
  | HotChocolate.AspNetCore, HotChocolate.Types.Analyzers | 16.6.6 | 16.0.0 |
  | FluentValidation | 12.1.1 | 12.0.0 |
  | Newtonsoft.Json | 13.0.4 | 13.0.1 |
  | Mediator.Abstractions | 3.0.2 | 3.0.1 |

  These are minimums, not pins: any newer version still resolves, exactly as before. Only the lower
  bound moved, so no consumer has to change anything.
- The Build and Test workflow gained a job that builds and tests the whole solution against those
  minimums, next to the existing job on the newest versions. `Directory.Packages.props` explains the
  two sets; `-p:DDDDependencyVersions=Floor` reproduces the job locally.

## [3.0.0]

A breaking release. Upgrading from 2.0.22 needs code changes in every project that raises a domain
event, and a retarget to .NET 10. [Migrating to 3.0](docs/migrating-to-3.md) walks through each break
with the before and the after.

The theme is removing silence. In 2.x a misapplied attribute generated nothing and said nothing, a
generic type produced a second unrelated type, colliding hint names threw inside the generator and
left you with no code and no error, and the generators did not load at all under current SDKs. Every
one of those now either works or reports a diagnostic that names the type and the fix.

### Added

**Identifiers**

- `[EntityId<T>]` on a `readonly partial record struct` generates a complete, allocation-free
  identifier: `Value`, a constructor, `Empty`/`IsEmpty`, `CreateUnique`/`CreateSequential` for
  `Guid`, `ToString()` with the optional prefix, `Parse`/`TryParse` accepting the value with or
  without the prefix, `IParsable<T>`, `IComparable<T>`, explicit conversions in both directions, and
  a nested `System.Text.Json` converter. This is now the recommended shape for an id.
- An id can be generated from the entity that owns it. `[AggregateRoot<Guid>("ORD")]` on `Order` also
  produces `OrderId`; `[Entity<T>]` does the same for a child. Naming an existing id type keeps
  working exactly as before.
- `IEntityId<TValue>`, `IEntity<TId>` and `IAggregateRoot` marker interfaces in
  `DDDToolkit.Abstractions`.

**Entities and aggregates**

- Get-only `partial` collection properties on `[Entity<T>]` and `[AggregateRoot<T>]` classes.
  `IReadOnlyList<T>`, `IReadOnlyCollection<T>` and `IEnumerable<T>` are backed by a `List<T>`,
  `IReadOnlySet<T>` by a `HashSet<T>`. The generator writes the `_camelCase` backing field, the
  read-only view, and Entity Framework's `[BackingField]` when Entity Framework is referenced.
- `AggregateRoot<TId>.Version`, a `long` optimistic concurrency token, and
  `ConcurrencyConflictException` naming the aggregate type and id.

**Domain events**

- `IDomainEvent` now carries `EventId` and `OccurredAt`, and the `DomainEvent` record base supplies
  both. `EventId` is a version 7 `Guid`, so events sort in the order they happened.
- `[DomainEventName]` gives an event a stable wire name, and `DomainEventName.Of` resolves it from a
  type, a generic argument or an instance.
- `DomainEventClock.Use(timeProvider)` replaces the clock those defaults read, for the current
  asynchronous flow only, so a test can make the timestamp of an event raised inside an aggregate
  deterministic. It also accepts an id factory when the whole `EventId` has to be predictable.

**Entity Framework**

- `AddDDDToolkitConventions()`: maps `Version` as a concurrency token, maps generated read-only
  collections of primitives and identifiers as primitive collections, and ignores `[Internal]`
  members.
- `AddDDDToolkitEntityFramework(options => ...)` and `UseDDDToolkit(serviceProvider)`, replacing the
  2.x pair of registration calls.
- A transactional outbox: `OutboxMessage`, `modelBuilder.AddDomainEventOutbox(Database)`,
  `DomainEventTypeRegistry`, `OutboxProcessor<TContext>` and `OutboxBackgroundService<TContext>`.
  Events are written in the same transaction as the aggregate and delivered at least once
  afterwards.
- `AggregateVersionInterceptor`, which bumps `Version` once per root per save (including saves that
  only changed something the root owns) and translates concurrency failures into
  `ConcurrencyConflictException`.
- `options.MaxDispatchRounds`, which caps the in-process dispatch loop and throws naming the events
  still pending, and `options.TimeProvider` for outbox timestamps.
- `DomainEventTimestamps`, which decides what the outbox and inbox timestamp columns become.
  `AddDomainEventOutbox` and `AddDomainEventInbox` take the context's `Database` and default to the
  provider's own instant type: `datetimeoffset` on SQL Server, `timestamp with time zone` on
  PostgreSQL, and a UTC `DateTime` on SQLite, which cannot order by a `DateTimeOffset`. Pass
  `DomainEventTimestamps.UtcDateTime` for the UTC `DateTime` column on every provider.
  <br>**If you have a database from an earlier 3.0 build on SQL Server, read
  [Timestamps](docs/entity-framework.md#timestamps) before upgrading.** The column moves from
  `datetime2` to `datetimeoffset`, which needs a migration; without one, reading the outbox throws.
  The stored instant does not change, and PostgreSQL and SQLite are unaffected.

**Read-only collections**

- The generated view is held in a field rather than rebuilt on every read. `AsReadOnly()` allocates a
  new wrapper per call, so reading `order.Lines` cost 24 bytes every time; sixteen reads went from
  68 ns and 384 bytes to 10 ns and none. Safe because the backing field is `readonly`, so the
  collection the view wraps cannot be replaced, and the view is a window rather than a copy.
- Behaviour change worth knowing: `ReferenceEquals(order.Lines, order.Lines)` is now `true` where it
  used to be `false`. Code that leans on reference equality of a view is rare and was already wrong,
  but the difference is real.

**Invariants**

- Two stages rather than one. `GetInvariantViolations()` asks what is broken and never throws;
  `EnsureInvariants()` throws `InvariantViolationException`, and is the shape the save insists on,
  because at the save "not yet consistent" is no longer an answer. Both are on `IHasInvariants`, both
  are generated from the same routine so they can never disagree about what counts as broken, and both
  run the rules and then the seam.
- Both of them answer for the whole aggregate. Asking an aggregate root runs its own rules and its
  seam, and then asks every child entity it holds, each of which answers for its own children the same
  way. That is the consistency boundary written down: a handler acts on an aggregate in memory, asks
  one question, and is told about a broken line without knowing the line has rules. No `DbContext` is
  involved, because none is needed. Only child entity collections are walked and never a single
  reference to another entity: a child pointing back at its parent would recurse, and the visited set
  that would stop it costs an allocation on the consistent path, which is the path that runs every
  time.
- `EnsureOwnInvariants()` and `GetOwnInvariantViolations()`, the same two stages asked about one
  object alone. For a caller that is already walking the graph and would otherwise hear about a child
  twice, once from the child and once from its root. `InvariantInterceptor` is that caller. An entity
  written by hand rather than generated should state its rules in these two, because the walking pair
  on `Entity<TId>` delegates to them.
- A generated `partial void CheckInvariants()` seam on every `[Entity<T>]` and `[AggregateRoot<T>]`.
  The seam is a `partial void` so the compiler erases it, and every call to it, when a type states no
  invariants. What it throws is caught by the check stage and reported as violations carrying
  `InvariantViolation.SeamCode`, so one call reports both kinds of rule.
- `IInvariant<TEntity>`, one rule as a type of its own: a `Code` to branch on and a `Check` returning
  `null` when the rule holds. Write these as nested types of the entity they are about, one per file
  under an `Invariants` folder. Nesting is what lets a rule read the entity's private state, and what
  lets the generator find rules without scanning the compilation. The generator builds one instance
  of each per entity type and reuses it, so a rule must be stateless.
- `InvariantViolation`, a `Code`, a `Message`, and the `EntityType` and `EntityId` of whatever
  reported it, so a violation found while walking an aggregate says which child is the problem rather
  than only that something is. `ToString()` reads
  `OrderLine LINE_0199 LINE_HAS_NO_SKU: A line must name the thing it is ordering.` Not a
  `ValidationError` and not a `Result<T>`: you are handed the failures and you put them in whatever
  result type your application already uses.
- `InvariantViolationException`, carrying `AggregateType`, `AggregateId` and `Violations`, and the
  `protected InvariantViolation(...)` helpers on `Entity<TId>` that build one already naming the
  aggregate. There is an overload for several broken rules found in one check. With named rules in
  play, `EnsureInvariants()` throws once with every broken rule's message and keeps whatever the seam
  threw as the inner exception. The exception names the boundary that was asked, so a child's message
  is prefixed with that child's own type and id and the root's own are not.
- `IHasInvariants`, with all four members, so the persistence layer can ask a tracked object to prove
  it is consistent without knowing its id type.
- `InvariantInterceptor`, registered by `UseDDDToolkit`. It runs `EnsureOwnInvariants()` on every
  entity a `SaveChanges` adds or modifies, **child entities included**, and on the aggregate root of
  each changed child. It runs after event dispatch so it sees what handlers changed, and before
  versioning so a rejected save leaves no version bumped. It skips deletions and aggregates the save
  does not touch. Who gets asked is read from the change tracker and never from a navigation property,
  so the pass cannot trigger a lazy load and cannot fail on a graph whose children were never loaded,
  and asking each of them about itself alone is what keeps a changed child from being counted twice.
  A root rule that spans its children is therefore still the root's own work. The domain walk asks
  more than this does, so an aggregate that answers clean is never contradicted by the save.
- `InvariantInterceptor.CheckInvariants(context)` runs the save-time pass on demand, and
  `InvariantInterceptor.GetInvariantViolations(context)` runs the asking stage over a whole unit of
  work, returning every violation in order, roots before the children they own, and writing nothing.
- Four diagnostics for the one failure this feature can have, which is a rule that is written,
  tested, and never run: DDD00024 to DDD00027. See [Invariants](docs/invariants.md).

**Validation**

- A failure model that is not only exceptions: `ValidationError` with `Message`, `PropertyName`,
  `Code` and `AttemptedValue`, and `ValidationErrorBuilder` for the
  `protected override void Validate(ValidationErrorBuilder)` overload.
- `TryToValid`, `TryValidate` and `ValidationErrors`, so a value object can report every failure
  without throwing. They are extension methods on the generated `IValidatable<TValid>` interface, so
  nothing new lands on your types and nothing new reaches an Entity Framework model or a GraphQL
  schema.
- `Prefixed()` and `ToErrorDictionary()` for collecting failures across a request and answering with
  `Results.ValidationProblem`, and `ToValidationErrors()` in `DDDToolkit.FluentValidation`.
- `InvalidValueObjectException` now carries `Errors` and `ObjectType`, so the throwing path says why.

**Modules**

- `[assembly: Module("Name")]` declares an assembly to be a module, and `[ModuleContract]` publishes
  a type from it. A type marked `[IntegrationEvent]`, and anything nested inside a published type,
  is published too.
- `ModuleBoundaryAnalyzer`, reporting DDD00022 where one module names another module's unpublished
  type and DDD00023 where it holds another module's entity as stored state. Both are warnings, both
  are silent unless both assemblies declare a module, and both come from a real analyzer, so
  `#pragma warning disable`, `[SuppressMessage]`, `NoWarn` and `WarningsAsErrors` all work on them.

**Integration events**

- `IIntegrationEventSink` and the `IntegrationEventMessage` envelope, which lives in the core package
  and carries `MessageId`, `Name`, `Version`, `Payload`, `ContentType`, `OccurredAt`,
  `AggregateType`, `AggregateId` and `Body`. `outbox.SendTo<TSink>()` and `outbox.SendTo(sink)`
  attach one; every sink is attempted, and a failure fails the message without stopping the sinks
  behind it.
- `outbox.PublishAs<TEvent, TContract>(...)` maps a domain event to the contract that leaves the
  process, and `outbox.DoNotPublish<TEvent>()` keeps one in. Returning `null` from the mapping drops
  that occurrence. With nothing mapped the domain event is published as it stands, reusing the JSON
  already in the row.
- `ModuleIntegrationEventSink` and `outbox.SendToModules<TContext>()`, for the common case of another
  module in the same process. Handlers implement `IIntegrationEventHandler<TContract>`, are typed on
  the contract rather than on the domain event, and each gets its own inbox row, so a retry re-runs
  only the handlers that failed. `[IntegrationEventConsumer("name")]` pins the inbox key.
- An inbox: `InboxMessage`, `modelBuilder.AddDomainEventInbox()` and
  `DomainEventInbox<TContext>.ExecuteOnceAsync`, which writes the handler's changes and the row that
  says "applied" in one `SaveChanges` inside one transaction. The key is the message and the
  consumer, so adding a consumer later does not replay its backlog through the others.
- Versioning and upcasting. `[IntegrationEvent(name, Version = n)]` pins the published name and the
  schema version, the outbox stores the version in the row, and
  `contracts.UpcastFrom<TOld, TNew>(...)` converts an older payload on both read paths. Registration
  refuses a `[DomainEventName]` and an `[IntegrationEvent]` name that disagree.
- `outbox.DeliverInTransaction`, which wraps one message's delivery and its "processed" mark in a
  single transaction, and `outbox.AlsoDispatchInProcess`, which runs the in-process delegate before
  the sinks.

**New packages**

- `DDDToolkit.Mediator`, which adds `options.DispatchWithMediator()` over
  [martinothamar/Mediator](https://github.com/martinothamar/Mediator). An event that does not
  implement `INotification` makes the dispatch throw naming the event type rather than being
  silently skipped.
- `DDDToolkit.Testing`, an aggregate testing kit. `AggregateScenario.Given(...).When(...)` returns
  only the events that call raised, and `Raised`, `RaisedNo`, `RaisedNothing`, `RaisedExactly`,
  `RaisedExactlyThese`, `SingleEvent` and `EventsOf` assert on them. `WhenThrows` also asserts that
  nothing was raised on the way out. Payload comparison ignores `EventId` and `OccurredAt`, because
  record equality never matches two events that mean the same thing. It references `DDDToolkit` and
  nothing else, so it brings no test framework and no assertion library.
- `DDDToolkit.Messaging.Postgres`, a [pgmq](https://github.com/pgmq/pgmq) sink. `pgmq.send` is
  an ordinary insert, so `PgmqSink<TContext>` enqueues inside the transaction that writes the
  aggregate. `PgmqQueue` exposes send, read, archive and delete directly, queues are created on
  first use unless you turn that off, and a missing extension reports `PgmqNotInstalledException`
  naming the database instead of failing somewhere inside the driver.

**GraphQL**

- `GraphQlSubscriptionSink` and `AddIntegrationEventSubscriptions(map => ...)`, an integration event
  sink that publishes a contract to HotChocolate's `ITopicEventSender`. Nothing is pushed until the
  map names the message's name and version. It publishes the contract rather than the domain event,
  because a subscription payload is a schema type that the schema's own authorisation applies to, and
  it does no upcasting, because the payload was produced seconds ago by this process.

**Everything else**

- `ValueObjectRules.MustBeValid()` in `DDDToolkit.FluentValidation`, for folding a value object's own
  rules into a validator you write yourself.
- Fifteen new diagnostics: DDD00003 to DDD00009, DDD00020, DDD00021 and DDD00025 to DDD00027 from the
  generators, and DDD00022 to DDD00024 from the two new analyzers. They join DDD00001, DDD00002,
  DDD00010, DDD00011 and DDD00013 from 2.x. DDD00004 and DDD00021 to DDD00026 are warnings; the rest
  are errors.
- `Benchmarks/DDDToolkit.Benchmarks`, a BenchmarkDotNet suite measuring the struct identifier against
  the record one: construction, iteration, dictionary and set lookup, equality, hashing, text, and an
  Entity Framework round trip. Writing it found the two performance defects listed under *Fixed*.
  [Performance](docs/performance.md) is the write-up, including the two places where the measurement
  disagrees with the advice.
- `Tests/DDDToolkit.EntityFramework.Providers.Tests`, which runs the mapping, concurrency, outbox and
  inbox suites against real PostgreSQL 17 and SQL Server 2022 in
  [Testcontainers](https://dotnet.testcontainers.org). Without Docker every test in it skips with a
  message naming the reason. It pins down three things SQLite never could: the `ddd` schema is real,
  a struct id and a record id produce the same column, and the outbox's UTC `DateTime` timestamps
  land in `datetime2` on SQL Server where a `DateTimeOffset` would have been `datetimeoffset`.
- Documentation: a page per feature under [docs/](docs/), this changelog, and a
  [migration guide](docs/migrating-to-3.md).
- Central package management, an `.slnx` solution, and a CI job that packs every package on every
  pull request.

### Changed

**Breaking**

- Target framework is `net10.0`, up from `net8.0`. The generators still target `netstandard2.0` and
  reference nothing at run time.
- Domain events moved from `Entity<TId>` to `AggregateRoot<TId>`. A `[Entity<T>]` class no longer has
  an event list, because a child entity is not a consistency boundary.
- `AddDomainEvent(evt)` is replaced by `protected RaiseDomainEvent(evt)`. Nothing outside an
  aggregate can put an event into it.
- The public `ClearDomainEvents()` is gone. `IHasDomainEvents` now declares `DomainEvents`,
  `DequeueDomainEvents()` and `ClearDomainEvents()`, and `AggregateRoot<TId>` implements it
  explicitly, so draining takes a cast and application code cannot discard events by accident.
- `IDomainEvent` gained two members, so an event type that implemented the empty 2.x interface no
  longer compiles. Derive from `DomainEvent` or supply `EventId` and `OccurredAt`.
- `PublishDomainEventsInterceptor` dispatches before the database write on both save paths. In 2.x
  the synchronous path dispatched before the save and the asynchronous path after it, so handlers saw
  different data depending on which overload the caller used. Handlers that relied on the row already
  being committed must move to the outbox.
- `UseDomainEvents(Func<IServiceProvider, List<IDomainEvent>, Task>)` is `[Obsolete]`. It still works
  and now dispatches before the save like everything else.
- `Entity<TId>` implements `IEntity<TId>`, and `TId` is constrained to `IEntityId, IEquatable<TId>`.
  A hand-written identifier that implements `IEntityId` alone has to add `IEquatable<TId>`.
- `[ValueObject]`, `[SingleValueObject<T>]`, `[Entity<T>]`, `[AggregateRoot<T>]` and `[EntityId<T>]`
  now target `Class | Struct` with `Inherited = false`. Applying one to the wrong declaration kind
  used to be a `CS0592` from the compiler; it is now a DDDToolkit diagnostic naming the type and the
  requirement.
- HotChocolate moves from 14.0.0-rc.1 to 16.6.6. `HotChocolate.Execution` has no stable 16 release,
  so the execution engine now comes from the `HotChocolate` package, and
  `GraphQLTypeAttribute<TSchemaType>` is constrained on `ITypeDefinition` instead of `INamedType`.
- Entity Framework Core moves from 8.0.8 to 10.0.12, FluentValidation from 11.9.2 to 12.1.1.

**Not breaking**

- The generators are self-contained. They no longer reference `SourceGeneratorsToolkit` or
  `DDDToolkit.Abstractions` at run time; shared infrastructure is compiled into each analyzer and
  attributes are matched by metadata name.
- Generator pipelines carry equatable records instead of syntax nodes and symbols, so incremental
  caching works and an unrelated edit does not rerun them.
- Struct identifiers serialize as the value they wrap, in both System.Text.Json and Newtonsoft, and
  can be used as dictionary keys.
- The examples publish through Mediator instead of MediatR, which is commercially licensed from
  version 13. MediatR still works with the toolkit; the Entity Framework page shows the delegate.
- Entity equality is null-safe in every operator and `GetHashCode` no longer throws on a null id.
- The GraphQL `DomainEvent` interface exposes `eventId` and `occurredAt` beside `eventType`, and
  `[Internal]` members are removed during type discovery rather than flagged at completion, so the
  types they referenced stop leaking into the schema.

### Fixed

- The generators did not load under current SDKs. Analyzer dependencies were not being resolved,
  which surfaced as `CS8784`. Making the generators self-contained fixes it.
- `record with` on a value object copied the cached validity verdict, so a copy reported its source's
  verdict even when the changed property was the one that had been validated. `ValueObject` now has a
  copy constructor that resets the cache.
- A generic type carrying a DDDToolkit attribute produced a second, unrelated, non-generic type that
  compiled on its own while the real type got nothing. It now reports DDD00006.
- A nested type and a top-level type of the same name in the same namespace produced the same
  generator hint name; the second `AddSource` threw inside the generator, which then contributed
  nothing for either type, with no code and no error. Hint names are built from the fully qualified
  name.
- A class carrying both `[Entity]` and `[AggregateRoot]` crashed the generator with `CS8785` and said
  nothing about the two attributes. It now reports DDD00009.
- The System.Text.Json converter factory claimed every `ISingleValueObject` and then threw from
  `CreateConverter` for anything whose direct base was not `SingleValueObject<T>`, which is every
  reference-type identifier. Struct identifiers it did not claim at all.
- `BlockAlwaysValidSerialization` wrote `{}` for every always-valid twin, because the declared type
  it is handed is an empty marker interface.
- The Newtonsoft converter looked up a `Value` property by name, and quietly produced the default
  identifier when handed `null` for a non-nullable struct id.
- The Newtonsoft contract resolver promoted non-public setters on structs, which writes to a copy.
- `[GraphQLType<T>]` was ignored on a generated identifier. The generated part cannot carry the
  attribute, so the author's part is now read.
- `AggregateVersionInterceptor.SaveChangesFailed` rethrows `ConcurrencyConflictException` instead of
  leaving it wrapped in a `DbUpdateException`, so callers can catch the type the documentation names.
- `OutboxProcessor` orders messages by `CreatedAt`, then `OccurredAt`, then `Id`.
- The prefix documentation comment on a generated id no longer promises `Parse` for an underlying
  type that has no `TryParse`.
- All four analyzer packages failed `dotnet pack` with `NU5017`, and the release workflow would have
  stopped at the first one. The analyzer projects opt out of symbol packages, and the pull request
  workflow now packs every package so a build is not the last thing that checks.
- Equality on a reference-type identifier and on a single value object walked
  `GetEqualityComponents()` through `Enumerable.SequenceEqual`, which allocated two iterators and
  boxed the value on every comparison. A dictionary keyed by a record identifier was 17 times slower
  than one keyed by a struct identifier, and hashing allocated 128 bytes per probe. The generators
  now emit a direct comparison of `Value`, which is the same answer for a type with exactly one
  component; the gap is 1.4 to 2.5 times with nothing allocated. Multi-property `[ValueObject]`
  records still compare components, which is correct for them.
- A struct identifier's generated `ToString()` formatted through
  `Convert.ToString(object, IFormatProvider)`, which boxed the value, and then concatenated, for 232
  bytes and three allocations against the record form's 104 and one. It now uses an interpolated
  string pinned to the invariant culture. Both forms allocate 104 bytes.
- Both defects existed only because nobody had measured. They were found by writing the benchmarks.

### Removed

- `IRaw`, `IAllowInvalid` and `IValueObject<T>` from `DDDToolkit.Abstractions.Interfaces`. None of
  them had an implementer, a reference or a mention anywhere in the repository, in 3.0 or in 2.0.22.
- `Raw<TValueObject>`, the old `IValidatable` and `NodeIdSerializer`. All three files were entirely
  commented out. The name `IValidatable` came back later in this release as
  `IValidatable<TValid>`, which is a different interface with a real job: it is what `TryToValid`
  hangs off. Nothing that compiled against the 2.x name compiles against the new one, because the
  old one was commented out and therefore never existed to compile against.
- `ModuleAttribute` as it stood, which was marked `[Obsolete("Currently unused")]` and hidden from
  IntelliSense. The name also came back later in this release, as the assembly attribute that
  declares a [module](docs/modules.md) boundary. It targets an assembly rather than a type, so a 2.x
  usage would not have compiled against it anyway.
- `AddFluentValidation()`. It was an empty method with no call sites; the FluentValidation
  integration is compile-time and needs no registration. `ValueObjectRules.MustBeValid()` covers
  what was actually missing.
- The `MediatR` and `SourceGeneratorsToolkit` dependencies.
