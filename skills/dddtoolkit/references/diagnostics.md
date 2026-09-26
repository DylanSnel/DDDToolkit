# DDD diagnostics: what each one means and how to fix it

Every id below comes from a DDDToolkit generator or analyzer. The build output shows the id and the
message; the full entry, with examples, is at
`https://dylansnel.github.io/DDDToolkit/docs/diagnostics#ddd000xx` (lower case id), and the whole page
as Markdown at `https://dylansnel.github.io/DDDToolkit/docs/diagnostics.md`.

How to work with them:

- **Errors mean nothing was generated for that type.** The errors that follow are the same mistake:
  a missing base class, `Id`, `_field` or `base(id)`; CS0246 for an id the short form
  (`[AggregateRoot<Guid>]`) should have generated, in every file that names it; CS9248 for a partial
  collection property with no implementation. Fix the DDD error, rebuild, then look at what is left.
  Other errors can stay hidden until then too.
- **Fix the code, do not silence the diagnostic.** Each one names code that compiles and does not do
  what it looks like it does. Suppress only a warning the user has decided to accept, and say so.
- Most ids are reported by a generator: `#pragma warning disable` and `[SuppressMessage]` do not reach
  them, only `<NoWarn>` in the project file does, for the whole project. DDD00022, DDD00023, DDD00024
  and DDD00032 come from analyzers, so pragmas work on those.
- DDD00012 and DDD00014 to DDD00019 have never been assigned; an id missing below does not exist.

## DDD00001

Error. `[ValueObject]` or `[SingleValueObject<T>]` on something other than a record class. Declare it
`public partial record Name`, not `class` or `record struct`.

## DDD00002

Error. `[Entity<T>]` or `[AggregateRoot<T>]` on a record or struct. Declare it `public partial class
Name`: entities have identity, not value equality.

## DDD00003

Error. `[EntityId<T>]` on a plain class, struct or interface. Declare it
`public readonly partial record struct NameId;` (or `public partial record NameId;` for the class form).

## DDD00004

Warning; the id is still generated. A record struct id without `readonly`. Add `readonly`.

## DDD00005

Error. A type with a DDDToolkit attribute that is not `partial`. Add `partial` to the declaration, and
to every containing type it is nested in.

## DDD00006

Error. The annotated type is generic, or nested inside a generic type. Give it a concrete, non-generic
declaration; a type nested in a non-generic container is fine.

## DDD00007

Error. `[AggregateRoot<Guid>]` or `[Entity<Guid>]` over a raw value would generate `<Name>Id`, and a
type of that name already exists in the namespace or containing type. Either declare the entity over
the existing id, `[AggregateRoot<OrderId>]` (with `[EntityId<Guid>]` on `OrderId`), or rename one of the
two. A `partial record struct OrderId` without `[EntityId]` and without a different accessibility is
allowed: it is your half of the generated id, for adding members.

## DDD00008

Error. The type argument of `[Entity<T>]` or `[AggregateRoot<T>]` is neither an identifier (a type with
`[EntityId<T>]` or implementing `IEntityId`) nor a value type or `string`. Use a declared id or a raw
value such as `Guid`, `int`, `long`, `string` or `DateOnly`. An optional id is `OrderId?`, not
`[AggregateRoot<Guid?>]`.

## DDD00009

Error. Both `[Entity<T>]` and `[AggregateRoot<T>]` on one type. Keep `[AggregateRoot<T>]` for what is
loaded, saved and referenced from elsewhere, `[Entity<T>]` for a child inside one aggregate.

## DDD00010

Error. A property declared on a `[ValueObject]` whose setter is not `protected`. Make it
`{ get; protected init; }` (`private protected init` on an `internal` property); an IDE code fix does
this. Callers that need a changed copy use the generated `With(...)`, not `with { }`.

## DDD00011

Error. A property declared on a `[ValueObject]` with a `set` rather than an `init` accessor. Make it
`{ get; protected init; }`. A `public set` reports both DDD00010 and DDD00011; the one fix clears both.

## DDD00013

Error. A `sealed` value object record, so its always-valid twin `Valid<Name>` cannot derive from it.
Remove `sealed`. The same applies to a record class id.

## DDD00020

Error. A generated collection property with a setter, such as
`public partial IReadOnlyList<OrderLine> Lines { get; set; }`. Make it get-only and change the
collection through the generated field (`_lines`) inside the entity.

## DDD00021

Warning. A field or property of an aggregate typed as another aggregate root (also in a collection, an
array, a dictionary, or as a nullable). Hold the other root's id instead, `public CustomerId Buyer
{ get; private set; }`, and take the root as a method parameter when a method needs to read it. A child
entity pointing back at the root that owns it is allowed. The project can turn the rule off with
`<NoWarn>$(NoWarn);DDD00021</NoWarn>`, but only when the user decides the two really are one unit.

## DDD00022

Warning, only when both assemblies declare `[assembly: Module(...)]`. Code in one module names a type
of another module that the other module does not publish. Either depend on something it publishes (its
ids, its `[IntegrationEvent]` records, its `[ModuleContract]` read models and interfaces), or, if the
type really belongs in the contract, mark it `[ModuleContract]` in the module that owns it. That is the
owning team's decision, so point it out rather than adding the attribute silently.

## DDD00023

Warning. An entity of one module stores an entity or aggregate root of another module in a field or
property. Store the other module's published id instead and react to its integration events.
Publishing the entity with `[ModuleContract]` does not fix this and does not silence it.

## DDD00024

Warning. A type implementing `IInvariant<T>` that is not nested inside an entity or aggregate root, so
nothing ever runs it. Move it inside the entity it checks, as a nested type in a `partial` part of that
entity: `public partial class Order { public sealed class MustHaveLines : IInvariant<Order> { ... } }`.

## DDD00025

Warning. A rule nested inside one entity but implementing `IInvariant<Other>`, so the entity it sits in
never runs it. Move it into `Other`, or change the type argument to the entity it is nested in. Usually
the type argument was copied from a neighbouring rule.

## DDD00026

Warning. Two rules of the same entity return the same `Code`, so a caller cannot tell them apart. Give
each rule its own code, ideally a `public const string ViolationCode` on the rule.

## DDD00027

Error; the rule is dropped. A rule without an accessible parameterless constructor. The generator
creates one instance per entity type and reuses it, so a rule must be stateless: remove the
constructor parameters and read what the rule needs from the entity passed to `Check`. A `private`
constructor also reports; drop it or make it public.

## DDD00028

Error. `[KeyPart]` on a property of something that is not an `[AggregateRoot<T>]` or `[Entity<T>]`, such
as a value object. Remove it, or move the property to the entity that is keyed on it.

## DDD00029

Warning. A `[KeyPart]` property with a public, non-init setter. Set it once in the constructor and make
it `{ get; }` (or `private set`, `init`); a primary key value never changes.

## DDD00030

Error. `[KeyPart]` properties of one type spread over several files of a partial class, so the key's
column order is undefined. Declare them all in one part of the class, in the order the key should have.

## DDD00031

Error, in the project that turns the Supabase export on. A `[SupabaseMigrations]` factory the build
cannot create. Make it a `public`, non-abstract, non-generic class with a public parameterless
constructor implementing `IDesignTimeDbContextFactory<TContext>`, for a context the exporting project
can see. The message names what is missing.

## DDD00032

Warning. `AddNodeIdValueSerializerFrom<OrderId>()` on a toolkit identifier writes a serializer that
stores nothing. Remove the call: the generated `Add{Module}GraphQlRuntimeBindings()` already registers a
working serializer for every identifier.

## DDD00033

Warning. A class is left out of the generated `Add{Module}IntegrationEvents()` registration because it
cannot be built with `new`: an outbound `IOutboundIntegrationEvent<,>` class or an
`IIntegrationEventHandler<T>` whose constructor is inaccessible, ambiguous (two longest constructors of
equal length), uses `ref`, `out` or `params`, or has parameter types the module cannot see. Give it one
public constructor whose parameters are services from the container, or register it by hand.

## DDD00034

Warning. An event's class name ends in a version (`OrderPlacedV2`) and `[IntegrationEvent(Version = 3)]`
states another; the stated one wins and the suffix is ignored. Rename the class to the version it is
(`OrderPlacedV3`), or remove `Version` if the name was right. Code fixes do either.

## DDD00035

Error. An event's class name ends in `V0` or a version with a leading zero (`V01`), which cannot be a
version. Rename it: `V1` for a first version, or a name that does not end in `V` and digits. Digits not
after a `V`, as in `Level2Reached`, are fine.

## DDD00036

Error. Two domain events, or two contracts, of one module get the same name and version, usually two
classes of one name in different namespaces. Rename one, or pin another name on one:
`[DomainEventName("ordering.returns-order-placed")]` on a domain event, the name in
`[IntegrationEvent("...")]` on a contract. A domain event and the contract it is published as may share
a name, and so may the versions of one event.

## DDD00037

Error. Two event names that differ only in punctuation (`ordering.order-placed` and
`ordering.order.placed`) would get the same constant in the generated `{Module}EventNames`. Pin one of
them to a name that reads differently.

## DDD00038

Error. A `[RowAccess<TAggregate>]` rule has the wrong shape. Make the class `static partial` and give it
exactly one `public static bool Allows(TAggregate x, Caller caller)` whose body is one expression (after
`=>`, or a single `return`). `Caller` is `DDDToolkit.Abstractions.Access.Caller`.

## DDD00039

Error. Part of `Allows` cannot become SQL. A rule may use the aggregate's properties (including `.Value`
of a typed id and properties of a value object), constants, `caller.UserId`, `caller.IsSignedIn`,
`caller.Role` and `caller.Claim("path")`, compared with `==`, `!=`, `<`, `<=`, `>`, `>=` and combined with
`&&`, `||`, `!`. Replace method calls, locals, other objects and the clock: store the value as a property
of the aggregate, or read it from a claim in `app_metadata`.

## DDD00040

Error. The rule is on a type that is not an aggregate root. Put it on the root; the export makes the
tables of the root's entities follow it.

## Not a diagnostic: the owned type must carry the key part

An exception when the Entity Framework model is built, not at compile time: an aggregate with a
`[KeyPart]` owns a child that has no property for that key part. Give the child a `[KeyPart]` property of
the same name and type, get-only, and set it from the parent when the child is created. See
`composite-keys.md` in the docs.

## Nothing was generated and there is no diagnostic

Check that the generator's package is referenced (Entity Framework, FluentValidation and HotChocolate
each bring their own), that the type is `partial` and carries the generic attribute (`[EntityId<Guid>]`),
and read the generated files with `EmitCompilerGeneratedFiles` turned on.
