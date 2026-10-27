---
title: "Membership: from the check to the save"
sidebar_label: Checks and saves
---

# Membership: from the check to the save

How a request on a resource is checked, what holds its change until the save, the questions inside your own
statements, system work, and the expert hold. Read it when you write commands, lists, or work no person does.

## Requirements that combine

Membership's two requirements, beside the rest of [the vocabulary](../access-requirements.md#the-vocabulary):

| On the request | The call | For |
|---|---|---|
| `[RequiresPermission(p)]`, `[OnResource]` on the resource, `[ExpectedVersion]` on the version read | `MemberAccess.On(p, resource, expectedVersion)` | a request on one resource |
| `[SeenWith<TResourceId>(p)]` | `MemberAccess.SeenWith<TResourceId>(p)` | a list ([below](#inside-your-own-statements)) |

They [combine](../access-requirements.md#requirements-that-combine) with the others; each package's check, registered
for the same interface, decides its own cases.

- **The caller's own, or the permission**: `[OrOwn]` beside `[OnResource]`. A member leaves on its own, or anybody
  with the permission takes it off: `LeaveDocument([OnResource] DocumentId Document, [OrOwn] UserId Member)` is
  `Any(All(Own(request.Member), MemberAccess.Sees(permission, request.Document)), MemberAccess.On(permission, request.Document))`
  ([Yours, or the permission](../access-requirements.md#yours-or-the-permission)).
  - `MemberAccess.Sees`: the caller sees the document, holding the permission or not; like `On` it refuses
    `documents.not-found`, compares the version and keeps the hold.
  - Where a permission is marked `LocksMembers`, the database refuses that caller its own member row, so the handler
    saves it as the application's own work ([The caller's own on a resource](../access-requirements.md#the-callers-own-on-a-resource)).
- **Membership's and Tenancy's together**: moving a project takes edit on the project and open at the unit it goes
  to, `All(MemberAccess.On(ProjectPermissionNames.Edit, Project, ExpectedVersion), TenancyAccess.AtUnit(ProjectPermissionNames.Open, To))`.
- **The [expert hold](#the-expert-hold)** keeps a hold once the whole combination let the caller through, for the
  cases it was let through by: `All(MemberAccess.On(p, From), MemberAccess.On(p, To))` keeps one per document.

## From the check to the save

Other requests go on between the check and the save. On the default path five things hold a change:

| Step | Holds | Refused with |
|---|---|---|
| 1. The request's check | who: the permission held, then the version the caller named | `not-found`, `not-permitted`, 409 |
| 2. The load by id, then `db.ExpectVersion(document, command.ExpectedVersion)` | the resource checked, still at the version named (none named: as it is) | 404 when gone, 409 |
| 3. The save | the version it loaded | 409 |
| 4. The aggregate's rules | the resource at any version: an archived document takes no member, the owner stays | your codes |
| 5. The database, checking every row as the caller ([On Postgres](postgres.md)) | the write, for a caller that lost every permission that writes the resource | 403 `access.refused`; hidden by then: 404 at the load |

The permission is about who, the version about what the caller read; a rule about how and until when the caller holds
it goes [in the handler](permissions-and-roles.md#who-may-give-a-role-is-yours-to-decide). A rename of the Tenancy
sample's projects, with a change between check and handler, on Postgres with the policies forced
(`AccessHoldScenarios`):

| Between the check and the handler | The rename |
|---|---|
| the role that gave the permission is taken | 403 `access.refused`, from the database |
| the reach from above is revoked, or the project moved out of reach | 404: none to load |
| somebody else renamed it; the caller named its version | 409, at the load |
| somebody else renamed it; the caller named no version | saved: the last write wins |
| the project moved where the caller still holds the permission | saved |

What the default path does not do:

- **Nothing finer than the policies.** One that lets any writing permission through lets through a caller that lost
  the command's permission and kept another. Write the policy per permission where that matters.
- **A handler reached around its checks** is held by the database alone, to what its caller may; a test holds that
  whatever sends your requests asks the checks. A save as the application's own work is held by no row rule, so begin
  it only for a request whose check passed: `RequestInHand.Current.Request` is that request
  ([The request in hand](../access-requirements.md#the-request-in-hand)), as in the sample's `OwnPlaceOnTheCrew`.
- **No tie between the save and the check's version**: for a caller that named none, the last write wins.
  [The expert hold](#the-expert-hold) ties them.

## Inside your own statements

[`IMemberQuestions<DocumentId>`](reference/use-cases.md#imemberquestionstresourceid) answers about one resource
(`HoldAsync`, `RequireAsync`, `ViaAsync`) or up to 200 (`PermissionsOnAsync`), each in one statement. For your own
queries it hands out a **reach**, which goes into the query: a list is one statement, never ids fetched first
([tutorial step 6](tutorial.md#step-6-open-a-board-see-it-list-them)). Two more forms:

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

**A list declares `[SeenWith<DocumentId>(permission)]`**, since the checks
[fail closed](../access-requirements.md#what-answers-it):

- It refuses nobody for want of the permission: the reach is the filter. So it goes on queries only: on a Mediator
  command or plain `IRequest` it is [DDD00082](../diagnostics.md#ddd00082); with another dispatcher, a
  [test](../access-requirements.md#holding-it-with-a-test) holds it.
- A caller who did not sign in reaches nothing, so it is refused, `documents.not-permitted`, and no statement is sent.
- With Tenancy it asks nothing of Tenancy: a caller who names no tenant gets an empty list, where a command is refused
  with `tenancy.tenant-required`.

**Which context the questions read on**:

- One of their own where you registered a factory (a pool with `AddScopedFromPool`), since a request's questions may
  run side by side; otherwise the request's context.
- The request's own when the resource or the custom role class has a query filter that reads a member of your
  context, which a context made for one reading knows nothing of. A filter on what is around the work, as
  [Tenancy's](../tenancy/keeping-tenants-apart.md), holds on every context.
- A reach is data for any context. One that adds a query of its own (custom role rows,
  [an answer of yours](beside-an-organization.md#an-organization-of-your-own)) is handed the context when it is not
  the request's: `db.Crates.Within(reach, db)`.

## System work

- **`Caller.System`**, the application itself, holds every permission on every resource.
- **`Caller.SystemIn("filing")`** is nobody's member: it holds every permission where the rules let its scope through,
  nothing elsewhere.
- **The rules let the module's own scope through** by default (`filing` for the module `Filing`,
  [Names](resources-and-members.md#names)), so a seeding or a handler that saves as the application needs nothing said.
- **`systemScopes`** replaces it: name the scopes, the module's own among them to keep it, or `[]` for none:

  ```csharp
  // The boards' own work, and the nightly import that fills them, which runs in a scope of its own
  public static MembershipRules Rules { get; } = MembershipRules.For<Board>(
      Roles.Custom(starters: [new("editor", [BoardPermissions.View, BoardPermissions.Edit])]),
      systemScopes: ["boards", "boards-import"]);
  ```

- **Name a scope for the work it is**; never `tenancy`. That is the scope of every piece of work that calls Tenancy's
  use cases (provisioning, placing a seat, a grant), and it would then do everything on every board.
- **On Postgres** the scoped system role may ask the functions, and [the lock](postgres.md#who-writes-the-member-rows)
  lets work in a scope the rules do not name write no member row.

## The expert hold

The **expert hold** ties every save to its request's check, with one line on the context:

```csharp
services.AddDbContext<FilingContext>((serviceProvider, options) => options
    .UseNpgsql(connectionString)
    .UseDDDToolkit(serviceProvider)
    .UseMemberHolds(serviceProvider));             // every save of a document held to its request's check
```

A save that changes a resource with members, or a member or role of one, is then held to what its request's check read:

| The save changes | Under the hold |
|---|---|
| the resource the check read, at that version | saved |
| that resource, changed since the check, version named or not | 409, whatever changed |
| a resource no check of its request read: a handler called directly, a transport around the pipeline, a save after the handling returned, another resource than the request names | refused, `InvalidOperationException`; nothing is saved |
| another resource, as the application's own work begun by trusted code (`Callers.Begin(Caller.System)`, a scope the rules name) | saved; a resource the check read stays held to its version |
| a new resource | saved |

- **It finds the check through [the request in hand](../access-requirements.md#the-request-in-hand)**:
  `AccessChecks.RequireAsync` puts the request in hand for the flow of work that asked, `MemberAccessCheck` keeps what
  it read with it (`Checked<MemberHold<DocumentId>>`), and the save looks there. The handler passes nothing.
- **The flow, not the scope**: a scope can handle several requests (GraphQL mutations, a query before a command); a
  hold a query's check kept lets no handler called directly save.
- **Ask the checks and run the handler in one `async` method**, as the generated behavior and
  [a dispatcher of your own](../access-requirements.md#asking-the-checks-without-mediator) that awaits both do. A
  helper that only asks the checks has the request in hand inside itself alone, so every later save is refused; a
  non-`async` dispatcher that returns the chained task leaves it in hand for its caller.
- **Save inside the access behavior**: register a unit of work after `Add{Module}AccessBehavior()`, not in
  `MediatorOptions.PipelineBehaviors` (those run first). An endpoint does not save after `Send`.
- **A second save** is held to the version the first left. A failed save counts for nothing: retried as loaded, it is
  a 409 again.
- **`Version` is a concurrency token**, or the first held save is refused. `UseMemberHolds` goes after `UseDDDToolkit`
  (or `UseDDDToolkitCore`), or the context is refused where it is built; twice holds once.
- **It runs after domain event handlers**: one that changes another resource with members is refused, unless it
  begins the application's own work.
- **It costs no statement**, and adds a 409 for a change the caller's rights did not depend on (a move to where the
  caller still holds the permission) and a refusal for a handler reached around its checks. Off, it does nothing.

The Tenancy sample ships the default path; its tests switch the hold on through `ConfigureDbContext` and play the
races both ways (`AccessHoldScenarios`).
