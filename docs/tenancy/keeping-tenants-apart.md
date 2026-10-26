---
title: "Tenancy: keeping tenants apart"
sidebar_label: Keeping tenants apart
---

# Tenancy: keeping tenants apart

How every read and save stays in the caller's tenant on any database, and how work no person does, system work,
is begun. Read it when you add a table that belongs to a tenant, wire a context, or write a job, an import or a
seeding. The words are in the [overview](overview.md).

## The filter and the save check

Two things keep tenants apart in C#, on every database. `AddTenancy()` puts them on Tenancy's classes,
`ScopeToTenant` on yours:

- **The tenant filter**: a query filter that keeps every read to the tenant the caller acts in, and finds nothing
  for a caller in none.
- **The save check**: an interceptor that refuses to write a row of another tenant, whoever loaded or made it, with
  `tenancy.other-tenant`, before anything is written.

**A row never moves to another tenant.** `ScopeToTenant` marks the tenant property fixed once the row exists
(`IsFixedAfterInsert`): Entity Framework refuses a save that changed it, after the save check refused it first, and
[privileges written from the policies](../row-level-security.md#privileges-from-the-policies) leave the column out of
`UPDATE`.

**The filter is named, `TenancyQueryFilter.Name`.**

- A filter of your own on the class, a soft delete say, sits beside it instead of replacing it.
- A read that must look across tenants skips it by name, `IgnoreQueryFilters([TenancyQueryFilter.Name])`, and keeps
  your filters.
- Your filter hides rows from your reads only. Tenancy still sees them when it writes the rights a save changes,
  and when it checks that a slug, a person or a role name is free, because the database sees them too.
- A seat your filter hides keeps its rights: suspend or deactivate it to take them away.

### Where the save check goes

- It is Tenancy's part of a context's options. `AddTenancy` brings it; `UseDDDToolkit` puts it on every context it
  wires, after the toolkit's interceptors, last of all the parts.
- So it sees what the domain event handlers changed, only aggregates that passed their invariants, and every row
  another part added. Earlier, it would check rows still to change.
- It goes on every context alike: the model decides its work, and the options are built before the model. So it
  asks the model at every save, once per model:

| The context | At a save, the check |
|---|---|
| Tenancy's own | checks the rows, and writes the closure (every pair of a unit and a unit at or below it) and the rights the save changes |
| a module's, with `ScopeToTenant` | checks the rows `ScopeToTenant` keeps to a tenant |
| one with neither | does nothing |

- No registration lists Tenancy's contexts; a module added later is kept to the tenant by the same call.

### A context without it fails loudly

- A context that keeps rows to a tenant cannot do without the save check, and its model says so: `ScopeToTenant`
  states it ([A part a model cannot do without](../entity-framework.md#a-part-a-model-cannot-do-without)).
- Without it, the context fails at its first save, before anything is written. The message names `AddTenancy` where
  nothing registered Tenancy, and `UseTenancy` where the context was given the base alone.
- Tenancy's store checks its own context before every save.
- `TenancyChecks.EnsureWired(context)` checks any other, order included: the save check before the toolkit's
  interceptors is refused too.
- `AddTenancy` registers that as the [start-up check](../startup-checks.md) `tenancy.contexts-wired`, over every
  context the host registers, beside `tenancy.catalogue-builds` and `tenancy.unknown-stored-permissions`. A host
  runs them with `services.RunStartupChecks()`.
- A context configured with `UseDDDToolkitCore` is held to the same: it takes the save check with `UseTenancy`,
  after it.

<details>
<summary>Show the code: a module's context, and one that takes the parts one by one</summary>

```csharp
// A module's context that keeps its own rows to a tenant
services.AddDbContext<ProjectsContext>((serviceProvider, options) => options
    .UseNpgsql(connectionString)
    .UseDDDToolkit(serviceProvider));   // the toolkit's interceptors, row level security where it is registered, the save check

// A context that runs as the login role on purpose, where that role may read the tables, and still keeps its rows to a tenant
services.AddDbContext<ReportsContext>((serviceProvider, options) => options
    .UseNpgsql(connectionString)
    .UseDDDToolkitCore(serviceProvider)
    .UseTenancy(serviceProvider));      // after the toolkit's interceptors: EnsureWired refuses it the other way round
```

</details>

A context given the base alone does what the login role may. Where that role owns and holds nothing, as
[a login that owns nothing](../row-level-security.md#a-login-that-owns-nothing) asks, the database refuses its first
command; the cross-tenant reads Tenancy needs go through [system work](#system-work) instead.

### Contexts from a pool

- The filter reads the caller when a query runs, the save check at every save. Neither keeps anything on a context
  or its options.
- So a context from a [pool](../entity-framework.md#contexts-from-a-pool) is kept to the tenant of whoever rented
  it, for saves and reads. An instance another tenant's request returned a moment ago refuses that tenant's rows
  like any other.
- The same call goes in the pool's options, Tenancy's own context included:

```csharp
services.AddPooledDbContextFactory<ProjectsContext>((serviceProvider, options) => options
    .UseNpgsql(connectionString)
    .UseDDDToolkit(serviceProvider));
services.AddScopedFromPool<ProjectsContext>();   // a scope's own context, which a command saves through
```

- `TenancyChecks.EnsureWired` passes for a pooled context as for any other, the scope's own or one from the
  factory. Tenancy's store saves through the scope's.

### On Postgres, a second lock

Row level security (RLS) adds a second lock under these two: policies that keep every caller to its tenant and ask
Tenancy's questions as SQL functions. A query that skips the filter or bypasses Entity Framework still reaches only
its tenant. [On Postgres: the second lock](postgres.md) says what it checks, and what it leaves to the use cases.

## System work

Nothing becomes system work by itself: a person reaches Tenancy through a seat (their place in one tenant).

- Begun on purpose, with `TenancyWork`:
  - work nobody asked for in a request: seeding, an import, an operator's command;
  - work a handler does that no caller of its request could:
    [provisioning a tenant](who-may-do-what.md#who-may-ask-and-what-the-work-runs-as).
- It begins two callers at once, Tenancy's and the toolkit's, ended together.
- `TenancyWork`'s methods are generic over your tenant and seat ids. A project that sees your classes calls them
  closed over those, through the class your use cases are named through, naming neither
  ([Your ids, named once](your-module.md#your-ids-named-once)):

| | Tenancy's caller | The toolkit's caller |
|---|---|---|
| `TenancyUseCases.BeginSystem()` | system work outside any tenant, which provisions tenants, and reads and writes no tenant's rows | `Caller.SystemIn("tenancy")` |
| `TenancyUseCases.BeginSystemIn(tenant, actingSeat, scope)` | system work in that tenant, holding every permission there and nothing anywhere else | `Caller.SystemIn(scope)`, `"tenancy"` unless you pass another |
| `TenancyUseCases.BeginOperator(identity)` | as `BeginSystem`, recorded as the operator a tenant is provisioned for | `Caller.SystemIn("tenancy")` |
| `TenancyUseCases.BeginOperatorIn(tenant, identity, scope)` | as `BeginSystemIn`, recorded as the operator the work is carried out for | `Caller.SystemIn(scope)` |
| `TenancyUseCases.BeginTokenIn(tenant, seat, scope)` | as `BeginSystemIn`, recorded as a link's token and the seat it stands for | `Caller.SystemIn(scope)`, the module's name |

A module that sees only your ids calls `TenancyWork`'s own: `TenancyWork.BeginSystemIn(tenant, seat, "billing")`,
ids inferred from the arguments, or `TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant, scope: "billing")` with no
seat.

### Scoped, not the application itself

- Neither caller is the toolkit's `Caller.System`, the application itself.
  [Row level security](../row-level-security.md#the-scoped-system-role) runs that as a role no policy is written for: past every
  policy as the login role or a bypassing role, or, on Supabase unless the project says otherwise, as `ddd_system`,
  which reaches the toolkit's bookkeeping and no module's rows.
- The scoped system caller runs as a role that cannot bypass the policies; they decide what it reaches.
- The scope says whose work it is:
  - work that calls Tenancy's use cases keeps Tenancy's scope;
  - a module's own work in a tenant, only reading Tenancy's rows beside its own, passes the module's name;
  - so does a module's work on its resources with members: their rules let the module's scope through, and
    Tenancy's holds nothing on them ([Who holds a permission](../membership.md#who-holds-a-permission)).
- The sample seeds a tenant's organization as Tenancy's work, its projects as the Projects module's.

The naming follows the toolkit's caller of the application itself, `Caller.System`, so one word says it everywhere:

| System work | Spelled |
|---|---|
| the application itself, which no policy is written for | `Caller.System`, on the role `SystemRole` names: on Supabase `ddd_system` unless the project says otherwise, the toolkit's bookkeeping and nothing else; past every policy only on the login role (`system=none`) or a role that bypasses them |
| the application at work in a scope, inside the policies | `Caller.SystemIn(scope)`, on `ddd_system_in` unless the host names another |
| begun in Tenancy, outside any tenant or in one | `TenancyUseCases.BeginSystem()`, `TenancyUseCases.BeginSystemIn(tenant)`: `TenancyWork`'s, closed over your ids |
| a request it sends | what any request says, who may send it: `[InTenant]` for system work in a tenant, which lets it through there |

The database roles keep their names: migrations already applied grant them.

### A scope, never a requirement

- No request requires system work; no door opens because the code behind it runs as the application.
- Trusted code begins it around what it does or sends; when the scope ends, the caller before it is current again.
- A request system work sends in a tenant says who may send it, `[InTenant]`. What only system work may do is
  refused past that door, where the work happens:
  - a handler acting in a tenant asks Tenancy which one and whether it runs as system work, as the sample's
    `MarkTenantAsDemo` does;
  - Tenancy's system-only use cases (provisioning a tenant; suspending, reactivating or closing one) refuse everyone
    else with `access.system-only`.
- Work nobody began a caller for is no system work either, also in a host that does not
  [require explicit callers](../row-level-security.md#fail-closed-callers), whose accessor answers the application
  itself for such work.

```csharp
using (TenancyUseCases.BeginSystemIn(tenant, actingSeat))
{
    await seats.PlaceAsync(seat, unit, primary: true, cancellationToken);
}
```

### Outside any tenant

System work outside any tenant can neither read nor write a tenant's rows: the save check refuses each with
`tenancy.other-tenant`. So provisioning, once it knows the new tenant's id, checks that the slug is free and saves as
system work in that new tenant, writing its rows and nothing else.

### Reads before any tenant is known

- Two answers come before any tenant is known:
  - the permissions stored on every tenant's roles, reported at start-up by
    `TenancyChecks.UnknownStoredPermissionsAsync`;
  - the tenants to visit, listed by `TenancySystemReads.TenantsToSweepAsync` for a round of any module's system work.
- Each is one query answering ids or permissions only, as a caller Tenancy begins around that query alone.
- It runs on a context of its own, which connects separately unless your contexts share one connection, outside any
  transaction the caller began: an open transaction neither narrows it nor carries its reach into the caller's work.
- That caller is `Caller.System` where nothing but the filter holds anyone to a tenant, as on SQLite. With
  [the second lock](postgres.md) it is scoped system work in no tenant, asking a database function; nothing of
  Tenancy's runs as the application ([The scoped system role](postgres.md#the-scoped-system-role-grants-and-reads-across-tenants)).
- Its caller is begun before its context is taken, so a host that makes a context per caller makes it for the read,
  never for the seat the surrounding work acts as.
- Because it connects separately, ask before you begin a transaction, not inside one. Code holding a connection
  while waiting for a second can wait for itself where the data source has few
  ([With a context pool](../row-level-security.md#with-a-context-pool)). Tenancy's use cases never do: they complete
  on a data source of one connection.
- A read of your own that is no seat's and no tenant's, in the middle of somebody's work, begins
  `TenancyCallers.BeginNone()`: nobody is the Tenancy caller until it is disposed, as `Callers.BeginNone()` does for
  the toolkit's caller, so the surrounding tenant does not travel with the read.

```csharp
// A round of a module's own: the tenants to visit, then its system work in each, under its own scope
foreach (var tenant in await TenancySystemReads.TenantsToSweepAsync<ShopTenant, TenantId>(tenancy, "ordering", cancellationToken))
{
    using (TenancyUseCases.BeginSystemIn(tenant, scope: "ordering"))
    {
        await orders.EndWhatHasRunOutAsync(cancellationToken);
    }
}
```

- `tenancy` is Tenancy's context, resolved from your services. It names the context the read makes its own of, and
  is not queried itself.
- Active and suspended tenants are answered; closed ones are not.

### Explicit callers

A host that requires explicit callers, `services.RequireExplicitCallers()`, fails work that says nothing about who
it runs as instead of running it as the application. The sample does. See
[Fail-closed callers](../row-level-security.md#fail-closed-callers).
