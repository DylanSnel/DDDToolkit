---
title: "Tenancy: keeping tenants apart"
sidebar_label: Keeping tenants apart
---

# Tenancy: keeping tenants apart

How every read and save stays in the caller's tenant on any database, and how system work, work no person does, is
begun. Read it when you add a table that belongs to a tenant, wire a context, or write a job, an import or a seeding.

## The filter and the save check

Two locks in C#, on every database. `AddTenancy()` puts them on Tenancy's classes, `ScopeToTenant` on yours:

```csharp
modelBuilder.Entity<Project>().ScopeToTenant(project => project.TenantId);
```

| Lock | What it does |
|---|---|
| **The tenant filter** | A query filter: every read is kept to the tenant the caller acts in; a caller in none finds nothing. |
| **The save check** | An interceptor: a row of another tenant, whoever loaded or made it, is refused with `tenancy.other-tenant` before anything is written. |

- **A row never moves to another tenant**: the tenant property is fixed after insert, and
  [privileges written from the policies](../row-level-security.md#privileges-from-the-policies) leave its column out
  of `UPDATE`.
- **The filter is named**, `TenancyQueryFilter.Name`: a filter of your own (a soft delete) sits beside it, and a read
  across tenants skips it alone with `IgnoreQueryFilters([TenancyQueryFilter.Name])`.
- **Your filter hides rows from your reads only**: Tenancy still sees them when it writes rights and checks that a
  slug, person or role name is free, as the database does. A seat your filter hides keeps its rights; suspend or
  deactivate it instead.

### Where the save check goes

`UseDDDToolkit` puts it on every context it wires, after the toolkit's interceptors, so it sees what domain event
handlers changed and only aggregates that passed their invariants. It asks the model what to do, once per model, so
a module added later is covered by the same call:

| The context | At a save, the check |
|---|---|
| Tenancy's own | checks the rows, and writes the closure (every pair of a unit and a unit at or below it) and the rights the save changes |
| a module's, with `ScopeToTenant` | checks the rows `ScopeToTenant` keeps to a tenant |
| one with neither | does nothing |

### A context without it fails loudly

- `ScopeToTenant` puts in the model that its context needs the save check
  ([A part a model cannot do without](../entity-framework.md#a-part-a-model-cannot-do-without)). Without it the first
  save fails before anything is written, naming the call that is missing.
- `UseDDDToolkitCore`, the toolkit's interceptors without any package's part, takes the save check with
  `.UseTenancy(serviceProvider)` after it; the other order fails the check below. It runs as the login role: where
  that role [owns and holds nothing](../row-level-security.md#a-login-that-owns-nothing), its first command is refused.
- [`TenancyChecks.EnsureWired(context)`](reference/entity-framework.md#ensurewireddbcontext) checks any context, order
  included. `AddTenancy` runs it over every registered context as the [start-up check](../startup-checks.md)
  `tenancy.contexts-wired`, beside `tenancy.catalogue-builds` and `tenancy.unknown-stored-permissions`.

### Contexts from a pool

Neither lock keeps anything on a context: each reads the caller when it runs. So a
[pooled](../entity-framework.md#contexts-from-a-pool) context is kept to whoever rented it, with the same call in the
pool's options:

```csharp
services.AddPooledDbContextFactory<ProjectsContext>((serviceProvider, options) => options
    .UseNpgsql(connectionString)
    .UseDDDToolkit(serviceProvider));
services.AddScopedFromPool<ProjectsContext>();   // a scope's own context, which a command saves through
```

### On Postgres, a second lock

Row level security (RLS) policies keep every caller to its tenant too, so a query that skips the filter or bypasses
Entity Framework still reaches only its tenant ([On Postgres: the second lock](postgres.md)).

## System work

Nothing becomes system work by itself: a person reaches Tenancy through a seat. Begin it on purpose for work nobody
asked for in a request (seeding, an import, an operator's command), or for work a handler does that no caller of its
request could ([provisioning a tenant](who-may-do-what.md#who-may-ask-and-what-the-work-runs-as)):

```csharp
using (TenancyUseCases.BeginSystemIn(tenant, actingSeat))
{
    await seats.PlaceAsync(seat, unit, primary: true, cancellationToken);
}
```

Each call begins Tenancy's caller and the toolkit's, ended together, closed over your ids
([Your ids, named once](your-module.md#your-ids-named-once)):

| `TenancyUseCases.` | Tenancy's caller | The toolkit's, `Caller.` |
|---|---|---|
| `BeginSystem()` | outside any tenant: provisions tenants, reads and writes no tenant's rows | `SystemIn("tenancy")` |
| `BeginSystemIn(tenant, actingSeat, scope)` | in that tenant: every permission there, nothing elsewhere | `SystemIn(scope)`, `"tenancy"` by default |
| `BeginOperator(identity)` | as `BeginSystem`, recorded as the operator a tenant is provisioned for | `SystemIn("tenancy")` |
| `BeginOperatorIn(tenant, identity, scope)` | as `BeginSystemIn`, recorded as the operator the work is for | `SystemIn(scope)` |
| `BeginTokenIn(tenant, seat, scope)` | as `BeginSystemIn`, recorded as a link's token and the seat it stands for | `SystemIn(scope)`, scope required |

A module that sees only your ids calls [`TenancyWork`](reference/callers.md#tenancywork)'s own:
`TenancyWork.BeginSystemIn(tenant, seat, "billing")`, ids inferred, or
`TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant, scope: "billing")` with no seat.

### Scoped, not the application itself

- Neither caller is `Caller.System`, the application itself. The scoped caller runs as a database role that cannot
  bypass the policies; they decide what it reaches ([The scoped system role](../row-level-security.md#the-scoped-system-role)).
- The scope says whose work it is: Tenancy's (`tenancy`) for work that calls its use cases; the module's name for a
  module's own work in a tenant, and for its work on its resources with members
  ([Who holds a permission](../membership.md#who-holds-a-permission)).

### A scope, never a requirement

- No request requires system work: no door opens because the code behind it runs as the application. When the
  scope ends, the caller before it is current again.
- A request system work sends in a tenant says who may send it, `[InTenant]`. What only system work may do is
  refused past that door: a handler asks Tenancy (the sample's `MarkTenantAsDemo`), and Tenancy's system-only use
  cases (provisioning, suspending, reactivating or closing a tenant) refuse everyone else with `access.system-only`.
- Work nobody began a caller for is no system work, even in a host that does not
  [require explicit callers](../row-level-security.md#fail-closed-callers).

### Outside any tenant

System work outside any tenant reads and writes no tenant's rows: the save check refuses each with
`tenancy.other-tenant`. So provisioning checks that the slug is free, then saves as system work in the new tenant.

### Reads before any tenant is known

Two reads come before any tenant is known, each one query answering ids or permissions only: the permissions stored on
every tenant's roles (a start-up check), and the tenants a round of system work visits, active and suspended, from
[`TenancySystemReads.TenantsToSweepAsync`](reference/entity-framework.md#tenantstosweepasyncdbcontext-string-cancellationtoken):

```csharp
// tenancy: Tenancy's context from your services, which names the context and is not queried itself
foreach (var tenant in await TenancySystemReads.TenantsToSweepAsync<ShopTenant, TenantId>(tenancy, "ordering", cancellationToken))
{
    using (TenancyUseCases.BeginSystemIn(tenant, scope: "ordering"))
    {
        await orders.EndWhatHasRunOutAsync(cancellationToken);
    }
}
```

- The read runs as a caller Tenancy begins for it alone: `Caller.System` where only the filter holds anyone to a
  tenant (SQLite); with [the second lock](postgres.md), scoped system work in no tenant asking a database function
  ([The scoped system role](postgres.md#the-scoped-system-role-grants-and-reads-across-tenants)).
- It runs on a context of its own, outside any transaction you began, so its reach never enters your work. It
  connects separately unless your contexts share one connection: ask before you begin a transaction, not inside one,
  or code holding a connection can wait for itself where the data source has few
  ([With a context pool](../row-level-security.md#with-a-context-pool)). Tenancy's use cases run on one connection.
- A read of your own that is no seat's and no tenant's, in the middle of somebody's work, begins
  `TenancyCallers.BeginNone()`, so the surrounding tenant does not travel with it.

### Explicit callers

`services.RequireExplicitCallers()` fails work that says nothing about who it runs as, instead of running it as the
application. The sample uses it ([Fail-closed callers](../row-level-security.md#fail-closed-callers)).
