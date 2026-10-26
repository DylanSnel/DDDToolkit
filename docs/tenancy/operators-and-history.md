---
title: "Tenancy: operators and history"
sidebar_label: Operators and history
---

# Tenancy: operators and history

Your own staff who look across tenants (operators), who wrote and changed each row, and the history of who changed
access. Read it when support needs every tenant, or a tenant asks who changed what. The words are in the
[overview](overview.md).

## Operators

An operator is one of your own staff who looks across tenants, such as support. It **holds no seat** and **changes
nothing itself**: it is no administrator of every tenant.

- **Who is one**: a signed-in user whose token carries a role in
  [`TenancyOptions.OperatorTokenRoles`](reference/use-cases.md#operatortokenroles), empty by default. Registration
  refuses a token role that is also seated, and tenant selection answers nobody for an operator.
- **What it reads**: [`TenantDirectory.ListAsync(after, size)`](reference/use-cases.md#listasyncstring-int-cancellationtoken),
  every tenant by slug, 1 to 200 a page, with name, status and active seats, read as the operator. Anyone else is
  refused with `tenancy.operators-only` before anything is read, as with
  [`[RequiresOperator]`](who-may-do-what.md#what-a-request-requires-of-its-caller) on a request of yours.
- **It writes nothing.** Your module records what the operator asks for, and system work that names the operator
  does it: `TenancyUseCases.BeginOperator(identity)` to provision, `BeginOperatorIn(tenant, identity)` in a tenant.
  The work holds what system work holds; the operator is only [recorded](#who-changed-a-row).
- The identity is the verified `sub` of the request that asked, never what a caller sends.

```csharp
using (TenancyUseCases.BeginOperatorIn(tenant, operatorIdentity))
{
    await tenantCommands.SuspendAsync("Asked for by the owner", cancellationToken);
}
```

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

| On | The operators' role |
|---|---|
| Tenancy's tables, your entities on them, the access history | reads every row of every tenant; writes none |
| A module's table kept to a tenant | reads what the module's rule for `RowAccessRoles.Token("operator")` admits; writes nothing, whatever a rule says |

- The export refuses an operator token role without a database role of its own, or on the signed-in users' role:
  either would open every tenant to callers who are no operators. A mapped token role nobody named an operator's
  stays [closed out](postgres.md#what-the-policies-check).
- `EnsurePoliciesAreInPlaceAsync` checks at start-up that the options and the mark agree; with the options alone, an
  operator gets an empty directory.

## Who changed a row

An entity kept to a tenant can record who wrote each row first and who changed it last:

```csharp
modelBuilder.Entity<Project>(project =>
{
    project.ScopeToTenant(row => row.TenantId);
    project.RecordsWhoChanged<Project, SeatId>();   // in Tenancy's own module: project.RecordsWhoChanged()
});
```

Six shadow columns ([`TenancyAttribution`](reference/entity-framework.md#tenancyattribution)), filled on every save
from the Tenancy caller: all six on a new row, `ChangedBy…` on a changed one.

| Who acted | `…ByKind` | `…BySeat` | `…ByIdentity` |
|---|---|---|---|
| A seat | `seat` | the seat | |
| An operator's system work (`BeginOperator`, `BeginOperatorIn`) | `operator` | | the operator's verified identity |
| System work (`BeginSystemIn`), even for a seat | `system` | | |
| A link's token (`BeginTokenIn`) | `token` | | |

- Ids, never a [name](names-and-languages.md#names); read one with
  `EF.Property<SeatId?>(row, TenancyAttribution.ChangedBySeat)`. Who wrote first is fixed once the row exists.
- A row whose own columns did not change keeps who changed it last; an aggregate with a version changes its row with
  every change to its parts.
- Adding them is your own migration. In a one-table hierarchy, call it on the root type: the first save refuses it
  on a derived type alone, or without `ScopeToTenant`.
- `AddTenancy` wraps the toolkit's `IActedByAccessor`, so an [event log](../entity-framework.md#an-event-log) records
  these kinds too. Register your own before `AddTenancy`, as a singleton; one registered after replaces Tenancy's,
  and `TenancyChecks.EnsureWired` refuses a context with the [access history](#access-history).

> [!IMPORTANT]
> `Caller.UserId` is always a token's verified `sub`; system work has none. Whom it acts for is only recorded, and
> gives it no rights.

**On Postgres** a trigger holds callers to themselves:

- a signed-in user writes the kind `seat`, its own seat and no operator's identity; the scoped system role never
  writes `seat`; neither changes who wrote first. A role that is no caller's (the tables' owner in a migration) is
  not held.
- A save fills the columns, so only a statement around the model is refused, such as an `ExecuteUpdate` of yours that
  leaves another seat in `ChangedBySeat`: SQLSTATE `42501`
  ([When the database refuses](../row-level-security.md#when-the-database-refuses)).

## Access history

Tenancy's events that change access can be kept in the toolkit's [event log](../entity-framework.md#an-event-log),
each with its tenant on the row:

```csharp
// Tenancy's context
modelBuilder.AddTenancy(database: Database);
modelBuilder.AddDomainEventOutbox(Database);
modelBuilder.AddTenancyEventLogTable(Database);   // the event log, with a TenantId column of your tenant id

// Where Tenancy is registered: keep the events that change access
options.UseOutbox<ShopTenancyContext>(outbox => outbox
    .AddTenancyDomainEvents()
    .KeepEventLog(log => log.AddTenancyEventLog()));   // both generated there, closed over your four ids
```

`AddTenancyEventLog` keeps, under the names the outbox stores them by:

| About | Events kept |
|---|---|
| The tenant | `tenancy.tenant-provisioned`, `tenancy.tenant-activated`, `tenancy.tenant-suspended`, `tenancy.tenant-reactivated`, `tenancy.tenant-closed`, `tenancy.tenant-shape-changed` |
| A unit | `tenancy.organization-unit-added`, `tenancy.organization-unit-moved`, `tenancy.organization-unit-archived` |
| A seat | `tenancy.seat-added`, `tenancy.seat-suspended`, `tenancy.seat-reactivated`, `tenancy.seat-deactivated`, `tenancy.seat-placed`, `tenancy.seat-withdrawn`, `tenancy.primary-placement-changed` |
| A seat's roles | `tenancy.organization-role-granted`, `tenancy.organization-role-revoked` |
| A role | `tenancy.role-created`, `tenancy.role-permissions-changed`, `tenancy.role-archived`, `tenancy.role-followed-its-pack` |

- Renames change nobody's access, so `tenancy.organization-renamed`, `tenancy.organization-unit-renamed` and
  `tenancy.role-renamed` are left out (the outbox still stores them).
- An event of yours that belongs in the history: one more `Keep<TEvent>()` beside the call. `KeepEventLog()` with
  nothing chosen keeps every event of the context.

**What a row holds**

- Every Tenancy domain event ends with `By`, the [actor](#who-changed-a-row), so events about a tenant, unit or role
  are closed over the seat id too (`TenantSuspended<TenantId, SeatId>`). An aggregate method you call yourself takes
  it last; with nobody to name, name the type: `tenant.Activate<SeatId>()`.
- `RolePermissionsChanged` and `RoleFollowedItsPack` name the permissions `Added` and `Removed`, implied ones
  included.
- A row is written in the save that raised its event, so a rollback leaves none. It names the event's tenant (or
  the save's) and who acted.
- The payload holds ids, permissions, dates, who acted and a few facts (a slug, a pack, a suspension's reason):
  never what anything is called, or why a role was given.

**Who reads it**: whoever holds **`tenancy.history.view`** for the whole tenant; it manages no access, and every
administrators' starter role holds it. No query filter keeps the rows to a tenant (the toolkit's retention reads the
table whole): code that reads them asks the permission and names the tenant.

**On Postgres** its policies come with Tenancy's tables: map it in the context that calls `AddTenancy` (the export
refuses it elsewhere).

| Who | Reads | Adds |
|---|---|---|
| A signed-in user | its tenant's rows, with `tenancy.history.view` | rows about its own seat, in the tenant the connection names |
| System work in a tenant | its tenant's rows | in Tenancy's own scope, never as a seat |
| An operator's role | every row | nothing |
| Your bookkeeping role, where the export writes privileges | every row | nothing; removes the rows the log's guard lets go |

- The policies read a row's columns, never its payload: where the two differ, trust the columns.
- Nobody changes a row, or removes one before its time: the log's [guard](../entity-framework.md#an-event-log)
  refuses that for every role, the tables' owner included.
- A seat that suspends or deactivates itself is still recorded: its seat counts in the transaction that wrote it.

The sample shows [the history as a list](sample/who-may-do-what.md#an-operator-who-changed-a-row-and-the-history-as-a-list).
