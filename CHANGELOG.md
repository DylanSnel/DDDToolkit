# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Releases before 3.0.0 have no changelog entry. Their history is in the
[commit log](https://github.com/DylanSnel/DDDToolkit/commits/main) and the
[git tags](https://github.com/DylanSnel/DDDToolkit/tags).

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
- A transactional outbox: `OutboxMessage`, `modelBuilder.AddDomainEventOutbox()`,
  `DomainEventTypeRegistry`, `OutboxProcessor<TContext>` and `OutboxBackgroundService<TContext>`.
  Events are written in the same transaction as the aggregate and delivered at least once
  afterwards.
- `AggregateVersionInterceptor`, which bumps `Version` once per root per save (including saves that
  only changed something the root owns) and translates concurrency failures into
  `ConcurrencyConflictException`.
- `options.MaxDispatchRounds`, which caps the in-process dispatch loop and throws naming the events
  still pending, and `options.TimeProvider` for outbox timestamps.

**New package**

- `DDDToolkit.Mediator`, which adds `options.DispatchWithMediator()` over
  [martinothamar/Mediator](https://github.com/martinothamar/Mediator). An event that does not
  implement `INotification` makes the dispatch throw naming the event type rather than being
  silently skipped.

**Everything else**

- `ValueObjectRules.MustBeValid()` in `DDDToolkit.FluentValidation`, for folding a value object's own
  rules into a validator you write yourself.
- Eight new diagnostics: DDD00003 to DDD00009 and DDD00020, joining DDD00001, DDD00002, DDD00010,
  DDD00011 and DDD00013 from 2.x.
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

### Removed

- `IRaw`, `IAllowInvalid` and `IValueObject<T>` from `DDDToolkit.Abstractions.Interfaces`. None of
  them had an implementer, a reference or a mention anywhere in the repository, in 3.0 or in 2.0.22.
- `Raw<TValueObject>`, `IValidatable` and `NodeIdSerializer`. All three files were entirely
  commented out.
- `ModuleAttribute`. It was already marked `[Obsolete("Currently unused")]` and hidden from
  IntelliSense.
- `AddFluentValidation()`. It was an empty method with no call sites; the FluentValidation
  integration is compile-time and needs no registration. `ValueObjectRules.MustBeValid()` covers
  what was actually missing.
- The `MediatR` and `SourceGeneratorsToolkit` dependencies.
