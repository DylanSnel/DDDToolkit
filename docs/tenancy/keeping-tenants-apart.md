---
title: "Tenancy: keeping tenants apart"
sidebar_label: Keeping tenants apart
---

# Tenancy: keeping tenants apart

Tenancy keeps every read and every save inside the caller's tenant, on any database. This page describes the two
checks that do this, and how you start system work, which is work that no person does. Read it when you add a
table that belongs to a tenant, configure a context, or write a job, an import or a seeding.

## The tenant filter and the save check

Tenancy adds two checks in C#, so they work on every database. `AddTenancy()` adds them to Tenancy's classes.
`ScopeToTenant` adds them to a class of yours:

```csharp
modelBuilder.Entity<Project>().ScopeToTenant(project => project.TenantId);
```

| Check | What it does |
|---|---|
| **The tenant filter** | It is a query filter. Every query of the class gets `WHERE "TenantId" = @tenant`, with the tenant of the current caller. A caller in no tenant gets no rows. |
| **The save check** | It is a save interceptor. Before anything is written, it refuses each row of another tenant with `tenancy.other-tenant`, whoever loaded or made the row. A caller that is nobody gets `tenancy.not-seated`. |

- **A row cannot move to another tenant.** The tenant property is fixed after insert (`IsFixedAfterInsert`), so
  Entity Framework refuses a save that changes it. The
  [privileges written from the policies](../row-level-security.md#privileges-from-the-policies) also leave the
  tenant column out of `UPDATE`.
- **The filter has a name**, `TenancyQueryFilter.Name` (`tenancy.caller-tenant`). A filter of yours on the same
  class, such as a soft delete, applies next to it. A read across tenants skips only the tenant filter with
  `IgnoreQueryFilters([TenancyQueryFilter.Name])`.
- **Your filter hides rows from your queries only.** Tenancy's bookkeeping skips every filter. It reads the hidden
  rows when it writes rights, and when it checks that a slug, a person or a role name is free. Those checks take the
  place of a unique index, so they must see every row, as a unique index in the database does. A seat that your
  filter hides keeps its rights. To take the rights away, suspend or deactivate the seat.

### Where the save check goes

`UseDDDToolkit` adds the save check to every context it configures. It adds the check last, after the toolkit's
interceptors. So the check sees the changes that domain event handlers made, and only aggregates that passed their
invariants reach it. The check reads the model once per model to decide what to do. A module you add later is
covered by the same `UseDDDToolkit` call.

| Context | What the check does at a save |
|---|---|
| Tenancy's context | Checks the rows. Writes the closure (one row for each pair of a unit and a unit at or below it) and the rights that the save changes. Where the database keeps the rights, as on Postgres with row level security, the database writes the rights and the save writes only the closure. |
| A module's context with `ScopeToTenant` | Checks the rows of the classes that `ScopeToTenant` marks. |
| A context with neither | Nothing. |

### A context without the save check

- `ScopeToTenant` records in the model that its context needs the save check
  ([A part a model cannot do without](../entity-framework.md#a-part-a-model-cannot-do-without)). A context without
  the check fails at its first save, before anything is written. The error message names the missing call.
- `UseDDDToolkitCore` adds the toolkit's interceptors and no package's part. After it, add the save check with
  `.UseTenancy(serviceProvider)`. With `UseTenancy` first, `TenancyChecks.EnsureWired` fails.
- A context configured with `UseDDDToolkitCore` runs as the login role. Where that role
  [owns and holds nothing](../row-level-security.md#a-login-that-owns-nothing), the database refuses the context's
  first command.
- [`TenancyChecks.EnsureWired(context)`](reference/entity-framework.md#ensurewireddbcontext) checks any context,
  including the order of its interceptors. `AddTenancy` runs it over every registered context as the
  [start-up check](../startup-checks.md) `tenancy.contexts-wired`. Tenancy's other start-up checks are
  `tenancy.catalogue-builds` and `tenancy.unknown-stored-permissions`.

### Contexts from a pool

The tenant filter reads the current caller at every query. The save check reads the current caller at every save.
Neither check stores the caller on the context. So a [pooled](../entity-framework.md#contexts-from-a-pool) context
checks each renter against the renter's tenant. Use the same call in the pool's options:

```csharp
services.AddPooledDbContextFactory<ProjectsContext>((serviceProvider, options) => options
    .UseNpgsql(connectionString)
    .UseDDDToolkit(serviceProvider));
services.AddScopedFromPool<ProjectsContext>();   // one context per scope, for the commands to save through
```

### Row level security on Postgres

On Postgres, row level security (RLS) policies return only the rows of the caller's tenant. A query that skips the
tenant filter, or that does not go through Entity Framework, still reads only the caller's tenant
([Tenancy on Postgres](postgres.md)).

## System work

A person reaches Tenancy through a seat. Code runs as system work only when you start system work explicitly. Start
it for work that no request asked for, such as seeding, an import or an operator's command. Also start it for work
in a handler that no caller of the request could do, such as
[provisioning a tenant](who-may-do-what.md#requests-that-provision-a-tenant):

```csharp
using (TenancyUseCases.BeginSystemIn(tenant, actingSeat))
{
    await seats.PlaceAsync(seat, unit, primary: true, cancellationToken);
}
```

Each `Begin…` call starts two callers, Tenancy's caller and the toolkit's caller. Disposing the result ends both.
The calls on `TenancyUseCases` are closed over your ids ([Your ids, named once](your-module.md#calls-closed-over-your-ids)),
so you write no id type.

| `TenancyUseCases.` | Tenancy's caller | The toolkit's caller, `Caller.` |
|---|---|---|
| `BeginSystem()` | Works outside any tenant. It provisions tenants, and reads or writes no tenant's rows. | `SystemIn("tenancy")` |
| `BeginSystemIn(tenant, actingSeat, scope)` | Works inside the tenant. It holds every permission there and none elsewhere. | `SystemIn(scope)`, `"tenancy"` by default |
| `BeginOperator(identity)` | Works like `BeginSystem`. The work is recorded as the operator the tenant is provisioned for. | `SystemIn("tenancy")` |
| `BeginOperatorIn(tenant, identity, scope)` | Works like `BeginSystemIn`. The work is recorded as the operator it is for. | `SystemIn(scope)` |
| `BeginTokenIn(tenant, seat, scope)` | Works like `BeginSystemIn`. The work is recorded as a link's token and the seat the token stands for. | `SystemIn(scope)`. The scope is required. |

- `actingSeat` is recorded as the seat that placed or granted. It adds no rights.
- The last-administrator rule applies to system work in a tenant too.
- A scope is lower case letters, digits, `_` and `-`. Any other scope throws `ArgumentException`, and nothing
  starts.

A module that sees only your ids, and not your classes, calls the same methods on
[`TenancyWork`](reference/callers.md#tenancywork). With a seat, C# infers both ids:
`TenancyWork.BeginSystemIn(tenant, seat, "billing")`. Without a seat, write the ids:
`TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant, scope: "billing")`.

### The scoped system caller

- Neither caller is `Caller.System`. `Caller.System` stands for the application itself, and row level security lets
  it past every policy. On Postgres the scoped caller runs as a database role that cannot bypass the policies, and
  the policies decide which rows it reaches
  ([The scoped system role](../row-level-security.md#the-scoped-system-role)).
- The scope names whose work the system work is:
  - `tenancy` is Tenancy's scope, for work that calls Tenancy's use cases. On Postgres only work in this scope
    writes Tenancy's tables.
  - A module's name is the scope for that module's work in a tenant, and for its work on its resources with members
    ([Who holds a permission](../membership/permissions-and-roles.md#who-holds-a-permission)). Work in a module's
    scope can read Tenancy's tables and cannot write them.

### System work and request requirements

- A request cannot declare system work as its requirement. The requirement decides who may send the request. The
  handler then starts system work itself, in trusted code, when the work needs it. For example, a handler that
  provisions a tenant starts system work whether its request requires `[AllowAnonymous]` (a public registration
  form) or `[SignedIn]`. When the system work ends, the caller from before it is current again.
- A request that system work sends inside a tenant declares who may send it with `[InTenant]`. A second check, after
  that requirement, refuses everything that only system work may do:
  - The handler checks the caller. The handler of the sample's `MarkTenantAsDemo` calls `tenancy.RequireTenant()`
    and refuses the caller with `access.system-only` unless `BySystem` is true.
  - Tenancy's system-only use cases (provisioning, suspending, reactivating or closing a tenant) refuse every other
    caller with `access.system-only`.
- Code that runs without a started caller is nobody to Tenancy. This holds where the toolkit's caller is
  `Caller.System`, and in a host that does not [require explicit callers](../row-level-security.md#fail-closed-callers).
  Tenancy's use cases refuse a caller that is nobody with `tenancy.not-seated`.

### System work outside any tenant

System work outside any tenant (`BeginSystem`) reads no tenant's rows, because the tenant filter matches no row for
it. The save check refuses each row of a tenant with `tenancy.other-tenant`. So provisioning first checks that the
slug is free, and then saves as system work inside the new tenant.

### Reads before any tenant is known

Two reads run before any tenant is known. Each one is a single query that returns ids or permissions only:

- The start-up check reads the permissions stored on the roles of every tenant.
- [`TenancySystemReads.TenantsToSweepAsync`](reference/entity-framework.md#tenantstosweepasyncdbcontext-string-cancellationtoken)
  reads the tenants that a round of system work visits, the active and the suspended ones.

```csharp
// tenancy: Tenancy's context, resolved from your services. The read takes the context type from it and does not query it.
foreach (var tenant in await TenancySystemReads.TenantsToSweepAsync<ShopTenant, TenantId>(tenancy, "ordering", cancellationToken))
{
    using (TenancyUseCases.BeginSystemIn(tenant, scope: "ordering"))
    {
        await orders.EndWhatHasRunOutAsync(cancellationToken);
    }
}
```

- **The caller of the read.** Tenancy starts a caller for the read alone. On a database where only the tenant
  filter keeps tenants apart (SQLite), the read runs as `Caller.System` over the tables. With
  [row level security](postgres.md), the read runs as scoped system work in no tenant and calls a database function
  ([The scoped system role](postgres.md#reads-across-tenants)).
- **A separate context.** The read runs on a new context from a new service scope, outside any transaction you
  started. So your transaction cannot change who the read runs as, and the read's wider access does not carry over
  into your work.
- **A separate connection.** The read opens its own connection unless your contexts share one. Call it before you
  start a transaction. Inside a transaction, your code holds one connection while the read waits for another. With
  few connections in the data source, the code can then wait for itself
  ([With a context pool](../row-level-security.md#with-a-context-pool)). Tenancy's use
  cases run on one connection.
- **A read that belongs to no tenant.** Wrap a read of yours that belongs to no seat and no tenant in
  `TenancyCallers.BeginNone()` when it runs in the middle of somebody's work. Then the surrounding tenant does not
  apply to that read.

### Explicit callers

`services.RequireExplicitCallers()` makes work fail when the work does not state who it runs as. Without it, work
that states no caller runs as the application. The sample uses it
([Fail-closed callers](../row-level-security.md#fail-closed-callers)).
