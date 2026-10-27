---
title: "Membership: from the check to the save"
sidebar_label: Checks and saves
---

# Membership: from the check to the save

Read this page when you write commands, lists, or work that no person does. It covers how a request on a resource is
checked, what stops a change between the check and the save, access inside your own queries, system work, and the
expert hold.

## Requirements that combine

Membership adds two requirements to [the vocabulary](../access-requirements.md#requirements-and-their-attributes):

| On the request | The call | For |
|---|---|---|
| `[RequiresPermission(p)]`, with `[OnResource]` on the resource and `[ExpectedVersion]` on the version read | `MemberAccess.On(p, resource, expectedVersion)` | a request on one resource |
| `[SeenWith<TResourceId>(p)]` | `MemberAccess.SeenWith<TResourceId>(p)` | a list ([Access in your own queries](#access-in-your-own-queries)) |

Both [combine](../access-requirements.md#requirements-that-combine) with the other requirements. Each package
registers its check for the same request interface, and each check decides its own cases.

**The caller's own, or the permission.** Put `[OrOwn]` beside `[OnResource]`. A member can then leave a document,
and anybody with the permission can take any member off. With `[RequiresPermission(DocumentPermissions.Share)]`, the
request `LeaveDocument([OnResource] DocumentId Document, [OrOwn] UserId Member)` gets this requirement:
`Any(All(Own(request.Member), MemberAccess.Sees(DocumentPermissions.Share, request.Document)), MemberAccess.On(DocumentPermissions.Share, request.Document))`
([Yours, or the permission](../access-requirements.md#the-callers-own-or-a-permission)).

The diagram shows the two ways through when Leo sends `LeaveDocument`. Purple: the request. Green: let through. Red:
refused.

```mermaid
flowchart TD
    req["<b>LeaveDocument</b><br/><small>Member = Leo</small>"]
    own["<b>Leo is the caller</b><br/><small>and sees the document</small>"]
    held["<b>The caller holds documents.share</b><br/><small>on the document</small>"]
    ok["<b>Let through</b><br/><small>the handler runs</small>"]
    no["<b>Refused</b><br/><small>not-found or not-permitted</small>"]
    req --> own
    own -->|yes| ok
    own -->|no| held
    held -->|yes| ok
    held -->|no| no
    classDef strong fill:#5b2ec933,stroke:#5b2ec9,stroke-width:2px
    classDef good fill:#1f8a5b26,stroke:#1f8a5b
    classDef bad fill:#c93c3c24,stroke:#c93c3c
    class req strong
    class ok good
    class no bad
```

- `MemberAccess.Sees` requires that the caller sees the document, whether or not the caller holds the permission.
  Like `On`, it refuses with `documents.not-found`, compares the version and keeps the hold.
- Where a permission is marked `LocksMembers`, the database lets only callers with that permission write member
  rows. A member without it cannot remove its own row. So the handler saves that change as the application's own
  work ([The caller's own on a resource](../access-requirements.md#the-callers-own-on-a-resource)).

**Membership and Tenancy together.** Moving a project requires edit on the project and open at the unit it moves to:
`All(MemberAccess.On(ProjectPermissionNames.Edit, Project, ExpectedVersion), TenancyAccess.AtUnit(ProjectPermissionNames.Open, To))`.

**Combined requirements and the expert hold.** The [expert hold](#the-expert-hold) keeps one hold for each case that
let the caller through, once the full combination let the caller through. `All(MemberAccess.On(p, From),
MemberAccess.On(p, To))` keeps one hold per document.

## From the check to the save

Other requests can run between the check and the save. On the default path, five steps stop a change that is no
longer allowed:

| Step | What it checks | Refused with |
|---|---|---|
| 1. The request's check | The caller holds the permission. Then, the version the caller named is the current one. | `not-found`, `not-permitted`, 409 |
| 2. The load by id, then `db.ExpectVersion(document, command.ExpectedVersion)` | The checked resource still exists at the version named. With no version named, any version passes. | 404 when the resource is gone, 409 |
| 3. The save | The resource is still at the version the handler loaded. | 409 |
| 4. The aggregate's rules | Rules that apply at any version, such as "an archived document takes no member" and "the owner stays". | your codes |
| 5. The database, which checks every row as the caller ([On Postgres](postgres.md)) | The write, for a caller who lost every permission that writes the resource. | 403 `access.refused`. A resource that is hidden by then is a 404 at the load. |

The permission is about who acts. The version is about what the caller read. A rule about how, and until when, the
caller holds the permission goes [in the handler](permissions-and-roles.md#who-may-give-a-role).

The Tenancy sample's tests (`AccessHoldScenarios`) rename a project after a change between the check and the
handler. They run on Postgres, with the policies forced:

| Between the check and the handler | The rename |
|---|---|
| The role that gave the permission is taken away. | 403 `access.refused`, from the database |
| The reach from above is revoked, or the project moves out of reach. | 404, because the load finds nothing |
| Somebody else renames the project, and the caller named its version. | 409, at the load |
| Somebody else renames the project, and the caller named no version. | Saved. The last write wins. |
| The project moves to a place where the caller still holds the permission. | Saved |

The default path has three limits:

- **It checks nothing finer than the policies.** A policy that lets any writing permission through also lets
  through a caller who lost the command's permission and kept another one. Where that matters, write the policy per
  permission.
- **When a handler is reached around its checks, only the database checks the write**, against what the caller may
  do. Add a test that proves your request sender runs the checks. No row rule restricts a save that runs as the
  application's own work. So begin that work only for a request whose check passed. `RequestInHand.Current.Request` is that
  request ([The request in hand](../access-requirements.md#the-request-in-hand)). The sample's `OwnPlaceOnTheCrew`
  does this.
- **The save is not tied to the version the check read.** For a caller who named no version, the last write wins.
  [The expert hold](#the-expert-hold) adds that tie.

## Access in your own queries

[`IMemberQuestions<DocumentId>`](reference/use-cases.md#membershipaccessmanagertresource) has methods about one resource
(`HoldAsync`, `RequireAsync`, `ViaAsync`) and a method about up to 200 resources (`PermissionsOnAsync`). Each method
runs one statement. For your own queries, `Reach(permission)` returns a **reach**. You put the reach into your query,
so a list runs as one statement and fetches no ids first ([tutorial step 6](tutorial.md#step-6-open-a-board-see-it-list-them)).
Two more forms:

```csharp
// Your own columns next to how a permission is held and until when
var seen = questions.Reach("documents.view");                    // IMemberQuestions<DocumentId>
var act = questions.Reach("documents.edit");
var answer = await db.Documents.Where(document => document.Id == id)
    .Within(seen)
    .Reached(act)
    .Select(found => new { found.Resource.Archived, found.AsMember, found.FromAbove, found.Until })
    .FirstOrDefaultAsync(cancellationToken);
var via = answer is null ? null : act.Via(answer.AsMember, answer.FromAbove);

// What the caller may do with each row of a page
var permissions = await db.Documents.Where(document => ids.Contains(document.Id))
    .PermissionsOn(questions.PermissionReach(["documents.edit", "documents.share"]))
    .ToListAsync(cancellationToken);
```

**A list declares `[SeenWith<DocumentId>(permission)]`**, because the checks
[fail closed](../access-requirements.md#what-answers-it):

- `[SeenWith]` refuses nobody for lacking the permission. The reach in the query is the filter. So `[SeenWith]` goes
  on queries only. On a Mediator command or a plain `IRequest` it is [DDD00082](../diagnostics.md#ddd00082). With
  another dispatcher, a [test](../access-requirements.md#testing-the-requirements) checks it.
- A caller who did not sign in reaches nothing. The check refuses that caller with `documents.not-permitted`, and no
  statement is sent.
- With Tenancy, the check first refuses a caller without an active seat in the tenant, with Tenancy's code:
  `tenancy.tenant-required` when the caller names no tenant, `tenancy.not-seated` or `tenancy.seat-suspended`. A
  list is refused the same way as a command.

**Which context the questions run on:**

- With a registered context factory (for example a pool with `AddScopedFromPool`), each question runs on a context
  of its own, because the questions of one request may run side by side. Without a factory, the questions run on the
  request's context.
- The questions run on the request's context when the resource or the custom role class has a query filter that
  reads a member of your context. A context made for one reading does not know that member's value. A filter that
  reads the ambient work, such as [Tenancy's](../tenancy/keeping-tenants-apart.md), works on every context.
- A reach is data, so it works on any context. Some reaches add a subquery of their own, over custom role rows or
  over [what you return](beside-an-organization.md#an-organization-of-your-own). On a context other than the
  request's, pass that context as well: `db.Crates.Within(reach, db)`.

## System work

- **`Caller.System`**, system work, holds every permission on every resource.
- **`Caller.SystemIn("filing")`** is nobody's member. It holds every permission where the rules let its scope
  through, and nothing elsewhere.
- **By default, the rules let the module's own scope through**, for example `filing` for the module `Filing`
  ([Names](resources-and-members.md#names)). So a seeding, or a handler that saves as the application, needs no
  setting.
- **`systemScopes` replaces the default.** Name the scopes, including the module's scope if you want to keep it, or
  pass `[]` for none. A scope has lower case letters, digits, `_` and `-`, and each scope is named once:

  ```csharp
  // The boards' own work, and the nightly import, which runs in its own scope
  public static MembershipRules Rules { get; } = MembershipRules.For<Board>(
      Roles.Custom(starters: [new("editor", [BoardPermissions.View, BoardPermissions.Edit])]),
      systemScopes: ["boards", "boards-import"]);
  ```

- **Name a scope after the work it does, and never use `tenancy`.** Every piece of work that calls Tenancy's use cases
  (provisioning, placing a seat, a grant) runs in the `tenancy` scope. With `tenancy` in `systemScopes`, all of that
  work would hold every permission on every board.
- **On Postgres**, the scoped system role may call the functions. [The lock](postgres.md#who-writes-the-member-rows)
  refuses member row writes from work in a scope the rules do not name.

## The expert hold

The **expert hold** ties every save to the check of its request. You switch it on with one line on the context:

```csharp
services.AddDbContext<FilingContext>((serviceProvider, options) => options
    .UseNpgsql(connectionString)
    .UseDDDToolkit(serviceProvider)
    .UseMemberHolds(serviceProvider));             // ties every save of a document to its request's check
```

A save that changes a resource with members, or one of its members or roles, then has these results:

| The save changes | Result |
|---|---|
| the resource the check read, at that version | saved |
| that resource, changed since the check, whether or not a version was named | 409, whatever changed |
| a resource that no check of the request read: a handler called directly, a transport that bypasses the pipeline, a save after the handling returned, or another resource than the request names | refused with `InvalidOperationException`. Nothing is saved. |
| another resource, as system work begun by trusted code (`Callers.Begin(Caller.System)`, or a scope the rules name) | saved. A resource the check read is still tied to its version. |
| a new resource | saved |

- **The hold finds the check through [the request in hand](../access-requirements.md#the-request-in-hand).**
  `AccessChecks.RequireAsync` puts the request in hand for the flow of work that called it. `MemberAccessCheck` keeps
  what it read with the request (`Checked<MemberHold<DocumentId>>`). The save looks it up there. The handler passes
  nothing.
- **The hold follows the flow of work.** One scope can handle several requests, such as GraphQL mutations, or a query
  before a command. A hold that a query's check kept lets no directly called handler save.
- **Run the checks and the handler in one `async` method.** The generated behavior does this, and so does
  [a dispatcher of your own](../access-requirements.md#asking-the-checks-without-mediator) that awaits both. A helper
  that only runs the checks has the request in hand inside the helper alone, so every later save is refused. A
  dispatcher without `async` that returns the chained task leaves the request in hand for its caller.
- **Save inside the access behavior.** Register a unit of work after `Add{Module}AccessBehavior()`. Do not register
  it in `MediatorOptions.PipelineBehaviors`, because those behaviors run first. An endpoint does not save after
  `Send`.
- **A second save** is tied to the version the first save left. A failed save counts for nothing. Retried with the
  values as loaded, it is a 409 again.
- **`Version` must be a concurrency token.** Otherwise the first held save is refused. `UseMemberHolds` goes after
  `UseDDDToolkit` or `UseDDDToolkitCore`. Otherwise the context is refused when it is built. Calling
  `UseMemberHolds` twice adds the hold once.
- **The hold runs after the domain event handlers.** A domain event handler that changes another resource with
  members is refused, unless it begins system work.
- **The hold costs no extra statement.** It adds a 409 for a change the caller's rights did not depend on, such as a
  move to a place where the caller still holds the permission. It also refuses a handler reached around its checks.
  When the hold is off, it does nothing.

The Tenancy sample ships the default path. Its tests switch the hold on through `ConfigureDbContext` and run the
races both ways (`AccessHoldScenarios`).
