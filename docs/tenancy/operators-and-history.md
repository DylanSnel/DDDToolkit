---
title: "Tenancy: operators and history"
sidebar_label: Operators and history
---

# Tenancy: operators and history

Your own staff who look across tenants (operators), who wrote and changed each row, and the history of who changed
access. Read it when support needs every tenant, or a tenant needs to see who changed what. The words (tenant,
seat, unit, role, grant) are in the [overview](overview.md).

## Operators

An operator is one of your own staff who looks across tenants: support, or whoever provisions and suspends tenants.
It is not an administrator of every tenant: it **holds no seat** in any tenant, and **changes nothing itself**.

**Who is one**

- A signed-in user whose token carries a role listed in `TenancyOptions.OperatorTokenRoles`. The list starts empty:
  no operators until you add one.
- A token role is never both an operator's and seated: registration refuses that, and tenant selection answers
  nobody for an operator, whatever tenant the request names.

**What it reads**

- `TenantDirectory.ListAsync(after, size)`: every tenant by slug, a page at a time, each with its name, status and
  number of active seats.
- Anyone else is refused with `tenancy.operators-only`, before anything is read. A request of yours for operators
  declares `[RequiresOperator]` and is refused the same way
  ([What a request requires of its caller](who-may-do-what.md#what-a-request-requires-of-its-caller)).
- A page holds 1 to 200 tenants (`tenancy.page-size-invalid`); the next is asked with the marker the last page gave
  (`tenancy.cursor-invalid` for anything else).
- The read runs as the operator, never as the system.

**How it writes: it does not**

- Your own module records what the operator asks for; system work (work no person does, begun explicitly) that names
  the operator carries it out: `TenancyUseCases.BeginOperator(identity)` to provision a tenant,
  `TenancyUseCases.BeginOperatorIn(tenant, identity)` for work in one.
- The work holds what system work holds; the operator is only who it is [recorded as](#who-changed-a-row).
- The identity is the verified `sub` of the request that asked, from its token or a record it wrote, never from what
  a caller sends.

<details>
<summary>Show the code: listing an operator's token role, and carrying out what one asked</summary>

```csharp
// The host: which token roles are operators'
services.AddTenancy<ShopTenancyContext>(options =>
{
    // the catalogue, when you have one
    options.OperatorTokenRoles.Add("operator");
});

// A route of the operators' own: the directory decides who may ask
var page = await tenants.ListAsync(after, size: 50, cancellationToken);

// Carrying out what an operator asked for: system work in the tenant, which names the operator
using (TenancyUseCases.BeginOperatorIn(tenant, operatorIdentity))
{
    await tenantCommands.SuspendAsync("Asked for by the owner", cancellationToken);
}
```

</details>

**On Postgres** an operator reads through a database role of its own. Map the token role to one
(`PostgresRowLevelSecurityOptions.TokenRoles`, or `token:operator=<role>` in `SupabaseRowAccessRoles`), and mark the
token roles you give Tenancy, so the export writes policies for the same ones:

```csharp
public static class ShopOperators
{
    [TenancyOperators]
    public static IReadOnlyList<string> TokenRoles { get; } = ["operator"];
}

// Where Tenancy is registered
options.OperatorTokenRoles.UnionWith(ShopOperators.TokenRoles);
```

The contribution then writes, for that role:

| On | The role |
|---|---|
| Tenancy's tables, the tables of your entities on them, and the access history | reads every row, in every tenant; adds, changes and removes none |
| A module's table kept to a tenant | reads what a rule of the module for `RowAccessRoles.Token("operator")` admits, nothing without one; adds, changes and removes nothing, whatever a rule says |

- No operator token roles: nothing is written for them. A mapped token role nobody named an operator's stays
  [closed out](postgres.md#what-the-policies-check).
- The database role is the operators' alone: the export refuses an operator token role mapped to no database role, to
  the signed-in users' role, or to one another token role shares, since those policies would open every tenant to
  callers who are no operators.
- `EnsurePoliciesAreInPlaceAsync` checks at start-up that the options (the operators for your application) and the
  mark (the operators for the database) agree; with the options alone, an operator gets an empty directory.

## Who changed a row

A module can keep, on each row of an entity it keeps to a tenant, who wrote it first and who changed it last:

```csharp
modelBuilder.Entity<Project>(project =>
{
    project.ScopeToTenant(row => row.TenantId);
    project.RecordsWhoChanged<Project, SeatId>();   // in Tenancy's own module: project.RecordsWhoChanged()
});
```

That adds six shadow columns, which Tenancy's save interceptor fills from the Tenancy caller the save runs as: all six
on a new row, the last three on a changed one.

| Who acted | `…ByKind` | `…BySeat` | `…ByIdentity` |
|---|---|---|---|
| A seat | `seat` | the seat | |
| System work that names an operator (`BeginOperator`, `BeginOperatorIn`) | `operator` | | the operator's verified identity |
| System work (`BeginSystemIn`), also when it is done for a seat | `system` | | |
| System work that answers a link's token (`BeginTokenIn`) | `token` | | |

- The columns: `CreatedBySeat`, `CreatedByKind`, `CreatedByIdentity`, `ChangedBySeat`, `ChangedByKind`,
  `ChangedByIdentity` (`TenancyAttribution`). Ids, never a name: a seat's name is asked of the
  [directory](names-and-languages.md#names), by id.
- Read one with `EF.Property<SeatId?>(row, TenancyAttribution.ChangedBySeat)`.
- Who wrote a row first is fixed once the row exists.
- A row whose own columns did not change keeps who changed it last; an aggregate with a version changes its row
  whenever one of its parts changes.
- Adding the columns to an existing table is a migration of your own.
- In a hierarchy stored in one table, call `RecordsWhoChanged` on the root type, so every row keeps them. The first
  save refuses a model that calls it on a derived type alone, or without `ScopeToTenant`.

**The same answer wherever a record keeps who acted**

- Every Tenancy caller that can change something carries its actor, `TenancyCaller.Actor`.
- `AddTenancy` wraps the toolkit's accessor in its own (`TenancyActedByAccessor`), so an
  [event log](../entity-framework.md#an-event-log) says `seat`, `operator`, `system` or `token` too; every Tenancy
  domain event carries the actor itself, as [`By`](#access-history). Without a Tenancy caller, the toolkit's own
  answer stands.
- An accessor of yours registered before `AddTenancy`, as a singleton, is wrapped the same way; a scoped one is
  refused there. One registered after `AddTenancy` replaces Tenancy's, and `TenancyChecks.EnsureWired` then refuses a
  context that keeps the [access history](#access-history), before anything is saved.

> [!IMPORTANT]
> Whoever a piece of work acts for is never put into the toolkit's caller. `Caller.UserId` is always the
> verified `sub` of a token, and system work has none: an operator or a seat it acts for is named by
> `TenancyWork`, and recorded, and gives the work no rights.

**On Postgres** a trigger on each such table, before every insert and update, holds callers to themselves:

- a signed-in user writes the kind `seat`, its own seat and no operator's identity, as who changed the row and, on a
  new row, as who wrote it first;
- the scoped system role never writes the kind `seat`;
- neither changes who wrote the row first.

What a refusal looks like:

- SQLSTATE `42501`, with the trigger's name and the toolkit's hint, like every access guard the toolkit writes; a
  refused Entity Framework save is `access.refused`
  ([When the database refuses](../row-level-security.md#when-the-database-refuses)).
- A save fills every column itself, so only a statement around the model is ever refused. Such a statement of yours,
  an `ExecuteUpdate` say, sets the last three columns itself, and is refused too if it leaves another seat there. It
  is no save, so you get the `PostgresException` itself, which `DatabaseRefusal.From` reads as a guard's.
- A role that is no caller's, such as the tables' owner in a migration, is not held: it fills the columns of the rows
  that predate them.

## Access history

Tenancy's events that change access can be kept as a history of who changed what: the toolkit's
[event log](../entity-framework.md#an-event-log), with each event's tenant on its row.

```csharp
// Tenancy's context
modelBuilder.AddTenancy(database: Database);
modelBuilder.AddDomainEventOutbox(Database);
modelBuilder.AddTenancyEventLogTable(Database);   // the event log, with a TenantId column of your tenant id

// Where Tenancy is registered, the project that declares your classes or your module's infrastructure project:
// keep the events that change access
options.UseOutbox<ShopTenancyContext>(outbox => outbox
    .AddTenancyDomainEvents()
    .KeepEventLog(log => log.AddTenancyEventLog()));   // both generated there, closed over your four ids
```

`AddTenancyEventLog` keeps every event that changes who may do what, under the name the outbox stores it by:

| About | Events kept |
|---|---|
| The tenant | `tenancy.tenant-provisioned`, `tenancy.tenant-activated`, `tenancy.tenant-suspended`, `tenancy.tenant-reactivated`, `tenancy.tenant-closed`, `tenancy.tenant-shape-changed` |
| A unit | `tenancy.organization-unit-added`, `tenancy.organization-unit-moved`, `tenancy.organization-unit-archived` |
| A seat | `tenancy.seat-added`, `tenancy.seat-suspended`, `tenancy.seat-reactivated`, `tenancy.seat-deactivated`, `tenancy.seat-placed`, `tenancy.seat-withdrawn`, `tenancy.primary-placement-changed` |
| A seat's roles | `tenancy.organization-role-granted`, `tenancy.organization-role-revoked` |
| A role | `tenancy.role-created`, `tenancy.role-permissions-changed`, `tenancy.role-archived`, `tenancy.role-followed-its-pack` |

- Renames change nobody's access, so `tenancy.organization-renamed`, `tenancy.organization-unit-renamed` and
  `tenancy.role-renamed` are left out; the outbox still stores them.
- A seat has no name of Tenancy's, so no such event. The sample renames a seat by its own command, which keeps no
  history of it.
- An event of yours that belongs in the history: one more `Keep<TEvent>()` beside the call. `KeepEventLog()` with
  nothing chosen keeps every event of the context.

**Every event says who made the change**

- Each Tenancy domain event ends with `By`, the [actor](#who-changed-a-row) of the caller whose command raised it: a
  seat, an operator by verified identity, the system with its scope, or a token.
- So an event about a tenant, a unit or a role is closed over the seat id too (`TenantSuspended<TenantId, SeatId>`).
- `RolePermissionsChanged` also names the permissions that came in, `Added`, and went out, `Removed`, implied ones
  included, as `RoleFollowedItsPack` does for a role that
  [followed its pack](roles-and-permissions.md#packs-after-provisioning) (a pack is a starter role, copied into every
  new tenant).
- The use cases pass their caller's actor. An aggregate method you call yourself takes it last, as `by`; a tenant, an
  organization and a role do not know the seat id's type, so with nobody to name you name the type:
  `tenant.Activate<SeatId>()`.

**The rows**

- Every kept event gets a row in the save that raised it; a change that rolls back leaves none.
- A row names the event's tenant, taken from the event, and who acted ([as above](#who-changed-a-row)): the save's
  caller, which is the actor a use case puts on its events. An event of yours in that context that names no tenant
  gets the save's tenant.
- The payload is the event as the outbox stores it: ids, permissions, dates, who made the change, and little else,
  such as the slug a tenant was provisioned under, the pack a role was copied from, the reason a tenant was suspended
  or closed for. Never what a tenant, unit, seat or role is called, or why a role was given.

**Who reads it**

- Whoever holds **`tenancy.history.view`**, Tenancy's own permission, for the whole tenant. It manages no access, so a
  role holding it is given like any other; an administrators' pack that lists its permissions holds it, as it holds
  all of Tenancy's.
- The query filter does not keep the rows to a tenant, because the toolkit's retention reads the table whole: code
  that reads the history asks the permission and names the tenant.

**On Postgres** the table is kept to itself, like Tenancy's own. Its policies are written with Tenancy's tables, so
map the history in the context that calls `AddTenancy`; the export refuses it in a module's context, where it would
have no policy.

| Who | Reads | Adds |
|---|---|---|
| A signed-in user | its tenant's rows, with `tenancy.history.view` for the whole tenant | rows about its own seat, the calling one, in the tenant the connection names |
| System work in a tenant | its tenant's rows, in any scope | in Tenancy's own scope, which is the scope Tenancy's events are raised in, as the system, an operator or a token and never as a seat |
| An operator's role | every row | nothing |
| The role of your own bookkeeping, where the export writes privileges and the table lets rows go | every row, to find the old ones | nothing; it removes rows the table's guard lets go |

- The policies read a row's columns (tenant, who acted), never its payload. The payload, `By` included, is the
  application's word: the two agree because the use cases put their caller's actor on every event, not because the
  database compares them. Where they differ, trust the columns.
- Nobody changes a row, or removes one before its time: the log's [guard](../entity-framework.md#an-event-log)
  refuses that for every role, the tables' owner included.
- A seat that suspends or deactivates itself is still recorded, though no calling seat once its row is written: its
  seat counts in the transaction that wrote it, and in no later one.

The [sample](the-sample.md#who-may-do-what-in-the-sample) keeps its history this way and reads it
[as a list](the-sample.md#an-operator-who-changed-a-row-and-the-history-as-a-list): a route for whoever holds the
permission and one for an operator, each with a GraphQL field.
