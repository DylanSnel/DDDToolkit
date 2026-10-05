# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Releases before 3.0.0 have no changelog entry. Their history is in the
[commit log](https://github.com/DylanSnel/DDDToolkit/commits/main) and the
[git tags](https://github.com/DylanSnel/DDDToolkit/tags).

## [Unreleased]

### Added

#### Core

- **Supporting domains.** A package can ship an abstract generic parent for aggregate roots
  (`[AggregateRootBase]`) or child entities (`[EntityBase]`), and an attribute of its own, marked
  `[AggregateRootTemplate]` or `[EntityTemplate]`, that an application declares its class with:
  `[Subscription<SubscriptionId>] partial class ShopSubscription`. The generator writes the base class, closed
  over the application's own ids, and the parent's invariants, seam and child entities are checked together with
  the class's own, parent first, so an application extends a package's aggregate without losing its rules.
  `[TemplateArgument]` fills a further type parameter of the parent from the class declared with another
  template, in the project or in one it references, such as the subscription's id or the application's own
  invoice line class. A parent's `[KeyPart]`s join the key in front of the class's own. See
  [Writing your own supporting domain](docs/writing-a-supporting-domain.md). The toolkit's own supporting
  domains ship as `DDDToolkit.Supporting.*`, starting with [Tenancy](docs/tenancy.md), described under Tenancy
  below.
- DDD00042 to DDD00048 report a parent or a template that cannot be generated, a template over a raw
  value instead of an id, a template argument no class or several classes provide, a class a template
  takes that does not meet the parent's constraints, and a class declared more than one way.
- DDD00053 reports a type argument a template supplies that does not meet the parent's constraints, most often
  an id declared as a record class for a parent that says `where TId : struct`. It is reported on the class
  declared with the template, and on every class that takes the id, rather than as a compile error inside
  generated code.
- **Registrations closed over the application's classes.** A package marks a generic registration method
  `[TemplateRegistration]` and its type parameters `[TemplateType]`, names the declaring type in
  `[assembly: TemplateRegistrations]`, and every project that declares a class with one of its templates
  gets an internal wrapper closed over its classes: `modelBuilder.AddSubscriptions()` instead of six type
  arguments. Type parameters without a template stay open, and default values are kept. DDD00049 reports a
  template the registration needs and nobody declared, once for the project, naming every registration that
  needs it, and not at all where DDD00044 says it already of a class whose parent takes from that template;
  DDD00050 reports a class, or an id, that does not meet the method's constraints, once however many of the
  package's registrations refuse it. See
  [A registration closed over your classes](docs/writing-a-supporting-domain.md#a-registration-closed-over-your-classes).
- **A registration takes every type argument of a template.** A template may have more type arguments than
  the id, such as what an application identifies an author by in `[Comment<CommentId, StaffId>]`, and
  `[TemplateType(typeof(CommentAttribute<,>), Argument = 1)]` hands the second one to a registration, and so
  on. A method that takes the class takes them all, or its wrapper does not compile: a type argument left open
  could not be closed over a class that is its parent over exactly the types it was declared with, and the
  generator writes such a wrapper as the package declares it, for the package's own tests to fail on. DDD00050 reports
  a later type argument that misses a constraint of the method. See
  [A template with more than one type argument](docs/writing-a-supporting-domain.md#a-template-with-more-than-one-type-argument).
- **A template says more than its parent takes, and a registration is named after it.** A template attribute
  may have more type arguments than its parent has type parameters: those are the template's own, such as the
  aggregate a class belongs to in `[Discount<InvoiceDiscountId, ShopInvoice>]`. A registration takes one by
  position, and with `IdOfArgument = true` on its `[TemplateType]` the id of the entity or aggregate root it
  names, read from how that class is declared, the id generated for a class over a raw value included.
  `[TemplateRegistration(Name = "Add{TOwner}Discounts")]` says what the wrapper is called, with the name of the
  type that fills a type parameter where the braces are: the application calls
  `modelBuilder.AddShopInvoiceDiscounts()`, with one class of the template and with several, so a second
  class changes no call. Classes whose registrations would be called the same are DDD00045, reported once, on
  the class to fix: one the type the registrations are named after holds no property of, or the one declared
  last. DDD00050
  reports a type argument whose id is taken and that is no entity, a child entity named where the
  registration asks for an aggregate root, and a type a registration is named after that has no name. See
  [A registration named after what it is for](docs/writing-a-supporting-domain.md#a-registration-named-after-what-it-is-for).
- **A template declared more than once.** A package marks a template an application declares once per thing
  it has, the comments on a subscription and the comments on an invoice, with `AllowSeveral = true` on
  `[AggregateRootTemplate]` or `[EntityTemplate]`. A project with several such classes gets each registration
  once per class, named after the class, `AddCommentsForShopInvoiceComment()`; with one class the registration
  keeps the method's name, unless the method says what its registration is called. For a template that does
  not say so, a second class is DDD00045. So are two templates of one method that each have several classes,
  and two classes of one name, and the message says what to do about each. See
  [A template declared more than once](docs/writing-a-supporting-domain.md#a-template-declared-more-than-once).
- A code fix for DDD00044 and DDD00049 declares the missing class, named after the class the error is on
  and declared with the id named after the parent's id parameter, or, when no id has that name, after the
  template (`OrganizationUnitId` for `[OrganizationUnit]`).
- A project of a module that declares none of a package's template classes, such as a module's
  infrastructure project next to its domain project, gets the registrations built from the classes the
  module's other projects declare: `modelBuilder.AddTenancy()` is written where the context is, and the
  domain project needs no Entity Framework. Only projects with the same `[Module]` are looked in; a project
  of another module, or of none, still gets nothing. DDD00049, DDD00045 and DDD00050 are reported on its
  `[assembly: Module]` attribute there. Only the lowest such project gets them: a project of the module above
  it, such as an API project that references the infrastructure project to compose the module, gets none, and
  calls the lower project's own public registration.
- **Refusals.** `RefusalException` says a command was refused, before anything changed: a stable `Code`,
  a `RefusalKind` (`Invalid`, `NotPermitted`, `NotFound`, `Conflict`) for the edge to choose a status
  from, and the `Arguments` its message was built from. `IFailureLocalizer.Localize(RefusalException)` is
  a default interface member, so existing localizers keep compiling; `FailureLocalizer` looks a refusal up
  by its code, and `DDDToolkit.HotChocolate`'s error filter turns one into a single coded GraphQL error
  with `kind` and `arguments`.
- `RefusalException.FieldArgument` (`"Field"`), the argument by which a refusal of kind `Invalid` names the
  input it is about, so a form can put the text under that input.
- **`ToolkitRefusals`**, the refusals the toolkit makes itself: `ToolkitRefusals.Refused`, `access.refused`,
  of the kind `NotPermitted`. `DDDToolkit.Localization` carries its text in English and Dutch.
- `ToolkitRefusals.RoleNotAllowed`, `access.role-not-allowed`, of the kind `NotPermitted`, with the argument
  `Role`, in English and Dutch; and `ToolkitRefusals.Of(code, arguments)`, which fills a text from its
  arguments.
- **Texts outside a request.** `CultureScope.Use(culture)`, in the core package, makes a culture the current
  culture and UI culture of a flow of work until it is disposed, and puts back the two before, also after an
  exception: for a mail, a notice or a job's result in its reader's language. `IFailureLocalizer` gains
  `Localize(refusal, culture)`, `Localize(error, culture)` and `Localize(violation, culture)` as extensions,
  which phrase one failure in the culture named whatever the current one is, a localizer of the application's
  own included; an exception handler needs them, because the culture a request's middleware chose is gone
  where the handler runs. See [Texts outside a request](docs/localization.md#texts-outside-a-request).
- **A resx read under other codes.** `AddResource<TResource>(entries)` on the localization options adds a
  resx for failures that carry other codes than its entries are named by: for each code a failure carries,
  the name of the entry that holds its text. It is for a package that names its texts once while the
  application chooses the codes, a prefix per module or per resource. Only the codes given are answered,
  several may read one entry, and `FailureTranslations` checks them language by language like any other
  source. See [A package's texts under your own codes](docs/localization.md#a-packages-texts-under-your-own-codes).
- **Texts a package's registration offers.** `services.AddFailureTexts<TResource>(entries)`, in the core, is what
  a package's registration calls to offer its texts, under the codes it was given: an application that
  localizes its failures with `AddDDDToolkitLocalization()` then adds no line for them. The localizer asks the
  application's own sources first, wherever they were added, then the offers in the order they were made,
  then the toolkit's own messages. An offer is read from the package's assembly by its marker type, without
  `AddLocalization()`, and gives nothing to an application that does not localize. See
  [Texts a package's registration offers](docs/localization.md#texts-a-packages-registration-offers).
- **Bearer tokens.** `BearerTokens.New()` in `DDDToolkit.Security` makes a token for a link or a message, 32
  random bytes as 43 characters that fit a URL, with its SHA-256 digest; `TryDigest` gives the digest of a
  token somebody sent, and refuses every spelling but the one it wrote, and `Matches` compares a token with a
  stored digest in constant time. Store the digest, send the token, keep the token nowhere: `BearerToken`
  does not print it.
- `ActedBy`, in `DDDToolkit.Abstractions`, is who acted as a record keeps it, a kind and an id, and
  `IActedByAccessor`, in `DDDToolkit`, answers it. The one `AddDDDToolkitEntityFramework` registers reads
  the caller. It and every `IEventLogFields` are singletons that read what they answer when asked, so the
  log is written from a pooled context without a scope as well.
- **Access requirements.** A request says what it requires of its caller, and the checks of its module hold it
  to that before its handler runs, without the toolkit knowing how requests are dispatched. A command or query
  implements its module's interface, which derives from `IRequireAccess`, and answers `RequiredAccess` with an
  `AccessRequirement` that says what it requires, from a small vocabulary: the core's own three, which are about
  who is calling and nothing else, `AccessRequirement.AllowAnonymous()` (anyone, a caller who did not sign in
  too), `AccessRequirement.SignedIn()` (a signed-in user, who need not hold anything yet) and
  `AccessRequirement.RequiresSystemWork()` (only the application itself: system work trusted code began,
  `Caller.System` or `Caller.SystemIn(scope)`; work nobody began a caller for is refused too, also where the
  host's accessor answers the system for it), and the cases a package or a module declares. There is no requirement that says
  nothing, and none that leaves the decision to a package: a request that declares nothing is stopped, and a
  request anyone may send says so, named as ASP.NET Core's `[AllowAnonymous]` is. `CallerAccessCheck` decides
  `SignedIn()` and `RequiresSystemWork()` from the host's `ICallerAccessor`, and every module's set asks it
  first, so a host without any supporting domain has them and no check of a module can take them over; a
  caller who is not who they require is refused with `access.not-signed-in` or `access.system-only`
  (`ToolkitRefusals.NotSignedIn` and `SystemOnly`, of the kind `NotPermitted`, in English and Dutch). Who may
  send a request is not what its handler runs with: a handler that does what no caller of its request could,
  such as provisioning a tenant for a registration form, begins system work itself, in trusted code. An
  `IAccessCheck` decides the cases of one owner, `services.AddAccessCheck<TRequests, TCheck>()` adds it to the
  set of one request interface, and `AccessChecks<TRequests>.RequireAsync(request)` asks the first check that
  decides the requirement. It fails closed: a request that declares nothing, or
  something no check of its module decides, is stopped rather than let through, with a message that names the
  call that adds the missing check: a package's own, which `[AccessCheckRegistration]` on its requirement
  says, `services.AddTenancyAccess<IBillingRequest, TContext>()` for Tenancy's cases, and otherwise
  `AddAccessCheck<IBillingRequest, TCheck>()`. A handler acts on its request: the check was asked about what
  the request names. `Checked<T>` keeps what a check worked out for the code after it that handles the same
  request, an answer the handler would otherwise ask for a second time, and `TakeFor` hands it out once. What
  is kept is what the request's latest pass read: a request that passed, never reached its handler and is
  sent again in the same scope is handed what the second pass read, and nothing of the first stays behind.
  `AccessChecks<TRequests>.RequireAsync(request)` puts the request in hand for the flow of work that asked, from
  the moment the checks let it through: `RequestInHand.Current` is that request in the method that asked and in
  what it runs after, the handler and its save included, and what a check kept is found there without the
  request, `Checked<T>.TryFindInHand(out var kept)`. Ask the checks and run the handler in one `async` method,
  as the generated behavior does: only an `async` method gives its caller the flow back as it was, and what runs
  after it returned has nothing in hand. `services.AddAccessChecks<TRequests>()`
  registers the set alone, for whoever asks it in front of the handlers of a module that adds no check, since
  its requests require only what the core decides. All of it is in `DDDToolkit.Access`. See
  [Access requirements](docs/access-requirements.md) and [the vocabulary](docs/access-requirements.md#the-vocabulary).
- **An access behavior, written where the Mediator library is used.** Mark a module's request interface,
  `[AccessRequests] public interface IBillingRequest : IRequireAccess;`, and in a project that references
  [Mediator](https://github.com/martinothamar/Mediator) (`Mediator.Abstractions`) the generator writes
  `BillingAccessBehavior<TMessage, TResponse>` beside it, for every message that implements the interface and
  for no other, and `services.AddBillingAccessBehavior()`, which puts it in the pipeline, per scope, with the
  set of checks it asks. The behavior calls `AccessChecks<IBillingRequest>.RequireAsync` and then the next
  step, and is as visible as the interface. A message answered with a stream passes a pipeline of its own in
  Mediator, so `BillingAccessStreamBehavior<TMessage, TResponse>` is written for `IStreamPipelineBehavior<,>`
  and registered by the same call: a stream query of the module is asked about before its handler streams
  anything. How the library's behaviors are implemented is read from the version the project references: the
  constraints on a message, the order of `Handle`'s parameters and what it answers. The toolkit references no
  dispatcher: the library is noticed by its type, and a project that cannot see it gets nothing written and
  calls `RequireAsync` itself. A host of one project, where Mediator's own generator runs beside the
  toolkit's, adds the behavior with the generated registration and cannot name it in
  `MediatorOptions.PipelineBehaviors`. See
  [The generated behavior, with Mediator](docs/access-requirements.md#the-generated-behavior-with-mediator).
- DDD00056 reports an `[AccessRequests]` interface no behavior can be written for: one that does not derive
  from `IRequireAccess`, has type parameters, is nested or file-local, or gives its behavior the name another
  marked interface of its namespace gives. DDD00057 reports a version of the Mediator library whose
  `IPipelineBehavior<,>` or `IStreamPipelineBehavior<,>` the generator does not know; nothing is written for
  it. DDD00058 reports a notification of the library that implements such an interface: a notification is
  published through no pipeline, so nothing would ask what it requires. It is reported once, a partial type
  at the part that lists what it implements.
- **Start-up checks, brought by the registrations and run with one call.** Every registration that brings
  something to check at start-up now registers the check with it (`services.AddStartupCheck(...)`), and
  `services.RunStartupChecks()` runs them all with one runner, so a host writes no start-up class of its own and a
  check a package adds later reaches it without a change. The runner is a hosted lifecycle service that does its
  work in `StartingAsync`, so the checks run before any hosted service starts and before the server binds its
  port, in a `WebApplication` and a generic host alike. Among the lifecycle services it sits where
  `RunStartupChecks()` is called, so one the host registers before the call, its own migration say, starts
  first. Each check runs as `Caller.System`, begun for that check alone, in four stages
  (`StartupCheckStage`): what the services say, then whether the login role may become every caller, then the
  migrations, then the database, each before the next because a failure of the earlier hides the cause of the
  later; within a stage as registered, and a check may say by name which checks of its stage it runs before
  (`StartupCheck.RunsBefore`). The first that fails stops the start with what it threw, unchanged, and its name in
  the exception's `Data` and the log. `SkipStartupCheck(name, reason)` turns one off and `SkipStartupChecks(reason)`
  every one, each with a reason the log repeats. The registrations' checks: `entity-framework.toolkit-wired`
  (`AddDDDToolkitEntityFramework`); `postgres.row-level-security-wired`, `postgres.login-role-may-switch-to-callers`,
  `postgres.login-role-owns-nothing` and `postgres.definer-owners-bypass` (`AddPostgresRowLevelSecurity`, and so
  `AddSupabaseRowLevelSecurity`); `supabase.migrations-applied` (`AddSupabaseMigrations`); Tenancy's and
  Membership's below; and `pgmq.extension-installed` (`AddPgmqSink`, `AddPgmqConsumer`). The methods behind them
  stay, for a host that calls them by hand; one that keeps its own class and asks for the runner too runs those
  checks twice, which reads the catalogs twice and changes nothing. The checks are off until the host asks for
  them: an application upgrading within 3.x would otherwise stop at a check it never ran, the login role that
  owns its tables for one. pgmq's check, which ran by itself since 3.0.0, stays on by default
  (`StartupCheck.OnByDefault`). The core package now depends on `Microsoft.Extensions.Hosting.Abstractions` and
  `Microsoft.Extensions.Logging.Abstractions` for it. See [Start-up checks](docs/startup-checks.md).
- **A request that goes round its access behavior no longer goes unnoticed.** Nothing noticed a module whose checks
  were registered and whose generated behavior was not, nor a handler called in code instead of sent: the request
  reached its handler with nothing asking what it requires, held only by what the database checks, which per table
  is coarser than one request's requirement. Now `AddAccessChecks<TRequests>()`, and so `AddAccessCheck`, a
  package's registration of its check (`AddTenancyAccess`, the generated `Add{Resource}MemberAccess`) and the
  generated `Add{Module}AccessBehavior()`, brings the start-up check `access.behaviors-registered`
  (`AccessBehaviorChecks.BehaviorsRegisteredCheck`, in the Services stage) for an interface the toolkit wrote a
  behavior for. It reads from the host's registrations every command and query the host can handle, by the
  handler Mediator registered for it, and a host that calls `RunStartupChecks()` does not start while one of an
  interface the toolkit wrote a behavior for lacks that behavior in its pipeline, or a stream query the one for
  streams. That holds every module, one whose registration was forgotten altogether included. The message names
  the behavior, its interface and the line that adds it, `services.AddBillingAccessBehavior()`. A behavior counts
  registered open, as the generated call adds it, or closed over the message, as Mediator registers one listed in
  `MediatorOptions.PipelineBehaviors`, so a module without a stream query needs nothing in the pipeline of
  streams, and a host that lists its behaviors is told to list the one it left out. One registered as itself, or
  under a key, does not count. `AccessBehaviorChecks.EnsureBehaviorsAreRegistered(services)` asks the same of a
  collection a test composes. The generator says what it wrote in a new assembly attribute beside the behavior,
  `[assembly: AccessBehavior(typeof(IBillingRequest), typeof(BillingAccessBehavior<,>), ...)]`, which is what the
  check reads; an interface without one, of a project without Mediator, is held to nothing. See
  [When nothing asks the checks](docs/access-requirements.md#when-nothing-asks-the-checks).
- DDD00061, a warning, reports a request of an `[AccessRequests]` interface handed to its handler directly:
  `Handle` of one of Mediator's handlers (`ICommandHandler`, `IQueryHandler`, `IRequestHandler`, their stream
  kinds, or a class that implements one or derives from one), called or made into a delegate, which passes no
  pipeline. A code fix sends the request with an `ISender` or `IMediator` the code can use where the call is,
  `Send`, or `CreateStream` for a stream query, with the arguments in the order of the handler's parameters.
  Constructing or injecting a handler is not reported, nor `base.Handle` in an overriding handler, nor a
  decorator that hands its inner handler the message it was given, nor generated code, nor anything in a test
  project: the toolkit's props now make `IsTestProject` and `IsTestingPlatformApplication` visible to its
  analyzers, and a test that calls a handler on purpose hears nothing. Elsewhere a call that is meant takes
  `#pragma warning disable DDD00061` with its reason.
- **The check a request passed stays with its handler, to be asked again.** `AccessChecks<TRequests>.RequireAsync`
  keeps the check it let a request through with, its requirement and the request, with the flow of work that
  handles it: `PassedAccessCheck.Current`, in `DDDToolkit.Access`. The handler and everything it awaits find it,
  what sent the request does not, a request the handler sends in turn has its own, and a request that requires
  nothing, one whose check refused and a handler called directly have none. `StillPassesAsync()` asks the check
  again, now, as the same caller: `true` when it still lets the caller through, `false` when it refuses with a
  `RefusalException`, and whatever else the check throws comes out as it is, a `ConcurrencyConflictException`
  too, which says nothing about the caller's rights. Asked again, a check keeps nothing for a handler:
  `Checked<T>.KeepFor` does nothing while it is asked again. Nothing to write for it where the checks are awaited
  in an `async` method that then calls the handler, as the generated behavior does; a dispatcher of your own is
  written that way, and the message of DDD00057 now says so. The toolkit asks it when the policies refuse a save,
  below. See [Access requirements](docs/access-requirements.md#what-answers-it).

#### Entity Framework and row level security

- `ISingleValue<TSelf, TValue>`, implemented explicitly by every generated id, single value object and
  always-valid twin, and `SingleValueConverter<T, TValue>`, which stores any of them. The generated
  `Add{Module}Converters()` also registers, with it, those of the module's other projects that have no
  converter of their own, and the published ones of other modules, with their own converter where their
  project references Entity Framework, so a module's one call covers every id it may store and its domain and
  contracts projects need no Entity Framework reference. The converter registrations of projects that reference
  Entity Framework themselves do not change, and where the module has such a project, whose registration
  this one calls, what that one registered is not registered again.
- `Add{Module}IntegrationEvents()` also registers the domain events of the module's projects that do not
  reference Entity Framework, under the names they had; such an event this project cannot see is DDD00033.
- **Timestamps SQLite can compare.** `configurationBuilder.StoreDateTimeOffsetsAsUtc()` stores every
  `DateTimeOffset` and `DateTimeOffset?` property of a model, owned and keyless types included, as its UTC
  instant in a `DateTime` column, so a query that compares or orders one runs in SQL on SQLite too. The
  two converters the outbox already used for this, `UtcDateTimeOffsetConverter` and
  `NullableUtcDateTimeOffsetConverter`, are public. See
  [Your own timestamps on SQLite](docs/entity-framework.md#your-own-timestamps-on-sqlite).
- **A unique index says what it refuses with.** `index.RefusesAs(code, message, kind)`, next to `IsUnique()`
  in the mapping, makes a save that breaks the index a `RefusalException` with that code instead of a
  `DbUpdateException`: the answer of the check in C# that lost a race, a conflict unless the index says
  otherwise. The message may name properties of the row in braces, `{Number}`, the index's own or any other
  the entity type maps; each is filled from the row that broke the index and is an argument of the refusal,
  through the property's value converter, so an id shows as the value underneath. It is configuration of the
  mapping and needs no migration. `IndexRefusalConvention`, part of `AddDDDToolkitConventions`, fails the
  model for a message that names, in braces, something the entity type does not map, and for an index that is
  not unique. Read on Npgsql,
  SQLite and SQL Server, by the names of each provider's own exception, so `DDDToolkit.EntityFramework`
  references no provider. When one save writes several rows of the entity type that differ in what the
  message names and the database does not say which one it refused, as Npgsql's batches do not, the refusal
  keeps its code and leaves the placeholders as written.
- **`DatabaseRefusalInterceptor`**, which `UseDDDToolkit` adds after the other three, makes those refusals,
  and answers a row a policy denied with `access.refused` (see Changed). `DatabaseRefusal.From(exception)`
  gives what it reads from a failed save, a duplicate key or a policy's denial with the index, table and
  columns the database named, for a translation of your own.
- **`ExpectVersion`.** `context.ExpectVersion(aggregate, version)` in `DDDToolkit.EntityFramework` says
  which version of an aggregate the client last saw: another loaded version is a
  `ConcurrencyConflictException` before anything changes, and the same version leaves the save to compare
  as it does, so a change made after the load is the same conflict. A request's `long? ExpectedVersion` goes
  in as it is, `context.ExpectVersion(aggregate, command.ExpectedVersion)`: `null` compares nothing, and the
  save compares the version loaded, so one line after the load serves a client that names a version and one
  that does not. See [The version the client saw](docs/entity-framework.md#the-version-the-client-saw).
- `IsFixedAfterInsert()`, in `DDDToolkit.EntityFramework`, says a property never changes once its row is
  saved: Entity Framework throws when a save would change it, and a script with `WriteGrants` leaves its
  column out of the `UPDATE` grant.
- **Contexts from a pool.** A context registered with `AddPooledDbContextFactory` or `AddDbContextPool` is
  supported, with the same `UseDDDToolkit(services)` in its options callback: the whole save pipeline, the
  outbox, the outbox processor, the inbox and retention, and row level security, where each rental runs as
  its own caller and nothing of one renter is left for the next. `services.AddScopedFromPool<TContext>()`
  makes the context a scope asks for one taken from the pool, bound to that scope and given back with it:
  it binds the scoped context Entity Framework 10 registers for both pool registrations (10.0.0 and
  10.0.12 both do), and registers one where there is only a factory a host registered itself.
  `context.BindToScope(scopedServices)` names the scope whose services the in-process handlers of a context
  get, for a context rented from the factory, and `context.IsPooled()` says whether a context's options are
  a pool's. A binding lasts as long as its scope, also under a container that hands a registration a
  provider of its own rather than the scope itself: the scope keeps what was bound. See
  [Contexts from a pool](docs/entity-framework.md#contexts-from-a-pool).
- **An event log.** `outbox.KeepEventLog()` keeps the events a context saves in a table of its own,
  `modelBuilder.AddEventLog(Database)`: one `EventLogEntry` per kept event, written with the event's outbox
  row in the same save, with who acted, and never updated. `Keep<TEvent>()` and `Only(...)` choose the
  events, `IEventLogFields` fills columns of the module's own, and `KeepEventLogFor` gives retention a
  window for it. On Postgres every script of policies and every Supabase access file of the context ends
  with the table's guard: triggers that refuse an update, a truncate and the delete of a row younger than
  `keepFor`, for every role, the owner and a superuser included. Other providers have no guard. See
  [An event log](docs/entity-framework.md#an-event-log).
- `EntityFrameworkChecks.EnsureToolkitWired(context)` throws at start-up for a context built without
  `UseDDDToolkit`, or whose model and outbox disagree about an event log or an outbox table, and
  `RegisteredContexts` lists every context type of the container to check. See
  [Checking the wiring](docs/entity-framework.md#checking-the-wiring).
- **A scope around each inbound handler.** `ModuleIntegrationEvents<TContext>.Around(scope)` begins an
  `IntegrationEventScope` around each delivery to the module's handlers: before the inbox reads whether the
  message was applied, until its transaction committed, so the inbox's read, the handler's work and the
  inbox row run as one caller. `IntegrationEventScopes.System` runs them as the application itself. With
  `RequireExplicitCallers`, a module without a scope does not run its handlers: the delivery fails with a
  `NoCallerException` naming the module, and is tried again. See
  [Who the handlers run as](docs/integration-events.md#who-the-handlers-run-as).
- **Fail-closed callers, opt-in.** `services.RequireExplicitCallers()` registers `CallerOptions` with
  `RequireExplicitCallers` on. An accessor that knows no caller then throws the new `NoCallerException` instead
  of answering the system: `AmbientCallerAccessor`, the Supabase accessor of
  `DDDToolkit.Auth.Supabase.AspNetCore` outside a request, and `GetSupabaseCaller()` of
  `DDDToolkit.Auth.Supabase.AzureFunctions` outside an HTTP trigger. The toolkit's own bookkeeping begins
  `Caller.System` for itself, the outbox processor, retention and the receiver that pgmq, Wolverine and
  MassTransit deliver through, and the handlers it calls run with no caller, inside the new
  `Callers.BeginNone()`, until something begins one for them. The outbox poller, `OutboxBackgroundService`,
  begins the system caller before it resolves its processor, and with it its context, so whatever makes that
  context, such as a factory that chooses a data source by who is calling, sees the poller's rental as the
  system's. An in-process handler that leaves changes unsaved on the outbox processor's context fails the
  message instead of having them saved with the mark as the system, and the interceptor refuses the system as
  the caller unless `Callers.Begin(Caller.System)` made it so, whichever accessor answered it.
  `PostgresRowLevelSecurityInterceptor.RequireExplicitCallers` says whether the interceptor the contexts use was
  built with the option, for a start-up check. Without the option nothing changes. See
  [Fail-closed callers](docs/row-level-security.md#fail-closed-callers).
- **The scoped system caller.** `Caller.SystemIn(scope)` is the application's own work inside a scope a
  module chose, such as its name: it runs as `PostgresRowLevelSecurityOptions.SystemInRole`, `ddd_system_in`,
  with the claims `{"role":"ddd_system_in","scope":"…"}`, so a policy can ask whose work it is. It is not
  `Caller.System`: `IsSystem` is false for it and `IsSystemIn` true. With `SystemInRole` set to `null` a
  scoped system caller fails before its query connects. See
  [The scoped system role](docs/row-level-security.md#the-scoped-system-role).
- **The scoped system role.** `ddd_system_in`, `PostgresRowLevelSecurityOptions.SystemInRole`, is a role
  that can neither log in nor bypass row level security, for the application's own work that should stay
  inside the policies. `PostgresRowAccess.SetupScript()` makes it and grants it to the login role, and a
  script or access file whose policies are for it makes it where it is missing and grants it to the role
  running the file; making it needs a role that may create roles. Both fail when a role of that name
  exists that can log in or bypass row level security, is a superuser, or has the privileges of a role
  that owns tables, and when the user's or the anonymous caller's role has its privileges. Like `anon` and
  `authenticated`, it needs privileges on your tables before its policies let it read or write anything.
  See [The scoped system role](docs/row-level-security.md#the-scoped-system-role).
- **Row access rules name roles by what they are for.** `RowAccessRoles.User`, `Anonymous` and `SystemIn` in a
  rule's `To` stand for the roles the host configures for those callers, and a script writes the roles a
  `RowAccessRoleNames` gives them: `authenticated`, `anon` and `ddd_system_in` by default, or with
  `RowAccessRoleNames.Of(options)` the roles `PostgresRowLevelSecurityOptions` configures.
  `RowAccessRoleNames.Of(options)` validates the options as the registration does, and throws
  `ArgumentException` for a role they would refuse. A role's own name keeps working. `PostgresRowAccess.Script`
  and `CreateStatements` take a `RowAccessExport` with the roles and the caller functions, and
  `PostgresRowAccess.Scripts` writes several contexts' scripts in the order to run them. See
  [One policy per command and role](docs/row-level-security.md#one-policy-per-command-and-role).
- **Row level security: token roles.** `PostgresRowLevelSecurityOptions.TokenRoles` maps the roles a
  signed-in user's token may carry besides `authenticated` to the database roles their queries run as,
  `options.TokenRoles["analyst"] = "desk_analyst"`, and a rule names such a role by the token role,
  `RowAccessRoles.Token("analyst")`, or `RowAccessRoles.TokenPrefix + "analyst"` in an attribute, so no role name
  is compiled into a module. `RowAccessRoleNames.TokenRoles` carries the map to a script,
  `RowAccessRoleNames.Of(options)` fills it in, and the Supabase export takes it from
  `token:<role>=<database role>` pairs of `SupabaseRowAccessRoles`; a rule or a contribution for a token role
  nobody mapped is refused when the script is written. `PostgresRowAccess.SetupScript(options)` makes each
  mapped role `NOLOGIN NOINHERIT` and grants it to the login role, and a script whose policies name one
  makes it where it is missing. Both refuse a mapped role that can log in, can bypass row level security, is a
  superuser, has the privileges of a role that owns tables, or whose privileges the user's or the anonymous
  caller's role has, and refuse a scoped system role whose privileges a mapped role has. A mapped role is
  kept apart from the user's and the anonymous caller's role in the other direction too: both scripts refuse
  one that has the privileges of either, `grant authenticated to desk_analyst` say, since the holder of its
  token would then get every policy and every privilege written for those callers. A map to
  `SystemRole`, to the scoped system role or to `AnonymousRole` is refused where the options are registered.
  See [Token roles](docs/row-level-security.md#token-roles).
- `PostgresRowLevelSecurityOptions.UnknownTokenRole` says what a signed-in user gets whose token carries a
  role on no list: `Refuse`, the default (see Changed), or `Anonymous`, the anonymous caller's role and
  claims, with the modules' settings asked for an anonymous caller.
- **Settings of your own on the connection.** An `IRowLevelSecuritySettings`, registered with
  `services.AddRowLevelSecuritySettings<TSettings>()`, puts settings next to the role and the claims, in the
  same statement: every name it declares, to its value or to `''`. Names are checked when the interceptor is
  built, `prefix.name` in lower case, not `request.*`, one owner each, and not `ddd.caller_mark`, which marks a
  transaction the caller was set for; a value for a name a provider did not declare fails the connection. See
  [Settings of your own](docs/row-level-security.md#settings-of-your-own).
- **Row level security through a transaction pooler.** `PostgresRowLevelSecurityOptions.Scope`, set to
  `RowLevelSecurityScope.Transaction`, sets the caller's role, claims and settings for one transaction at a
  time, so nothing lives on a session and a pooler that hands each transaction another server connection,
  PgBouncer in transaction mode or Supabase's transaction pooler, carries nothing from one client to the
  next. A command outside a transaction gets `RESET ROLE; CALL ddd.use_caller(…);` in front of its text, in
  the same round trip; a transaction a context begins, or is handed with `UseTransaction`, gets the settings
  as its first statement, and so does a `TransactionScope`; a save always runs in a transaction, the
  context's `AutoTransactionBehavior` being set to `Always`; and a
  command that would end the transaction or change the role itself, `COMMIT` or `SET ROLE` in SQL of your
  own, is refused. `No Reset On Close` and port 6543 are allowed in that scope. `PostgresRowAccess.SetupScript`,
  every script of policies and every exported access file make the procedure `ddd.use_caller`, which only
  the role the application logs in as may call. `Connection`, as before, stays the default. See
  [How the settings travel](docs/row-level-security.md#how-the-settings-travel) and, for Supabase,
  [Through the transaction pooler](docs/supabase.md#through-the-transaction-pooler).
- `PostgresRowLevelSecurityInterceptor.Scope` says how long the settings last, as the interceptor read it
  when it was built.
- **A statement timeout per kind of caller.** `PostgresRowLevelSecurityOptions.StatementTimeouts` gives a
  user's, an anonymous caller's or the system's statements a `statement_timeout` of their own, in the
  statement that sets the caller. Postgres applies a timeout stored on a role when that role logs in, not
  when a session switches to it, so without this every caller ran under the login role's. A kind left out
  gets the login role's own again. See
  [A statement timeout per caller](docs/row-level-security.md#a-statement-timeout-per-caller).
- **Connections for SQL of your own.** `CallerConnections`, registered with
  `services.AddCallerConnections(provider => dataSource)`, opens a connection, or begins a transaction, with
  the current caller set as a context's connections have it, for SQL a module sends outside Entity
  Framework. A context handed such a connection or transaction finds the caller there. See
  [SQL of your own](docs/row-level-security.md#sql-of-your-own).
- **Set-shaped questions in row access rules.** A question only the database answers may answer with a
  set, an `AccessSet<T>`, which a rule asks with `Contains`: `TicketsIWatch.Ids().Contains(ticket.Id)`
  becomes `"Id" = ANY (ARRAY(SELECT desk.tickets_i_watch()))`, which Postgres asks once per statement, as
  an InitPlan, and answers through the key's index. An access function with
  `Shape = AccessFunctionShape.Set` answers with the keys of every aggregate it allows, as
  `RETURNS SETOF`, and gets `Ids(...)` in place of `Allows(key)`; one whose whole question is about the
  aggregate's entities reads their table alone. `[AccessFunctions]` classes declare questions of their own
  without a body, `[AccessSet]` for a set and `[AccessScalar]` for one value, which the generator implements
  to throw `DatabaseOnlyException`; a scalar question with no argument that reads the row is asked once, as
  `(SELECT f())`. A comparison with a scalar question is SQL's own, `=`, `<>`, `<` and so on, which is
  unknown for a caller the question does not know, a policy's no, and stays unknown under `!`. See
  [Set-shaped questions](docs/row-level-security.md#set-shaped-questions).
- **Access functions over the entities of an entity.** Inside an `Any` over one of the aggregate's
  collections, an access function may ask the entity about its own entities in the same way, as deep as the
  model goes: `ticket.Comments.Any(comment => comment.Reactions.Any(reaction => reaction.User == caller.UserId))`.
  The inner question becomes an `EXISTS` inside the outer one, joined to the entity it is asked of, and reads
  that entity, the caller and the function's parameters. A set-shaped function over them still starts from the
  first entities' table and never reads the aggregate's. Asking one of the aggregate's own collections from
  inside another stays DDD00039. See
  [Asking the aggregate's entities](docs/row-level-security.md#asking-the-aggregates-entities-access-functions).
- **Access functions that take more than the aggregate.** `Allows(aggregate, caller, ...)` may take strings,
  numbers, flags, `Guid`s and ids after the caller, which become the SQL function's parameters after the
  key, and the generated `Allows(key, ...)` takes them too. A contract declares such a question,
  `static partial bool Allows(TKey key, ...)` or `static partial AccessSet<TKey> Ids(...)`, and the
  generator implements it. The generated class also has `RowAccessOwner`, `RowAccessParameters` and
  `RowAccessShape`, which the Supabase build hands to `RowAccessFunction.For`.
- **Function names relative to the module.** A function named without a schema, `tickets_i_watch`, is
  relative to its owner, the module its assembly declares or the `Owner` of the class's
  `[AccessFunctions]`: its logical name is `desk/tickets_i_watch`, and a script creates it in the schema of
  the context that maps its aggregate. A contract's generated `Name` is its logical name, so a definition in
  another module matches it. `PostgresRowAccess.Scripts` and the Supabase build resolve a logical name a
  rule of one module asks to the function another module defines; `PostgresRowAccess.FunctionNamesOf`,
  which names the functions of the contributions of a `RowAccessExport` too, and
  `RowAccessExport.FunctionNames` do it for a script of one context. Two functions that would be one in the
  database, one `schema.name`, are refused, whichever contexts write them, and so is a schema that is not
  lower case, which Postgres would not keep as a script writes a function's name. See
  [Names relative to the module](docs/row-level-security.md#names-relative-to-the-module).
- `DateTimeOffset.UtcNow` and `DateTime.UtcNow` in a rule are the database's `now()`, where they were
  DDD00039.
- DDD00051 reports an argument of a set-shaped question that reads the row, which would make the database
  ask the set once per row, and DDD00052 a function named without its schema in an assembly that declares
  no module, on a class without an `[AccessFunctions]` owner.
- **Column rules: a stricter key for one column.** A policy cannot see which column a statement changes, so a
  rule for changing a row has to let through whoever holds any key that changes it, and whoever holds one then
  changes every column. `Columns` on a row access rule, `[RowAccess<Project>(RowOperations.Change, Columns =
  [nameof(Project.State)])]`, makes it a column rule: one condition more, for a change of those columns alone.
  The rule is written where the other rules are, and nothing goes on the aggregate. The export writes it as a
  trigger, `BEFORE UPDATE OF` the columns, firing only where one of them changes, that asks the rule of the row as
  it was and as it is about to be, as a policy for `UPDATE` asks a rule, and raises `42501` with a message that
  names the rule. The row's policies still apply: a column rule allows nothing they refuse. Several column rules
  on one column add up, and the columns the same rules hold share one trigger. A value object is every column
  it is stored in, and `"Planned.From"` one of them. Every role a caller's statement runs as is held, the roles
  the rules are for, the user's, the anonymous caller's, each mapped token role's and any other role a policy
  lets change the table, and a held role no column rule is for may not change the column; the application's own
  work, the scoped system role and the bookkeeping role, passes unless a rule names it, and so does the tables'
  owner. A rule that reads only the row's key, as a set-shaped question about its id does, is asked once per
  changed row while the key stays, rather than twice; a trigger runs per row, so a statement that changes the
  column in many rows asks a set once for each, where a policy asks it once per statement. The trigger's
  function has an empty search path: DDD00038 reports a `Sql.Call` in a column rule that names its function
  without a schema, and a `Sql.Raw` there names every table and function with one. A column rule's
  `RowAccessSql` starts with `{columns}`: `RowAccessRule.ForColumns` makes one from it for a script of your own,
  `RowAccessRule.For` refuses it, and an export of an earlier version stops at the placeholder it does not know
  rather than write the rule as a policy for the whole row. `RowAccessRule.Columns` and `IsColumnRule` say what
  a rule is; the Supabase build lists a rule's columns from its attribute. DDD00038 reports `Columns` with an
  operation besides `Change`, and a name that is no property of the aggregate or of a value object it holds, or
  is a collection of its entities; a collection of values stored in the row, such as an array of strings, is one
  column. The export refuses a property the model stores in no column of the aggregate's table, naming the
  rule. See [Column rules](docs/row-level-security.md#column-rules).
- **An access guard of the database refuses as a policy does.** A trigger that raises SQLSTATE `42501` with the
  hint `ddd:access.refused` (`DatabaseRefusal.GuardHint`) is an access guard's refusal: through `UseDDDToolkit`
  a save it refuses throws a `RefusalException` with the code `access.refused` and the kind `NotPermitted`, a
  403 from a route and a `RefusalError` from a mutation, with what the save threw as its inner exception, where
  it ended as a `DbUpdateException`, a 500. The log line names the guard by the constraint it raised with, and the
  table where it is known: "the guard projects_owner_stays"; like a policy's refusal, it is an information line when
  the request's access check, asked again, refuses as well (`PassedAccessCheck`), and a warning otherwise. `RowAccessModel.Refusal(guard, message)` writes the
  statement for a trigger written in a contribution, `RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege',
  CONSTRAINT = ..., HINT = 'ddd:access.refused', MESSAGE = ...;`, and a trigger written by hand says the same;
  one that raises `42501` without the hint fails as before. Every access guard the toolkit writes raises it: the
  trigger of a column rule, the Membership package's lock on an owner column, and Tenancy's triggers on a seat's
  status and on who changed a row; the triggers that hold what may never be, whoever writes, keep their codes.
  The code stays `42501`, which the Data API answers with a 403 and a pgTAP test expects, and the caller learns
  nothing of the guard: its name belongs to the schema, and the warning carries it. The mark is the hint, which
  a `RAISE` hands on as written and Postgres never translates, so it reads the same in any server language.
  `DatabaseRefusal.From` reads such a failure as `DatabaseRefusalKind.GuardRefused`, with the guard's name as
  `Constraint`, for a save and for a statement of your own, such as an `ExecuteUpdate`, which no interceptor
  answers. The Membership start-up check refuses a database whose owner lock was written without the hint,
  until the new access file is applied. The other guards are not compared at start-up: a database whose access
  files an earlier build wrote refuses without the hint, a failure of the server, until the new ones are
  applied. See [When the database refuses](docs/row-level-security.md#when-the-database-refuses).
- **Row access contributions: policies a package ships.** A package or a module offers a class that implements
  `IRowAccessContribution` with `[assembly: RowAccessContribution(typeof(X))]`, and a host writes it into its
  migrations by listing it with `[assembly: UseRowAccessContribution(typeof(X))]` in the project that runs the
  Supabase export; DDD00054, a warning, reports an offer the host does not list, and says what to write for
  it: the attribute, the attribute closed over a type of the host's, or, for an offer whose constructor takes
  what only the host knows, a class of the host's that derives from it. For every context, the export
  asks each listed contribution for SQL functions, policies and statements written from the context's model
  (`RowAccessModel` writes the table and column names, column types and stored values), and writes them into
  that context's access file under a comment naming the contribution, its assembly and the assembly's version:
  the functions with the access functions, in the order they ask each other; the policies with the rules', a
  permissive one merged with theirs for the same table, command and role, a restrictive one apart; and the
  statements last. A contributed function is created in the context's default schema with
  `SET search_path = ''`, runs as its caller unless it says `SecurityDefiner`, is asked by its logical name,
  `owner/name`, and is executable by its `GrantTo` and no other role but its owner: a policy for another role
  that asks it, itself or through an access function, is refused, naming the rule and the function. A
  contribution can keep tables to itself, and a rule or another contribution that would add a policy to one is
  refused, as are two policies of one name on a table; a statement may make a `SECURITY DEFINER` function only
  for a trigger, with `SET search_path = ''`. A context whose only row access is a contribution gets an access
  file, and its migrations start with the drop. A host uses an offer by listing it, a class of its own derived
  from it, or, for a generic one, the offer closed with its own types, which is how a contribution gets what
  only the host knows. `RowAccessExport.Contributions` and `SupabaseMigrationOptions.RowAccessContributions`
  hand contributions to a script of your own and to an export by hand. An access file is normalized to the
  files' own line endings before it is written or compared, so a contribution whose SQL carries the platform's
  line endings, as SQL taken from Entity Framework does on Windows, does not make every build write another
  file. See [Policies a package ships](docs/row-level-security.md#policies-a-package-ships).
- **A contributed function may return rows of named columns.** `ContributedFunction.Returns` takes
  `TABLE ("TicketId" uuid, due timestamp with time zone)` next to a type and `SETOF` a type: each column a name,
  in double quotes or plain, and a type, and nothing else. The Supabase export's check that a function keeps
  what it returns covers the columns.
- **A contributed function Postgres folds into the query that asks it.** `ContributedFunction.Inlinable` is for
  a function that answers rows for other queries to read, as a view would: the script writes it without
  `SET search_path = ''`, which Postgres does not fold a function with, so a query over it is planned over the
  tables it reads, with their indexes and the caller's policies. Only a function that runs as its caller, is
  `STABLE` or `IMMUTABLE` and is one `SELECT` may say so; anything else is refused. See
  [Policies a package ships](docs/row-level-security.md#policies-a-package-ships).
- `{caller:claims}` in a rule's or a contribution's SQL is the caller's claims, `(SELECT auth.jwt())` on
  Supabase.
- **Privileges from the policies.** `RowAccessExport.WriteGrants` has a script of policies write the
  privileges of its tables as well, read from the policies it holds: every privilege the script's roles and
  `PUBLIC` held on a table is taken back, and a role gets each command a permissive policy allows it,
  `USAGE` on the schema and on the sequences of a table it may add rows to, and `UPDATE` on the columns that
  may change. An outbox table and an event log take `INSERT` from whoever saves, an inbox table gives
  `SELECT` and `INSERT` to the scoped system role, and a rule that lets a role change rows it may not read is
  refused when the script is written. Those three tables have no policies, so on them a script also takes
  back what a role an earlier script named still holds: every holder that cannot log in and is held to row
  level security. The Supabase build takes it as
  `<SupabaseRowAccessGrants>Write</SupabaseRowAccessGrants>`, and a module without a rule then gets an
  access file for its outbox. Off by default, and then every script and access file is what it was. See
  [Privileges from the policies](docs/row-level-security.md#privileges-from-the-policies).
- **A login that owns nothing.** `RowAccessRoles.System`, `@system`, is the role the application's own
  bookkeeping runs as, `RowAccessRoleNames.System`, which `RowAccessRoleNames.Of(options)` takes from
  `SystemRole` and the Supabase build from `system=<role>` in `SupabaseRowAccessRoles`. A script that names it
  makes the role, without a login and without `BYPASSRLS`, refuses one that is another caller's role, and with
  `WriteGrants` gives it the outbox, the inbox, the migration history and the rows of an event log that may go,
  and nothing else; on the outbox it updates only the four columns that say how a delivery went. Nothing a
  script writes changes while no rule or contribution is for `RowAccessRoles.System` and `WriteGrants` is off,
  also where a rule or a grant spells that role's name out. A host whose `SystemRole` bypasses row level
  security and that turns `WriteGrants` on writes its scripts with
  `RowAccessRoleNames.Of(options) with { System = null }`, because such a role is refused as a bookkeeping role.
  `PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync` checks at start-up that the role the application
  logged in as owns and holds nothing, may create nothing in the schemas, may neither create roles nor
  replicate, and may switch to no role that is a superuser, bypasses row level security or owns something
  there, naming the role and every finding with its
  fix; a login role that owns a schema there is the role the migrations run as, and that is the one finding,
  with the one fix, to log in as a role of its own, and
  `EnsureRowLevelSecurityWired` that a context runs its commands as the caller. See
  [A login that owns nothing](docs/row-level-security.md#a-login-that-owns-nothing).
- `PostgresRowAccessChecks.EnsureLoginRoleMaySwitchToCallersAsync` checks at start-up that the role the
  application logged in as may switch to every role the context's interceptor switches to: the user's, the
  anonymous caller's, the scoped system role, `SystemRole` and the role of every mapped token role, and, where the
  settings last one transaction, that it may call `ddd.use_caller`. A role is switched to when a caller of its kind
  connects, so without the check a grant left out passes the start and fails that caller's first request. It
  asks as the login role itself, on the context's connection opened past the interceptor, because the system
  caller's role is one of those it asks about, and names each role that is missing or not granted with the
  statement that fixes it and, for a host whose Supabase build writes the login role's migration, the pair of
  `SupabaseRowAccessRoles` that maps the role, since there a role left out is a pair left out.
- **Forced row level security.** `RowAccessExport.ForceRowLevelSecurity`, and
  `<SupabaseForceRowLevelSecurity>true</SupabaseForceRowLevelSecurity>` in the Supabase build, write
  `FORCE ROW LEVEL SECURITY` after every `ENABLE` of a script, so a table's owner is held to its policies
  too. `PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync` checks that the functions that run as their
  owner are owned by a role that may bypass it, without which they would answer that nobody may do
  anything; it passes while no table is forced. See
  [Forcing row level security](docs/row-level-security.md#forcing-row-level-security).

#### Supabase and Auth

- **Roles and caller functions for the Supabase export.** `SupabaseMigrationOptions.Roles` and
  `SupabaseMigrationOptions.CallerFunctions`, and in the build the project properties
  `SupabaseRowAccessRoles` (`user=…|anonymous=…|system-in=…`) and `SupabaseCallerFunctions`
  (`uid=…|role=…|claims=…`). The pairs are separated by `|`, because MSBuild splits the export's variables
  at every `;`; a `;` fails the build, and an unknown key, a malformed pair, a role no policy can be for
  or a caller function that is not a call without arguments is an `error :` line. See
  [Roles and caller functions of your own](docs/supabase.md#roles-and-caller-functions-of-your-own).
- **The role the application logs in as, written by the export.** `<SupabaseLoginRole>sample_api</SupabaseLoginRole>`
  in the project that exports, beside `SupabaseRowAccessRoles`, and `SupabaseMigrationOptions.LoginRole` by hand,
  write the migration that makes that role, `{version}_login_role.sample_api.ddd.sql`: `NOLOGIN NOINHERIT`,
  and granted the roles the policies are written for, the user's, the anonymous caller's, the scoped system
  role, the bookkeeping role and the roles of the mapped token roles, and nothing else: no table, schema or
  function. A role of that name that is there already is refused, with a hint that names the fix, where it is a
  superuser, may bypass row level security, create roles or replicate, or has the privileges of a role without
  switching to it, also by a grant made while it inherited. Each role is granted in a statement of its own
  unless the role may switch to it already, so the file runs again without harm, and beside the migration of
  another database on the server that grants the same at the same moment. It makes the roles the access files
  make where none has yet, and needs the user's and the anonymous caller's to exist. The login and its
  password stay the deployment's. The file is written after every other file of the
  build and, as an access file, never again: when the roles change, the next build writes a new one that grants
  them as they are and takes back what the one before granted and no caller runs as any more, and `Check` fails
  until it is there. A name that is not a plain lowercase identifier, is a word SQL keeps, is one of Postgres's or
  Supabase's own roles or is one of the mapped roles is an `error :` line that says what to use; unset, nothing
  is written and every file is what it was. `Export` and `Compare` of the sources report it in one more report,
  after the sources'. See
  [The role the application logs in as](docs/supabase.md#the-role-the-application-logs-in-as).
- DDD00055 warns about a `[SupabaseMigrations]` factory whose assembly and whose context's assembly both
  declare no module: its files are named after the context's class, so the export would recognize none of
  them once the class is renamed.
- **Accounts before the first sign-in.** `IIdentityAccounts`, in `DDDToolkit.Identity`, is a port for the
  accounts people sign in with, as an application's own server code needs them: `InviteByEmailAsync` makes
  an account and has the identity provider mail the person, `CreateAsync` makes one under an id the
  application chose and mails nobody, `InviteAccountAsync` has an account the application made mailed by its
  id, the first time or again, and answers with an `IdentityInvitation`, `FindAsync` says whether an account
  was ever used, and `DeleteAsync` deletes one, for an erasure or for an account nobody came for. An account is known by its id: nothing looks one up
  by an address and no answer carries one. `IdentityAccountOutcome.Created` is answered only for an account
  the call made itself, just now, so an id the application holds is never that of an account somebody else
  registered first. An address that already has an account is an answer,
  `IdentityAccountOutcome.AddressTaken`, rather than an error, also for the later of two calls that arrive
  together, so the application goes on in its own way and tells whoever asked the same as for a new address.
  An account an invitation made, could not have mailed and could not take away again is an
  `IdentityAccountLeftBehindException` with its id.
- `DDDToolkit.Auth.Supabase`: `SupabaseAuthAdmin`, a client for Supabase Auth's admin API, for server code:
  `CreateUserAsync` makes a user, under an id of your own when you give one, `InviteUserAsync` has Auth mail
  a user its invitation by id, `InviteByEmailAsync` is Auth's own invitation of an address, and
  `FindUserAsync`, `UpdateUserAsync` and `DeleteUserAsync` work by id. An invited address that has an
  account is `SupabaseInvitationResult.AlreadyRegistered`. Nothing is
  retried and nothing is logged, and a refusal is a `SupabaseAuthAdminException` with the status and Auth's
  error code and none of Auth's text, so neither an address nor the secret key reaches a log; its
  `DuplicateKey` says the database refused a taken id, or an address that another call registered at the
  same moment. No redirect
  is followed, so the key goes to the Auth URL alone: a handler of the host's own that follows redirects is
  refused, and so is an Auth URL in plain http to another machine unless the host passes `allowPlainHttp`,
  for an Auth server on a private network of its own. `SupabaseIdentityAccounts` is the adapter of
  `IIdentityAccounts` over it, and `services.AddSupabaseAuthAdmin(authUrl, secretKey)` registers both. Its
  invitation makes the user first and has Auth mail it after, because Auth's own invitation also mails a
  user who was there already, who in a project that lets anybody sign up can be a stranger's; a user whose
  mail could not be sent is taken away again, so the invitation can be repeated. See
  [Users before their first sign-in](docs/supabase.md#users-before-their-first-sign-in).
- `DDDToolkit.Auth.Supabase`: `SupabaseTokenHandler`, the token handler behind both the bearer scheme and
  `SupabaseTokenValidator`, for a host that checks tokens in a place of its own;
  `SupabaseAuthOptions.AuthUrl`, for an Auth server that answers elsewhere than `{ProjectUrl}/auth/v1`;
  `SupabaseAuthOptions.AllowPlainHttp`, for one that is reached in plain http on a private network of the
  host's own; and `SupabaseTokens.KeysAddressOf(authUrl)`. `DDDToolkit.Auth.Supabase.AspNetCore`:
  `AddSupabaseJwtBearer` takes a `SupabaseAuthOptions` as well as a project URL.
- `context.SupabaseCaller()`, in `DDDToolkit.Auth.Supabase.AspNetCore`, is a request's own caller, the
  user of its validated token or `Caller.Anonymous`, whatever caller is ambient, so a host can make it
  current at the start of every request.

#### HotChocolate

- **Typed errors in mutation payloads.** `AddDDDToolkitMutationConventions()` in `DDDToolkit.HotChocolate`
  turns on HotChocolate's mutation conventions for every mutation and gives every payload an `errors` list
  typed by the toolkit: `RefusalError` (code, kind, and `field` from the refusal's `Field` argument),
  `InvalidValuesError` (code `invalid-value`, one `ValueFailure` per validation error), `BrokenRulesError`
  (its first violation, and every `RuleViolation`) and `ConcurrencyConflictError` (code
  `concurrency-conflict`), all implementing the interface `CodedError` (`ICodedError`): a code, a message
  in the reader's language and the arguments as `FailureArgument`s. A resolver declares nothing and only
  throws. An exception of a derived type is not matched and stays a top-level error, which
  `AddDDDToolkitErrors()` codes for a class derived from a refusal, an invalid value or a broken rule; queries
  keep `AddDDDToolkitErrors()`, whose error for a refusal that names its input carries it as
  `extensions.field`. In a Fusion source schema the error types are `@shareable`
  (`ShareableErrorTypesInterceptor`), so every module can have the conventions. See
  [Typed errors in mutation payloads](docs/graphql.md#typed-errors-in-mutation-payloads).
- **Enum values, spelled the host's way.** `AddDDDToolkitEnumValues(EnumValueSpelling.LowerSnakeCase)`
  spells the values of the application's enums `not_permitted`, as `JsonNamingPolicy.SnakeCaseLower` does
  for a REST API beside the schema; `UpperSnakeCase` is HotChocolate's own. A member with `[GraphQLName]`
  keeps its name, HotChocolate's own enums keep its spelling, and a member the lower spelling would turn
  into `true`, `false` or `null` fails the schema's build. `AddDDDToolkitErrors(EnumValueSpelling)`
  spells a refusal's `kind` extension the same way; without the argument it stays `NotPermitted`. See
  [Enum values, spelled your way](docs/graphql.md#enum-values-spelled-your-way).
- **GraphQL bindings for ids of other projects.** The generated `Add{Module}GraphQlRuntimeBindings()` also
  binds the ids and single value objects of the module's other projects that have no nested provider of
  their own, and the published (`[ModuleContract]`) ones of other modules: each to the default scalar of its
  value, with `SingleValueChangeTypeProvider<T, TValue>` as its converter and, for an identifier over a value
  a node id can carry, `SingleValueNodeIdSerializer<T, TValue>`, both new in `DDDToolkit.HotChocolate.Types`
  and both through `ISingleValue`. A module's domain and contracts projects therefore need no HotChocolate
  reference, and so no ASP.NET Core, and a project that declares no id of its own, such as a module's API
  project, still gets the method. An assembly that declares no module is left alone, and the bindings of
  projects that reference HotChocolate themselves do not change; where the module has such a project, whose
  bindings this one calls, what that one bound is not bound again. See
  [Bound by a project that does not declare it](docs/graphql.md#bound-by-a-project-that-does-not-declare-it).
- **GraphQL types over an application's own records.** `AddDDDToolkitEntityNullability()` in
  `DDDToolkit.HotChocolate` makes every field of an object type with a key (`[EntityKey]`) nullable in the
  schema, except the fields of the key, whatever the type it is declared over says
  (`EntityFieldsNullableInterceptor`). A module can then declare its GraphQL types with HotChocolate's
  `[ObjectType<T>]` over the records its application layer answers, with nothing said per field, and a
  reference a gateway cannot resolve reaches the client as the object with its key and every other field
  `null`, without an entry in `errors`. Only the outermost nullability changes, and a type without a key and
  a type that is only its key are not touched. See
  [Types over your own records](docs/graphql.md#types-over-your-own-records).
- **A permission key on a GraphQL field.** `AddDDDToolkitKeyAuthorization()` in `DDDToolkit.HotChocolate`
  registers `KeyAuthorizationHandler` on HotChocolate's own authorization: the policy of `[Authorize("<key>")]`
  on a field is a permission key, asked of the `IFieldKeys<TParent>` a module registers for the type of the
  object the field belongs to. A refused field is `null` with the module's `RefusalException` at its path,
  coded by `AddDDDToolkitErrors()` as a refused query is, and the object and its other fields stay; an
  `IFieldKeys` that asks through a data loader makes a batch of parents, as a rule a page, one question. A
  rule nobody can answer is refused, never allowed: a parent type with nothing registered, a field without a
  parent, a rule that names roles or is applied after the resolver or during validation. It replaces any
  other authorization handler of the application, HotChocolate's ASP.NET Core policies included. See
  [A permission key on a field](docs/graphql.md#a-permission-key-on-a-field).
- **Paging by an id.** A generated struct identifier is a key HotChocolate's paging can order a list by:
  `OrderBy(order => order.Id)` and `ThenBy(order => order.Id)` in front of `ToPageAsync`, where HotChocolate
  answered "The key type is not supported". `SingleValueCursorKeySerializer<T, TValue>`, new in
  `DDDToolkit.HotChocolate.Paging`, writes the id into a cursor as HotChocolate writes its value, and the
  page after a cursor compares the column itself. The generated `Add{Module}GraphQlRuntimeBindings()` gains a
  line per struct identifier it binds, its own and those of other projects, that registers it when the method
  is called; `SingleValueCursorKeySerializer<T, TValue>.Register()` does the same by hand. A class
  identifier is not comparable and is not registered. The package gains no dependency: `ToPageAsync` itself
  is in `GreenDonut.Data.EntityFramework`, which the project that holds the context references. See
  [Paging by an id](docs/graphql.md#paging-by-an-id).
- **The schemas of a modular monolith, printed for tests.** `InMemoryFusionSchemas`, a singleton
  `AddInMemoryFusionGateway()` registers: `PrintGatewayAsync()` is the composed schema as the endpoint
  serves it, `PrintSourceAsync(name)` one module's source schema with the directives the gateway composes
  by, and `SourceSchemaNames` the modules' names, so a test compares each with a committed file. See
  [Your schemas in a test](docs/graphql.md#your-schemas-in-a-test).

#### Tenancy

- **Tenancy's checks are start-up checks.** `AddTenancy` brings `tenancy.catalogue-builds`, `tenancy.contexts-wired`
  and `tenancy.unknown-stored-keys`, which logs a key a role holds that the catalogue has lost; `AddTenancyPostgres`
  brings `tenancy.explicit-callers`, `tenancy.seated-token-roles`, `tenancy.system-in-role-confined`,
  `tenancy.system-reads-across-tenants` and `tenancy.policies-in-place`, the last three before the read of the stored
  keys whatever order the two were registered in. A host runs them with `RunStartupChecks()`; the methods of
  `TenancyChecks` and `TenancyPostgresChecks` stay. See [Setting it up](docs/tenancy.md#setting-it-up).
- **The supporting domains are published**: Tenancy as `Temp.DDDToolkit.Supporting.Tenancy`, `.EntityFramework` and
  `.Postgres`, Membership as `Temp.DDDToolkit.Supporting.Membership`, `.EntityFramework` and `.Postgres`, at the
  toolkit's version. Membership's two generators ship inside its packages, not as packages of their own.
- **Tenancy**, the toolkit's first supporting domain: who somebody is in a tenant, and what they may do where in
  its organization. It comes in three packages:
  `DDDToolkit.Supporting.Tenancy` holds the model, its rules and its use cases, with no database in it;
  `DDDToolkit.Supporting.Tenancy.EntityFramework` holds `AddTenancy()` for a context of the application's own,
  the organization tree as a closure table and the access questions as Entity Framework queries, on every
  provider; and `DDDToolkit.Supporting.Tenancy.Postgres` holds the same questions as SQL functions and row level
  security policies. An application declares its own ids and a class of its own for each of the package's
  aggregates, the tenant, the organization, the seat and the role, and may add to the catalogue: the role packs
  a tenant starts with, keys of its own and marks on keys that manage access. The use cases provision a tenant, place
  seats, give roles and change them, each held to who may give a role, and a tenant keeps an administrator.
  `TenantSelection` finds the seat a request's verified identity has in the tenant the request names,
  `ScopeToTenant` and `UseTenancy` keep an entity's rows to a tenant with a filter and a save check, and a
  module asks where the caller holds a key inside a query of its own, through `ITenancyQuestions`. Every refusal
  has a code, with texts in English and Dutch. See [Tenancy](docs/tenancy.md).
- **Tenancy: an administrators' pack may list its keys.** A `RolePack` with `Administers` that lists no keys
  makes a role that holds every live key. One that lists keys makes a role that holds those and no others, so an
  application can have administrators who run access without holding the keys to its modules' work. The list
  must include every one of Tenancy's own keys, `tenancy.history.view` among them, and every key that manages
  access, itself or through a key that implies it: `TenancyCatalogue.Build` refuses a pack that leaves one out
  and names each key, because a role that manages access is given only by a seat that holds its keys that do,
  and an administrator gives every role. With a pack that lists, marking another key as managing access stops
  the application at start-up until the pack lists that key too. The list is what the administrators' role
  starts with, not a wall around the seat: an administrator still puts any key into a role, and gives itself a
  role that manages no access. On Postgres `pack_keys` answers the listed keys for such a pack, so the access
  file written from the catalogue is the migration, as for any change of a pack.
- **Tenancy: an administrators' pack when the application declares none.** A catalogue that declares no
  administrators' pack at all gets `TenancyPacks.DefaultAdministrators` from `TenancyCatalogue.Build`: key
  `administrator`, named Administrator, for every shape, seeded on provision and listed first, with no keys, so
  it holds every live key, one a module adds later included. A tenant's first seat is given its role, so a
  catalogue need not name a pack: `new ApplicationCatalogue(Permissions: ...)`, or `Packs: []` as before, and
  calls that name packs are unchanged. A catalogue that declares an administrators' pack
  for one shape and not the other is still refused, and the problem now says the default is added only when
  none is declared. While the default is added, a pack of the application's that has its key, or one of its
  names ignoring case, Administrator or the Dutch Beheerder, is refused, with the fix: rename the pack, or
  declare it with `Administers: true`. `TenancyCatalogue.HasDefaultAdministrators` says whether a built
  catalogue has it. Its role is named by the application's `IRolePackTexts` first, by the pack's key, and
  otherwise by the package, in English or Dutch, from `TenancyPackTexts.resx` and its Dutch twin. On Postgres
  the access file written from the catalogue has the pack, so `pack_keys('administrator')` answers every live
  key and `EnsurePoliciesAreInPlaceAsync` agrees with the catalogue the application runs. An application that
  switches to it keeps its tenants' old administrators' roles; a change of shape then copies the default, and
  is refused while the tenant has a role of the same name.
- **Tenancy: a unit has no kind, and an application needs no catalogue.** No access rule read a unit's kind:
  Tenancy only checked that it was one of the catalogue's and kept it. So the package keeps none, and an
  application that tells its units apart adds a field of its own to its unit class, an enum say, set in a
  callback of the use case that makes the unit: `TenantToProvision.ConfigureRoot` for the root, next to
  `ConfigureTenant` and `ConfigureFirstSeat`, and the new `configure` of `OrganizationCommands.AddUnitAsync`
  (and of `OrganizationAggregate.AddUnit`) for every other unit. Each runs on the application's own class
  before the save, so the field is written with the unit, the class's rules judge it there, and a callback
  that throws saves nothing; `configure` runs before the organization takes the unit in, so one that throws
  leaves the organization as it was, for a later save in the same scope too. The directory's `ListUnitsAsync`
  and `UnitsByIdAsync` take a view, `(UnitSummary, TUnit) => TView`, so an application answers its own field
  beside what Tenancy keeps from the units the directory read, with no read more; the forms without one still
  answer `UnitSummary`. With the kinds gone, and the default administrators' pack above, every part of
  `ApplicationCatalogue` is optional, `new ApplicationCatalogue()` included, and so is
  `TenancyOptions.Catalogue`: left unset, Tenancy builds the catalogue from its own keys and the modules'
  contributions, and every tenant starts with the default administrators' role. An export that builds the
  catalogue without the registration calls `TenancyCatalogue.Build(contributed)`, the same catalogue. A
  catalogue is for what Tenancy decides access with and cannot know by itself: the packs a tenant starts with,
  keys no module owns, and marks on keys that manage access. See
  [What the catalogue is for](docs/tenancy.md#what-the-catalogue-is-for) and
  [The kind of a unit](docs/tenancy.md#the-kind-of-a-unit). From 3.2.0-preview.1 or 3.2.0-preview.2:
  - Drop `UnitKinds` from `ApplicationCatalogue`, and the `UnitKind` records with it; `Packs` may go too when
    it is empty. A positional `new ApplicationCatalogue(packs, kinds, ...)` becomes `new(packs, ...)`.
  - Drop the root's kind from `TenantToProvision` (the fifth argument, `RootKind`) and the kind from
    `AddUnitAsync(parent, name, kind, ...)`, `OrganizationAggregate.AddUnit` and
    `TenancyInstances.NewOrganization`. To keep a kind, add the field to your unit class and set it with
    `ConfigureRoot: root => root.SetKind(...)` and `configure: unit => unit.SetKind(...)`.
  - `OrganizationUnitEntity.Kind`, `MaxKindLength`, its rule `KindIsSet`, `UnitSummary.Kind`,
    `OrganizationUnitAdded.Kind`, `TenancyCatalogue.UnitKinds` and `KnowsUnitKind`, and the refusals
    `tenancy.kind-invalid` and `tenancy.unknown-unit-kind` are gone. An event already stored keeps its `Kind`
    in its payload.
  - `AddTenancy()` no longer maps the units' `Kind` column, which holds your catalogue's keys. Add a migration.
    Without a field of that name on your unit class, it drops the column and the kinds with it. To keep them,
    add a field named `Kind` and map it as text (`HaveConversion<string>()` or a converter of your own): mapped
    as a number, the migration cannot turn the stored keys into integers. Then make every stored key read as a
    member of the field's enum, because Entity Framework refuses a value the enum has none for, and with it
    every load of that tenant's organization. Either store the enum by the keys you had, as the sample's
    `UnitKindKeyConverter` does for keys that are its members' names in lower case (a read ignores case), or
    have the migration rewrite the keys to the members' names before the column changes
    (`UPDATE ... SET "Kind" = 'HeadOffice' WHERE "Kind" = 'head-office'`), as a role that row level security
    lets through: on Postgres the package's export forces it on the table, so its owner is held to it as well.
- **Tenancy: a module states its keys once.** A module marks the static list it declares its permission keys
  on with `[TenancyPermissions]`, and states them nowhere else. Tenancy's generator, which now ships inside
  `DDDToolkit.Supporting.Tenancy` in `analyzers/dotnet/cs` and is no package of its own, writes
  `TenancyPermissionsOfModules` into every project that references Tenancy and declares no module with
  `[assembly: Module]`, an internal class in the namespace named after the project's assembly: `All`, every
  module's keys, one list after the other, and `services.AddTenancyPermissionsOfModules()`, which adds them as one
  contribution. The host makes that one call, and an export builds with
  `TenancyCatalogue.Build(application, TenancyPermissionsOfModules.All)`, so neither names a module, and a module
  that is added reaches both with the next build. While no module marks a list, `All` is empty and both still
  compile. A marked list the composing project could not read is the new error DDD00063 where it is declared:
  one that is not static, has no getter, is a static virtual or abstract member of an interface, is declared in
  a generic type, an extension block or a file-local type, is no sequence of `Permission`, or, in a library,
  whether it declares a module or not, is not public. Only an application may keep a list of its own internal.
  The same list added twice, by the host's call and by the module's own `AddTenancyPermissions`, is refused at
  start-up with one problem that names its keys and the call to take out. A pack that names a key the catalogue
  does not know, and a question about one, now say how a module's keys reach the catalogue, which is what a host
  that leaves the call out meets. `AddTenancyPermissions` stays, for keys added by hand. The start-up check on
  Postgres that compares the database's functions with the catalogue the host runs with stays as well: the host
  and the export are two programs, and build the same catalogue only while they reference the same modules. See
  [A module states its keys once](docs/tenancy.md#a-module-states-its-keys-once). From 3.2.0-preview.1 or
  3.2.0-preview.2:
  - Mark each module's list of keys with `[TenancyPermissions]`, make it public, and take
    `services.AddTenancyPermissions(thatList)` out of the module's registration.
  - Call `services.AddTenancyPermissionsOfModules()` once in the host, with `using <the host's assembly name>;`
    in a top-level `Program.cs`, and build the export's catalogue from `TenancyPermissionsOfModules.All` instead
    of a list of the modules' keys, in a project that declares no module.
- `TenancyUseCases<…>.IStore` is what the use cases ask of a storage; the Entity Framework store implements it,
  and so does a store of your own. Among its members: `ListSeatsAsync(tenant, only, ...)`, the tenant's seats as
  the directory shows them, all or the ones among the ids given, read from the seats themselves and never with
  an identity; `ListTenantsAsync`, for the tenants' directory; and `AdministratorsAsync` and
  `RightsAMoveChangesAsync`, which answers `MoveReach` rows: what the use cases ask about every seat's rights,
  they ask the store, which a storage answers from wherever it can. `ITenancyReadSource.SeatsHoldingAt` is a
  default interface member, so a read source of your own need not implement it.
- `ITenancyQuestions.SeatsHoldingAt(key, unit)`: the active seats that hold a key at a unit now, held there or
  above it, as a query that composes into a module's own. A seat that manages grants, seats or units somewhere,
  or roles for the whole tenant, learns every holder; any other seat learns only whether it holds the key there
  itself; system work in a tenant learns every holder of that tenant. On Postgres the function
  `seats_holding_at` answers it for a seat, by the same rule.
- **Tenancy: key sets in one statement.** `ITenancyQuestions.WhereIHold(keys)` answers every pair of a unit and
  a key the caller holds there, `RoleKeys(keys)` every pair of an active role and a key it grants, and
  `UnitsUnder(unit)` the unit and every unit below it. Each is one query, composes into a module's own, checks
  its keys against the catalogue, and answers nothing to nobody and the whole tenant to system work in it. See
  [Key sets for navigation, abilities for actions](docs/tenancy.md#key-sets-for-navigation-abilities-for-actions).
- **Tenancy: the read model carries access facts only.** The rows a module maps of Tenancy's, the views of
  `AddTenancy` and `AddTenancyReadModel` and the read functions of `AddTenancyReadFunctions`, carry ids, keys,
  periods, statuses, a unit's parent and a role's pack and keys, and no text that is shown to people. So a
  module cannot read a name of Tenancy's in a query of its own; it answers ids, and names are asked of the
  directory, by id. The directory's lists and `WhoAmIAsync` answer from Tenancy's own seats, roles and units.
- **Tenancy: names, by id.** `TenancyDirectory.SeatsByIdAsync`, `RolesByIdAsync` and `UnitsByIdAsync` answer
  what the seats, roles and units with the ids given are called, to whoever works in the tenant, a seat or
  system work, with no key asked: a seat's name and status and never its identity, a role with whether it
  manages access, a unit with its path from the root, whichever unit the caller is placed under. An id of
  another tenant, or of nothing, is left out of the answer without a word. A question takes at most
  `TenancyDirectory.MostIds` ids, 200; more is the new refusal `tenancy.too-many-ids`, with texts in English
  and Dutch. A question costs the same statements however many ids it carries. See
  [Names](docs/tenancy.md#names).
- `TenancyModel.ReadsBeyondAccessFacts(model, schema, tables)`: what a module's model maps of Tenancy's beyond
  the rows of the read model, one sentence per finding: a type of the module's own on a table, view or function
  of Tenancy's, or a property added to a row, a shadow property included. Empty for a model that reads the
  read model alone, and for Tenancy's own. A test of the application holds every module's context to it. See
  [What a module reads of Tenancy](docs/tenancy.md#what-a-module-reads-of-tenancy).
- **Tenancy: which token roles hold seats.** `TenancyOptions.TenantSelection.SeatedTokenRoles`
  (`TenantSelectionOptions`) lists the token roles `TenantSelection` seats, `authenticated` by default. A
  signed-in user whose token carries another role is nobody in every tenant, `tenancy.not-seated`, and
  nothing is looked up for them. On Postgres, Tenancy's contribution closes its own tables and every table
  kept to a tenant to the database role of each mapped token role, with a restrictive policy, and
  `EnsureSystemInRoleIsConfinedAsync` also fails when such a role has the scoped system role's privileges, or
  the privileges of the user's role, or may execute a function in Tenancy's schema, by a grant of its own or
  with the privileges of another role it was given: the access files give Tenancy's functions to signed-in
  users and to the scoped system role alone. `TenantSelection.SeatsOfAsync(caller)` lists a person's own seats in
  every tenant by the same rule, none for a token role that holds no seat, and
  `TenancyPostgresChecks.EnsureSeatedTokenRolesAreSignedInUsers` fails at start-up for a seated token role
  that reaches the database as any role but a signed-in user's.
- **Tenancy: operators who only read.** `TenancyOptions.OperatorTokenRoles` lists the token roles of your own
  staff who look across tenants. An operator holds no seat: tenant selection answers nobody for it, and
  registration refuses a token role that is on both lists. `TenantDirectory.ListAsync(after, size)` lists every
  tenant by slug, a page at a time, with its name, its status and its count of active seats, to an operator and
  to nobody else (`tenancy.operators-only`; `tenancy.page-size-invalid` and `tenancy.cursor-invalid` for a page
  it cannot give), read as the operator and never as the system. On Postgres, `TenancyRowAccessContribution`
  takes the operators' token roles as a second constructor argument and writes their policies: the role each
  is mapped to reads every row of Tenancy's tables and of the access history and writes none, and on a module's
  table kept to a tenant it reads what a rule of the module admits and writes nothing. The export refuses an
  operator's token role that is mapped to no database role, to the role of a signed-in user or to a role another
  token role shares, and `EnsurePoliciesAreInPlaceAsync` checks that the options and the database agree. See
  [Operators](docs/tenancy.md#operators).
- **Tenancy: what a request requires, and the check that decides it.** `TenancyRequirement` is the set of
  cases a command or query declares where Tenancy is the one that knows, spelled with `TenancyAccess`:
  `InTenant()`, a caller who works in the tenant; `ForTheWholeTenant(key)`; `AtUnit(key, unit)`, a key held at
  a unit or above it, closed over the application's unit id from the argument; and `RequiresOperator()`, one of
  the application's operators. Each is refused with Tenancy's own codes. A request handed to a use case of the
  package says the first thing that use case asks, which the request itself can name, and the use case asks
  again past the check and keeps the rules only it can read, such as who may give a role that manages access or
  whether a unit may move; so a caller it would refuse first is refused at the door with the same code and key.
  System work is the core's `AccessRequirement.RequiresSystemWork()`, whatever tenant it acts in: a handler that
  acts in one asks Tenancy which, and Tenancy refuses system work outside any tenant there. Tenancy's own use
  cases that only system work may call, provisioning a tenant and suspending, reactivating or closing one,
  refuse a seat with the same `access.system-only`. Every name for
  system work in the C# API follows `Caller.System`: `Caller.SystemIn(scope)`, `TenancyWork.BeginSystem`,
  `TenancyWork.BeginSystemIn` and `RequiresSystemWork()`; the database roles `ddd_system` and
  `ddd_system_in` keep their names.
  `services.AddTenancyAccess<TRequests, TContext>()`, in `DDDToolkit.Supporting.Tenancy.EntityFramework`, adds
  the check that decides them to the checks of one module's request interface. It is generated closed over the
  application's four ids in the module that declares Tenancy's classes, and called with the ids written out
  in any other. Only a key for the whole tenant or at a unit reads: one statement, over the module's own context, on a
  context of its own where a factory is registered. `TenancyOptions.IsOperator(caller)` says who an operator
  is, for the check and the tenants' directory alike. The packages reference no dispatcher. See
  [What a request requires of its caller](docs/tenancy.md#what-a-request-requires-of-its-caller).
- **Tenancy: who acted.** `TenancyActor` names who made a change, with ids only: a seat, an operator's verified
  identity, the system with its scope, or a link's token. Every Tenancy caller that can change something
  carries one, `TenancyCaller.Actor`. `TenancyWork.BeginOperator` and `BeginOperatorIn` begin system work that
  is recorded as the operator it is carried out for, and `BeginTokenIn` system work that answers a link's
  token. `AddTenancy` puts `TenancyActedByAccessor` around the `IActedByAccessor` registered before it, so an
  event log says `seat`, `operator`, `system` or `token`; an accessor registered as a scoped service is refused
  there, since the wrapper is a singleton, and `TenancyChecks.EnsureWired` refuses a context that keeps the
  access history when an accessor registered after `AddTenancy` took its place.
- **Tenancy: every event says who made the change.** Each domain event of Tenancy's ends with `By`, the
  `TenancyActor` of the caller whose command raised it. An event about a tenant, a unit or a role is therefore
  closed over the seat id as well: `TenantSuspended<TenantId, SeatId>`,
  `OrganizationUnitAdded<TenantId, OrganizationUnitId, SeatId>`, `RoleCreated<TenantId, RoleId, SeatId>`;
  `AddTenancyDomainEvents` takes the four id types. `RoleKeysChanged` names the keys that came in and went out,
  `Added` and `Removed`. The methods of the aggregates that raise an event take the actor as a last argument,
  `by`, which the use cases fill. On a tenant, an organization and a role those methods are generic over the
  seat id, so a call that names nobody names the type (`tenant.Activate<SeatId>()`), and
  `TenancyInstances.NewTenant`, `NewOrganization` and `NewRole` take the seat id as a type argument. As JSON, a
  `TenancyActorKind` is its word, `seat`, `operator`, `system` or `token` (`TenancyActorKindJsonConverter`), and
  the kind of an actor is that word whatever the serializer's options say about enums.
- **Tenancy: who changed a row.** `entity.RecordsWhoChanged()` adds six shadow columns to an entity kept to a
  tenant, who wrote the row first and who changed it last, each as a kind, a seat and an operator's identity,
  and the save interceptor of `UseTenancy` fills them from the Tenancy caller. Who wrote a row first is fixed
  once the row is there. On Postgres the contribution writes a trigger on every such table that holds a
  signed-in user to its own seat, keeps scoped system work from writing a seat's kind, and lets neither change
  who wrote the row first. The first save refuses a model that calls it on an entity that is not kept to a
  tenant, or on a derived type alone in a hierarchy stored in one table. See
  [Who changed a row](docs/tenancy.md#who-changed-a-row).
- **Tenancy: the tenant of a row is fixed.** `ScopeToTenant`, and `AddTenancy` for Tenancy's own tables, mark
  the property that holds a row's tenant with `IsFixedAfterInsert()`. The save check refuses a row moved to
  another tenant, Entity Framework refuses the change as well, and privileges written from the policies leave
  the column out of `UPDATE`. It is configuration of the mapping, and needs no migration.
- **Tenancy: an access history.** `modelBuilder.AddTenancyEventLogTable(Database)` maps the event log with the
  tenant of each event on its row and an index on the tenant and the time, and `AddTenancy` registers what fills
  the tenant in. The new key `tenancy.history.view`, which manages no access, reads it. On Postgres the
  contribution keeps the table to itself: a person adds rows about their own seat in the tenant the connection
  names, a seat with the key for the whole tenant reads its tenant's rows, system work reads its tenant's and
  adds in Tenancy's own scope, never as a seat, an operator's role reads every row, and where the export
  writes privileges and the table lets rows go, the role of your own bookkeeping finds and removes the old
  ones. The history
  belongs in the context that maps Tenancy's tables: the export refuses it in any other. See
  [Access history](docs/tenancy.md#access-history).
- **Tenancy: the events that change access.** `log.AddTenancyEventLog<TenantId, SeatId, OrganizationUnitId, RoleId>()`,
  inside the outbox's `KeepEventLog`, keeps in the access history every event of Tenancy's that changes who may
  do what, in the save that raised it, with who made the change. The four renames change nobody's access and
  are left out; the outbox still stores them.
- **Tenancy's unique indexes answer for themselves.** A tenant's slug, a person's seat in a tenant, a role's
  name in a tenant, a tenant's root and a seat's primary placement each declare the refusal the use case
  gives for the same rule (`tenancy.slug-taken`, `tenancy.identity-has-seat`, `tenancy.role-name-taken`,
  the refusal of a second root and `tenancy.second-primary`), so a save that lost a race is refused the
  same way on SQLite and SQL Server as on Postgres, with nothing registered to translate it. The Tenancy
  sample's unique project number answers `projects.number-taken` the same way.
- **Tenancy: roles in the tenant's language.** `IRolePackTexts` gives a role pack's name and description in one
  language, and the use cases ask it when they make a role from a pack: with `TenantToProvision.Language` at
  provisioning, and with the language passed to `ChangeShapeAsync` when a change of shape copies packs, between
  the role ids and the cancellation token, `null` for the catalogue's own texts. Without texts or without a
  language a role gets the catalogue's. A translated name is checked like any role's name. See
  [Roles in the tenant's language](docs/tenancy.md#roles-in-the-tenants-language).
- **Tenancy: the application's own fields at provisioning.** `TenantToProvision.ConfigureTenant` and
  `ConfigureFirstSeat` are called on the new tenant and its first seat before the tenant is activated, so
  fields the application added to its classes are written in the save that provisions, and a callback that
  throws leaves nothing saved.
- Tenancy's refusals of kind `Invalid` name the input they are about in the `Field` argument: `name`,
  `displayName`, `description`, `reason`, `kind`, `slug`, `until`, `keys`, `identity`, `ids`, and `size` and
  `after` of the tenants' directory. `tenancy.tenant-required` names none, and no header either, in either
  language, "This request names no tenant.": how a request names its tenant is the host's. The Dutch texts have
  no word for the reader, neither the familiar nor the formal one: which of the two fits is the application's to
  choose, in a resx of its own added before the package's. `tenancy.not-permitted`, for one, is "Hiervoor is het
  recht {Key} nodig." See [Refusals in English and Dutch](docs/tenancy.md#refusals-in-english-and-dutch).
- **Tenancy: invitations.** Declare a class with `[InvitationAggregate<InvitationId>]`, map it with
  `modelBuilder.AddTenancyInvitations<TInvitation, TInvitationId>()` and register it with
  `services.AddTenancyInvitations<TInvitation, TInvitationId, TContext>(...)`, and `InvitationCommands` issues
  an invitation for an address into a unit, with a role and an end for its grant, lists a tenant's open
  invitations, cancels one, and accepts one with its token as a signed-in identity, which makes the seat, its
  primary placement and the grant in one save. Issuing takes what adding the seat and giving the role take, a
  role that manages access is offered only by a seat that holds its keys that do, and accepting asks the
  issuer's rights again, so an invitation never gives more than its issuer could give at that moment. An
  invitation ends after `TenancyInvitationOptions.DefaultLifetime`, works once, forgets its address when it is
  over, and is refused for another address than the one the host says the identity has. Its token is stored as a
  digest only, in a table of its own. New events `tenancy.invitation-issued`, `tenancy.invitation-cancelled` and
  `tenancy.invitation-accepted` (`outbox.AddTenancyInvitationEvents`), each with who acted, and new refusals
  with English and Dutch texts: `tenancy.invitation-not-found`, `invitation-lapsed`, `invitation-used`,
  `invitation-cancelled`, `invitation-state`, `invitation-unbacked`, `address-mismatch`, and, with a `Field`,
  `address-invalid`, `invitation-lifetime` and `invitation-grant-ends-first`. `TenancyTableNames` has
  `Invitations` and `InvitationDigests` as its last two names, and `TenancyTable` has `InvitationDigest`. On
  Postgres the contribution writes the policies for both tables where the model maps them: an invitation is read
  by the seats that manage seats at its unit and added by a seat that could make the grant itself, a trigger
  keeps what it offers as it was issued, the digests are read by no caller's role, and `invitation_of_digest`
  answers which invitation a token is for to Tenancy's own system work alone; the start-up checks cover them. A
  context that adds invitations needs a migration for the two tables and a new export of the access files. See
  [Invitations](docs/tenancy.md#invitations).
- **Tenancy under a naming of your own.** `TenancyTableNames.SnakeCase` names Tenancy's tables, and the two
  filtered indexes `AddTenancy` names itself, in snake_case, for a context that names everything else with a
  naming convention such as `UseSnakeCaseNamingConvention()`. `TenancyTableNames.RootIndex` and `PrimaryIndex`
  name those two indexes under any other naming; left out, they are named after their tables. Every
  other name, and every stored status, in the SQL Tenancy writes is the model's:
  - the conditions of the two filtered indexes read their columns by the names the model has when
    `AddTenancy` maps them;
  - the two indexes keep the names they are given, whatever a convention would name them after their table
    and columns, which for the root's index is the name of the plain index on the same column;
  - a row the store or a module reads from one of Tenancy's functions is mapped to no table, so a convention
    that names every entity type has no migration create one;
  - on Postgres the functions, policies and triggers name your tables and columns and compare a status with
    the value as it is stored, while the functions keep their own names and the columns they answer under, so
    a module's model is the same under every naming.

  Tenancy's tests on Postgres run under both namings. See [Your own naming](docs/tenancy.md#your-own-naming).
- Tenancy works with contexts from a pool: its tenant filter, its save check and its rights writer read the
  caller at each use, which its tests hold for saves through a pooled context, on SQLite and on Postgres, and on
  a data source of one connection. Its messages for a read across tenants that finds no context to make, and for
  a start-up check on Postgres that finds no context with Tenancy's tables, name `AddScopedFromPool`.
- `TenancyCallers.BeginNone()`: no Tenancy caller until it is disposed, for a read that is no seat's and no
  tenant's made in the middle of somebody's work, as `Callers.BeginNone()` is for the toolkit's caller.
- `TenancySystemReads.TenantsToSweepAsync(context, scope, ...)`: the ids of the active and the suspended tenants,
  for the rounds of any module, which then begins its own system work in each tenant under its own scope. On
  Postgres the function `tenants_to_sweep` answers it, to scoped system work of every scope, in no tenant.
- **Tenancy on Postgres**, in `DDDToolkit.Supporting.Tenancy.Postgres`. Tenancy's rules as row level security, a
  second lock under the tenant filter and the save check: `TenancyRowAccessContribution`, which a host derives
  with its catalogue and lists, writes Tenancy's questions as SQL functions, the policies on its tables and on
  every table kept to a tenant, and triggers that keep a tenant's administrator, back every right with a grant,
  keep the closure to the tree, and keep a seat, a placement and a grant to what they are about. The policies
  know which keys manage access and which keys a copy of each pack holds, from functions written from the
  catalogue. `AddTenancyPostgres()` carries the tenant in the setting `tenancy.caller_tenant`, turns the last
  administrator its trigger keeps into Tenancy's refusal and requires explicit callers, and
  `TenancyPostgresChecks` checks at start-up that the host and the database are set up as the policies rely on.
  Modules ask Tenancy's questions in their rules through `TenancyRowAccess`. See
  [On Postgres: the second lock](docs/tenancy.md#on-postgres-the-second-lock).
- `TenancyStoreOptions`, with `DatabaseKeepsRights`: the database writes the rights and answers the questions
  about other seats' rights, so the save writes none and Tenancy's store asks the database's functions, which
  `TenancyFunctionNames` names. `AddTenancyPostgres()` turns it on; on SQLite, and on a Postgres without the
  package, nothing changes.
- **Tenancy on Postgres: a seat reads only its own rights, and the database writes them.** The policies let a
  seat read its own rights and no other seat's, whatever it manages, and let a grant be read by its own seat and
  by the seats that manage grants, seats or units somewhere, or roles for the whole tenant; units, seats,
  placements, roles and the tree are every member's to read. No caller writes a right, system work included: a
  trigger on the grants, the seats and the roles writes each right as the row it follows from is written, in the
  same statement, from `key_is_live`, a function written from the catalogue like `manages_access`, so retiring a
  key writes a new access file, after which no question in SQL answers for it, the rights rows written before
  notwithstanding. A seat's status, which the rights follow, changes only by a seat that holds
  `tenancy.seats.manage` for the whole tenant and, at each grant of the seat that has not ended, the keys that
  manage access of its role (the trigger `tenancy_seat_status_is_managed`, which refuses with `42501` and the
  toolkit's hint, so a use case whose seat lost a key between its check and its save is refused with
  `access.refused`; the triggers that hold what may never be raise `check_violation`); and a unit that has a parent is
  never left without one. `rewrite_tenant_rights()` writes a tenant's rights again for Tenancy's own system
  work, after rows were written past the trigger. The rule that a tenant keeps an administrator and the check of
  a move ask the database about other seats through `tenant_administrators()` and `rights_a_move_changes(...)`,
  which answer ids, keys and dates to a seat that manages access, or units at both parents of the move. Where
  the database answers nothing about a move, because it does not see the calling seat, the store fails rather
  than let the move through; where it does see the seat, a grant started or ended between the check and the
  answer, and the move is a `ConcurrencyConflictException`. `EnsurePoliciesAreInPlaceAsync` checks
  `key_is_live`, the trigger, those functions and that the store leaves the rights to the database, and
  `EnsureSystemInRoleIsConfinedAsync` that no function of Tenancy's that runs as its owner is executable by
  every role.
- **Tenancy on Postgres: the unique index on a tenant's root is required.** `EnsurePoliciesAreInPlaceAsync`
  refuses a database whose units' table has no unique index that keeps a tenant's organization to a single root,
  the one `AddTenancy(database: Database)` maps. The policies keep a seat from making a second root; Tenancy's
  own system work and the tables' owner they do not hold, and a key held at any unit without a parent is held
  for the whole tenant. The check finds the index by what it does, whatever it is called, and says which half is
  missing: the migration, where the model maps the index, or the context's `Database` in the call to
  `AddTenancy`, where it does not. See
  [The index on a tenant's root](docs/tenancy.md#the-index-on-a-tenants-root).
- Tenancy on Postgres: a unit is changed with the units key at the unit itself or at its parent, which says the
  same by the tree. So a move by a seat that holds the key at the old parent through one grant and at the new
  parent through another goes through, although the save removes the unit's old paths before it changes the
  unit.
- `AddTenancyReadFunctions` and the six read functions of Tenancy on Postgres, which `TenancyFunctionNames`
  names: `caller_rights`, `tenant_unit_paths`, `tenant_units`, `tenant_roles`, `tenant_placements` and
  `tenant_seats`. A module's context maps its read model to them in place of the views of `AddTenancyReadModel`,
  so its model names no table of Tenancy's, and `answers.Over(db)` asks in one statement all the same. Where the
  database keeps the rights, as `AddTenancyPostgres()` says it does, `answers.Over(db)` refuses a module's
  context that maps the views and names `AddTenancyReadFunctions`; Tenancy's own context and every other
  database are asked through the views. They answer under the read rows' own names, with a status as the name of
  its enum member, whatever Tenancy's tables and columns are called and however they store them.
  `TenancyModel.ReadsThroughFunctions` says whether a model does, and `EnsurePoliciesAreInPlaceAsync` checks
  that the six functions are in the database as the contribution writes them and that each answers exactly the
  columns of its row of the read model, in order, naming the function that differs. See
  [Modules read through functions](docs/tenancy.md#modules-read-through-functions).
- **Tenancy's questions that take the tenant as an argument**, for a policy that runs where the connection names
  no tenant, such as one on the path of a stored file or on a channel: `seat_in_tenant`, `seated_in_tenant`,
  `holds_key_in_tenant`, `units_where_i_hold_in_tenant` and `roles_with_key_in_tenant` on Postgres, and
  `TenancyRowAccess.SeatInTenant`, `SeatedInTenant`, `HoldsKeyInTenant`, `UnitsWhereIHoldInTenant` and
  `RolesWithKeyInTenant` for a module's rules and access functions. They read no setting, may be run by
  signed-in users alone, and answer for the seat the caller's verified identity has in that tenant, only while
  that seat and the tenant are active: a suspended seat, a suspended or closed tenant, a tenant the caller has
  no seat in and someone without a seat learn nothing, the tenant's roles included. See
  [Where the connection names no tenant](docs/tenancy.md#where-the-connection-names-no-tenant).
- **Tenancy on Postgres: the reads across tenants run without a bypass.** The few reads Tenancy makes before any
  tenant is known do not run as the application itself, `Caller.System`, past the policies. Where the database
  keeps the rights, each runs as Tenancy's scoped system work, `Caller.SystemIn`, in no tenant, where the
  policies show it no row, and asks a function that runs as its owner and answers ids or keys only:
  `role_keys_in_use()`, which `TenancyChecks.UnknownStoredKeysAsync` asks, and `seats_of_identity(identity)`
  answer Tenancy's own scope alone, and `tenants_to_sweep()` every scope. None may be run by a signed-in user or
  by `anon`. So the role the application logs in as needs no reach past the policies for Tenancy, and nothing in
  Tenancy begins `Caller.System` there but the start-up checks' reads of Postgres's catalog. On SQLite, and on a
  Postgres without the package, the reads run as the system. `EnsureSystemReadsAcrossTenantsAsync` proves those
  functions: each is there, runs as its owner with an empty `search_path`, has an owner that reads the table it
  answers from past the policies, and may be run by the scoped system role and by neither the user's role,
  `anon` nor `PUBLIC`; the context that maps Tenancy's tables says who is calling; and the keys in use are
  answered to Tenancy's own scope and to no other. A host whose system role is not the tables' owner passes it.
  `EnsurePoliciesAreInPlaceAsync` checks that these functions and the ones that take the tenant are in the
  database, running as their owner.

#### Membership

- **`AddMembershipPostgres()`.** It registers the package's start-up check, `membership.functions-in-place`, which
  a host runs with its other checks (`RunStartupChecks()`): the functions and the lock of every registered
  resource's membership are written from its rules. `MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync` stays
  for a host that calls it by hand. See [On Postgres: the second lock](docs/membership.md#on-postgres-the-second-lock).
- **Membership**, the toolkit's second supporting domain. `DDDToolkit.Supporting.Membership` is access to one
  resource through its members: a document shared with people, a folder with staff on it. An application
  declares a member class with `[Member<TId, TMemberId, TRoleId, TResource>]`, naming the resource it is a
  member of, keeps its aggregate as it was, and changes the members through a `MemberList`, which the
  package's generator writes on that aggregate as a private `Members` property, from the one collection of
  the member class, the one property of the member's id, which is the owner, and the one static
  `MembershipCodes` the aggregate declares. An aggregate that declares a list itself is left alone, and one
  that has none and cannot be given one is told what stands in the way, DDD00059. A member class that names no
  aggregate root, or a resource that keeps its members as another member class, is DDD00060, on the member
  class; the list of the class the resource keeps is still written, so the resource's own methods compile.
  The list keeps the
  rules: a member is on the list once and holds a role once, each for a period of its own, a role counts only
  inside its membership, and the owner stays, with no end, in the owner's role. Adding a member and giving
  a role take the moment they are done at, next to the period: what has ended by then is replaced, and a
  membership or a role that still runs, or is still to start, is not, whenever the new one would start.
  Naming an owner answers everything it changed (`OwnerNamed`): whether the membership began anew, the roles
  that went, the end it lifted and the start it moved. An owner's place has no end and keeps none after the
  resource is handed on, and the roles held in that membership are kept to the period it had: they stop
  where it would have, and count no sooner than it was to start. `MembershipRules` declares
  once, as data, who holds which key: the keys of the resource, every one of which its owner holds, and the
  roles with the keys each gives. `IMemberQuestions<TResourceId>`, `MemberAccess.On` and `SeenWith` with their
  check, and `MemberAdmission` are each asked for by the resource, so an application has as many kinds of
  resource with members as it needs, in one project too. A resource is seen by its members now and by its owner,
  from the moment its row names the owner. `SeenWith` refuses nobody for want of the key, and refuses a caller
  who did not sign in, who reaches nothing under any rules, before the query's statement is sent. Who may
  add a member or give a role is the
  application's to decide: the package has no key of its own for it and no rule about who gives what. A
  command requires the key the application chooses, and the questions say until when its caller holds that
  key (`HoldAsync`, `MemberHold.Until`, no end for the owner and for the application's own work), for a rule
  the application writes itself, such as that nobody gives a role for longer than they hold the key. The application itself
  (`Caller.System`) holds every key; its work in a scope (`Caller.SystemIn`) holds nothing on a resource
  unless the resource's rules name that scope, `systemScopes`. Refusals carry the
  resource's own codes, in English and Dutch, and registering a resource offers the texts under them, so
  an application that localizes its failures adds no line for them. The package needs no dispatcher and no
  Tenancy: members are users, known by their id or by a claim only the server can change (one in
  `user_metadata` is refused), unless the application says who else they are. For a screen that lists the
  members of a resource, `MemberOverviews.Of(members, owner, now)` puts them in one order, the owner first,
  each with its roles and whether they count now, and reads nothing. It comes in three
  packages. See [Membership](docs/membership.md).
- **Membership stored with Entity Framework.** `DDDToolkit.Supporting.Membership.EntityFramework` maps a
  resource's members into the application's own context,
  `modelBuilder.Entity<Document>().HasMembers(document => document.Shares, document => document.OwnerId)`,
  as two tables owned by the resource, under the application's names (`MemberTableNames`) and the resource's
  whole key, the parts it declares with `[KeyPart]` included, with who a member's row is of fixed once the row
  is there (`IsFixedAfterInsert`: a save that changed it is refused, and exported privileges leave it out of
  UPDATE), and registers a
  resource with a call named after it, `services.AddDocumentMembership<TContext>(rules)`: generated for each
  member class, closed over the class, the resource it names and the resource's id, under the same name with
  one member class in a project as with several, and callable once for each kind of resource in one
  container. Beside it, `services.AddDocumentMemberAccess<TRequests>()` adds the resource's access check to
  the checks of the request interface a module names, closed over the resource and its id. What is held on
  one resource and until when, and the keys held on up to 200, are one statement each, and `Within`, `Reached` and `KeysOn` put a reach into a statement of the module's own, so a list of
  what a caller may see is one query, and a resource's own columns are read next to how a key is held. What
  the application answers, the roles that give a key and where a key is held above, is a query the access
  questions put into that one statement, asked for the context it runs on; a query of the module's own on
  another context than the request's hands that context over, `Within(reach, context)`.
  `AddDocumentMembership<TContext, TPorts>(rules)` takes the application's class that answers and registers
  each port of the resource it implements. Rules the registration cannot answer are refused when the
  application starts, with what to register: a member id that is not what its source answers, roles the
  rules declare that are not `NamedRole`, and anything the rules have the application answer that nobody
  registered.
- **Membership: from the check to the save.** A handler of a command on a resource with members takes nothing
  from the check: it loads the resource its request names, which is the one that was checked, and holds it to
  the version the caller named, `context.ExpectVersion(document, command.ExpectedVersion)`. The rest holds the
  change already: the save compares the version it loaded, the aggregate keeps its rules, and a database that
  checks every row checks the write again as the caller, so one who lost every key that writes the resource
  since the check is refused, `access.refused`. Permission is about who; the version is about what the caller
  read. A rule that needs how or until when the caller holds the key asks the questions for it, in one
  statement. See [From the check to the save](docs/membership.md#from-the-check-to-the-save).
- **Membership: the expert hold.** `options.UseMemberHolds(serviceProvider)`, one line on a context after
  `UseDDDToolkit`, holds every save that changes a resource with members, a member or a role of one included,
  to what the access check of the request in hand read of it, with nothing written in a handler: a resource
  changed since the check is a `ConcurrencyConflictException`, whether the caller named a version or not; one
  no check of the request in hand read, a handler called directly, a transport around the checks, another
  resource than the request names, is refused with an `InvalidOperationException` and nothing is saved; the
  application's own work that trusted code began, and a new resource, pass. `MemberAccessCheck` keeps what it
  read with the request in hand, in `Checked<MemberHold<TResourceId>>`, and the interceptor finds it there at
  the save: the request in hand, not the scope, so a hold another request of the same scope left, a query's
  say, lets nothing through. It costs no statement and works with context pools. A handling that saves twice is
  held to the check at its first save and, once that succeeded, to the version it left; a save that failed
  counts for nothing, so one tried again after it lost the race loses again. A save after the request's
  handling returned, in a unit-of-work behavior outside the access behavior or an endpoint after `Send`, has no
  request in hand and is refused, with a message that says so. A model that does not compare `Version` is
  refused at the first held save, and `UseMemberHolds` before `UseDDDToolkit` where the context is built. See
  [The expert hold](docs/membership.md#the-expert-hold).
- **Membership on Postgres.** `DDDToolkit.Supporting.Membership.Postgres` offers
  `MembershipRowAccessContribution<TMember>`: for each kind of resource, a class the application derives
  with that resource's rules and lists with `[assembly: UseRowAccessContribution]` writes four set functions
  under the names the rules give them, the resources the caller is a member of, those where a role gives it
  a key, those it sees, as a member or as the owner, so a read rule that asks it lets an owner write the member
  rows of a resource it has just opened, and those it holds a key on, as a member, as the owner or from above.
  What the rules
  have the application answer, the functions ask of functions the application defines, each by the logical
  name the rules give it, `owner/name`: a text, so nothing refers to what defines them, and the export
  refuses a name nothing it is written with defines. The four run as their owner with an empty search path
  and are executable by the database roles the rules name; the application's own need be executable by no
  caller, and the two that take a key are never asked without one. The application's own row access rules
  ask the functions, and the member tables follow the resource's rules. Since those rules let whoever may
  change a resource write its member rows and its owner column, past the application, the rules name the
  keys the application's commands require for that, and a lock is written from them for the database roles
  the rules name: `changeMembersKey` gives a restrictive policy on both member tables, and `changeOwnerKey`
  a trigger that keeps the owner column as it was unless the caller held the key, refusing as the toolkit's
  access guards do, so a save it refuses is `access.refused` rather than a failure of the server. Where the roles are kept
  for the resource, `changeMembersKey` gives a second restrictive policy, on the table of the roles a member
  holds: a role added or changed there is one the caller sees in the application's role table, asked as the
  caller, so the application's own rule on that table, which shows a caller its own customer's roles, keeps
  another customer's off a member list written past the application. All of it only narrows what the
  application's rules allow, and no code of the package gates a command by it. Work in a scope the rules
  name (`systemScopes`) is answered every resource by the functions, as in C#.
  `MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync` refuses, at start-up, a database that lacks a
  function of a registered resource, has one written from other rules than the resource runs with or by
  another version of the package, the names of the application's functions and what a member's role gives
  among them, or lets other roles execute one than the rules name; one whose lock is not in place; one whose
  kept roles have lost the trigger that keeps the owner's role in use; and one whose table of kept roles has
  no row security and may be written by a role the rules name. It returns,
  and logs as a warning, a line for each resource whose rules name no key for the lock, and refuses nothing
  for that.
- **Membership beside an organization.** A resource's rules say three things apart from each other, each with
  the default a resource of plain users has, and any of the one goes with any of the others. Who a member is:
  the caller's id, a claim, or an id the application resolves for the caller, `MemberSource.Resolved(...)`,
  answered through `ICallerMember<TResourceId, TMemberId>`. Where the roles come from: declared in the rules,
  or kept elsewhere, `rolesKeptElsewhere: new(...)`, where the application answers which of them give a key
  (`IRolesWithKey<TResourceId, TRoleId>`) and which there are (`IMemberRoles`); what such a role gives a
  member is still cut by the rules' `MemberKeys`. And whether a key held where the resource sits, or above
  it, reaches the resource: `above: new(...)` with `HasMembers(..., at: ...)`, answered through
  `IPlacesReached<TResourceId, TPlaceId>`. A caller that holds a key both ways holds it as a member
  (`MemberVia.Members` before `MemberVia.Above`). A key held from above is held for as long as the caller
  sees the resource: with no end the package knows where it sees it from above as well, and until its
  membership ends where it sees it as a member alone (`MemberHold.Until`). So the people of an organization
  can be the members of a resource with roles of its own, or with the organization's roles, with reach from
  above or without, and the package references no package that keeps an organization. See
  [Beside an organization](docs/membership.md#beside-an-organization).
- **Roles a customer makes.** A third place a resource's roles can come from, next to the rules and to roles
  kept elsewhere: rows kept for the resource, `rolesKept: true`. An application declares a role class with
  `[KeptRole<TRoleId, TResource>]`, an aggregate of its own named for the resource it is a role of, so
  several kinds of resource in one project each have theirs, and a member holds a role by that class's id.
  A role has a name, a description, the keys it gives, and is in use or archived; it is made with the
  application's constructor and changed with `Rename`, `SetKeys` and `Archive`, each under the resource's
  rules and each answering what happened (`RoleKeysSet`), so the application raises its own events. A key
  must be one the rules let a member's role give (`MemberKeys`), which is asked again where the keys are
  read; a name is trimmed and not empty; an archived role gives nothing from then on, is not given again
  and stays on the members that hold it; and the role every owner holds is not archived, which on Postgres a
  trigger on the role table, written from the rules, refuses as well, whoever writes the row. Five refusals come
  with it, under the resource's codes, in English and Dutch. The roles the rules declare are then the
  starter roles: `StarterRoles.Missing(rules, rolesOfTheScope)` answers the ones a scope does not have yet,
  each made once and told by what it was made from (`MadeFrom`), which is also how the owner's role is
  found after a customer renamed it. Whose a role is, is the application's: it adds its own column, keeps
  the rows apart by its own rule, and declares its own unique indexes over that column with `Name` and
  with `MadeFrom`; the package assumes no customer. `IsKeptRole()` maps the package's part of the role
  class, with `MadeFrom` fixed once the row is there (`IsFixedAfterInsert`: a save that changed it is
  refused, and the privileges an export writes leave it out of what a caller may update), and marks whose
  roles they are, so `services.AddPlotMembership<TContext>(rules)` registers the
  resource as before and nothing is registered for the roles. A role's keys are read from its row inside
  the one statement that reads the members, and on Postgres the four functions join the role table of the
  same context: no port, and no function of the application's. The functions' fingerprint covers that the
  roles are kept and what a member's role can give, and the start-up check refuses rules that keep the
  roles over a context that maps no role class. Who may make, change or archive a role is the application's
  to decide. See [Roles a customer makes](docs/membership.md#roles-a-customer-makes).
- **Membership with Tenancy.** In an application that has both supporting domains, a member of a resource can
  be a seat of a tenant, and what joins the two is written into the application. Membership's Entity
  Framework package ships a generator of its own, which notices Tenancy by its types and, for each member
  class whose member id is the id of the application's seat class, writes
  `services.AddCourseMembershipWithTenancy<TContext>(rules)` and the class it registers: a member is the
  caller's seat, somebody is made a member while their seat is active, a key held at an organization unit
  reaches the resources at that unit and below it, and, where a member holds roles by Tenancy's role id, the
  roles are the tenant's own. The application writes the three rule entries,
  `members: MemberSource.Resolved("tenancy/caller_seat")`, `above: new("tenancy/units_where_i_hold")` and
  where its roles come from, and that one registration, which refuses rules that take a member from the
  caller itself. A module that knows the organization only by its ids names the tenant's, the unit's and the
  role's id in the call. Nothing is written without Tenancy, or, where a project sees the application's
  seat class, for a member class whose members are not seats. Neither package references the other, and
  the toolkit's own generator names neither. See
  [With Tenancy](docs/membership.md#with-tenancy).

#### Samples

- **The Tenancy sample has no start-up class of its own.** `PostgresStartupCheck`, `TenancyStartupCheck` and
  `ProjectFunctionsCheck` are gone: the host calls `RunStartupChecks()`, and the checks are the ones the
  registrations of its modules bring, Membership's through `AddMembershipPostgres()` in the Projects module. The
  tests that start it on a database made wrong in one respect, a policy written from another catalogue among them,
  now say which check stopped it.
- **The Tenancy sample's host is held to its modules' access behaviors.** Its modules' registrations bring
  `access.behaviors-registered`, which runs with its other start-up checks; a test takes the Projects behavior out
  and reads the refusal that names `services.AddProjectsAccessBehavior()`, and the test of what a handler does
  when nothing asks its request's checks turns the check off, with that reason, to get there.
- **The webshop's Supabase host runs its start-up checks with `RunStartupChecks()`**, where it called
  `EnsureSupabaseMigrationsAppliedAsync` by hand. Signed in with row level security, it logs in as `postgres`,
  the owner of its tables, so it turns `postgres.login-role-owns-nothing` off, with that reason: the one opt-out
  the Supabase page shows, until a host logs in as a role of its own.
- `Examples/Tenancy`, a second sample, on Tenancy: crews that work on projects, in two tenants, in three modules
  that share one Postgres database. Its host signs seeded people in with a dev login that issues Supabase access
  tokens and picks the tenant from a header, and its Blazor UI shows each person what they may do and lets them
  try what they may not, with the status and the code of every refusal. The host requires explicit callers: each
  request runs as its own token's user, and the seeding as system work begun in each tenant. Each module is
  split into a domain, an application, an infrastructure and an API project, and Tenants, the module that holds
  the application's tenancy classes, and Projects into a contracts project besides: the application project
  declares the ports it reads and saves through, the infrastructure project implements them with Entity
  Framework, and the domain, application and contracts projects reference neither Entity Framework nor ASP.NET
  Core. The API project holds the module's entry, which calls the infrastructure project's registration and the
  application project's and maps the routes, so the host references each module's API project and nothing else
  of it, and still registers each module with one call. Every use case is a command or a query of the
  source-generated [Mediator](https://github.com/martinothamar/Mediator) with a handler of its own, and a route
  only sends one. A request declares what it requires of its caller, a key and the project or unit it is held
  on, and its module's pipeline behavior, which the toolkit generates from the module's request interface, asks
  the module's checks before the handler runs; the check keeps what it read for the handler to act on. Commands
  save through a write port on the request's context; queries read through a read port, each on a context of its
  own, so the queries of one request can run side by side: each module's contexts come from a pool.
  `PostgresPools.AddContext`, in the samples' hosting project, registers the pools and `AddScopedFromPool`: a
  read takes a context from the pool for its one query, and the request's own context, the unit of work of its
  commands, is taken from the same pool and bound to the request's scope. The options callback a module passes
  runs once per pool, with the application's services, so it reads no caller and nothing scoped. The webshop's
  modules keep `AddDbContext`. The sample is in the namespace `Examples.Tenancy` and its settings are under
  `Sample:`. See
  [Who may do what, in the sample](docs/tenancy.md#who-may-do-what-in-the-sample).
- **Tenancy sample: no module writes its access behavior.** A module's request interface is one line,
  `[AccessRequests] public interface IProjectsRequest : IRequireAccess;`. A request declares an
  `AccessRequirement`: one of the Tenancy package's cases, which the package's check decides in every module,
  or a case of the module's own, which a check of the module decides (`ProjectsAccessCheck`,
  `InspectionsAccessCheck`; Tenants has none). A handler takes nothing from its check: it acts on what its
  request names, loads a project with the version its caller named, and asks Projects' gate itself for what it
  needs of a project. A request that declares a case no check of its module decides is stopped where it is
  sent, and a test holds every declared case to having a check. The sample ships without the expert hold;
  `AccessHoldScenarios` switches it on and plays the races between a check and its handler both ways.
- **Tenancy sample: deep folders.** Every project of the sample is laid out with the thing first and the kind
  second, and one type per file. A domain project has a folder per aggregate, `Aggregates/Projects`, with
  `Entities`, `Events`, `Invariants` and `ValueObjects` beside the aggregate and its refusals. An application
  project has a folder per feature with `Commands` and `Queries` in it (`Crew/Commands/AddCrewMember.cs`), and
  the ports several features share in a folder named for what it holds, `StoredProjects` and `StoredTenancy`,
  or, in Inspections, in the one feature that uses them. An API project has the same feature folders, each
  with its routes in `Rest` and its GraphQL in `GraphQL`. The tests follow the modules and their features.
  The namespace of a type is its folder in every project, the domain included:
  `...Projects.Domain.Aggregates.Projects.Project`, `...Projects.Application.Crew.Commands`. The names the
  outbox stores events under do not follow the namespaces. `FeatureFolderTests`, `SourceTreeTests`,
  `MigrationTests` and `StoredNameTests` hold it. See
  [Folders inside the layers](docs/modules.md#folders-inside-the-layers).
- **Tenancy sample: answers carry ids.** A project names its unit, its crew and their roles by id (`unitId`,
  `myRoleIds`, and `seatId` and each role's `roleId` on a crew member), and `GET /access/units?key=` answers
  unit ids. A crew comes the owner first, then by when each membership starts. Listing the projects is one
  statement. The Tenants module answers what ids are called, `POST /tenancy/directory/seats`, `/units` and
  `/roles`, and the UI asks it for the names a page shows (`DirectoryNames`): once per id per load, and only for
  what the page did not list already. A crew's project roles are named by `GET /project-roles`, once per load.
- **Tenancy sample: crew memberships with dated roles, and an access admin.**
  - A seat is on a project's crew for a period, and holds crew roles there, each for a period of its own.
    Being on the crew lets it see the project; every other key comes from a role it holds there, while the
    membership lasts. `POST /projects/{id}/crew` takes an optional `roleId`,
    `POST /projects/{id}/crew/{seatId}/roles` gives a role next to the ones held, and
    `DELETE /projects/{id}/crew/{seatId}/roles/{roleId}` takes one away. An answer carries `myRoleIds`, and
    `roles` on each crew member. A role held already is 409 `projects.crew-role-held`.
  - A membership that ended can begin again. `POST /projects/{id}/crew` for a seat whose membership has ended
    puts it on the crew anew, without the roles of the ended membership; giving such a seat a role is 404
    `projects.member-not-found`; and an owner named from an ended membership holds the lead role and no other.
    In the UI a day picked as "until" counts through that day, and the owner picker marks a seat that is not
    active.
  - A project's owner holds the crew lead's project role on the crew and keeps it until another owner is named.
    Naming another owner takes that role from the old owner and nothing else: the seat stays on the crew.
  - A hierarchical tenant's administrators' pack is Access admin, which lists its keys: Tenancy's own,
    `projects.owner.change`, `projects.crew.manage` and `projects.view`. Tenant admin is the flat tenants'.
    One of the demonstration's people, maud, holds Access admin in harbor, next to ada, and her rename of a
    project is refused.
  - `GET /projects/{id}/crew` answers what `GET /projects/{id}` carries as `crew`, for whoever may see the
    project, in one statement: the query `AllCrewMembers`. Each member's roles are answered to a caller who
    manages the crew, and are `null` for anybody else.
- **Tenancy sample: crews on the Membership package, with the project roles each tenant keeps.** A crew member
  is declared with the [Membership](docs/membership.md) package's member template,
  `[Member<CrewMemberId, SeatId, ProjectRoleId, Project>]`, and a project keeps its crew through the member list
  the package writes on it. The projects' rules, `ProjectMembership`, say what an owner holds, what a crew role
  may give, and which keys change a crew and name an owner. The roles a crew holds are project roles,
  `[KeptRole<ProjectRoleId, Project>]`: each tenant's own, made from three starter roles when it is set up
  (`SetUpProjectRoles`, system work in the tenant, which the seeding sends), and made, renamed, re-keyed and
  archived by whoever holds `tenancy.roles.manage` for the whole tenant: `GET` and `POST /project-roles`,
  `PUT /project-roles/{id}`, `PUT /project-roles/{id}/keys`, `POST /project-roles/{id}/archive`, the field
  `projectRoles` and the mutations `projectRoleCreate`, `projectRoleRename`, `projectRoleKeysSet` and
  `projectRoleArchive`, and the UI's Crew roles page. A role of the organization is refused on a crew, 400
  `projects.role-not-for-members`, a project role with a key no crew gives is not made, 400
  `projects.key-not-for-members`, and the crew lead's is not archived, 409 `projects.owner-role-stays`. The
  access check for a key on a project, the reaches and the four functions the database asks are the package's,
  under the names the functions had: `AddProjectMembershipWithTenancy` and
  `AddProjectMemberAccess<IProjectsRequest>()` register them, and the exported access file writes the
  functions and the lock that holds a crew's rows to `projects.crew.manage` (or `projects.owner.change`, which
  writes them too) and to the project roles the seat sees, its own tenant's, and a project's owner to
  `projects.owner.change`; the crew lead's project role is archived by no statement, and no caller changes
  where a project role came from. Two rules of the module's own hold the rest of what a crew and an owner
  are: a seat opens a project for itself unless it may name owners at the unit, as `OpenProject` asks, and a
  crew takes only seats of the project's tenant (`CrewSeatsOfTheProjectsTenant`). The rule that lets a seat
  change the project roles asks whether it manages the tenant's roles, and Tenancy's own policy keeps the rows
  to the tenant. The Tenants module keeps nothing about crews any more: what a role is used for,
  `PUT /tenancy/roles/{id}/use`, the mutation `roleUseChange`, the contract `ICrewRoleUses` and
  `projects.role-not-for-crews` are gone, and a migration drops the column.
- **Tenancy sample: a date range two modules share.** `DateRange`, a range of calendar days, is a value object
  in a project of its own, `Examples/Tenancy/Shared/Examples.Tenancy.Shared.Domain`: what several modules share
  and none owns, referenced by a module's domain and contracts projects and referencing no module. A project may
  have a planned range, given when it is opened (`plannedFrom`, `plannedUntil`) or with
  `PUT /projects/{id}/planned-range` under `projects.edit`; an inspection covers a range of days (`from`,
  `until`, today when left out). When the project is planned, an inspection's days lie within its planned range:
  Projects' gate answers the range and Inspections refuses with `inspections.outside-planned-range`.
- **Tenancy sample: REST conventions.**
  - The seat a route inside a tenant requires is an authorization policy, `SamplePolicies.SeatRequired`, and
    tenant selection runs before authorization. Authorization runs before a route reads its arguments, so a
    malformed id or body from a caller without a seat is 403 `tenancy.not-seated`, not 400 `invalid-request`.
  - Enum values are written and read by name in lower snake case, by the host's JSON options: `"state":
    "closed"`, `"via": "crew"`, `"status": "active"`, `"shape": "hierarchical"`, `{ "use": "both" }`. A number
    is not read for a shape.
  - `GET /projects` answers a page, `{ items, next, total? }`, takes `after`, `size`, `state`, `unit`, `text`
    and `count`, and every item carries `can`. `GET /projects/{id}/inspections` is paged too,
    `{ items, next, canRecord }`, newest first.
  - A project carries `version`, sent as `ETag` by `GET /projects/{id}`; the routes that change a project or
    its crew take `If-Match`, and a project that changed since is 409 `concurrency-conflict`. The commands
    behind them take `ExpectedVersion`.
  - `GET /access/keys?keys=` and `POST /access/projects/keys` answer the keys a caller holds at the tenant's
    root and on each of up to 200 projects, the latter in one statement.
  - CORS for a browser client, from `Sample:Cors:Origins`; none when it is empty.
  - A body that does not read is answered where it is wrong, by its path (`$.use`), without the name of the
    type it was read into, and a request without the body its route takes is answered in the host's own
    words.
- **Tenancy sample: GraphQL, one schema composed from a source schema per module.** The host serves `/graphql` next
  to its routes. Each module's API project holds a source schema of its own, in a `GraphQL` folder per feature
  beside `Rest`, and the host composes the three in the process with `AddInMemoryFusionGateway()`. Every
  query and mutation sends the command or query its route sends. See
  [GraphQL in the sample](docs/tenancy.md#graphql-in-the-sample).
  - A query or a mutation is a static method marked `[Query]` or `[Mutation]`, with `[Service]` on what is
    injected, and HotChocolate's generated `Add{Name}Types()` registers them.
  - A type of a schema is declared over the record the application's query answers with, in a static partial
    class marked `[ObjectType<T>]`, so a module has no output records and no mappings, and what the record
    holds the type shows: `Seat`, `OrganizationUnit`, `Role`, the catalogue and the calling seat's overview
    in Tenants, `ProjectOverview` in Projects, `Inspection` in Inspections. `TenantOfSeat` is a record of the
    schema's own, because no record of the application has its shape. The host's conventions add
    `AddDDDToolkitEntityNullability()` and `AddDDDToolkitKeyAuthorization()` to every module's schema.
  - A module names another module's entity by its id (`[EntityKey]`), and Tenants answers seats, units and
    roles through three lookups that are there for the gateway alone (`[Lookup]`, `[Internal]`), each behind a
    data loader that HotChocolate's generator writes from a method marked `[DataLoader]` and that asks the
    directory once per kind, in parts no larger than one question takes. `project(id:)` is a lookup clients
    ask too, and a project is a node, so `node(id:)` finds one through the same read.
  - Every field but the key of a type another module refers to may be null: an entity that is not there, or
    not the caller's to see, is its id with nothing else, and no error.
  - A mutation answers what it changed, read after the save, with typed errors in its payload. A project's
    mutations take `expectedVersion` and answer the new `version`.
  - A request to `/graphql` needs a token (`UseSignedInOnly`, in front of the gateway) and a seat in the
    tenant it names (`SeatGate`, which answers the refusals of the routes' policy), except for the fields
    `SeatGate` is told need no seat, `seatsOfMine` and `invitationAccept`, and the fields that ask for an
    operator instead, which answer anybody else `tenancy.operators-only`.
  - `projects` is a connection: `first`/`after` and `last`/`before`, `nodes`, `edges`, `pageInfo` and
    `totalCount`, ten to a page and fifty at most. `VisibleProjects` takes GreenDonut's `PagingArguments` and
    answers a `Page<T>`, the read adapter calls `ToPageAsync`, and `GET /projects` pages through the same
    query and answers `{ items, next, total? }`: its `next` is the cursor GraphQL gives as `endCursor`.
  - A project's `crew`, `myRoles` and `can` are resolvers behind generated data loaders, over three queries
    that answer for the projects of a page at once, `CrewsOfProjects`, `AbilitiesOnProjects` and
    `ProjectsById`. A list that asks for none of them costs one statement, and with all of them three;
    `project(id:)` and `node(id:)` ask one loader. `ProjectOverview` is the project's own row and how the
    caller reaches it, and `GET /projects` and `GET /projects/{id}` send the same queries.
  - A project's inspections are a field of the project. The Inspections schema adds the paged field
    `inspections` to the type `Project`, which Projects owns, and the gateway composes the two: a client asks
    for projects with their inspections in one query, and there is no root field for them. The list pages by
    HotChocolate's own paging (`PagingArguments`, `Page<T>`, `ToPageAsync`, a connection with `first`/`after`
    and `last`/`before`), newest first with the inspection's id as the second key, and the route
    `GET /projects/{id}/inspections` reads the same pages: its `next` is the cursor of the page's last row,
    and a cursor is HotChocolate's to write. The field loads through a generated data loader that pages every
    project at once (`ToBatchPageAsync`), and Projects' gate has a question about many projects,
    `IProjectGate.AskAsync(projects, key)`, answered in one statement, so a batch of projects, as a rule a
    page, costs one access question and one statement. A project the caller may not see gets no inspections,
    and no error. The query `InspectionDetail` reads one inspection of a project: what `inspectionRecord`
    answers.
  - A field can ask for a permission key, with `[Authorize]` and the toolkit's key authorization. `Role.keys`
    asks for `tenancy.roles.manage`: a seat that does not manage the tenant's roles gets the roles with
    `keys: null`, and `tenancy.not-permitted` at each refused field. `CrewMember.roles`, which is nullable,
    asks for `projects.crew.manage`: who does not manage the crew gets `roles: null` with one
    `projects.not-permitted` error at the field, and keeps the member.
  - A rule on a field guards that field alone, so the two rules are held where the data is read, and the
    attribute declares them (`RoleListing.KeysKey`, `CrewOverview.RolesKey`). `TenantRoles` and `RolesById`
    ask `tenancy.roles.manage` for the whole tenant, and `RoleListing.Keys`, in the application's own flat
    record of a role, is `null` without it: `GET /tenancy/roles` and `POST /tenancy/directory/roles` answer
    a role's keys only to a seat that holds the key, and `"keys": null` to any other, and asking for the key
    costs `RolesById` a third statement. `CrewsOfProjects` and `AllCrewMembers` read, in their one
    statement, whether the caller holds `projects.crew.manage` on each project, and `CrewOverview.Roles` is
    `null` without it: `GET /projects`, `GET /projects/{id}` and `GET /projects/{id}/crew` answer a member's
    roles only to who manages that crew. A seat still reads its own crew roles (`myRoleIds`) and the keys of
    the roles it holds (`GET /me`).
  - The gateway bounds a request as a whole, ten levels deep and at most 200 fields
    (`AddSampleRequestBounds`).
  - `Project` and `Inspection` answer `changedBy`, with `kind` and `seat`, as the routes do. `ChangedBy` is
    one value object, in the contracts of the Tenants module, that Projects and Inspections both answer; a
    value object is shareable, so the composed schema has one type and a client reads both with one
    fragment. A project answers `planned`, a `DateRange` of `from` and `until`, `projectPlan` plans one and
    `projectOpenAtUnit` takes one, each as a `DateRangeInput`; an inspection answers `days`, the same type,
    and `inspectionRecord` takes them.
  - Invitations are fields: `personInvite`, `invitationCancel` and `invitationAccept`, with a refusal as a
    typed error in the payload, and `openInvitations`, whose `Invitation` is declared over the package's
    record and shows the unit, the role and who issued it through the directory's data loaders. Accepting
    needs a token and no seat.
  - `accessHistory` is a connection over the query `GET /tenancy/history` sends, declared as the project list
    is: a page of the field is the page of the route, ten rows by default and fifty at most. Each row says who
    acted, `byKind` and for a seat `bySeat` and `bySeatId`.
  - What the four routes of the operators answer is a field each, in the `Operators` feature of the module
    that owns it: `tenants` and `tenantAccessHistory(tenant:)` in Tenants, `tenantProjects(tenant:)` in
    Projects and `tenantProjectInspections(tenant:, project:)` in Inspections. An operator has no seat for
    Tenancy's directory to answer, so a unit and a seat are their ids there.
  - In GraphQL a project's inspections come ten to a page and fifty at most, HotChocolate's own sizes, as the
    list of projects does; the route keeps fifty and two hundred, sizes at which a page of projects that asked
    its inspections by their nodes and by their edges would be estimated to cost more than a request may. A
    field that loads through a data loader says that it weighs one (`[Cost(1)]`): a project's `inspections`,
    `canRecord`, `crew`, `myRoles` and `can`, the lookup `project(id:)` and Tenants' three lookups.
  - A refusal's `message` is in the language the request asks for, as the `title` of a route's refusal is.
  - The composed schema and each module's source schema are committed as `schema.graphql` and compared by
    `GraphQLSchemaTests`. Tenants' schema declares `@authorize`, and every source schema the `ApplyPolicy`
    enum that comes with it.
- **Tenancy sample: a paged list refuses a marker that is not its own cursor, and a page asked for from both
  ends.**
  - The lists of projects, of a project's inspections and of the access history hold the marker a page is
    asked with to their own list, before any statement runs. A text that is no cursor, a cursor of a list
    ordered by other keys and a cursor whose head was written by hand are 400 `projects.cursor-invalid`,
    `inspections.cursor-invalid` or `tenancy.cursor-invalid` from a route, and a coded error at the field in
    GraphQL. GreenDonut's paging by itself answers the first an empty page, fails inside the read on the
    second, and believes the third. One class checks it for all three, `ListCursors`, in a project that the
    modules' infrastructure projects share and nothing else references,
    `Examples/Tenancy/Shared/Examples.Tenancy.Shared.Infrastructure`. A cursor of another list that is
    ordered by keys of the same types is read as the place it names.
  - `first` with `last` would reach the paging library, which throws. The paged lists refuse it with
    `projects.page-from-both-ends`, `inspections.page-from-both-ends` and
    `tenants.history.page-from-both-ends`, and hold a size from either end to their range, through one check,
    `PageSizes`, in a project the modules' application projects share,
    `Examples/Tenancy/Shared/Examples.Tenancy.Shared.Application`. Each of the four connections says its
    sizes on the field (`DefaultPageSize = 10, MaxPageSize = 50`).
- **Tenancy sample: an operator who only reads, who changed a row, and the access history as a list.**
  - A person whose token carries the operators' role, `tenancy_operator`, lists every tenant with its active
    seats (`GET /operations/tenants`) and reads one tenant's projects, a project's inspections and the
    tenant's access history (`GET /operations/tenants/{tenant}/projects`, `.../projects/{project}/inspections`
    and `.../history`). Each module maps its own of these, behind a host policy, `SamplePolicies.OperatorRequired`,
    and every query declares that it is for operators only. An operator has no seat, so every route inside a
    tenant refuses it with `tenancy.not-seated`. In the database the read rules `OperatorsSeeEveryProject` and
    `OperatorsSeeEveryInspection` let the operators' database role read, and it holds no privilege to write;
    the exported access files have them. The dev login signs a person in as an operator when
    `DevLogin:Operators` lists them; the development settings list `orla`, who has no seat.
  - `GET /projects`, `GET /projects/{id}` and `GET /projects/{id}/inspections` answer `changedBy`: the kind of
    actor that changed the row last, `seat`, `system`, `operator` or `token`, and for a seat its id. The
    project's page in the UI shows it by the name the directory gives.
  - `GET /tenancy/history?size=&after=` answers the tenant's access history, newest first, with who made each
    change, to whoever holds `tenancy.history.view` for the whole tenant. It is paged with GreenDonut's
    `PagingArguments`, `ToPageAsync` and `Page<T>`; `GreenDonut.Data.Primitives` is the one package the
    application project references for it. A size outside 1 to 200 is 400 `tenants.history.page-size-invalid`,
    and a marker the list did not give 400 `tenancy.cursor-invalid`. The UI has a page for it, and the try-it
    presets read it once with the key and once without.
- **Tenancy sample: a person is invited by address and gets a seat when they accept.**
  - The Tenants module has the package's invitations: `Invitation` and `InvitationId`,
    `AddTenancyInvitations` on the model and the services, `AddTenancyInvitationEvents` on the outbox, a
    migration for the two tables, and their policies in the exported access files.
  - `POST /tenancy/invitations` invites a person by address into a unit, with a role there until a moment or
    for good, and answers the invitation's token once; `GET /tenancy/invitations` lists the open ones for
    whoever manages seats at their unit, and `DELETE /tenancy/invitations/{id}` revokes one. The package
    decides who may: whoever could add the seat and give the role.
  - The handler asks the identity provider for an account at the address through `IIdentityAccounts`: Supabase
    Auth's admin client when the host has `Supabase:SecretKey`, which mails a new address a link that lets
    its person in, and `DevIdentityAccounts` with the dev login alone. Whoever invites is answered the same
    whether or not the address had an account.
  - An invitation keeps the id of the account the identity provider made for its address
    (`Invitation.InvitedAccount`, with a migration of its own). Inviting an address again whose account
    nobody signed in with has that account mailed again (`InviteAccountAsync`), and cancelling deletes such
    an account unless another open invitation keeps it. Auth's request log and the mail hold the token of an
    invitation that is mailed, so it rests on the sign-in with the invited address.
  - `POST /invitations/accept` gives the signed-in caller the seat, its placement and its role in one save.
    It needs a token and no tenant, and the address in the caller's token must be the invitation's:
    otherwise 403 `tenancy.address-mismatch`. A revoked, used or run-out invitation is 409 with its own code.
  - The UI has the pages **Invitations** and **Accept an invitation**, the `.http` file the requests, and the
    try-it page two presets.
- **Tenancy sample: a person invited by mail chooses a password and accepts, through the UI.** With
  `Sample:Invitations:AcceptPage` set to the address of the UI's page, the mail Supabase Auth sends a new
  account leads to that page with the invitation's token after the `#`, so nobody hands the token over. The page
  takes the sign-in Auth's link gives, once Auth has said whose it is, and asks first in a tab that is signed in
  as somebody else (`Ui/Auth/LinkSignIn.cs`); it has the person choose a password (Auth's own `PUT user`), and
  shows the invitation with its token filled in; both are read from the part of the address a browser sends to
  no server, and taken off it at once. The page opens signed out, and a link Auth no longer takes is told as
  such. With the dev login, and without the setting, the token reaches the person from whoever invited them. The
  AppHost sets it and names the UI as Auth's site, and `supabase/config.toml` names the UI as
  the site of the stack the Supabase CLI starts: Auth sends a browser on only to its site, told by host name.
  See [Inviting a person by address](docs/tenancy.md#inviting-a-person-by-address).
- **The Tenancy sample runs on Postgres, as Supabase runs it, and on no other database.** Its host logs in at
  `ConnectionStrings:Supabase` as a login role that owns nothing, `tenancy_api`, under forced policies and
  privileges written from them; without that setting it stops before it is built and says how to get one. The
  files under `Examples/Tenancy/supabase/migrations` are written by the build of a program of its own,
  `Examples.Tenancy.Exporter`; the catalogue is in `Examples.Tenancy.Catalogue`, which the exporter and the host
  both reference, and which has a version of its own, as Projects' infrastructure project has, because
  an access file names both and a release of the toolkit is no reason for a new one. The host's connections are
  two data sources, for requests and for background work, each with its own maximum (`PostgresPools` and
  `PostgresPoolBudget` in the samples' hosting project). Projects publishes two row access contracts,
  `ProjectsISee` and `ProjectsWhereIHold`, which Inspections' rules ask; a seat opens and changes projects and
  records inspections under rules of their own. Projects' context maps Tenancy's read functions and none of
  its tables. A project's and an inspection's rows say who wrote and who changed them, a project's
  number and an inspection's project are fixed once saved, and Tenancy's events that change access are kept in
  an access history, each with who made the change (`AddTenancyEventLog` inside `KeepEventLog`). A trigger of
  the Projects module, `UnitChangesWithItsKeys`, written into the module's exported access file, holds a
  project's unit closer than the policy does: it changes only to a unit of its tenant, for a seat with
  `projects.edit` on the project and `projects.open` where it goes; its owner changes only with
  `projects.owner.change` on it, by the Membership package's lock; its name and planned days only with
  `projects.edit`, and its state only with `projects.close`, by two column rules,
  `NameAndPlanChangeWithTheEditKey` and `StateChangesWithTheCloseKey`, so a seat that only manages a crew or
  only names owners, or only opens projects at the project's unit, renames, plans, closes and reopens nothing,
  by a statement of its own, an insert that turns into an update, or a handler that skipped its check, and
  neither does one whose role lost the key between the check and the handler. Each of these refuses with the
  toolkit's hint, the module's trigger through `RowAccessModel.Refusal`, so a save one of them refuses is
  `access.refused`, a 403 from a route and a `RefusalError` from a mutation, where an owner named by a seat that
  lost the key after the check ended as a failure of the server;
  [What stays in C#](docs/tenancy.md#what-stays-in-c) lists what the policy still lets a statement do that goes
  round the application. A statement that runs for a signed-in user has a timeout of ten seconds
  (`StatementTimeouts[CallerKind.User]`). At start-up the host checks that
  every migration is applied, that the login role owns nothing and that Tenancy's second lock is in place.
  A field of `/graphql` reads through the same contexts as a route, on the connections for
  requests and as its caller. The names are as Entity Framework gives them.
- **Tenancy sample: a module's migrations are in its infrastructure project.** A module has one set, beside
  its context in `Persistence/Migrations`, and one `[SupabaseMigrations]` factory (`TenantsContextFactory`,
  `ProjectsContextFactory`, `InspectionsContextFactory`) that `dotnet ef`, the export and the host's start-up
  check all build the context with. The module registers its context with that factory
  (`PostgresPools.AddContext<TContext, TFactory>`), and the migrations are found in the context's own assembly.
  `MigrationTests` holds each module to it without a connection: no pending model change, the ids a database
  already holds, and each module's own tables and no other's.
- **The Tenancy sample's AppHost starts Supabase's own images, with the real login.** One command,
  `dotnet run --project Examples/Tenancy/Examples.Tenancy.AppHost`, with Docker running and nothing to
  configure, starts Supabase's Postgres and Auth images and a mail
  catcher, a step that applies `Examples/Tenancy/supabase/migrations` as the role that owns the database and
  turns the login role on, and the API logged in as that role. With `Sample:SeedAuthUsers` the host makes the
  demonstration people users of Auth through the Auth admin client, under the ids their seats are found by, and
  with `Supabase:AuthUrl` the UI's login page offers Auth's sign-in with a password beside the dev login, in
  English and Dutch as the page's own texts are. The AppHost starts no gateway in front of Auth: it has the link
  in a mail name Auth's own address, so the link opens as it is written. Without Aspire the sample runs on the
  stack the Supabase CLI starts. The AppHost and the stack the sample's tests start give Auth a signing key,
  made up for each run, the way the Supabase CLI gives one to the Auth server it starts (`AuthSigningKeys`).
  A person's token is then signed with that key (ES256) and checked by the host with the public half Auth
  publishes, so the scenarios on Supabase's images take the path a hosted project with signing keys takes,
  with real Auth. `SupabaseStackTests` pins what Auth signs with and what it publishes.
- **Tenancy sample: its tests run on Supabase's own images.** A test class that needs the host with a database
  asks one fixture for its hosts (`SampleHosts`) and never makes one. The fixture starts Supabase's own
  images through Testcontainers, once for a run, and gives every host a copy of a database that the
  exported files made and a host seeded once, so a scenario runs under the exported policies and
  privileges, as the login role that owns nothing. A period that has to run out is given an end a few seconds
  ahead and waited for: no test moves a clock on to see one end. What starts no database carries no trait:
  the architecture tests read the host's registrations from a host that connects to nothing
  (`SampleWithoutDatabase`), and `ContainerTraitTests` holds every class to one side or the other.
- **Tenancy sample: where its tests run.** The classes on Supabase's images carry `Category=Samples`, so the
  release workflow and both jobs of Build and Test, which filter that category out, no longer run the sample's
  host tests: they run what starts no database. The Sample Tests workflow runs the host tests, as
  `Tenancy.Supabase`, and once more against the oldest dependency versions the packages allow, as
  `Tenancy.Supabase.Floor`. It runs on the events Build and Test runs on, a pull request and a push to `main`;
  it does not run when a release is created.
- **Tenancy sample: a seat's own place on a crew.** A seat that takes its own role on a crew, or itself off
  the crew, is saved as the application's work for the seat: the save removes the rows its right comes from
  before it writes the project's own row, which the database would refuse a seat. The two commands,
  `TakeCrewRole` and `RemoveCrewMember`, save through `IProjectStore.SaveOnlyAsync`, which refuses when the
  unit of work holds a change to anything but that project.
- **Every project fits a path budget.** The longest path a build writes for a project is at most 200
  characters below the repository's root, held for every project of the repository by
  `SourceTreeTests.Every_project_of_the_repository_fits_the_path_budget` in the Tenancy sample's tests. Visual
  Studio's MSBuild writes no path over 259 characters, and the command line builds a longer one all the same.
- **Tenancy sample: on the stack the Supabase CLI starts.** `supabase start` in `Examples/Tenancy` applies the
  sample's files, and the steps `Examples/README.md` gives from there lead to a running host and a person
  signed in at the stack's Auth; `SampleOnTheCliStackTests` takes them against a running stack, and the Sample
  Tests workflow starts one and runs it as `Tenancy.SupabaseCli`. That stack's Auth signs a person's token with
  a key it publishes, where the dev login signs with the local secret, and the sample's host takes both under
  one issuer, each checked with its own kind of key, through the toolkit's bearer scheme (see "Supabase Auth:
  two kinds of token" under Changed). The sample's `config.toml` turns the e-mail provider on, with signing
  up closed, and analytics off.
- **The Tenancy sample in English and Dutch.** A refusal is answered in the language its request asks for in
  `Accept-Language`: each module keeps the texts of its codes in two resource files beside them and adds them
  to the toolkit's localizer with its own registration, the host chooses the language per request and its
  exception handler names it, and the UI has a language switch for its own pages that it sends with every call. See
  [A sample in two languages](docs/localization.md#a-sample-in-two-languages).
- **The Tenancy sample's login role is written by its export.** `Examples.Tenancy.Exporter` sets
  `SupabaseLoginRole` to `tenancy_api`, so a token role added to its roles reaches the login role with the next
  build. The migration that made the role by hand, `*_tenancy_login_role.sql`, stays, since a database that
  applied it keeps its version in its history; the two say the same and both run again without harm. The host
  checks first, before anything runs as the system caller, that `tenancy_api` may switch to every role its
  callers run as; `LoginRoleFileTests` applies the file the export writes to Supabase's own Postgres and starts
  the host as the role it made.
- **The Tenancy sample: every request says what it requires.** The Tenants module's requests declare the
  first thing the package's use case asks: `TenancyAccess.InTenant()` for the directory's queries, a key for
  the whole tenant or at the unit the request names for the commands, `AccessRequirement.SignedIn()` for a
  person's own seats and for accepting an invitation, and `AccessRequirement.RequiresSystemWork()` for marking a
  tenant as a demonstration and for setting up its project roles, whose handlers ask again for system work in
  the tenant they act in. Archiving and moving a unit, and cancelling an invitation, declare a caller of the
  tenant, since the key they take is at a unit only the use case reads. The operators' queries declare
  `TenancyAccess.RequiresOperator()`. `AccessDeclarationTests` holds every request to what it declares, finds no
  request anyone may send, and holds every request handed to the package to a requirement the use case asks
  first; `RequestPipelineTests` holds the door and the use case to refusing a caller alike.
- **The Tenancy sample keeps a unit's kind itself.** Its `OrganizationUnit` has a `UnitKind` enum (company,
  region, area, site), stored by its key in lower case (`UnitKindKeyConverter`), as the rows from before hold
  it and as REST spells it; `AddOrganizationUnit` sets it in the package's callback, and the seeder sets the
  root's when it provisions. Its queries answer a `UnitListing`, made by the view the directory's unit queries
  take from the units the directory read, so `GET /tenancy/units`, the directory's `/tenancy/directory/units`
  and GraphQL's `OrganizationUnit` still carry `kind`, with no read more; in GraphQL it is now the enum
  `UnitKind`, and `organizationUnitAdd` takes it as one, optional. A kind the enum does not have is a 400
  `invalid-request` where it was `tenancy.unknown-unit-kind`, and the catalogue's answer has no `unitKinds`.
  The migration `UnitKindAsEnum` makes the column the enum's, nullable, and leaves the stored keys as they are;
  its exported file follows.
- **The Tenancy sample states each module's keys once.** `ProjectCatalogue.Permissions` and
  `InspectionCatalogue.Permissions` are marked `[TenancyPermissions]`, and neither module's registration adds
  them any more. The host adds both with the generated `AddTenancyPermissionsOfModules()`, and
  `SampleCatalogue.Built`, which the export writes the policies from, is built from the generated
  `TenancyPermissionsOfModules.All` of the catalogue's project, which no longer lists the modules' keys.
  `ModuleKeysTests` holds the host to one contribution of every module's keys, and every list a module declares
  to its mark; the exported access files are unchanged.

#### Docs

- **Docs: Start-up checks, a page of its own.** [Start-up checks](docs/startup-checks.md) says what each
  registration brings, the order and why, how to turn one off, why they wait for the host to ask, and how a
  package registers one of its own. Row level security, Supabase, Tenancy, Membership, Entity Framework, Transports
  and Getting started show the one call where they showed the methods, and the methods by hand after it.
- **Docs: Access requirements, a page of its own.** [Access requirements](docs/access-requirements.md) says
  what a request declares, what answers it, how the checks are asked without a dispatcher and the behavior
  written for Mediator, once, and the Tenancy and Membership pages link to it for their own cases. Its
  examples, and the first example of [Membership](docs/membership.md), are read from the pages by docs tests
  and compiled as they stand.
- **Docs: Tenancy's design choices, each with where to see it.**
  [The Tenancy page](docs/tenancy.md#design-choices-and-where-to-see-them) lists every choice with its code,
  something to try on the sample and the test that holds it, and a docs test fails for a path, a test or a
  preset it names that is gone. The page goes from what Tenancy is, through adopting it step by step and the
  sample, to the reference for Postgres, and the agent skill has a reference for supporting domains and Tenancy
  and one for GraphQL.

### Changed

- **For the 3.2.0 previews: the access hold is no longer the default.** A handler on a resource with members
  no longer takes what the check read, `Checked<MemberHold<TResourceId>>.TakeFor(command)`, to load the
  resource at that version: it loads the resource its request names and holds it to the request's own
  `ExpectedVersion` with `ExpectVersion`, which now takes a `long?`. A change between the check and the load is
  then a lost race only where the caller named a version; otherwise it is the last write that wins, which is
  what a caller that names no version asks for, and a caller that lost the key by then is refused by a database
  that checks every row. `TakeFor` still works, and `MemberAccessCheck` still keeps the hold; a host that wants
  the save tied to it adds `UseMemberHolds(serviceProvider)` to its context, and its handlers drop the line. The
  Tenancy sample's handlers take nothing from their checks any more: `IProjectStore.LoadAsync(id,
  expectedVersion)`, `ProjectsAccessCheck` keeps no unit, `InspectionsAccessCheck` keeps no project, and
  `GatedProject` and `GatedProjects` are gone; a handler called directly is held by the database alone, where it
  threw before, and recording an inspection asks Projects' gate a second time, in the handler, for the
  project's planned range. A seat that gives up its own place on a crew is saved as the application's work only
  for the command whose check let it through, the request in hand, and as the caller otherwise, so a handler
  reached around its check is refused by the database there too. `AccessChecks.RequireAsync` puts the request in hand for the flow that asked
  (`RequestInHand`).
- **For the 3.2.0 previews: every request says what it requires.** `3.2.0-preview.1` and `3.2.0-preview.2`
  shipped `AccessRequirement.Open(reason)` and Tenancy's `DecidedByThePackage`, `SystemWorkInTenant` and
  `OperatorsOnly`. They are gone, so a request that still declares one no longer compiles; each maps onto the
  [vocabulary](docs/access-requirements.md#the-vocabulary):
  - `new AccessRequirement.Open(reason)`: `AccessRequirement.AllowAnonymous()` where anyone may send the
    request, or `AccessRequirement.SignedIn()` where the caller only had to be signed in.
  - `new TenancyRequirement.DecidedByThePackage()`: what the package's use case asks first and the request can
    name, `TenancyAccess.InTenant()`, `TenancyAccess.ForTheWholeTenant(key)` or `TenancyAccess.AtUnit(key,
    unit)`, and `AccessRequirement.SignedIn()` for accepting an invitation or listing one's own seats. The use
    case still asks the rest. The sample's requests show each
    ([What a request requires of its caller](docs/tenancy.md#what-a-request-requires-of-its-caller)).
  - `new TenancyRequirement.SystemWorkInTenant()`: `AccessRequirement.RequiresSystemWork()`, which takes system
    work that trusted code began and says who may send the request, not where the work acts: it lets system
    work outside any tenant through as well. A handler that must act in a tenant asks Tenancy which
    (`RequireTenant()`, and `BySystem` where only system work may go on), as the sample's `MarkTenantAsDemo`
    does.
  - `new TenancyRequirement.OperatorsOnly()`: `TenancyAccess.RequiresOperator()`, whose record is now
    `TenancyRequirement.Operator`.
  - `new TenancyRequirement.InTenant()`, `new TenancyRequirement.ForTheWholeTenant(key)` and
    `new TenancyRequirement.AtUnit<TUnitId>(key, unit)` still compile; `TenancyAccess.InTenant()`,
    `.ForTheWholeTenant(key)` and `.AtUnit(key, unit)` are how a request spells them now.
  - What a client is answered changes in three places. `access.system-only` where `tenancy.system-only` was,
    from the door and from Tenancy's provisioning, suspending, reactivating and closing a tenant
    (`TenancyRefusals.SystemOnly` is gone). `access.not-signed-in` for a caller who did not sign in, on a
    request that requires a signed-in user, where listing one's own seats answered `tenancy.not-seated` and
    accepting an invitation `tenancy.identity-required`. And in a host that requires explicit callers, a
    request that requires system work and runs as nobody fails with `NoCallerException`.

- **The pgmq check is one of the start-up checks.** `AddPgmqSink` and `AddPgmqConsumer` register it with the
  others, on by default, where they registered a hosted service of its own; it runs as before, in `StartingAsync`,
  and also turns off by its name, `pgmq.extension-installed`. It now runs as `Caller.System`, as every start-up
  check does.
- **The drop at the start of every script of policies takes the triggers of column rules away too,** found by
  the comment each carries, `PostgresRowAccess.ColumnRuleComment`, as the policies are found by theirs, so a
  column rule taken out loses its trigger with the next script, and a migration of a module with rules may
  drop or change a column a column rule holds. The next build of an application that exports its migrations
  writes a new access file for every module that has one; a migration exported before is compared without its
  drop, as it always was.
- **Supabase Auth: two kinds of token, each checked with its own kind of key.** Auth signs a user's token
  with a signing key whose public half it publishes (ES256 or RS256), or with the project's JWT secret
  (HS256). A host given the secret, with `UseSupabaseJwtSecret` or `SupabaseAuthOptions.JwtSecret`, used to
  check every token with it, and so refused the tokens of a stack whose Auth signs with a key, as the one
  the Supabase CLI starts does. It now takes both: a token whose header says HS256 is checked with the
  secret and nothing else, and one that says ES256 or RS256 with the keys Auth publishes. A host without
  the secret takes published keys only, as before. Each kind keeps to its own algorithms, so a published
  key is never taken for a secret, and a token signed any other way is refused without asking anybody.
  Nothing is fetched for a token signed with the secret. This holds for `AddSupabaseJwtBearer` and for
  `SupabaseTokenValidator`, and so for the Azure Functions middleware. See
  [Two kinds of token](docs/supabase.md#two-kinds-of-token).
  - The published keys are fetched from `{Auth}/.well-known/jwks.json` directly, where the project's
    discovery document was read first, when the first token needs them and not before. They are kept and
    fetched again as ASP.NET Core's JWT bearer does, by the scheme's `AutomaticRefreshInterval`,
    `RefreshInterval` and `RefreshOnIssuerKeyNotFound`, through its `Backchannel`. A key Auth stopped
    publishing is refused from the next fetch on. A request for the keys ends with the timeout of the
    client that fetches them, and a token that waits behind another token's fetch stops waiting after as
    long.
  - The published keys are fetched over https. Plain http is taken for an Auth server on the same machine,
    `localhost` or a loopback address. A project URL or an Auth URL in plain http to another machine is
    refused when the host starts, where it used to be asked in the clear: whoever answers for the keys
    decides who is signed in. A host whose Auth server is on a private network of its own says so with
    `SupabaseAuthOptions.AllowPlainHttp`, as it passes `allowPlainHttp` to the admin client.
  - `AddSupabaseJwtBearer` no longer sets `JwtBearerOptions.Authority`, and `UseSupabaseJwtSecret` no
    longer sets `ValidAlgorithms` or clears the authority: it adds the secret as the scheme's
    `IssuerSigningKey`. A host that sets an authority or a metadata address itself has the published keys
    come from there. `TokenValidationParameters.ValidAlgorithms`, where a host sets them, narrow both kinds.
  - The scheme's token handlers are replaced by `SupabaseTokenHandler`. A scheme with
    `JwtBearerOptions.UseSecurityTokenValidators` on would pass it by, and is refused when its options are
    first read.
  - The webshop's Supabase host (`Examples/ModularMonolith.Supabase`) and the Tenancy sample's host use the
    same registration.
- `PostgresRowLevelSecurityOptions.SystemRole`'s documentation recommends a role without `BYPASSRLS` that
  holds only the toolkit's own tables, where the login role holds nothing.
- **A connection opened outside Entity Framework gets the caller.** A connection passed to
  `UseNpgsql(connection)` already open used to get nothing and run as the login role. It now gets the caller
  before the context's first command, which costs one round trip, once. Where a transaction is
  already open on it, the command is refused with `RequireExplicitCallers`, and logged once per connection
  without, because the caller cannot be set safely there. What a connection carries is remembered for the
  whole process, so a connection one context opened carries its caller into another context with another
  instance of the interceptor, and it is forgotten when the connection closes, whoever closes it. See
  [A connection you pass in](docs/row-level-security.md#a-connection-you-pass-in).
- Every script of policies and every exported access file makes `ddd.use_caller` in its prelude, so the next
  build of an application that exports its migrations writes a new access file for every module that has one. The setup script
  gives the login role `USAGE` on the `ddd` schema, which the procedure needs.
- The refusals of `No Reset On Close` and of Supabase's transaction pooler name the `Transaction` scope as a way
  out.
- **Samples: a GraphQL operation of the webshop is a static method.** A query, a mutation or a subscription is a
  static method marked `[Query]`, `[Mutation]` or `[Subscription]`, with `[Service]` on what is injected, and
  HotChocolate's generated `Add{Name}Types()` registers them; a class no longer extends `Query` with
  `[ExtendObjectType]`. Every schema holds the fields it held. In a module's source schema the fields of `Query`
  and `Mutation` now come in the order of their classes' names, and the webshop's `AddInventoryProductStock`,
  `AddPaymentsOrderStub` and `AddShippingOrderStub` are gone, because the generated registration adds what they
  added. See [Register](docs/graphql.md#register).
- **Samples: the webshop is `Examples.Webshop`.** Its modules, hosts and services are projects and namespaces
  `Examples.Webshop.*` (`Examples.Webshop.Ordering`, `Examples.Webshop.Host`,
  `Examples.Webshop.Pgmq.Storefront`), in the folders they were in; its tests are `Tests/Examples.Webshop.Tests`
  and, with the Tenancy sample's AppHost, `Tests/Examples.AppHost.Tests`; and the two projects the samples share
  are `Examples.Hosting` and `Examples.ServiceDefaults`. No module, event, schema or migration changed its name.
  A sample is an application, and an application is not in the toolkit's namespace: inside `DDDToolkit.*` the
  code HotChocolate generates for an `[ObjectType<T>]` class takes `HotChocolate` for the toolkit's
  `DDDToolkit.HotChocolate`. Each storefront service now adds a line's product to `OrderLine` in a static
  partial class with `[ObjectType<OrderLine>]`, `GraphQL/OrderLineProduct.cs`, where it had a class with
  `[ExtendObjectType]`.
- **Samples: the shared hosting opens SQLite without the driver's connection pool**
  (`ModuleDatabase.SqliteConnectionString`), for the webshop's modules: with the
  pool on, Microsoft.Data.Sqlite can hand one native connection to two connections opened at the same moment.
- The parameterless constructor generated for a `sealed` entity or aggregate root is `private` instead
  of `protected`. A protected member of a sealed class is warning CS0628, in generated code, where a
  project building with `TreatWarningsAsErrors` could not get rid of it. Entity Framework uses either.
- **A save a row level security policy denies is refused with `access.refused`.** Through `UseDDDToolkit`,
  an insert or an update whose new row a policy refuses used to throw a `DbUpdateException` around
  Postgres's `42501`, and an update or a delete of a row a policy hides from the statement, which finds no
  row, used to throw a `ConcurrencyConflictException`, sending a client into a retry that could never
  succeed. Both now throw a `RefusalException` with the code `access.refused` and the kind `NotPermitted`,
  with what the save threw as its inner exception. To tell a denial from a lost race,
  `AggregateVersionInterceptor` reads each row the failed statement was for again, by its key and as the same
  caller: its own concurrency tokens, or its aggregate root's `Version` for a child that has none. A row
  that is gone, hidden or changed stays the `ConcurrencyConflictException` it was. That is one query for each
  row of a save that found no row, on every database, and none for a save that succeeds. A policy's denial of
  a new row is known by the routine Postgres names with the error, `ExecWithCheckOptions`, untranslated, and
  never by the words of its message, so a server whose `lc_messages` answers in another language is read the
  same; the log line names the table the message quotes, or, where it cannot be read, the one table the failed
  save wrote. A missing privilege, which is `42501` as well, is not a refusal and fails as before, and neither
  is a statement with `row_security` off by a role the policies hold, whose `42501` names row level security
  too. Code that caught either exception to detect a policy's denial catches the refusal instead. A refusal,
  a policy's or a guard's, is logged through the context's logger factory, under the category of
  `DatabaseRefusalInterceptor`, as what it was. Where the request being handled passed an access check
  (`PassedAccessCheck`, under Added), the check is asked again first. When it refuses now, the caller's rights
  changed between the check and the save, a key taken from its role say, and C# and the database agreed: an
  information line says so, with no stack trace. When it still lets the caller through, or the flow passed no
  check, the application allowed what the database does not, and that is a warning: "C# and the policies
  disagree", or the guards, naming the request and its requirement when there is one, so whoever reads it sees
  what was asked again. A check that gives no answer when it is asked again, a failure or a
  `ConcurrencyConflictException`, leaves the warning, which says why. The check is asked only for a refused
  save, and only when a logger listens. See
  [When the policies refuse what C# allowed](docs/row-level-security.md#when-the-policies-refuse-what-c-allowed).
- `UseDDDToolkit` adds a fourth interceptor, `DatabaseRefusalInterceptor`, after
  `AggregateVersionInterceptor`. A context that adds the toolkit's interceptors by hand adds it too, or
  keeps the failures it had.
- **`UseDDDToolkit` builds its domain event interceptor instead of resolving it**, so it works in the
  options callback of a context pool, which is handed the root provider; the scoped registration of
  `PublishDomainEventsInterceptor` stays for a host that adds it by hand. Which services the in-process
  handlers get is decided per save: the scope the context was bound to, or, for a context that is not
  pooled, the provider its options were built with, as before. Two refusals follow from it, both an
  `InvalidOperationException` thrown before any event leaves its aggregate. A pooled context that was given
  no scope refuses to dispatch in process; the outbox needs no scope and is not affected. And with options
  built once without a pool, `AddDbContext(..., optionsLifetime: ServiceLifetime.Singleton)` or
  `AddDbContextFactory`, a container that validates scopes used to refuse while the options were built;
  it now refuses at the first in-process dispatch, and a save with nothing to dispatch goes through.
- `FailureTranslations.ToolkitCodes()` expects `access.refused`, `access.role-not-allowed`,
  `access.not-signed-in` and `access.system-only` as well. They ship in English and Dutch; for any other
  language your check now reports them until your own resource has the keys.
- **Row level security: a token's role claim no longer runs as a signed-in user whatever it says.** A
  signed-in user's queries ran as `UserRole` for any `role` claim. The claim now picks the database role
  only through a list: `authenticated`, or no claim, is `UserRole`; a role in `TokenRoles` is the role it
  is mapped to; a token that names `UserRole` or `AnonymousRole` itself, or `anon`, runs as that role; and
  any other role is refused with a `RefusalException`, `access.role-not-allowed`, before the context
  connects, so a token that names the system's role or the login role runs as neither. A host whose tokens
  carry `authenticated` or no role is not affected. One whose tokens carry a role of their own maps it:
  `options.TokenRoles["member"] = options.UserRole` keeps what it had.
- `Callers.FromClaims` gives a caller whose `role` claim is not text, a list of roles say, that claim as it
  is written for its `Role`, where it gave no role at all: such a token is no longer read as one without a
  role claim, which is a signed-in user's.
- `PostgresRowLevelSecurityInterceptor` reads its options once, when it is built: changing the options
  object afterwards no longer changes the roles of later connections. The anonymous and the system claims
  were already read once, so such a change gave a connection one role and another's claims.
- **Row level security: every policy is for one command and one role.** The policies of an aggregate's
  own table are now written like its entities': one per command and role, and never `FOR ALL`. Where
  several rules grant a role the same command on a table, they share one policy whose condition is theirs
  OR-ed together, which Postgres did with the separate policies anyway, so no caller gains or loses a row.
  Every policy therefore has a new name:
  - a policy from one rule is `"<rule> (<command>) for <role>"`, for example
    `"A customer has their orders (select) for anon"`, where 3.0.0 wrote `"A customer has their orders (read)"`
    for `anon, authenticated` together, or `"Nobody orders for somebody else"` for a rule with one
    operation;
  - a policy several rules share is `"<table> (<command>) for <role>"`, for example
    `"Orders (select) for authenticated"`, with a comment above it naming the rules;
  - the commands are `select`, `insert`, `update` and `delete`, where 3.0.0 wrote `read`, `create`,
    `change` and `remove`, or nothing for a rule with one operation or all four.

  The next script or access file drops the old policies, found by their comment, and makes the new ones.
  SQL, a test or a pgTAP check of your own that names a generated policy needs the new name: find it with
  `select policyname from pg_policies where schemaname = '<schema>' and tablename = '<table>'
  and cmd = '<SELECT|INSERT|UPDATE|DELETE>' and '<role>' = any(roles)`, and prefer asking that over a
  name, because a policy's name changes when another rule starts or stops granting the same command to the
  same role. A Supabase host's next build writes a new access file to commit; a host on a Postgres of its
  own renders its script again, as a new migration.
- A rule's roles are written as they are spelled, in double quotes where Postgres would otherwise fold them,
  so `To = ["Moderator"]` is the role `Moderator`, where 3.0.0's unquoted name meant `moderator`. A rule
  for `PUBLIC`, in any spelling, is refused when its policies are written, and so is a role whose name has
  a `$` or is longer than the 63 bytes Postgres keeps. `PostgresRowLevelSecurityOptions` refuses the same
  for its roles, and a `pg_` name as well.
- `PostgresRowAccess.SetupScript()` also makes `ddd_system_in`, checks it, and grants it to the login role,
  so it now needs a role that may create roles even where `anon` and `authenticated` exist already; set
  `PostgresRowLevelSecurityOptions.SystemInRole` to `null` to leave the role out. Where the setup and the
  migrations run as different roles, run this version's `SetupScript()` first, as the role that ran it
  before, so a script whose policies are for `ddd_system_in` finds the role made and granted.
- `PostgresRowAccess.Script` and `CreateStatements` have overloads that take a `RowAccessExport`, so a call
  that passes `null` for the caller functions by position, `Script(context, rules, functions, null)`, is
  ambiguous now and no longer compiles. Pass it by name, `callerFunctions: null`, or leave it out.
- `CallerKind` has a fourth value, `SystemIn`. A `switch` of your own over it that has no default arm
  misses a case, and the interceptor refuses a kind it has no role for rather than running it as the
  system.
- `EnsureSupabaseMigrationsAppliedAsync` reads the migration history as `Caller.System`, whatever caller is
  current, so it works on a context with row level security in a host that requires explicit callers.
- `PostgresRowLevelSecurityInterceptor` is a command and a transaction interceptor as well, and has a
  constructor that takes the settings, the `CallerOptions` and a logger, which dependency injection uses.
  `AmbientCallerAccessor` has a second constructor, which takes the `CallerOptions`.
- A role named `none` is refused where a role is configured, in `PostgresRowLevelSecurityOptions` and
  `RowAccessRoleNames`: switching to it goes back to the role the application logged in as.
- **Row level security: `==` between values that cannot be null is `=`.** A rule's `==` and `!=` between two
  operands whose C# types are value types that are not nullable, an enum, a number, a flag, a `Guid` or an
  id that is a struct, are now `=` and `<>`, which an index can answer, where 3.0.0 wrote
  `IS NOT DISTINCT FROM` and `IS DISTINCT FROM` for every comparison. No row changes its answer: neither
  side can be null. With a nullable operand they stay as they were. The policies' text changes with it: a
  Supabase host's next build writes a new access file for a module whose rules compare such values, and a
  host on a Postgres of its own renders its script again, as a new migration.
- `RowAccessFunction` takes a logical name, `owner/name`, or a name relative to an owner it is given, as
  well as `schema.name`, and has `LogicalName`, `Owner`, `IsQualified`, `Parameters` and `Shape`. Two access
  functions are one name when their logical names are, whatever schema they would be created in.
  `RowAccessFunction.For(aggregate, name, sql)` keeps its meaning.
- DDD00038's title and message cover access functions' parameters, contracts' questions, `[AccessFunctions]`
  questions and function names, and DDD00039's message names what a rule may ask besides the aggregate and
  the caller.
- **Row level security: access functions are executable by the roles that ask them, and no others.** A
  script, and so every Supabase access file, now revokes each access function from `PUBLIC`, takes back
  every other grant on it but its owner's, what a schema's default privileges gave included, and grants it
  to the roles of the policies that ask it; before, every role could execute it. A script of one context,
  `PostgresRowAccess.Script`, knows only that context's policies: a policy of another module that asks one
  of its functions, through a contract, for a role this context's policies are not for fails with
  "permission denied for function" until the modules are written together, with `PostgresRowAccess.Scripts`
  as the Supabase build does, or the function is granted after the script. A `GRANT EXECUTE` of your own on
  an access function is taken back by the next script, so SQL of your own that calls one as a role no policy
  asks it for needs that grant after every script. A host's next build writes a new access file for every
  module with access functions, and a host on a Postgres of its own renders its script again, as a new
  migration.
- **Functions are created in the order they ask each other**, within a script, rather than by name: a
  function whose body asks one that sorts after it was refused where Postgres checks function bodies when
  it creates them, as the Supabase CLI does. Functions that ask each other in a circle are refused, naming
  them.
- **Scripts and access files are ordered by what they ask.** `PostgresRowAccess.Scripts` and the Supabase
  export put a context before every context whose rules, access functions or contributions ask a function
  it defines, and otherwise keep the order given, where they put every context that defines an access
  function first. Contexts that ask each other's functions are refused, naming them and the functions.
  `SupabaseMigrations.Export` and `Compare` of several sources report them in that order.
- **The Supabase export refuses an access function whose parameters or return type changed** against the
  module's newest access file, with a message that says to give it a new name: Postgres cannot change
  either in place, and cannot drop the function while other modules' policies use it.
- **A script leaves a function it no longer writes while something still asks it**, a policy of a module
  whose script comes later, rather than failing to drop it; the context's next script drops it once
  nothing does.
- The code the Supabase build generates into a host calls
  `SupabaseMigrationBuild.RunIfRequested(All, Rules, Functions, Contributions)`, with the contributions the
  host lists; `RunIfRequested` and `Run` have overloads that take them.
- **`DDDToolkit.HotChocolate` asks for HotChocolate 16.6.6 or later**, where it asked for 16.0.0: it declares
  `HotChocolate.AspNetCore` and `HotChocolate.Types.Analyzers` at 16.6.6, the version
  `DDDToolkit.HotChocolate.Fusion.InMemory` has asked of Fusion since 3.0.0. HotChocolate asks that all of its
  packages in one application are the same version, and a module compiled against 16.0.0 did not run under
  the in-memory gateway, which brings 16.6.6: a field paged with a total count threw a
  `MissingMethodException`. An application on an older HotChocolate 16 moves its HotChocolate packages to
  16.6.6 or later, all of them to one version.
- **`DDDToolkit.HotChocolate.Fusion.InMemory`: source schemas that do not compose fail the start at once,
  and say why.** The start used to wait out `CompositionTimeout` and then, more often than not, report only
  that there was no schema: HotChocolate's in-memory connector composes in its constructor and tells only
  who is listening at that moment. The package now listens before the composer starts, stops waiting when
  the composer refuses, and lists every error it found, where the composer's own exception names the first.
- **`DDDToolkit.HotChocolate`: the error types describe themselves in the schema.** `CodedError`, the four
  error types, `ValueFailure`, `RuleViolation`, `FailureArgument` and `RefusalKind`, with every field and value,
  carry a description written for the client that reads the schema, where they had none, so a schema snapshot
  of your own changes once. The toolkit gives them itself rather than leaving them to its XML documentation,
  which the packages now ship: that is written for the C# reader, and HotChocolate finds it only where the build
  put the file beside the assembly, so the source schemas of one gateway could describe a type they share
  differently. Every schema with the conventions now describes them alike, whether it reads XML documentation or
  not. Your own types are described from your own XML documentation, as before. See
  [Typed errors in mutation payloads](docs/graphql.md#typed-errors-in-mutation-payloads).

### Fixed

- **Membership no longer has Entity Framework warn of a limit without an order.** Finding the role made from a
  starter role, as opening a resource with an owner does, read at most two rows in no order, which Entity
  Framework logs as a warning the first time a process compiles the query: the Tenancy sample's seeding did,
  whenever its host was the first to open a project. The rows are read in the order of their ids.

- **The packages ship their XML documentation.** No package carried the XML documentation file beside its
  assembly in `lib/`, so Visual Studio and other editors showed none of the toolkit's `///` comments, however
  well the source is documented. Every package now does, and the consumption check fails a package that does
  not. A parent the generator writes for `[AggregateRootBase]` or `[EntityBase]` now documents every parameter
  of the method its derived class calls, so a package of your own that generates its documentation gets no
  CS1573 from it.

- **The Supabase export given for a whole build.** `SupabaseMigrationsExport` given on the command line, in a
  `Directory.Build.props` or by CI reached every project that references `DDDToolkit.EntityFramework.Supabase`,
  or a project that does, because the package's build step and generator arrive through `buildTransitive`.
  Each module wrote the export's list into itself, reported DDD00054 for what only the host lists, and then the
  step started the module's library, which failed with `MissingMethodException` and MSB3073. The step and the
  generator now run only in an application that is not a test project: an `OutputType` of `Exe` or `WinExe`,
  and neither `IsTestProject` nor `IsTestingPlatformApplication` set to `true`. A value given for the whole
  build reaches the host, and every library and test project ignores it, without a warning. Another
  application in the same build that references the modules still exports, so a solution with more than
  one keeps the property in the host's project file.
- **`DDDToolkit.HotChocolate.Fusion.InMemory`: two lookups of one module in one answer, each for several
  keys.** A query that named, say, the seats and the roles of a list of crews answered "Unexpected Execution
  Error" for every field their owner was to fill. HotChocolate's in-memory client (16.6.6 and 16.6.7) writes
  the variables of every request of a batch into one buffer, and reads those of a request with several sets
  of variables from the start of it, so the second such request read the first one's as well. The gateway now
  sends the requests of such a batch one after the other. The remarks of `CompositionTimeout` now say what
  it does not bound: a source schema that never finishes building holds the start in HotChocolate's own
  warm-up, before the gateway's check runs.
- **Row level security: a condition in parentheses that are not its own.** A policy wants its condition in
  parentheses of its own, and a script kept a pair a condition already started and ended with. That pair can be
  a subquery's: a rule that is one scalar question and nothing else, `=> Questions.OnDuty()`, is
  `(SELECT f())`, and was written `USING (SELECT f())` and `WITH CHECK (SELECT f())`, which Postgres refuses as
  a syntax error, so the whole access file failed. Such a condition now gets a pair of its own,
  `USING ((SELECT f()))`, and one whose own pair holds more than the subquery, `((SELECT f()) = 1)`, keeps
  it. A quote of the other kind inside a literal or a quoted name, such as `'5"'`, no longer throws the
  count of parentheses off, and neither does a parenthesis in a comment: the pair is counted outside
  literals, quoted names and comments, a literal with escapes, `E'it\'s'`, and one between dollar quotes,
  `$$it's$$`, included; a comment inside a comment is not told apart. A condition with a `--` comment, which
  runs to the end of its line, was written with the closing parenthesis inside the comment, and the access
  file failed: such a condition now gets its closing parenthesis on a line of its own. Every other condition
  is written as it was.
- **Row level security: a caller that changes while a connection is open.** The interceptor set the caller
  only when a context opened a connection, so a caller begun while it was open, on a connection opened with
  `OpenConnection()` or inside a transaction, was ignored, and the work ran as the caller before it. Before
  every command, and before a transaction begins, it now compares the caller and the settings with what it
  set: outside a transaction a change is set first, in one extra round trip; inside one it is logged as a
  warning, once per connection, and the transaction goes on as before, or, with `RequireExplicitCallers`,
  refused with an `InvalidOperationException` naming both callers. See
  [A caller that changes on an open connection](docs/row-level-security.md#a-caller-that-changes-on-an-open-connection).
- **Row level security: settings left on a server connection.** Supabase's `auth.uid()`, `auth.role()`,
  `auth.email()` and `auth.jwt()` read `request.jwt.claim.sub`, `request.jwt.claim.role`,
  `request.jwt.claim.email` and `request.jwt.claim` before the claims, so a value something else left on the
  server connection decided who a query ran as, or whose e-mail a policy saw. The interceptor's statement
  now empties the four.
- **Row level security: a connection the caller could not be set on.** When the statement that sets the
  caller failed, a role the login role may not switch to for one, Entity Framework kept the connection open
  without counting it as opened, and the context's next query ran on it as the login role. The interceptor
  now closes the connection when the statement fails, so the next query opens it and sets the caller again.
- **Row level security: a caller undone by a rollback.** A connection opened inside a `TransactionScope`
  sets the caller inside that transaction, and a rollback undid the setting while the connection stayed
  open; its next command ran as the login role. The caller is now set again before that command.
- **Row level security: a hierarchy mapped to one table.** A rule about a type derived in a hierarchy that
  Entity Framework maps to one table was written as a policy on the whole table, so it let a caller read or
  write the rows of every type there; a rule about priority orders reached every order. Its condition now
  also asks the table's discriminator, so it holds for the rows of that type and the types derived from it
  only. The types of such a hierarchy also share the policies of their entities' tables, which a script
  wrote once for each type before, and Postgres refused the second.
- **Row level security: an aggregate's entities follow its write rules.** In 3.0.0 the table of each of an
  aggregate's entities got one `FOR ALL` policy that only asked whether the row it belongs to was visible,
  so a caller who could read an aggregate could also add, change and remove its entities, with SQL of its
  own or through the Data API; Entity Framework's version bump of the root hid most of it. Each such table
  now gets one policy per command and role that a rule of the root grants, and each asks the root's rules
  about the root row: reading follows the row the entity belongs to, as before; inserting needs a `Change`
  rule, or a `Create` rule and a root row the running transaction wrote; updating needs `Change`; deleting
  needs `Change` or `Remove`. An entity of an entity goes up through its parent to the root. A command no
  rule grants gets no policy, and Postgres refuses it. This fix leaves the root's own policies as they
  were; they changed separately, see Changed.
  Right after its drop, every script and every access file now makes what these policies ask, where the
  database does not have it as it should: the `ddd` schema, `ddd.written_in_this_transaction(xid)`, and
  the right to use both for every role a policy names. It leaves what is already right alone, so a script
  run by the application's own role still works after somebody else ran `PostgresRowAccess.SetupScript()`,
  which makes the function as well. See
  [The aggregate's entities](docs/row-level-security.md#the-aggregates-entities).
  - The entity tables' policies have new names, one per command and role, `"<table> (<command>) for <role>"`,
    for example `"OrderLine (insert) for anon"`, instead of `"OrderLine goes with its Orders"`. The next
    script or access file drops the old one, found by its comment; SQL or a test of your own that names it
    needs the new names.
  - Each entity policy is for the roles of the rules that grant its command. A rule without `To` now gives
    `anon` and `authenticated` their entity policies even next to a rule for a role of your own, which in
    3.0.0 left them none. Reading an entity needs a `Read` rule for the role: a role that reads the root
    only through a policy you wrote by hand no longer reads its entities, until it gets a `Read` rule or a
    `SELECT` policy of your own on the entity's table as well.
  - A host that relied on "may read the root, may write its entities" says so with a rule now: `Change`
    for the callers who write the entities of existing roots, or `Create` for those who only add them to
    a root they create, in the same save. A later save in the same transaction bumps the root's version,
    which is an update and needs `Change`.
  - The `Create` case counts a root row as written when the running transaction inserted it, or updated it
    in any way: through an `UPDATE` policy you wrote by hand, a trigger, a `SECURITY DEFINER` function or a
    foreign key's `ON UPDATE` or `ON DELETE` action. The rules' own policies are not such a way, because
    updating the root needs `Change`.
  - Nothing changes in a database until the new policies are applied. A Supabase host's next build writes a
    new access file: commit it (`Check` fails in CI until it is there) and apply it with `supabase db push`.
    A host on a Postgres of its own renders `PostgresRowAccess.Script(context, rules, ...)` again and adds
    it as a new migration, although its rules did not change: its drop takes the old policy off, and its
    prelude makes the function. Where `SetupScript()` ran as another role than the migrations run as, run
    this version's `SetupScript()` again first, as that role, which owns `ddd` and so may add the function
    to it.

## [3.1.0] - 2026-10-01

The module names its generated code. This renames generated methods in a project that declares
`[assembly: Module]` and sets no `DDD_Module`, or sets another name;
[The names of generated registrations](docs/migrating-to-3.md#the-names-of-generated-registrations)
has the cases and what to do in each. A project without the attribute is not affected.

### Changed

- **The module names its generated code, and `DDD_Module` is the default beneath it.**
  `Add{Module}Converters`, `Add{Module}IntegrationEvents` and `Add{Module}GraphQlRuntimeBindings` now
  take their name from `[assembly: Module]` first, then from `DDD_Module`, then from the assembly name,
  the order `{Module}EventNames` already had. A `Directory.Build.props` can set `DDD_Module` for a
  whole folder, and an assembly that declares a module still gets the module's name. This renames
  generated methods in a project that declares a module and either sets no `DDD_Module` or sets
  another name; a project without the attribute is not affected.
  [The names of generated registrations](docs/migrating-to-3.md#the-names-of-generated-registrations)
  has the three cases and what to do in each.
- Two assemblies of one module, such as a module and its contracts project, give their registrations
  the same name. `Add{Module}Converters` and `Add{Module}GraphQlRuntimeBindings` therefore call the
  ones of the module's other assemblies they reference, so a context or a schema makes one call for
  the module. Another module that references only the contracts calls the contracts' method, under
  that same name. See [Two assemblies, one module](docs/modules.md#two-assemblies-one-module).
- The example modules no longer set `DDD_Module`. Ordering's context and schema make one generated
  call where they made two, and the modules that store an `OrderId` call `AddOrderingConverters`.

## [3.0.1] - 2026-10-01

`DDD_Module` now reaches the generators wherever they run. Nothing to change in a project that
references `DDDToolkit`; a project that references only `DDDToolkit.Abstractions` and
`DDDToolkit.Analyzers` gets the names it asked for after the update.

### Fixed

- `DDD_Module` was ignored in a project that had the generators without the `DDDToolkit` package. The
  property was declared to the compiler by a props file in `DDDToolkit`, while the generators that
  read it ship in `DDDToolkit.Analyzers`. A contracts project referencing `DDDToolkit.Abstractions`
  and `DDDToolkit.Analyzers` therefore ran the generators without the property, and
  `{Module}EventNames` was named after the assembly without a word. The same held for a project that
  got the generators through another project, because the file was only in `build/`, which does not
  travel past the project that references the package. `DDDToolkit.Analyzers` now carries the props
  file itself, in `build/` and in `buildTransitive/`, and `DDDToolkit` no longer has one.
  See [DDD_Module, and the package that brings it](docs/modules.md#ddd_module-and-the-package-that-brings-it).

### Added

- [DDD00014](docs/diagnostics.md#ddd00014), a warning for a project the generators run in while the
  props file was not imported, for example because the reference excludes the package's build assets.
  `DDD_Module` is ignored there, and the build now says so. A project that imports the file and sets
  no `DDD_Module` is not reported, and neither is an assembly that declares `[assembly: Module]`.
- `build/verify-package-consumption.sh` builds three more consumers against the packed packages: one
  with only `DDDToolkit.Abstractions` and `DDDToolkit.Analyzers`, one with only `DDDToolkit`, and one
  that gets the toolkit through a project reference. Each has to name its generated class after
  `DDD_Module`. It also checks that DDD00014 appears without the props file and stays away with it.

### Changed

- The MSBuild properties the generators read are declared in the props file of `DDDToolkit.Analyzers`,
  the package the generators ship in. Referencing `DDDToolkit.Abstractions` and
  `DDDToolkit.Analyzers` without `DDDToolkit` is a supported setup.

## [3.0.0] - 2026-09-27

A breaking release. Upgrading from 2.0.22 needs code changes in every project that raises a domain
event, and a retarget to .NET 10. [Migrating to 3.0](docs/migrating-to-3.md) walks through each break
with the before and the after.

The theme is removing silence. In 2.x a misapplied attribute generated nothing and said nothing, a
generic type produced a second unrelated type, colliding hint names threw inside the generator and
left you with no code and no error, and the generators did not load at all under current SDKs. Every
one of those now either works or reports a diagnostic that names the type and the fix.

The packages are published as `Temp.DDDToolkit.*` for now; see
[The package ids](docs/migrating-to-3.md#the-package-ids).

The release was written in two stages. What each section lists under *Later in this release* came
after the first 3.0 builds. Where it changes what such a build did, to a database or to the names on
messages, [From an earlier 3.0 build](docs/migrating-to-3.md#from-an-earlier-30-build) says what to do.

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
  [Outbox and inbox timestamps](docs/migrating-to-3.md#outbox-and-inbox-timestamps) before upgrading.** The column moves from
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
- `ModuleIntegrationEventSink` and `outbox.SendToModules()`, for the common case of another module in
  the same process. The producing module does not name its consumers: each consuming module registers
  itself with `services.AddModuleIntegrationEvents<TContext>(module => module.Handle<TContract, THandler>())`,
  and its handlers run under its own inbox. Handlers implement `IIntegrationEventHandler<TContract>`,
  are typed on the contract rather than on the domain event, and each gets its own inbox row, so a
  retry re-runs only the handlers that failed. They are called through delegates captured at
  registration, without reflection. `[IntegrationEventConsumer("name")]` pins the inbox key.
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

**Later in this release**

- `DDDToolkit.EntityFramework.Postgres`: row level security for an application's own queries, on any
  Postgres. An application that connects as the tables' owner is not subject to their policies, so they
  used to guard everything but the application. `options.UsePostgresRowLevelSecurity(provider)` on a
  context now sets the caller's role and token claims on every connection it opens, as PostgREST does for
  each request, so every policy applies to the application's queries too: a signed-in user runs as
  `authenticated`, a request without a token as `anon`, and background work as the login role or
  `SystemRole`. It refuses `No Reset On Close`, `Multiplexing` and Supabase's transaction pooler, which
  would hand one caller's settings to another. Registered with `services.AddPostgresRowLevelSecurity()`;
  on Supabase, `AddSupabaseRowLevelSecurity()` and `UseSupabaseRowLevelSecurity(provider)` do the same
  with Supabase's roles. `PostgresRowAccess.SetupScript()` makes the roles and the `ddd.caller_id()`,
  `ddd.caller_role()` and `ddd.caller_claims()` functions on a Postgres that is not Supabase's. See
  [Row level security](docs/row-level-security.md).
- Who the application is acting for, in the toolkit itself: `Caller` (a user, somebody who has not signed
  in, or the system), an `ICallerAccessor` that says which one it is now, and `Callers.Begin(caller)`,
  which makes one current for a flow of work, so a queued job can keep the user it was queued for.
  `Callers.FromClaims(claims)` makes a user of a validated token's claims.
- Row access rules written in C#. `[RowAccess<TAggregate>(RowOperations.Read | ...)]` on a
  `static partial class` whose `static bool Allows(TAggregate, Caller)` is one expression: the generator
  translates it into SQL when it compiles, with C#'s equality and nulls, columns from the Entity
  Framework model and enum constants as the column stores them, and reports what it cannot translate
  ([DDD00038](docs/diagnostics.md#ddd00038) to [DDD00041](docs/diagnostics.md#ddd00041)). `Sql.Call<T>`
  and `Sql.Raw<T>` put SQL of your own in a rule, which then holds in the database only.
  `PostgresRowAccess.Script(context, rules)` writes the policies, the tables of an aggregate's entities
  following their root. The rule stays a method, so a handler asks the same rule in C#. See
  [Row access rules written in C#](docs/row-level-security.md#row-access-rules-written-in-c).
- Access functions, for rules about an aggregate's entities: `[AccessFunction<Project>("projects.is_member")]`
  on a class shaped like a rule, whose `Allows` may ask `project.Members.Any(member => ...)`. It becomes
  one `SECURITY DEFINER` function with an empty search path, which reads the entities without their
  policies, so a rule that calls it, `ProjectMembership.Allows(project, caller)`, does not make Postgres
  recurse; a rule that reads the entities itself is [DDD00041](docs/diagnostics.md#ddd00041). Only the
  context that maps the aggregate writes the function, before its policies, and replaces it in place
  later so other modules' policies that call it keep working. The generator adds `Name` and
  `Allows(key)`, so a rule holding only an id asks it typed, `ProjectMembership.Allows(task.ProjectId)`;
  `[AccessFunctionContract<ProjectId>("projects.is_member")]` in a module's contracts publishes the same
  to other modules, and the Supabase build refuses a rule that asks a function no module defines. See
  [Access functions](docs/row-level-security.md#asking-the-aggregates-entities-access-functions).
- The Supabase build writes the row access rules of every module a host references into
  `supabase/migrations`, a file per module, `{version}_access.{module}.ddd.sql`, asking `auth.uid()`. The
  file says what the rules are now: it drops the policies the previous one made and makes them again, so
  a rule taken out disappears and a hand-written policy is left alone. A new one is written when a rule
  changes or a migration of the module comes after it, and `Check` fails in CI until it is there. Every
  migration of a module with rules starts by taking its generated policies off, so a policy never stands
  in the way of dropping a column. See [Row access rules in the build](docs/supabase.md#row-access-rules-in-the-build).
- `DDDToolkit.Auth.Supabase`: Supabase Auth's access tokens validated without a web framework, against
  the keys the project publishes (fetched from its discovery document, and again when a token names a new
  one) or a JWT secret for the CLI's local stack. `SupabaseTokenValidator` turns a token into the caller,
  with its claims exactly as signed; `services.AddSupabaseAuth(projectUrl)` registers it. It needs no
  Entity Framework.
- `DDDToolkit.Auth.Supabase.AspNetCore`: `AddSupabaseJwtBearer(projectUrl)`, a JWT bearer scheme for
  Supabase Auth, so `[Authorize]` and `HttpContext.User` work as usual, and each request's user becomes the
  caller. `UseSupabaseJwtSecret` for the local stack.
- `DDDToolkit.Auth.Supabase.AzureFunctions`: `builder.UseSupabaseAuth()`, a worker middleware for the
  isolated worker, which has no ASP.NET Core pipeline. It validates the token of each HTTP-triggered
  invocation and runs the function as its user; other triggers run as the system unless they begin a
  caller themselves. `context.GetSupabaseCaller()` says who it is.
- The Supabase monolith with signed-in customers. With `Supabase:Url` set, an order is its customer's: an
  order knows who placed it, and two row access rules in Ordering, `ACustomerHasTheirOrders` and
  `NobodyOrdersForSomebodyElse`, become the policies the build writes to `supabase/migrations`, so each
  caller sees their own orders while guests' orders work as before.
  The AppHost's container gets Supabase's roles and `auth` functions, the Supabase samples gain two
  scenarios with signed-in customers, and the Supabase Live workflow plays them with real users of the
  project's Auth.
- An agent skill, [`skills/dddtoolkit`](skills/dddtoolkit), that teaches an AI coding agent the
  declarations, the rules the generators enforce, the wiring for Entity Framework, event delivery and
  modules, and the fix for every DDD diagnostic. The repository is a Claude Code plugin marketplace
  for it (`/plugin marketplace add DylanSnel/DDDToolkit`), and `npx skills add DylanSnel/DDDToolkit`
  installs it for other agents. A test fails when a diagnostic has no section in the skill.
- The docs site writes the docs for language models: `llms.txt`, an index of every page with the
  description from the README's table, `llms-full.txt` with every page in one file, and each page as
  Markdown at its own address with `.md` added. Relative links in them are made absolute.
- Every diagnostic carries a help link to its heading in [Diagnostics](docs/diagnostics.md) on the docs
  site, so an IDE opens it from the error list and an agent reading the build output can follow it. A
  test holds every id to a heading of its own on that page.
- The pgmq sink and consumer check the database when the application starts. `AddPgmqSink` and
  `AddPgmqConsumer` register a lifecycle service that reads the installed pgmq version once per database
  in `StartingAsync`, before any consumer or the outbox processor starts. Without the extension the start
  fails with `PgmqNotInstalledException`. With `UseTopics` or `BindTopics` on a pgmq older than 1.11 it
  fails with the new `PgmqTopicsNotSupportedException`, which names the installed version, says that
  Supabase ships 1.5.1 and that `UseQueue` and `UseQueues` work there. Before, both failures came with the
  first message sent, or when the consumer bound its queue. `PgmqQueue.InstalledVersionAsync` and
  `PgmqQueue.EnsureTopicRoutingAsync` do the same for a check of your own, and
  `CheckExtensionOnStart = false` on the sink's or the consumer's options turns it off. The topic
  functions of `PgmqQueue` throw `PgmqTopicsNotSupportedException` as well, an
  `InvalidOperationException` as before.
  `Examples/Microservices.Pgmq` drops its hand-written check for this one. See
  [Queues, creation and the missing extension](docs/transports.md#queues-creation-and-the-missing-extension).
- Long polling in `PgmqConsumer`. While the host runs, an empty read waits inside Postgres with
  `pgmq.read_with_poll` for up to `LongPollTimeout` (five seconds) instead of returning at once and
  sleeping `PollingInterval`, so a message is picked up within `LongPollInterval` (100 milliseconds) of its
  commit and a quiet queue costs one round trip per wait. It is on by default, and holds one connection per
  consumer while it waits; `LongPollTimeout = TimeSpan.Zero` goes back to polling every `PollingInterval`.
  `ConsumeOnceAsync` still reads once and does not wait. `PgmqQueue.ReadWithPollAsync` is the read on its
  own. `read_with_poll` is in pgmq 1.5.1, so this works on Supabase too, and is tested there. See
  [Reading the queue](docs/transports.md#reading-the-queue).
- The pgmq sink and consumer read their settings from configuration. `AddPgmqSink` and
  `AddPgmqConsumer` take an `IConfiguration` section, such as `Pgmq:Sink` or `Pgmq:Consumer`, before an
  optional lambda that runs after it; `ReadFrom(section)` on `PgmqSinkOptions` and `PgmqConsumerOptions`
  does the same by hand. Every consumer option is a key of the same name, and the sink reads `Queue`,
  `Queues`, `Topics`, `CreateQueueIfMissing`, `SendHeaders` and `CheckExtensionOnStart`. A key the options
  do not know, a value that does not parse, or a sink section that routes more than one way fails at
  registration, naming the key, instead of being ignored. So that code can override a section, the last of
  `UseTopics`, `UseQueues` and `UseQueue` called now decides how the sink routes; before, topics beat
  several queues and several queues beat one, whatever the order. The package now references
  `Microsoft.Extensions.Configuration.Abstractions`. See
  [Settings from configuration](docs/transports.md#settings-from-configuration).
- Events are named by convention. An event without `[DomainEventName]` is stored, and a contract without
  a name in `[IntegrationEvent]` is published, under its module and its class name in kebab case:
  `OrderPlaced` in `[assembly: Module("Ordering")]` is `ordering.order-placed`. A class name that ends in
  `V` and a number carries the version, so `OrderPlacedV2` is `ordering.order-placed` version 2 and every
  version of an event shares one name. `[IntegrationEvent(Version = n)]` states a version explicitly and
  wins over the suffix. The rule is one source file compiled into both the runtime and the generators, so
  `DomainEventName.Of`, `IntegrationEventContract.NameOf` and `VersionOf` and the generated
  `Add{Module}IntegrationEvents()` cannot disagree. See [Stable names](docs/domain-events.md#stable-names).
- `{Module}EventNames`, a constant for every name a project's events are stored or published under,
  written by the core generator: `OrderingEventNames.OrderPlaced` is `"ordering.order-placed"`. A contracts
  assembly's class is `[ModuleContract]`, so other modules can bind to its names.
- Four diagnostics about event names, all reported where the module compiles.
  [DDD00034](docs/diagnostics.md#ddd00034) (warning): the class name's version and `Version` disagree, so
  the suffix is ignored, with code fixes that rename the class to the stated version or remove `Version`. [DDD00035](docs/diagnostics.md#ddd00035) (error):
  a class name that ends in `V0` or `V01`. [DDD00036](docs/diagnostics.md#ddd00036) (error): two domain events, or two
  contracts, of one module under one name and version, typically two classes of one name in different
  namespaces, with a code fix that pins another name on one of them, such as
  `[DomainEventName("ordering.returns-order-placed")]`. [DDD00037](docs/diagnostics.md#ddd00037)
  (error): two names that would share a constant, which code could then use for the wrong event.
- Broker exchanges named after the event rather than the CLR type. `rabbit.UseIntegrationEventNames()` for
  MassTransit and `conventions.UseIntegrationEventNames()` for Wolverine's conventional routing name a
  contract's exchange `ordering.order-placed.v1`, from the new `IntegrationEventContract.EntityNameOf`, so
  renaming or moving a contract class no longer moves its messages. Message types the toolkit does not
  name keep the transport's names. The MassTransit and Wolverine samples use it. See
  [Transports](docs/transports.md).

- A documentation site, [dylansnel.github.io/DDDToolkit](https://dylansnel.github.io/DDDToolkit/): the
  `docs/` folder rendered by Docusaurus from `website/`, with a sidebar, a landing page and links
  to the examples on GitHub. The Docs workflow builds it on every pull request that touches the docs,
  failing on a broken link, and publishes it to GitHub Pages from `main`.
- [What the generator writes](docs/generated-code.md): the generated code for one small aggregate,
  file by file, and why it is generated rather than written. The site's homepage shows the same
  output, compiled from `website/sample` rather than typed out.
- [FluentValidation](docs/fluent-validation.md), a page of its own instead of a section at the end of Value
  objects: a value object's rules as a validator, which types get one, the two shapes its failures come
  in, `MustBeValid()` in a request validator with the failures it reports, one list for a whole request,
  and what it does not do. Getting started and the sidebar point to it.
- Diagrams in the documentation, each with a "Show the code" section under it holding the registration
  or setup it shows: delivering domain events in process and through the outbox, one message from
  one module's save to another's inbox, the example shop's checkout across its modules, the roads a
  message can take between modules, the two stages of an invariant, a module's contracts project, an
  aggregate's boundary and what the generators write. And for how modules refer to each other: what a
  module keeps and what its contract publishes, with the two references the analyzer refuses drawn in
  red; the example shop's modules and the contracts between them; a domain event becoming a contract.
  Also the order's states, the always-valid twin, one set of modules under two kinds of host, three
  modules composing one GraphQL `Product`, and a migration's way from `dotnet ef` to Supabase. They are
  Mermaid, so GitHub draws the same diagrams in `docs/`.
- The Supabase monolith through Supabase Queues. With `Messaging=pgmq` the example host sends every
  module's messages to one pgmq queue and reads it back into the modules, with no module sink in between;
  a hand-written migration, `enable_queues`, turns the extension on. It runs on pgmq 1.5.1, the version
  Supabase ships, which has no topic routing (that came in 1.11), and the Supabase Live workflow plays the
  scenarios against a real project both in process and through its queue. The AppHost's container is now
  Postgres 17 with that same pgmq. [Transports](docs/transports.md#when-a-module-becomes-its-own-deployable-pgmq)
  lists what each pgmq version supports.
- `DDDToolkit.HotChocolate.Fusion.InMemory`, one GraphQL schema over a modular monolith. Every module
  serves a source schema of its own and a HotChocolate Fusion gateway inside the application composes
  them and calls them in memory: `services.AddInMemoryFusionGateway()` and `app.MapInMemoryFusionGateway()`.
  It gives the gateway a service container of its own, because HotChocolate's `AddInMemorySchema` cannot
  serve a gateway over HTTP next to more than one source schema, and fails the application's start with
  the composer's error instead of hanging when the schemas cannot be composed. Needs HotChocolate Fusion
  16.6.6 or later, which the package declares. See
  [One schema over a modular monolith](docs/graphql.md#one-schema-over-a-modular-monolith).
- `DDDToolkit.Messaging.MassTransit`, MassTransit 8 as the transport between one process's outbox and
  another's inbox, used the way MassTransit is used. `outbox.SendToMassTransit()` publishes each contract
  as a message type of its own, with the outbox's message id as MassTransit's and the toolkit's headers
  alongside, so MassTransit gives it the exchange of its type;
  `bus.AddIntegrationEventConsumers(services.IntegrationEventSubscriptions())` registers an
  `IntegrationEventConsumer<TContract>` for every contract the modules handle and the process does not
  publish itself, and `endpoint.ConfigureConsumers(context)` binds the service's queue to exactly those.
  Built on MassTransit 8, the last version under the Apache 2.0 licence.
- `Examples/Microservices.MassTransit` runs the same three services over RabbitMQ with MassTransit, each on
  a SQL Server database of its own.
- Value objects are `@shareable` in a Fusion source schema. A value object has no owner, so every service
  that returns one answers for it alike, and composition refused a second service returning a `Money`
  until each field said so. `AddDDDToolkitTypes()` now marks every value object type `@shareable` when the
  schema declares itself a source schema with HotChocolate's `AddSourceSchemaDefaults()`, and leaves
  other schemas alone. See [Value objects in a Fusion source schema](docs/graphql.md#value-objects-in-a-fusion-source-schema).
- The microservices samples answer GraphQL through a Fusion gateway: every service serves its modules'
  source schema, Payments and Fulfilment add `payment` and `shipment` to Ordering's `Order` through a
  stub keyed on its node id, and the AppHost composes the gateway's schema with
  `HotChocolate.Fusion.Aspire` before the gateway starts. The query the monoliths answer,
  `order { lines { product { name } } payment { status } shipment { destination } }`, and `node(id:)` now
  pass against all three samples. Every sample has a gateway of its own.
- The monoliths compose their GraphQL with Fusion too, inside the process. Every module serves a source
  schema of its own and declares its own part of the types others own (Ordering's `Product` by SKU,
  Payments' and Shipping's `Order` by id), and a Fusion gateway in the monolith composes the five at
  start-up and calls them in memory, with no HTTP, through `DDDToolkit.HotChocolate.Fusion.InMemory`. The
  joins the examples used to make by hand are gone; a module's GraphQL is the same code as a service and in the monolith. A module
  registers its source schema in `Add{Module}Module` when the host serves GraphQL
  (`ModuleHost.WithGraphQL`), and the gateway composes whatever the modules registered. Inventory
  contributes a product's stock to `Product`, keyed on the SKU, in the monoliths and the services alike.
  `Tests/Spikes/DDDToolkit.Spikes.FusionInProcess` shows why the gateway has a service container of its
  own in the application, and two traps on the way.
- A Supabase Live workflow runs the Supabase monolith against a real Supabase project: the exported
  `supabase/migrations` go on with `supabase db push`, then the checkout scenarios run against the
  project. With `SUPABASE_BRANCHING=true` every run gets a preview branch of its own, which needs a Pro
  organisation; otherwise it resets the example's schemas on a project kept for these tests.
- `DDDToolkit.Messaging.Wolverine`, Wolverine as the transport between one process's outbox and another's
  inbox, used the way Wolverine is used. `outbox.SendToWolverine()` publishes each contract as a message
  type of its own, the toolkit's headers alongside, routed by Wolverine's rules: with RabbitMQ's
  conventional routing, an exchange per contract type and a queue per handling service.
  `wolverine.ReceiveIntegrationEvents(services.IntegrationEventSubscriptions())` adds an
  `IntegrationEventHandler<TContract>` to Wolverine's discovery for every contract the modules handle and
  the process does not publish itself, with retries and Wolverine's error queue. Wolverine's own outbox,
  inbox and sagas stay out of it.
- The receiving end of a transport. `IntegrationEventReceiver` hands a message that arrived from another
  process to the modules in this one, each handler inside its module's inbox, as the module sink does for
  a message from next door; `AddModuleIntegrationEvents` registers it. `IntegrationEventHeaders` is the
  envelope on the wire, the headers every transport writes and every consumer rebuilds the message from.
  In `DDDToolkit.Messaging.Postgres`, `PgmqConsumer` (`services.AddPgmqConsumer(dataSource, queue)`)
  reads a queue into the receiver, archives what was applied, lets a failure come back after the
  visibility timeout and archives a message as poison after `MaxDeliveries`. Between services it uses
  pgmq's own topic routing (pgmq 1.11 and later): `pgmq.UseTopics()` sends under the contract's published
  name with `pgmq.send_topic`, and `consumer.BindTopics = true` binds the service's queue at start-up to
  every contract its modules handle and another service publishes, so no sender names a receiver.
  `PgmqSinkOptions.UseQueues(...)` still enqueues on named queues for a pgmq without topics. The tests
  and the sample run on pgmq 1.13.
- Relay node ids for identifiers. HotChocolate's global object identification needs an
  `INodeIdValueSerializer` for a type it has never seen, so `ImplementsNode().IdField(order => order.Id)`
  over an `OrderId` failed with *No serializer registered*. Every identifier over a `Guid`, `string`,
  `int`, `long` or `short` now gets a nested `NodeIdValueSerializer`, derived from HotChocolate's
  `CompositeNodeIdValueSerializer<T>`, and `Add{Module}GraphQlRuntimeBindings()` registers it. The node
  id is the one HotChocolate writes for the bare value, so any HotChocolate server or Fusion gateway
  reads it, `node(id:)` finds the entity, and `[ID<Order>] OrderId id` arrives as the `OrderId`.
  DDD00032 warns against `AddNodeIdValueSerializerFrom<OrderId>()`, whose HotChocolate-generated
  serializer cannot see the toolkit-generated `Value` and stores nothing. See
  [Relay node ids](docs/graphql.md#relay-node-ids).
- `DDDToolkit.EntityFramework.Supabase`, a new package that depends on nothing but Entity
  Framework's relational layer. Its `SupabaseMigrations` exports Entity Framework migrations as
  files in `supabase/migrations`, one per migration and named after it. `supabase db push`,
  `db reset` and branching then apply the same changes `dotnet ef database update` would. The files
  have no transaction statements of their own, keep the `__EFMigrationsHistory` insert and turn on
  row level security for new tables in `public`. `Export` writes only missing files and never
  rewrites one. `EnsureInSync` fails a test when a migration was not exported, when an exported file
  was changed, or when a file's migration was removed. Several contexts can export into one
  directory, and `FindDirectory()` finds `supabase/migrations` the way the CLI finds its project.
  The export is part of the build: mark a module's design-time factory `[SupabaseMigrations]`, set
  `<SupabaseMigrationsExport>` to `Write` or `Check` in the host, and its build writes, or only checks,
  the files of every marked factory it references. A source generator finds the factories at compile
  time and a module initializer runs the export before `Main`, so neither a command nor the
  application's own start-up is involved, and nothing is found by reflection. Files are named
  `{migration}.{module}.ddd.sql` after the assembly's `[assembly: Module]`. DDD00031 reports a marked
  factory the build cannot create. `services.AddSupabaseMigrations<TContext, TFactory>()` plus
  `app.Services.EnsureSupabaseMigrationsAppliedAsync()` refuse to start the application while any
  module has a migration missing. See [Entity Framework → Supabase](docs/supabase.md).
- Registration a module can own. `AddDDDToolkitEntityFramework` may be called any number of times and
  every call configures the same options, so each module registers its own part next to its own
  context. `options.UseOutbox<TContext>(...)` gives one context an outbox of its own, next to the
  shared `UseOutbox(...)`, and `options.OutboxFor(type)` says which one a context gets. See
  [An outbox per context](docs/event-delivery.md#an-outbox-per-context).
- `Examples/ModularMonolith` is registered the way a modular monolith should be: `AddOrderingModule`
  and `AddShippingModule` register each module's context, outbox, consumers and migrations, and the
  host only switches them on. It also runs on a local Supabase as well as on SQLite, each module in a
  schema of its own with its own migration history, written into one `supabase/` project by the
  host's build and checked by CI.
- The examples' modules moved to `Examples/Modules`, apart from any host, and the modular monolith's
  host and its `supabase/` project to `Examples/ModularMonolith.Supabase`. More hosts over the same
  modules follow.
- The example is now a shop in five modules: Catalog, Ordering, Inventory, Payments and Shipping.
  An order is confirmed once Inventory has reserved the stock and Payments has taken the money, and
  cancelled, with the other modules undoing their part, when either says no. It shows domain services
  (`OrderPricer`, `StockAllocator`), an anti-corruption layer in front of a payment provider, a read
  model of another module's prices, a shared kernel (`Money`), policies that tolerate messages arriving
  out of order, and every module with an outbox and an inbox in its own schema of one Supabase
  database. Each module is laid out as `Domain/Aggregates/<Aggregate>/{Entities,Events,Invariants}`,
  `Domain/ValueObjects`, `Domain/Services`, `Application`, `Infrastructure` and `Api`, and maps its
  own endpoints. `Order.AddLine` is gone: an order being paid for cannot change its lines; it can be
  cancelled instead.
- The example shop runs under Aspire, on Supabase's Postgres and on SQL Server. A host hands each module
  a `ModuleHost`: the database (`ModuleDatabase.Sqlite`, `.Supabase`, `.Postgres`, `.SqlServer`) and
  where its messages go. `Examples/ModularMonolith.SqlServer` runs the same modules on SQL Server, with
  the SQL Server migrations in an assembly of their own. The Supabase AppHost seeds a Postgres container
  from `supabase/migrations`, or points at a live project. `Tests/DDDToolkit.Examples.AppHost.Tests`
  plays the same checkout scenarios against every sample, containers included, in a new Sample Tests
  workflow; Build and Test and the release leave them out with `Category!=Samples`.
- The example shop serves one GraphQL schema over its five modules, next to REST:
  `order { lines { product { name } } payment { status } shipment { destination } }` in one query.
  Each module publishes its part and a lookup in `Api/GraphQL`; the fields that cross modules are
  composed in `Shared/DDDToolkit.Examples.GraphQL`, so no module references another. Every entity is a
  Relay node with the toolkit's identifier as its id, rules come back as errors with their codes, and
  `orderConfirmed`/`orderCancelled` subscriptions are fed by the outbox.
- `Examples/Microservices.Wolverine` runs the same three services over RabbitMQ with Wolverine, each on a
  database of its own: Storefront on SQL Server, Payments and Fulfilment on Postgres.
- `Examples/Microservices.Pgmq` runs the shop as three services (storefront, payments, fulfilment) behind
  a YARP gateway, talking through pgmq queues in the one Postgres they share. The same checkout scenarios
  pass against it as against the monoliths. Every microservices sample has a project per service, each
  referencing only the modules it runs and saying itself what it sends and consumes; the gateway's routes
  are in its `appsettings.json`. Each module's SQL Server migrations are a project next to the module, so a
  service on SQL Server loads only its own.
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

- `DDDToolkit.Localization`, a new package that phrases failures in the reader's language. An
  `IFailureLocalizer`, registered with `AddDDDToolkitLocalization()`, looks a failure up by its code in
  your resx (or any `IStringLocalizer`) for the current UI culture and fills named placeholders such as
  `{MaxLength}` from its arguments; an invariant violation is looked up as `{EntityType}.{Code}` before
  the bare code. A failure nobody translated keeps its own message. `Localized(localizer)` and
  `ToErrorDictionary(localizer)` do a whole list. The toolkit's own two messages ship in English and
  Dutch. Calls to `AddDDDToolkitLocalization()` add up, so each module can register its own resx.
  `IFailureLocalizer` and the list extensions live in the core, so integrations need not reference the
  package. See [Localization](docs/localization.md).
- `ValidationError.Arguments` and `InvariantViolation.Arguments`: the values a message was built from,
  by name, so it can be phrased again. `With(name, value)` adds one; `ValidationErrorBuilder.Add` takes
  them too. Both records compare arguments by content.
- FluentValidation's placeholder values (`MaxLength`, `ComparisonValue`, ...) arrive in `Arguments`,
  from the generated validators and from `ToValidationError()` alike. `MustBeValid()` and the
  `Unspecified` failure name the value object in a `ValueObject` argument.
- `FailureTranslations` in `DDDToolkit.Localization`: checks that every failure is translated in every
  language you support, and throws a report listing what is missing, what falls back to another
  language, and where a neutral override hides the toolkit's own translation. It finds every
  `IInvariant` in the assemblies you name; other codes are named explicitly. See
  [Localization](docs/localization.md#checking-that-everything-is-translated).
- `AddDDDToolkitErrors()` in `DDDToolkit.HotChocolate`: an error filter that turns
  `InvalidValueObjectException` and `InvariantViolationException` into one GraphQL error per failure,
  with `code`, `field` or `entity`/`entityId`, and `arguments` in the extensions, phrased by the
  `IFailureLocalizer` when one is registered. The rejected value is never sent back. See
  [GraphQL](docs/graphql.md#failures-as-graphql-errors).
- `InvariantViolationException.InvariantViolations`: the violations whole, with code, entity and
  arguments, one for one with `Violations`, so the throwing path can be translated as well as the
  asking one. A new constructor takes them.
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
- `IOutboundIntegrationEvent<TDomainEvent, TContract>`, the translation from a domain event to its
  published contract as a class, the outbound counterpart of `IIntegrationEventHandler<TContract>`, so the
  translations can live next to the aggregates they publish for rather than as lambdas in the module's
  registration. It is async and is built from the outbox processor's scope, so it can read the module's
  own context; it runs at delivery, so the docs say what it may and may not read. `IntegrationEventMap`
  gains `PublishWith<TDomainEvent, TContract>(name, version, create)`, `ConvertAsync`, `IsMapped` and
  `TryDescribeContract`. See [A class per published event](docs/integration-events.md#a-class-per-published-event).
- A module's integration event registration, generated. `DDDToolkit.EntityFramework.Analyzers` writes
  `Add{Module}IntegrationEvents()` on the outbox (every domain event under its stored name and version,
  every outbound class with its contract's name and version), on the contract registry (every contract
  the module's handlers read) and on `ModuleIntegrationEvents<TContext>` (every handler, under its
  consumer name and its contract's name). Every name and version is read by the compiler and written out
  as a literal, and every class is built with `new`, so nothing is scanned, read or activated by
  reflection at run time; DDD00033 reports a class it cannot construct. The registries gain the explicit
  overloads it calls: `DomainEventTypeRegistry.Register<TEvent>(name, version)` and `TryDescribe`,
  `OutboxOptions.RegisterEvent<TEvent>(name, version)`, `IntegrationEventContractRegistry.Register<TContract>(name, version)`
  and `ModuleIntegrationEvents.Handle<TContract, THandler>(contract, consumer, create)`, and the outbox and
  the processor now look names and versions up in the registries before asking a type's attributes. See
  [Registered when the module compiles](docs/integration-events.md#registered-when-the-module-compiles).
- `IntegrationEventSubscriptions` (`services.IntegrationEventSubscriptions()`): the contracts a process
  handles, the ones it publishes, and `FromElsewhere`, the ones other services have to send it, with
  `VisitFromElsewhere` handing each contract type over as a generic argument. The transports read it to
  subscribe in their own terms, and `IntegrationEventReceiver.ReceiveAsync<TContract>(contract, headers)`
  takes a message a broker has already deserialized.
- `outbox.SendTo<TSink>(services => ...)`, a sink built by a factory from the processor's scope rather than
  constructed by reflection; `SendToMassTransit()` and `SendToWolverine()` use it. `GraphQlSubscriptionSink`
  now pushes through a typed call the map captured when the contract was registered, and finds the entry
  by the type of the contract the message carries, instead of making a generic method by reflection per
  message; `Published` reads the contracts' attributes only when it is asked for.
- The example shop registers its integration events through the generated methods, keeps its handlers in
  `Application/<slice>/{DomainEvents,IntegrationEvents/{Inbound,Outbound}}`, and its microservices route
  natively: pgmq topics, Wolverine's conventional routing, MassTransit's topology. No service names a
  contract or another service.
- Retention for the outbox and the inbox, which never shrank by themselves.
  `services.AddDomainEventRetention<TContext>(retention => { retention.KeepOutboxFor = ...; retention.KeepInboxFor = ...; })`
  registers `DomainEventRetention<TContext>` and a background service that deletes rows older than their
  table's window every `Interval`, with `ExecuteDelete` in batches of `BatchSize`. Only delivered outbox
  rows are deleted; a row still waiting, or out of attempts, stays. An inbox row is what makes a repeat a
  repeat, so a message that comes back after its row was deleted is applied again; keep inbox rows longer
  than any redelivery can take. The example modules keep a week of outbox and a month of inbox. See
  [Keeping the tables small](docs/integration-events.md#keeping-the-tables-small).

### Changed

- **The packages are published as `Temp.DDDToolkit.*` for now.** The nuget.org account that owns the
  `DDDToolkit.*` ids cannot publish at the moment, so every package ships under its old name with
  `Temp.` in front, from a second account, through trusted publishing instead of a stored API key.
  Assembly names and namespaces are unchanged; a consumer swaps the `PackageReference` and nothing else.
  The prefix is one property, `DDDPackageIdPrefix` in `Directory.Build.props`. See
  [Migrating to 3.0](docs/migrating-to-3.md#the-package-ids).

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
- HotChocolate moves from 14.0.0-rc.1 to 16. `HotChocolate.Execution` has no stable 16 release,
  so the execution engine now comes from the `HotChocolate` package, and
  `GraphQLTypeAttribute<TSchemaType>` is constrained on `ITypeDefinition` instead of `INamedType`.
- Entity Framework Core moves from 8.0.8 to 10, FluentValidation from 11.9.2 to 12. The minimum version
  of each dependency is under *Later in this release* below.

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

**Later in this release**

- `AddPgmqSink` needs the database when the application starts, for the pgmq check above; before, it did
  not touch the database until the first send. Set `CheckExtensionOnStart = false` on the sink's options
  to start without it.
- **Breaking for events without `[DomainEventName]`:** such an event used to be stored and published under
  its bare class name, `OrderPlaced`, and is now named by convention, `ordering.order-placed`, or
  `order-placed` in an assembly without `[assembly: Module]`. Rows an earlier build wrote under the class
  name are still read: the outbox registry finds a type by its class name as well, and the processor
  publishes such a row under the type's current name. What changes is the name on messages published from
  now on, so a consumer in another process that routes on the old name has to be deployed with the
  producer, or the producer's events pinned to their old names with `[DomainEventName("OrderPlaced")]`. A
  contract whose class name ends in `V` and a number, and that states no `Version`, is now that version
  instead of version 1.
- `[IntegrationEvent]` takes its name as an optional argument, and `IntegrationEventAttribute.Name` is
  `string?`. `[IntegrationEvent]` alone marks a contract named by convention.
- The example shop's events and contracts carry no names any more; the convention gives them the names
  they had, which `EventNameTests` in the examples' tests pins down.

- The documentation builds up. Each page starts with the problem it solves and the simplest use, and
  leaves storage, GraphQL, modules and design rationale for later, so a first example no longer carries
  `ColumnLength`, `[ModuleContract]` or `DDD_Module` before they mean anything. Every building block
  shows the code the generator writes for it, copied from a real build. Getting started now builds one
  module step by step, from an identifier to a second module that reacts to it. New pages split out of
  the long ones: [Module contracts](docs/module-contracts.md) (why a module publishes anything),
  [Designing aggregates](docs/aggregate-design.md), [Delivering domain events](docs/event-delivery.md),
  [Supabase](docs/supabase.md) and [Transports](docs/transports.md). [DDD00033](docs/diagnostics.md#ddd00033)
  is documented. Several statements were corrected on the way: `UseDDDToolkit` adds three interceptors,
  not two, the save calls `EnsureOwnInvariants()`, and an MVC controller binds an identifier through its
  generated `TryParse` with nothing extra.
- **Breaking for schemas that relied on it:** an entity, an aggregate or a value object bound by
  convention no longer publishes its methods as GraphQL fields, only its properties.
  `DomainBehaviourFieldsInterceptor`, registered by `AddDDDToolkitTypes()`, removes them. Before, a
  `Money.Times(int)` was published as `times(quantity: Int!)`, and a method on an aggregate that changed
  state and returned a result became a field that ran inside a query. A type declared with
  `BindFieldsExplicitly()` still publishes every method it names, and type extensions are untouched. See
  [Domain types publish their data, not their behaviour](docs/graphql.md#domain-types-publish-their-data-not-their-behaviour).

- **Breaking:** `IInvariant<T>.Check` returns `InvariantFailure?` instead of `string?`, so a rule can
  hand on the values its message names. A string converts to `InvariantFailure` and `null` still means
  the rule holds, so only the signature changes: replace `string? Check(` with
  `InvariantFailure? Check(`. The happy path still allocates nothing.
- The packages ask for the oldest dependency versions they work with instead of the newest.
  Earlier 3.0 builds declared the patch this repository was built with, so installing one moved a
  consumer's Entity Framework Core to at least 10.0.12, HotChocolate to 16.6.6 and FluentValidation to
  12.1.1. The minimums 3.0.0 declares are:

  | Dependency | Earlier 3.0 builds required | 3.0.0 requires |
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

### Deprecated

- `IntegrationEventMap.TryConvert`. It cannot run an `IOutboundIntegrationEvent`, which needs services
  and may complete later, and throws for one. Use `ConvertAsync`. `PublishAs` and `DoNotPublish` entries
  keep working through it.

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

**Later in this release**

- [Getting started](docs/getting-started.md) mapped the outbox with `modelBuilder.AddDomainEventOutbox()`,
  which does not compile: the method takes the context's `Database`. It now shows the call the example
  makes, `AddDomainEventOutbox(Database, schema: Schema)`.
- A consumer that failed inside the inbox could still have its changes saved, without its inbox row, by
  the next save on the same context. The transaction was rolled back but the change tracker was not, and
  the module sink and the receiver run every handler of a module on one context. So the next consumer's
  save wrote the failed consumer's changes, and the retry applied them a second time. A failed attempt
  now detaches everything it started tracking.
- When two copies of one message ran at the same moment, the inbox made the losing copy throw a
  `DbUpdateException` on the inbox's primary key (or on an aggregate the winner had changed), so the
  transport counted a message that had been applied as a failure and retried it. When the inbox owns the
  transaction it now looks again after the rollback and returns `false` if the other copy applied the
  message, as for any repeat. Inside a caller's transaction it still throws, because only the caller can
  roll that back.
- A positional `[ValueObject]` record, such as `record Money(decimal Amount, string Currency)`, came
  back from System.Text.Json with every property at its default. The generated properties are
  `protected init`, which the serializer cannot reach by itself, so a value object inside a domain
  event was published from the outbox empty. The generated properties are now `[JsonInclude]`.
- `ToValid()` could hand back a twin holding a different value from the one it had just validated.
  The twin started from an empty object and copied the settable properties one by one, so a get-only
  property or a private field stayed at its default without any warning, and a `protected` property
  failed to compile with CS1540. The twin is now built from the record's copy constructor, which
  copies every field. This applies to `[ValueObject]`, `[SingleValueObject<T>]` and record
  identifiers alike. An `[Internal]` property now travels with the twin as well; it still takes no
  part in equality.
- A value and its always-valid twin were equal one way only: `money == money.ToValid()` was `true`
  while `money.ToValid() == money` was `false`, and `Equals` behaved the same, which breaks the
  contract a dictionary or a `HashSet` relies on. The twin is a derived record, and the compiler
  writes the `Equals(Money?)` that answers for it, casting to `ValidMoney`; it does not allow that
  member to be replaced, so the twin cannot be made to accept a plain value. The generated `Equals`
  now also compares the runtime type, as the compiler's own record equality does, so a value and its
  twin are unequal from either side. Two plain values, or two twins, with the same components are
  still equal, and hash codes are unchanged. This applies to `[ValueObject]`,
  `[SingleValueObject<T>]` and record identifiers. Code that compared a plain value to a twin and
  relied on `true` should compare twin to twin, or compare the components.
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
- `GetInvariantViolations()` and `GetOwnInvariantViolations()` were published as GraphQL fields on
  every entity whose schema type bound its fields by convention, so any client could run an entity's
  invariant checks and the schema carried an `InvariantViolation` type nobody meant to publish. Both are
  `[Internal]` now, like the rest of the toolkit's bookkeeping.
- With `DDDToolkit.FluentValidation` referenced, a value object that wrote its own `Validate()` or
  `Validate(ValidationErrorBuilder)` failed to compile with CS0111: the generator added both overrides
  to every value object, so a project could not mix hand-validated value objects with ones validated by
  rules. The generator now leaves such a type alone, whichever part declares the method: no `Validator`,
  no `Errors`, no generated overrides. This applies to `[ValueObject]`, `[SingleValueObject<T>]` and
  record identifiers.
- A failing outbox message held back every message written after it. The processor loads pending
  messages oldest first, and a failure only incremented `Attempts`, so a failed message was loaded
  again on the next poll. When the oldest batch all failed, every poll loaded the same messages and the
  newer ones were not reached until those had used up `MaxAttempts`: about 50 seconds when failures
  were quick, hours when each attempt waited out a timeout. A busy drain also retried a failing message
  in every batch, so a sink outage of a few seconds could use up all ten attempts. A failed message now
  waits before it is tried again: the outbox row records when in a new `NextAttemptAt`, the processor
  loads only messages that are due, and the wait grows with every failure, from 5 seconds to 10
  minutes. `OutboxOptions.RetryDelay` sets the schedule. See
  [Failures, retries and poison messages](docs/event-delivery.md#failures-retries-and-poison-messages);
  a database from an earlier 3.0 build needs
  [the column](docs/migrating-to-3.md#the-outbox-nextattemptat-column).
- With `DeliverInTransaction`, a failed delivery could lose its attempt. A sink that saved through the
  outbox's own context, such as a module consumer in the same database, saved the incremented
  `Attempts` inside the transaction, and the rollback that followed the failure took it back. `Attempts`
  stayed at 0, so the retry wait never grew and `MaxAttempts` never stopped the message. The count is
  now written again after the rollback.

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
