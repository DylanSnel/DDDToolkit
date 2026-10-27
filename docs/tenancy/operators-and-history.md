---
title: "Tenancy: operators and history"
sidebar_label: Operators and history
---

# Tenancy: operators and history

This page covers operators (your staff who look across tenants), the record of who wrote and changed each row, and
the history of who changed access. Read it when support needs to see every tenant, or when a tenant asks who
changed what. The words are defined in the [overview](overview.md).

## Operators

An operator is a member of your staff who looks across tenants, for example in support. An operator **holds no
seat** and **changes nothing directly**. So an operator is not an administrator of every tenant.

- **Who is an operator.** A signed-in user is an operator when the user's token carries a role listed in
  [`TenancyOptions.OperatorTokenRoles`](reference/use-cases.md#operatortokenroles). The list is empty by default.
  Registration refuses a token role that is also a seated token role. Tenant selection returns nobody for an
  operator, whatever tenant the request names.
- **What an operator reads.**
  [`TenantDirectory.ListAsync(after, size)`](reference/use-cases.md#listasyncstring-int-cancellationtoken) returns
  every tenant by slug, 1 to 200 per page, with its name, status and number of active seats. The read runs as the
  operator. Every other caller gets `tenancy.operators-only` before anything is read, as with
  [`[RequiresOperator]`](who-may-do-what.md#what-a-request-requires-of-its-caller) on a request of yours.
- **An operator writes nothing.** Your module records what the operator asks for. System work that names the
  operator then does the work. Use `TenancyUseCases.BeginOperator(identity)` to provision a tenant, and
  `BeginOperatorIn(tenant, identity)` for work inside a tenant. The work holds what system work holds. The operator
  is only [recorded](#who-changed-a-row) as the person the work was for.
- The identity is the verified `sub` of the request that asked. Never take it from what a caller sends.

```csharp
using (TenancyUseCases.BeginOperatorIn(tenant, operatorIdentity))
{
    await tenantCommands.SuspendAsync("Asked for by the owner", cancellationToken);
}
```

**On Postgres** an operator reads through a database role of its own. Map the token role to a database role, with
`PostgresRowLevelSecurityOptions.TokenRoles` or with `token:operator=<role>` in `SupabaseRowAccessRoles`. Mark the
token roles you give Tenancy with `[TenancyOperators]`, so the export writes policies for the same roles:

```csharp
public static class ShopOperators
{
    [TenancyOperators]
    public static IReadOnlyList<string> TokenRoles { get; } = ["operator"];
}

// Where Tenancy is registered
options.OperatorTokenRoles.UnionWith(ShopOperators.TokenRoles);
```

| Table | What the operators' database role may do |
|---|---|
| Tenancy's tables, your classes on them, and the access history | Read every row of every tenant. Write no row. |
| A module's table kept to a tenant | Read the rows that the module's rule for `RowAccessRoles.Token("operator")` allows. Write no row, whatever a rule says. |

- The export refuses an operator token role without a database role of its own. It also refuses an operator token
  role mapped to the signed-in users' role. Either one would open every tenant to callers who are not operators. A
  mapped token role that nobody marked as an operator's role stays
  [closed out](postgres.md#what-the-policies-check).
- `EnsurePoliciesAreInPlaceAsync` checks at start-up that the options and the `[TenancyOperators]` mark agree. With
  the options and no mark, an operator gets an empty directory.

## Who changed a row

An entity kept to a tenant can record who wrote each row first and who changed it last:

```csharp
modelBuilder.Entity<Project>(project =>
{
    project.ScopeToTenant(row => row.TenantId);
    project.RecordsWhoChanged<Project, SeatId>();   // in the module that declares your seat class: project.RecordsWhoChanged()
});
```

`RecordsWhoChanged` adds six shadow columns ([`TenancyAttribution`](reference/entity-framework.md#tenancyattribution)).
Every save fills them from the Tenancy caller. A new row gets all six. A changed row gets the three `ChangedBy…`
columns.

| Who acted | `…ByKind` | `…BySeat` | `…ByIdentity` |
|---|---|---|---|
| A seat | `seat` | the seat | |
| An operator's system work (`BeginOperator`, `BeginOperatorIn`) | `operator` | | the operator's verified identity |
| System work (`BeginSystemIn`), also when it acts for a seat | `system` | | |
| A link's token (`BeginTokenIn`) | `token` | | |

- The columns hold ids and never a [name](names-and-languages.md#names). Read one with
  `EF.Property<SeatId?>(row, TenancyAttribution.ChangedBySeat)`. The three `CreatedBy…` columns are fixed once the
  row exists.
- When only a row of one of the entity's parts changes, in another table, the entity's row keeps its `ChangedBy…`
  values. An aggregate with a version changes its row with every change to its parts.
- Adding the columns to an existing table needs a migration of yours. In a hierarchy stored in one table, call
  `RecordsWhoChanged` on the root type. The first save refuses the call on a derived type alone, and refuses it
  without `ScopeToTenant`.
- `AddTenancy` wraps the toolkit's `IActedByAccessor`, so an [event log](../entity-framework.md#an-event-log)
  records these kinds too. Register your own accessor before `AddTenancy`, as a singleton. An accessor registered
  after `AddTenancy` replaces Tenancy's accessor. `TenancyChecks.EnsureWired` then refuses a context that keeps the
  [access history](#access-history).

> [!IMPORTANT]
> `Caller.UserId` is always the verified `sub` of a token. System work has no `UserId`. The person that system work
> acts for is only recorded, and that record gives the work no rights.

**On Postgres** a trigger checks the six columns on every write:

- A signed-in user can write only the kind `seat`, the user's own seat, and no operator identity.
- The scoped system role never writes the kind `seat`.
- Neither role changes the `CreatedBy…` columns.
- The trigger does not check a role that no caller uses, such as the tables' owner in a migration.
- A save through the model fills the columns correctly. So the trigger refuses only a statement that goes around the
  model, such as an `ExecuteUpdate` of yours that leaves another seat in `ChangedBySeat`. The error is SQLSTATE
  `42501` ([When the database refuses](../row-level-security.md#when-the-database-refuses)).

## Access history

Tenancy's events that change access can be kept in the toolkit's [event log](../entity-framework.md#an-event-log).
Each row stores its tenant in a column:

```csharp
// Tenancy's context
modelBuilder.AddTenancy(database: Database);
modelBuilder.AddDomainEventOutbox(Database);
modelBuilder.AddTenancyEventLogTable(Database);   // the event log, with a TenantId column of your tenant id

// Where Tenancy is registered: keep the events that change access
options.UseOutbox<ShopTenancyContext>(outbox => outbox
    .AddTenancyDomainEvents()
    .KeepEventLog(log => log.AddTenancyEventLog()));   // both calls are generated into your project, closed over your four ids
```

`AddTenancyEventLog` keeps these events, under the names the outbox stores them by:

| About | Events kept |
|---|---|
| The tenant | `tenancy.tenant-provisioned`, `tenancy.tenant-activated`, `tenancy.tenant-suspended`, `tenancy.tenant-reactivated`, `tenancy.tenant-closed`, `tenancy.tenant-shape-changed` |
| A unit | `tenancy.organization-unit-added`, `tenancy.organization-unit-moved`, `tenancy.organization-unit-archived` |
| A seat | `tenancy.seat-added`, `tenancy.seat-suspended`, `tenancy.seat-reactivated`, `tenancy.seat-deactivated`, `tenancy.seat-placed`, `tenancy.seat-withdrawn`, `tenancy.primary-placement-changed` |
| A seat's roles | `tenancy.organization-role-granted`, `tenancy.organization-role-revoked` |
| A role | `tenancy.role-created`, `tenancy.role-permissions-changed`, `tenancy.role-archived`, `tenancy.role-followed-its-pack` |

- A rename changes nobody's access. So the history leaves out `tenancy.organization-renamed`,
  `tenancy.organization-unit-renamed` and `tenancy.role-renamed`. The outbox still stores them.
- To add an event of yours to the history, add one more `Keep<TEvent>()` next to the call. `KeepEventLog()` with
  nothing chosen keeps every event of the context, renames included.

**What a row holds**

- Every Tenancy domain event ends with `By`, the [actor](#who-changed-a-row). So the events about a tenant, a unit
  or a role are closed over the seat id too (`TenantSuspended<TenantId, SeatId>`). An aggregate method you call
  yourself takes the actor as its last argument. With nobody to name, name the type: `tenant.Activate<SeatId>()`.
- `RolePermissionsChanged` and `RoleFollowedItsPack` list the permissions `Added` and `Removed`, implied permissions
  included.
- The save that raised an event writes the event's row, so a rollback leaves no row. The row names the event's
  tenant (or the save's tenant) and who acted.
- The payload holds ids, permissions, dates, who acted and a few facts, such as a slug, a pack or the reason for a
  suspension. It never holds what anything is called, or why a role was given.

**Who reads the history**

- A seat reads the history when it holds **`tenancy.history.view`** for the whole tenant. This permission manages
  no access. Every administrators' starter role holds it.
- No query filter limits the history's rows to a tenant, because the toolkit's retention reads the full table. So
  code that reads the history checks the permission and filters by tenant itself.

**On Postgres** the history's policies come with Tenancy's tables. Map the history in the context that calls
`AddTenancy`. The export refuses the history in any other context.

| Who | Reads | Adds |
|---|---|---|
| A signed-in user | The tenant's rows, with `tenancy.history.view`. | Rows about the user's own seat, in the tenant the connection names. |
| System work in a tenant | The tenant's rows. | Rows in Tenancy's scope, never as a seat. |
| An operator's role | Every row. | Nothing. |
| Your bookkeeping role, where the export writes privileges | Every row. | Nothing. It removes rows older than the log's `keepFor`, which the log's guard allows. |

- The policies read a row's columns and never its payload. Where the two differ, trust the columns.
- Nobody can change a row, or remove a row younger than `keepFor`. The log's
  [guard](../entity-framework.md#an-event-log), a set of triggers, refuses this for every role, the tables' owner
  included.
- A seat that suspends or deactivates itself is still recorded, because its seat counts in the transaction that
  wrote the row.

The sample shows [the history as a list](sample/who-may-do-what.md#operators-who-changed-a-row-and-the-access-history).
