# DDDToolkit — review and backlog

A working document for whoever picks this up next. Written 2026-09-13 from a
full read of `Source/`, `Tests/` and `Examples/` at commit `2024-09-24`.

**Why it exists.** The toolkit was evaluated as a possible foundation for
another codebase (a .NET 10 modular monolith on Postgres). Most of it was not
adopted, and the reasons turned out to be a better critique of the toolkit than
a feature list would have been. What follows is that critique plus the backlog
it implies.

**How to trust it.** Every claim below was read out of the source, not inferred
from a name. Where a line is cited, it was opened. A section at the end lists
claims that were made during the review and turned out to be **wrong** — read
that before repeating them.

---

## 1. What is here today

| Project | Target | What it holds |
|---|---|---|
| `DDDToolkit` | net8.0 | `BaseTypes` (Entity, AggregateRoot, ValueObject, SingleValueObject, EntityId), `Interfaces`, `Exceptions`, serialization |
| `DDDToolkit.Abstractions` | netstandard2.0 | the attributes an author writes, marker interfaces, validation |
| `DDDToolkit.Analyzers` | netstandard2.0 | five source generators |
| `DDDToolkit.EntityFramework(.Analyzers)` | net8.0 | a SaveChanges interceptor, generated value converters |
| `DDDToolkit.HotChocolate(.Analyzers)` | net8.0 | GraphQL type converters, interceptors, serializers |
| `DDDToolkit.FluentValidation(.Analyzers)` | net8.0 | generated validators |
| `DDDToolkit.NewtonSoft.Json` | net8.0 | contract resolver honouring `[Internal]` |

The author writes an attribute — `[AggregateRoot<UserId>]`, `[Entity<OrderId>]`,
`[EntityId<Guid>("PRS")]`, `[SingleValueObject<string>]`, `[ValueObject]` — and a
generator supplies the base class, the equality members, the EF converter and a
`Valid<T>` twin. That is a good shape and it is the thing worth keeping.

**Pinned versions:** net8.0, EF Core 8.0.8, HotChocolate 14.0.0-rc.1, MediatR
12.4.0, Npgsql 9.0.0-preview.3. All of that is two years old; HotChocolate is
two majors behind current and was a release candidate.

---

## 2. Defects, worst first

### 2.1 The EF interceptor publishes at different times depending on which overload you call

`Source/DDDToolkit.EntityFramework/Interceptors/PublishDomainEventsInterceptor.cs`

- Line 9, `SavingChanges` (sync): publishes **before** the save.
- Line 23, `SavedChangesAsync` (async): publishes **after** it.
- Lines 15–21: `SavingChangesAsync` is commented out.

So `SaveChanges()` and `SaveChangesAsync()` give different event semantics from
one interceptor, and the commented-out block suggests this was known and left
unresolved. Either is defensible; differing is not.

Worse, both are outside any transaction the caller controls, and the delegate is
in-process. If the commit fails after an async publish, handlers have already
run against a state that was rolled back. If a publish throws on the sync path,
the save never happens. Neither is recoverable and nothing records the loss.

**This is the single most important thing to fix or to delete.** An in-process
dispatch on SaveChanges cannot give at-least-once delivery, and a DDD toolkit
that ships one implies it can.

### 2.2 Ids cannot be structs

- `Source/DDDToolkit.Abstractions/Attributes/EntityIdAttribute.cs:7` —
  `[AttributeUsage(AttributeTargets.Class)]`
- `Source/DDDToolkit.Analyzers/Generators/EntityIdGenerator.cs:30` — the provider
  filters on `RecordDeclarationSyntax`

`AttributeTargets.Class` admits `record class` but not `record struct` and not
`struct`. The generator's syntax filter admits both record forms. The
intersection is `record class` only — so every id is heap-allocated, and the
natural declaration for an identifier, `readonly record struct`, is rejected at
the attribute.

Fix: `AttributeTargets.Class | AttributeTargets.Struct`, and decide deliberately
whether `EntityId<T>`'s base-class requirement survives that (it cannot — a
struct has no base class, so the generated surface has to become an interface
plus generated members).

### 2.3 Diagnostics that can never fire

`DDD00001` (ValueObjectShouldBeRecord) and `DDD00002` (EntityShouldBeClass) are
reported from the null branch after a cast — for example
`AggregateRootGenerator.cs:33`, `EntityGenerator.cs:33`. But the providers at
line 26 in each already filter to `ClassDeclarationSyntax`, so the cast cannot
fail and the branch is unreachable.

The consequence is the failure mode worth caring about: a `record` or `struct`
carrying `[AggregateRoot<T>]` is **silently skipped**. No generated code, no
diagnostic, no build error — the author gets a type that looks annotated and
behaves like a plain class.

A generator that produces nothing when misused is the hardest kind of bug to
find. Every attribute in this toolkit should have a test that applies it to the
wrong kind of declaration and asserts a diagnostic.

### 2.4 `AggregateRoot<T>` carries nothing

`Source/DDDToolkit/BaseTypes/AggregateRoot.cs:5-16` is an abstract subclass of
`Entity<TIdObject>` with two constructors and no members.

The generators do emit distinct bases (`AggregateRootGenerator.cs:55` writes
`: AggregateRoot<T>`, `EntityGenerator.cs:55` writes `: Entity<T>`), so the two
*are* distinguishable by type — but the distinction carries no behaviour. Every
concept that belongs to a root and not to a child entity is missing: the event
list lives on `Entity` (so child entities raise events too), and there is no
version, no invariant check, no "this is the consistency boundary" anything.

Decide what a root *is* in this toolkit, then put it here. If the answer is
"nothing", delete the type and let the attribute be the only marker.

### 2.5 The event model is a blank interface

`Source/DDDToolkit/Interfaces/IDomainEvent.cs` is:

```csharp
public interface IDomainEvent
{
}
```

An event knows nothing about itself: no event id, no occurrence time, no
aggregate reference, no name or version. Every consumer therefore has to
`switch` on concrete types and reconstruct that metadata from context, which is
exactly the work a toolkit exists to remove.

A useful minimum: a stable event id, `OccurredAt`, the aggregate's identity, and
a name that survives renaming the class (for anything that will ever be
serialized to a queue or a table).

### 2.6 Anyone can add or drop events

- `Entity.cs:46` — `public void AddDomainEvent(IDomainEvent)`
- `Entity.cs:48` — `public void ClearDomainEvents()`
- `IHasDomainEvents.cs:10` — the interface *requires* `ClearDomainEvents()` to be public

So code outside the aggregate can inject an event into it, and any caller can
discard pending events before they are persisted. In a system where those events
are the integration contract, that is a way to write a row whose event never
happened.

Raising should be `protected`. Draining should be visible to the persistence
layer and to nobody else — an explicit interface implementation, or `internal`
with `InternalsVisibleTo`.

### 2.7 No concurrency, no auditing

Grepped the whole of `Source/`: no row version, no concurrency token, no `xmin`,
no `CreatedBy`/`UpdatedBy`, no `IAuditable`. Two aggregates loaded and saved
concurrently is last-write-wins, silently.

A toolkit can legitimately say "auditing belongs to your database triggers" —
but it should say so, because the absence currently reads as an oversight.
Concurrency is harder to hand-wave: an aggregate is the unit of consistency, and
without a token it is not one.

### 2.8 The event plumbing is untested

`Tests/DDDToolkit.Tests` has 8 files, all value-object and serialization tests.
**Nothing** references `AggregateRoot` or `DomainEvent`.

Every defect in 2.1, 2.4, 2.5 and 2.6 is in the untested half. That correlation
is not a coincidence and it is the cheapest thing on this list to change.

### 2.9 HotChocolate integration will not compile against 15 or 16

`Interceptors/IgnoreInternalFieldsInterceptor.cs` uses `DefinitionBase` and
`ObjectTypeDefinition`, both renamed in HotChocolate 15. The package targets
14.0.0-rc.1.

Note what *does* survive: `BindRuntimeType`, `AddTypeConverter` and
`IChangeTypeProvider` all still exist in 16.6.4 (checked against the 16.6.4
assemblies). So the *technique* for getting typed ids across the GraphQL
boundary is sound and worth keeping — it is the interceptor and the attribute
that need rewriting.

### 2.10 The README is two lines

For a package with five generators and five diagnostics whose behaviour is
mostly invisible, the docs are the product. `Examples/` is currently the only
real documentation.

---

## 3. What a DDD toolkit should have

Beyond fixing the above. Roughly in order of value.

1. **Identity that works for structs**, with parsing, `TryParse`, JSON, EF
   conversion and a GraphQL converter generated from one declaration.
2. **A real root/entity distinction** — events on the root only, or a documented
   reason they are on both.
3. **A rich event contract** — id, occurred-at, aggregate identity, stable name.
4. **Transactional delivery.** The outbox pattern, not in-process dispatch:
   write events to a table in the same transaction as the row, and let a
   separate reader deliver them. This is the single biggest gap between the
   toolkit and what production systems need, and 2.1 is a symptom of not having
   made the choice.
5. **Optimistic concurrency** — a version on the root, mapped by the EF
   integration, surfaced as a typed conflict rather than a `DbUpdateException`.
6. **A failure model that is not only exceptions.** Everything here throws
   (`InvalidValueObjectException`, the `Valid<T>` twin). A `Result`-shaped
   alternative composes better with validation and with API layers that must
   return a refusal rather than a 500. Offer both; do not force the throw.
7. **Diagnostics that fire, with tests.** Every attribute misapplied should
   produce an error, and each should have a test that proves it. See 2.3.
8. **A testing kit** — given-these-events / when-this-method / then-these-events,
   so aggregates can be tested without a database. This is the piece most
   toolkits skip and most teams then write badly.
9. **Event schema evolution** — a name that survives a class rename, and a story
   for versioning a published event.
10. **Invariant expression** — somewhere to put "these must hold after every
    mutation", checked once rather than at each call site.
11. **Docs** — for each generator: what it emits, what it requires, and what
    happens when the requirement is not met.

---

## 4. Suggested order of work

1. **Tests for the event plumbing** (2.8). Do this first; it will surface 2.6
   and probably 2.4 on its own.
2. **Make the diagnostics fire** (2.3), with a test per attribute for the wrong
   declaration kind.
3. **Close the event surface** (2.6) — protected raise, non-public drain.
4. **Decide the interceptor's fate** (2.1). Either commit to outbox delivery or
   remove the interceptor and document that delivery is the host's problem. The
   current state promises something it cannot keep.
5. **Structs for ids** (2.2).
6. **Enrich `IDomainEvent`** (2.5), which is a breaking change and so wants to
   ride with the others.
7. **Concurrency token** (2.7).
8. **Bring HotChocolate to 16.x** (2.9) — keep `BindRuntimeType` and
   `IChangeTypeProvider`, rewrite the interceptor.
9. **README** (2.10).

---

## 5. Claims made during this review that were WRONG

Recorded so they are not repeated. Each was asserted confidently and then
refuted by checking the code.

- **"`AggregateRoot<T>` is indistinguishable from a child entity at the type
  level."** False. `AggregateRootGenerator.cs:55` and `EntityGenerator.cs:55`
  emit different base types. The real criticism is 2.4 — the distinction exists
  and carries nothing.
- **"Two entities with the same Guid would compare equal."** False.
  `Entity.cs:8` constrains `TIdObject : IEntityId`, so `Entity<Guid>` does not
  compile, and the type test at line 29 is closed over the id type.
- **"`BindRuntimeType` no longer exists in HotChocolate 16."** False. It is in
  `HotChocolate.Types.dll` in 16.6.4.
- **A batch of csproj line citations** that were out of range — the facts were
  right, the line numbers invented. `DDDToolkit.csproj` is 34 lines, not 150+.

The pattern: claims about *what exists* were reliable; claims about *where* and
about *consequences* needed checking. Open the file.
