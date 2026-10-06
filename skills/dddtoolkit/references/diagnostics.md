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
- DDD00012 and DDD00015 to DDD00019 have never been assigned; an id missing below does not exist.

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

## DDD00014

Warning, once per project and without a line number. The generators run here but the props file of the
`DDDToolkit.Analyzers` package was not imported, so `<DDD_Module>` is ignored and `{Module}EventNames`,
`Add{Module}Converters`, `Add{Module}IntegrationEvents` and `Add{Module}GraphQlRuntimeBindings` are
named after the assembly. Look at how the project references the generators. A `PackageReference` with
`ExcludeAssets`, or with an `IncludeAssets` that leaves out `build` and `buildTransitive`: remove the
restriction (`PrivateAssets="all"` is fine). An `<Analyzer Include="...dll" />` or a project reference
with `OutputItemType="Analyzer"`: add `<ItemGroup><CompilerVisibleProperty Include="DDD_Module" /></ItemGroup>`
to the project or to `Directory.Build.props`. Do not rename the call sites to the assembly-named
methods. Not reported for an assembly with `[assembly: Module]`, whose generated code is named after
the module.

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
public constructor whose parameters are services from the container, or register it by hand. Reported on
the `[assembly: Module]` attribute instead, it names a domain event of another project of the same module
(one without Entity Framework, such as the domain project) that this project cannot see: make the event
`public`.

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
`=>`, or a single `return`). `Caller` is `DDDToolkit.Abstractions.Access.Caller`. The same shape holds for
an `[AccessFunction]`, which may take `string`, `bool`, `int`, `long`, `Guid` or id parameters after the
caller, and needs a one-column key with `Shape = AccessFunctionShape.Set`. A contract declares nothing, or
one `static partial bool Allows(TKey key, ...)` or `static partial AccessSet<TKey> Ids(...)`. A question
is a `static partial` method without a body in a `static partial [AccessFunctions]` class: `[AccessSet]`
returns `AccessSet<T>`, `[AccessScalar]` a value. A name is `schema.name`, `owner/name` or a relative
`name`, letters, digits and underscores.

## DDD00039

Error. Part of `Allows` cannot become SQL. A rule may use the aggregate's properties (including `.Value`
of a typed id and properties of a value object), constants, `caller.UserId`, `caller.IsSignedIn`,
`caller.Role` and `caller.Claim("path")`, `DateTimeOffset.UtcNow` (the database's `now()`), access
functions and `[AccessFunctions]` questions, compared with `==`, `!=`, `<`, `<=`, `>`, `>=` and combined
with `&&`, `||`, `!`. A set-shaped question is asked only with `.Contains(...)`. Replace method calls,
locals and other objects: store the value as a property of the aggregate, or read it from a claim in
`app_metadata`.

## DDD00040

Error. The rule is on a type that is not an aggregate root. Put it on the root; the export makes the
tables of the root's entities follow it. A supporting domain's abstract parent counts as not a root: put
the rule on the application's class declared with the parent's template.

## DDD00041

Error. A `[RowAccess]` rule reads the aggregate's entities itself, `project.Members.Any(...)`. A policy on
the aggregate's table cannot: the entities' policies ask that table back, and Postgres stops with infinite
recursion. Move the expression into an `[AccessFunction<Project>("schema.name")]` class with the same
`Allows(Project, Caller)` shape, and have the rule call `ThatClass.Allows(project, caller)`.

## DDD00042

Error. A class marked `[AggregateRootBase]` or `[EntityBase]` has the wrong shape. A parent is an
`abstract partial class` with type parameters, the id first, constrained
`where TId : IEntityId, IEquatable<TId>`, and not nested in a generic type. The message names what is
missing; a parent that is not partial or not a class reports DDD00005 or DDD00002 instead. Classes
declared with its template get nothing generated until it is fixed, and report no diagnostic of their own; the
compiler errors in them come from the missing base class and go away once the parent is fixed.

## DDD00043

Error. The first type argument of a template attribute (`[Subscription<Guid>]`) is not an entity id.
Templates never generate an id: declare `[EntityId<Guid>] public readonly partial record struct SubscriptionId;`
(in the contracts project when other modules refer to it) and use `[Subscription<SubscriptionId>]`.

## DDD00044

Error. The template's parent takes a type argument from the class declared with another template, and
neither this project nor the projects it references declare one. Add that class, with the attribute the
message names, once, for example `[Subscription<SubscriptionId>] public sealed partial class ShopSubscription;`.
A class in a referenced project counts, so do not add a second one here if another module already has it.
The IDE offers a code fix that declares it next to the class the error is on, named with that class's
prefix and declared with the id named after the parent's id parameter (`TTenantId` gives `TenantId`), or,
when no id has that name, after the template (`OrganizationUnitId` for `[OrganizationUnit]`).

## DDD00045

Error. Two or more classes are declared with the template the message names, so the parent cannot tell
which one to take its type argument from. Keep one. A `[TemplateRegistration]` method reports it too, when
one of its `[TemplateType]` parameters finds several classes.

When the message does not end in "keep one", the template allows several classes (`AllowSeveral = true` on
its marker) and the registration is written once per class, as `AddCommentsForShopInvoiceComment()`; do not
delete a class. Two templates of the method each have several classes: call the package's generic method
for these, with the type arguments written out. Two classes share a name: rename one. "The registration of
each would be called '...'": the package names its registration after something the class says of itself,
such as the aggregate it belongs to (`[Member<..., Document>]` gives `AddDocumentMembership`), and two classes
say the same; make them differ, which for Membership means one member class per resource.

## DDD00046

Error. The template attribute does not fit its parent: the marker names no parent, a parent of the wrong
kind (`[AggregateRootTemplate]` needs an `[AggregateRootBase]` parent, `[EntityTemplate]` an
`[EntityBase]` one), or its own type arguments plus its `[TemplateArgument]`s do not fill every parent type
parameter exactly once, or a parameter that takes the application's class (`Take = TemplateArgumentKind.Type`)
is constrained `new()`, which a generated entity never meets. It is reported on the attribute when the package
is built. This is a bug in the package that declares the attribute; report it there rather than working
around it. More type arguments on the attribute than the parent takes are not this error: they are the
template's own, for a registration to take.

## DDD00047

Error. The class is declared more than one way: two of `[AggregateRoot<T>]`, `[Entity<T>]`,
`[AggregateRootBase]`, `[EntityBase]` and a package's template attributes (`[AggregateRoot<T>]` with
`[Entity<T>]` is DDD00009 instead). Each gives the class a base class. Keep one; the template when the
class extends what a package ships.

## DDD00048

Error. A class a template takes with `Take = TemplateArgumentKind.Type` does not meet a constraint of the
parent's type parameter, usually an interface the parent creates it through
(`IInvoiceLineFactory<ShopInvoiceLine, InvoiceLineId>`). The message
names the class and what it needs. Add it to that class; nothing is generated for the class that takes it
until then.

## DDD00049

Error. The project declares a class with one of the templates of a package's `[TemplateRegistration]`
method (such as `modelBuilder.AddTenancy()`), so it gets that method closed over its classes, and no class
is declared with another template the method needs, here or in a referenced project. Reported once per
missing template, naming every registration that needs it; where a class's parent takes a type from that
template, DDD00044 on that class says it instead. Declare the class the message names, once, for example `[TenantAggregate<TenantId>] public sealed partial class ShopTenant;`; the
same code fix as DDD00044 does it. Until then the call without type arguments does not exist, and its
CS0411 or CS0305 goes away with this error. Reported on the `[assembly: Module]` attribute, the project
declares none of the classes and takes them from the projects of its module (a module's infrastructure
project next to its domain project): declare the missing class in the project that declares the others;
there is no code fix, since it would declare it in the wrong project.

## DDD00050

Error. A class a `[TemplateRegistration]` method takes with `Take = TemplateArgumentKind.Type` does not meet
a constraint of the method's type parameter, usually an interface. The message names the class and what it
needs. Add it to that class; no registration is written until then. An id the method takes is reported the
same way when it is a class where the method asks for a struct, or the other way round: declare the id as a
`readonly partial record struct`. A later type argument of the template (`[Comment<CommentId, StaffId>]`,
taken with `Argument = 1`) is reported when the type written there misses a constraint of the method: use a
type that meets what the message names. "Requires an entity or an aggregate root, whose id it takes": the
type argument must be a class declared `[AggregateRoot<TId>]`, `[Entity<TId>]` or with a template, such as
the resource in `[Member<ShareId, UserId, NamedRole, Document>]`. "Requires an aggregate root": the type
argument names a child entity (`[Entity<TId>]`) where the registration needs an aggregate; name the aggregate.
"Requires a type with a name of its own": the registration is named after that type argument, so it cannot
be an array.

## DDD00051

Error. An argument of a set-shaped question (an `[AccessSet]` method, or `Ids(...)` of a set-shaped access
function or contract) reads the row: a column, or a question about the row. The policy asks the set once
per statement as `column = ANY (ARRAY(SELECT f(args)))`, so its arguments must be constants, the caller or
the enclosing access function's own parameters. Only the argument of `.Contains(...)` may read the row. To
ask about the row, use an access function's `Allows` or an `[AccessScalar]` question instead.

## DDD00052

Error. A function is named without a schema (`"tickets_i_watch"`), so its name is relative to its owner,
and there is none: the assembly has no `[assembly: Module("...")]` and the class has no
`[AccessFunctions(Owner = "...")]`. Add either, or write `schema.name`. The logical name is then
`owner/name`, which the export writes in the schema of the context that defines the function.

## DDD00053

Error. A type argument a template supplies does not meet a constraint of the parent's type parameter: one
of the attribute's own (`[TenantAggregate<TenantId>]`), the id first, or an id the template takes from
another class. Almost always the id is a `partial record`, a class, and the parent says
`where TId : struct`, as every Tenancy parent does. The message names the type, the parameter and what it
requires. Declare the id as `[EntityId<Guid>] public readonly partial record struct TenantId;`. Every class
whose parent takes that id reports it, so one change clears them all, and the errors about their missing
base classes go with it. Other constraints, such as an interface, are judged only of a type from a
referenced project or one that is not `partial`; for a `partial` type in the same project a missing
interface is still CS0315 in generated code: add the interface to the type the error names. "Requires
'IEntityId'" next to DDD00003, DDD00005 or DDD00013 on the id means the id itself was not generated: fix
that error and this one goes with it.

## DDD00054

Warning, in the project that turns the Supabase export on. A referenced package or module offers a row
access contribution (`[assembly: RowAccessContribution(typeof(X))]`: functions, policies and statements
for its tables) that this project does not list, so none of its SQL reaches the migrations. Add
`[assembly: UseRowAccessContribution(typeof(X))]` to the host, the project that runs the export, to use it;
listing a class of the host's derived from `X`, or `X` closed with the host's types when it is generic,
counts too. If leaving it out is deliberate, suppress the warning with `<NoWarn>`.

## DDD00055

Warning, in the project that turns the Supabase export on. A `[SupabaseMigrations]` factory whose
assembly and whose context's assembly both declare no `[assembly: Module("...")]`, so its migration files
are named after the context's class and the export would recognize none of them after a rename of the
class. Add `[assembly: Module("...")]` to the project that holds the context; where files already exist
under the context's name, use that name for the module, or rename the files once.

## DDD00056

Error. An interface marked `[AccessRequests]` that no access behavior can be written for: it does not derive
from `DDDToolkit.Access.IRequireAccess`, has type parameters, is nested in a type or `file`-local, or gives
its behavior the same name as another marked interface of its namespace (`IBillingRequest` and `IBilling`
both give `BillingAccessBehavior`). Declare it at namespace level as `[AccessRequests] public interface
IBillingRequest : IRequireAccess;`, or rename one of the two. Until then nothing asks the access checks for
its requests.

## DDD00057

Error, on an `[AccessRequests]` interface. The referenced version of the Mediator library declares
`IPipelineBehavior<,>`, or `IStreamPipelineBehavior<,>` for the messages answered with a stream, with a shape
the generator does not know (more to implement than one `Handle`, a `Handle` that does not take the message,
a cancellation token and the next-step delegate once each, or one that answers neither a `ValueTask` nor a
`Task` of the response, for a stream an `IAsyncEnumerable` of it), so no behavior is written. Remove
`[AccessRequests]` and write the behavior by hand for that version: a class implementing
`IPipelineBehavior<TMessage, TResponse>` constrained to the interface, whose `Handle` awaits
`AccessChecks<TheInterface>.RequireAsync(message, cancellationToken)` and then calls the next step; register
it as an open generic `IPipelineBehavior<,>`, scoped. A module with stream queries needs the same for
`IStreamPipelineBehavior<,>`: they pass no `IPipelineBehavior<,>`.

## DDD00058

Error, on a class, struct or interface that implements an `[AccessRequests]` interface and Mediator's
`INotification`. Mediator publishes a notification through no pipeline, so the generated access behavior
never sees it and nothing asks the requirement it declares. Take the `[AccessRequests]` interface off the
notification. If the work needs a check, send it as a command or a query that implements the interface; if
only some callers may publish it, call `AccessChecks<TheInterface>.RequireAsync(request, cancellationToken)`
with a request of the module's before publishing.

## DDD00059

Warning, on the aggregate a `[Member<TId, TMemberId, TRoleId, TResource>]` class names as its resource
(`DDDToolkit.Supporting.Membership`). The package's generator writes a private `Members` property on that
aggregate, a `MemberList<TMember, TId, TMemberId, TRoleId>`, and could not: the message lists what it could
not tell. It needs exactly one of each on the aggregate: a get-only `partial` collection of the member class
(`IReadOnlyList<T>`, `IReadOnlyCollection<T>` or `IEnumerable<T>`); a property of `TMemberId`, the owner; a
static property or field of `MembershipCodes`; and `TId` must be an `[EntityId<Guid>]`. A member id that
another generator writes, the id of an `[AggregateRoot<Guid>]`, cannot be seen by it: declare that id with
`[EntityId<Guid>]` yourself. Add what is missing,
for example `public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");`. If the
shape cannot be told (two properties of the member's id, codes kept elsewhere, an id over a `long`), write
the property by hand on the aggregate, which also silences the warning:
`private MemberList<DocumentShare, DocumentShareId, UserId, NamedRole> Members => new(_shares, OwnerId, DocumentShareId.CreateSequential, codes);`.
Do not rename a hand-written list to dodge the warning: any member of that `MemberList` type counts.

## DDD00060

Warning, on a `[Member<TId, TMemberId, TRoleId, TResource>]` class whose `TResource` cannot be its resource:
either `TResource` is no aggregate root (a child entity, the member class itself, a plain class), or it keeps
its members as another member class already, in a collection of that class. A resource has one member class:
name the aggregate root the members belong to, or remove the extra member class. The list of the class the
resource keeps is still written. In a project that also references `DDDToolkit.Supporting.Membership.EntityFramework`
the same mistake is DDD00045 or DDD00050 instead, an error, on the same class.

## DDD00061

Warning, on a call to `Handle` of a Mediator handler (`ICommandHandler`, `IQueryHandler`, `IRequestHandler`, their
stream kinds, or a class implementing one) whose message implements an `[AccessRequests]` interface, and on a
reference to such a `Handle` made into a delegate. The call skips the pipeline, so the generated access behavior
never asks what the request requires. Send the request instead: `await sender.Send(request, cancellationToken)`,
or `sender.CreateStream(...)` for a stream query, with an `ISender` injected where the handler was; the code fix
does this when an `ISender` or `IMediator` is already in reach. It is not reported in a test project
(`IsTestProject` or `IsTestingPlatformApplication` set), for `base.Handle` in an overriding handler, for a
decorator that hands its inner handler the message it was given, or for constructing or injecting a handler.
A generic dispatcher over an unconstrained message type parameter is not seen: send through `ISender` there. A call that is meant elsewhere gets `#pragma warning disable DDD00061` with
the reason. Do not hide it by calling a method of the handler's own instead: the check is skipped all the same.

## DDD00063

Error, on a property or field marked `[TenancyPermissions]` (`DDDToolkit.Supporting.Tenancy.Catalogue`) that the
project composing the modules cannot read as `Type.Member`, so the module's keys would be missing from the
catalogue. Make it `public static`, readable (a field or a property with a getter), declared in a class that is
not generic (nor nested in one), not in an extension block or a file-local type, not a static virtual or abstract
interface member, and of a type that is a sequence of `Permission`:
`[TenancyPermissions] public static IReadOnlyList<Permission> Permissions { get; } = [...];`. Every library's list
is public, whether the library declares a module or not; only an application (an `Exe`) may keep its own list
internal, though not private or protected.
Do not also pass the list to `services.AddTenancyPermissions(...)`: the host's generated
`services.AddTenancyPermissionsOfModules()` adds it, and the catalogue refuses a list added twice.

## DDD00064

Warning, at the project file. Two projects carry one module's name in `<DDD_Module>` and the generators do not
take them together: no `AddTenancy()` or converters written in the infrastructure project from the domain
project's classes and ids. Reported in the project that references the other, naming it, when only one of the two
declares the module, or when neither does and a package's registration is written for the referenced project's
template classes nowhere (what deleting an `<AssemblyAttribute>` item that declared the module, without adding the
switch, comes to). Declare the module where the message says: `<DDD_DeclareModule>true</DDD_DeclareModule>` next
to `<DDD_Module>`, best in the `Directory.Build.props` that sets the name for the whole folder, or
`[assembly: Module("Name")]` in the project. A project that carries the name and must stay no module sets
`<DDD_DeclareModule>false</DDD_DeclareModule>`. Do not silence it with `NoWarn`: the missing registrations are real.
An `.editorconfig` `[*.cs]` severity does not reach it; a `.globalconfig` or `<WarningsAsErrors>` does.

## DDD00062

Error. A class marked `[GraphQLSchema("admin", OperationType.Query)]` also carries something HotChocolate's own
generator registers, which would put its fields into every schema: `[QueryType]`, `[MutationType]`,
`[SubscriptionType]`, `[ExtendObjectType]`, `[ObjectType]`, a base class such as `ObjectTypeExtension`, or a static
method marked `[Query]`, `[Mutation]` or `[Subscription]`. Remove what the message names; the attribute on the class
says what its public static methods are. Also reported when the attribute names no schema (use the name the schema
is registered under, `AddGraphQLServer("admin")`), for a generic class, a `file` class or one that is private or
protected inside another (make it a non-generic internal class), for an instance method (make it static), for a class
with no field, for two methods that are one field (two overloads, or `GetX` beside `GetXAsync`: rename one or give it
a `[GraphQLName]`), and for a method whose name another has and that takes a parameter of a type another generator
writes, such as a data loader's interface (give the method a name of its own).

## Not a diagnostic: the owned type must carry the key part

An exception when the Entity Framework model is built, not at compile time: an aggregate with a
`[KeyPart]` owns a child that has no property for that key part. Give the child a `[KeyPart]` property of
the same name and type, get-only, and set it from the parent when the child is created. See
`composite-keys.md` in the docs.

## Nothing was generated and there is no diagnostic

Check that the generator's package is referenced (Entity Framework, FluentValidation and HotChocolate
each bring their own), that the type is `partial` and carries the generic attribute (`[EntityId<Guid>]`),
and read the generated files with `EmitCompilerGeneratedFiles` turned on.
