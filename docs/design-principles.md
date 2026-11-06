---
title: Design principles
sidebar_label: Design principles
---

# Design principles

This page lists the principles the toolkit's packages follow. Tenancy and Membership are built on them, and so is
every new supporting domain. Read it before you design a package of your own, and again when you review one. Each
principle links to the page that shows it at work.

A principle here is a rule with a reason. When a design breaks one, the reason says what goes wrong.

## What the application writes

- **The application writes as little as possible.** A package asks only for what it reads to decide something.
  Labels, kinds and display names that the package never reads are the application's own fields, on its own classes.
  A seat's job title is a property of the application's seat class, not an argument of the package.
- **Defaults with an opt-out.** A package works with its defaults. A setting changes a default, and nothing must be
  configured before the first run. One collector call registers everything, and each part stays callable on its own,
  as with `UseDDDToolkit` and `UseDDDToolkitCore` ([Setting up a context](entity-framework.md#setting-up-a-context)).
- **Nothing is generated without a visible opt-in.** Generated code starts from something the developer wrote: an
  attribute, a package reference or an MSBuild property. A project name carries no meaning. A project called
  `*.Contracts` is no contracts project unless it says so.
- **The application's declaration wins.** Where the application and a package both say something, the application's
  word counts. A name the application gives a GraphQL type replaces the package's display name
  ([Display names of the packages' classes](graphql.md#display-names-of-the-packages-classes)).
- **The application owns the ids, the classes and the database.** It declares each id with `[EntityId<T>]` over any
  value type, and each of the package's aggregates as a class of its own. The package brings no migrations and no
  `DbContext` ([Writing your own supporting domain](writing-a-supporting-domain.md)).

## How a package plugs in

- **A supporting domain plugs in like any module.** The application's context stays a plain `DbContext`. The package
  adds one call in `OnModelCreating`, such as `modelBuilder.AddTenancy()`, shaped like the calls of every module.
  There is no base class and no attribute of the package's own on the context.
- **A new mechanism is for every module.** When a package needs something the toolkit lacks, the toolkit gains it for
  all modules. A feature that only one package uses splits the toolkit in two.
- **The core knows no supporting domain.** The markers a domain class carries for a package, such as `[OfTenant]` and
  `[At]`, live in a small `DDDToolkit.Supporting.<Domain>.Abstractions` package. A domain project takes that package
  and nothing else of the domain. General hooks, such as `[ModelConvention]`, stay in the core.
- **A package forces no dependency on the application.** A package references no mediator, no transport and no
  provider beyond what its own job needs. Its events are domain events, and the application decides how they are
  delivered ([Delivery](domain-events.md#delivery)).
- **App code writes no type arguments.** A package's generic classes are closed over the application's classes by the
  generator. The application writes `SeatManager`, not a manager with nine type arguments
  ([A class closed over your classes](writing-a-supporting-domain.md#a-class-closed-over-your-classes)).

## The domain

- **Aggregates reference each other by id.** A package's aggregate holds the id of another aggregate, never the
  aggregate itself ([Reference other aggregates by id](entities-and-aggregates.md#reference-other-aggregates-by-id)).
- **A class inside an aggregate is an entity, with the identity it has there.** Tenancy's grant is known by its seat,
  its unit and its role, and Membership's held role by its member and its role. Each implements `IEntity` without an
  id of its own. A class gets an id of its own only when something needs one, such as two periods of the same role.
- **Who may change what stays out of the domain.** A permission, a policy and a check live in the application layer
  and the infrastructure. A fact about the domain may be marked on the domain. `[Owner]` says who owns an aggregate,
  and `[PersonalData]` says that a member is about a person.
- **Whatever happens is a domain event.** A package raises an event for every change that someone may want to react
  to or keep. Keeping the events is the application's choice. Tenancy's access history is an event log that the
  application turns on ([Access history](tenancy/operators-and-history.md#access-history)). The application writes its
  own integration events.
- **Handlers return the aggregate.** A command returns the aggregate it changed, and a query returns the aggregates it
  read. The API layer picks the fields that leave the process. An application that wants smaller objects makes them
  itself.

## Access

- **Every request states what it requires.** The requirement comes from a small vocabulary: `AllowAnonymous()`,
  `SignedIn()`, `InTenant()`, a permission for the whole tenant or at a unit, or a permission on a resource with
  members. A request without a requirement is refused, so a forgotten requirement opens no door
  ([What a request declares](access-requirements.md#what-a-request-declares)).
- **The permission sits on the marker.** `[OnResource(permission)]` and `[AtUnit(permission)]` name the permission
  where the id is. Several markers on one request must all hold
  ([Where the permission is held](access-requirements.md#where-the-permission-is-held)).
- **Who may send a request is separate from the rights it runs with.** Trusted code begins system work for a scope,
  and the row access rules still hold that work. Work that bypasses them says so in its name,
  `BeginSystemBypassingRowAccess()` ([System work](access-requirements.md#system-work)).
- **Row level security is an extra layer.** The checks run in C#. On Postgres the policies refuse the same rows again,
  and a list narrows to what the caller may read through those policies. A policy is best set-shaped. It asks for the
  set of ids once, instead of calling a function per row ([Row level security](row-level-security.md)).
- **Access is asked when it is needed, and never copied.** A module asks the package what the caller holds, at the
  moment of the check. It keeps no copy of the access data. A copy of names that the back end needs is fed by
  integration events.
- **A package keeps its complex rules inside.** Rules such as who may give a role that manages access stay in the
  package's managers and in its policies. The application sees a refusal with a code.
- **The package guards its own data, and offers the rest as settings.** A package keeps its own data sound. What users
  may see is a product choice, so the package offers a default and a setting, not a fixed rule. The package does not
  try to protect itself from the developer.

## Names and the API

- **Use names a .NET developer already knows.** Tenancy's managers follow ASP.NET Identity: `SeatManager.AddAsync`,
  `RoleManager.RenameAsync`. Requests are named for what they do: `ListBoardQuery`, `EditBoardCommand`.
- **The toolkit shapes the classes it ships.** In GraphQL the toolkit names the packages' generic classes, leaves out
  their methods, and hides members marked `[PersonalData]` or `[Bookkeeping]`. The application shapes its own classes,
  and binds them by convention with opt-outs
  ([Binding fields by convention](graphql.md#binding-fields-by-convention)).
- **Ids are made in code.** Every id is made before the save, never by the database. Every id has a static `Create()`
  ([Creating identifiers](identifiers.md#creating-identifiers)).
- **A diagnostic fires where the code fails.** An analyzer reports the call that would fail. It never reports a route
  that the toolkit allows ([Diagnostics](diagnostics.md)).
- **A module has layers, and folders by feature.** A module is split into projects per layer, such as domain,
  application, infrastructure and API. Inside a project, a folder is one feature first and the kind of file second
  ([A module in layers](modules.md#a-module-in-layers)).

## Writing about it

- **One fact per sentence.** A sentence says the mechanism, not how it feels. A heading names the topic.
- **A diagram uses colour for meaning.** Strong, good, muted and bad each have one colour across the docs.
- **A tutorial explains the concept before the code.** No concept first appears inside a snippet.
