---
title: "Tenancy: keeping tenants apart"
sidebar_label: Keeping tenants apart
---

# Tenancy: keeping tenants apart

Tenancy keeps every read and every save inside the caller's tenant, on any database. This page describes the two
checks that do this, and how you start system work, which is work that no person does. Read it when you add a
table that belongs to a tenant, configure a context, or write a job, an import or a seeding.

## The tenant filter and the save check

Tenancy adds two checks in C#, so they work on every database. `AddTenancy()` adds them to Tenancy's classes. A
class of yours gets them when it marks the property that holds its tenant with `[OfTenant]`:

```csharp
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy;

[AggregateRoot<ProjectId>]
public sealed partial class Project
{
    [OfTenant]
    public TenantId TenantId { get; private set; }
}
```

The marker is a fact about the class, like `[Owner]`, so it goes on the domain class. `AddDDDToolkitConventions()` in
the context's `ConfigureConventions` finds the marker and adds both checks. The context needs no line for the class.

- **The marker's package.** `[OfTenant]` is in `DDDToolkit.Supporting.Tenancy.Abstractions`, in the namespace
  `DDDToolkit.Supporting.Tenancy`. The package holds Tenancy's markers for domain classes and has no Entity Framework.
  Tenancy's own package references it. A module's domain project that sees only Tenancy's ids references the marker's
  package by itself.
- **The marker names its convention.** The marker carries `[ModelConvention]`, which names `OfTenantConvention` in
  Tenancy's Entity Framework package. A context that maps a marked class while that package is not in the
  application is refused while its model is built, and the message names the package to reference.
- **A role class of Membership.** A class declared with `[CustomRole<…>]` that marks its tenant also gets two unique
  indexes from Membership's mapping: a name once per tenant, and a starter role once per tenant. Entity Framework names
  them `IX_{table}_TenantId_Name` and `IX_{table}_TenantId_MadeFrom`. A refusal of your own for a name used twice goes
  on the same index: `role.HasIndex(row => new { row.TenantId, row.Name }).RefusesAs(code, message)`.

For a class without the marker, or a tenant that the context reads from another property, call `ScopeToTenant` in
`OnModelCreating`. The call wins over the marker:

```csharp
modelBuilder.Entity<Delivery>().ScopeToTenant(delivery => delivery.ShipperTenantId);
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
| A module's context with `[OfTenant]` or `ScopeToTenant` | Checks the rows of the classes that are kept to a tenant. |
| A context with neither | Nothing. |

### A context without the save check

- `[OfTenant]` and `ScopeToTenant` record in the model that its context needs the save check
  ([A part a model cannot do without](../entity-framework.md#parts-a-model-requires)). A context without
  the check fails at its first save, before anything is written. The error message names the missing call.
- A context whose `ConfigureConventions` leaves out `AddDDDToolkitConventions()` keeps no marked class to a tenant.
  The save check refuses its first save, and the error message names the missing call.
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
it for work that no request asked for, such as seeding, an import or a nightly job. Also start it for work in a
handler that no caller of the request could do, such as
[provisioning a tenant](who-may-do-what.md#requests-that-provision-a-tenant). Your code takes `TenancyAccessManager`
and starts the work with one of its `Begin…` methods:

```csharp
// An import that an administrator started. It runs in a job, so no request carries the tenant.
public sealed class UnitImport(OrganizationManager organization, TenancyAccessManager access)
{
    public async Task RunAsync(ImportJob job, CancellationToken cancellationToken)
    {
        using (access.BeginSystemIn(job.Tenant, actingSeat: job.StartedBy))
        {
            foreach (var name in job.UnitNames)
            {
                await organization.AddUnitAsync(job.Parent, name, cancellationToken);
            }
        }
    }
}
```

Each `Begin…` call starts two callers, Tenancy's caller and the toolkit's caller. Disposing the result ends both, and
the caller from before is current again. The generator writes `TenancyAccessManager` closed over your ids
([Calls closed over your ids](your-module.md#calls-closed-over-your-ids)), so you write no id type. `AddTenancy`
registers it per scope.

| Call | Tenancy's caller | The toolkit's caller, `Caller.` |
|---|---|---|
| `access.BeginSystem()` | Works outside any tenant. It provisions tenants, and reads or writes no tenant's rows. | `SystemIn("tenancy")` |
| `access.BeginSystemIn(tenant, actingSeat, scope)` | Works inside the tenant. It holds every permission there and none elsewhere. | `SystemIn(scope)`, `"tenancy"` by default |
| `access.BeginTokenIn(tenant, seat, scope)` | Works like `BeginSystemIn`. The work is recorded as a link's token and the seat the token stands for. | `SystemIn(scope)`. The scope is required. |
| `[Operator]` on a command | Your code calls nothing. The access check starts the work around the handler. It works like `BeginSystem`, or like `BeginSystemIn` in the tenant the command names. The work is recorded as the operator who sent the command ([Operators](operators-and-history.md#operators)). | `SystemIn(scope)`, `"tenancy"` by default |

- `actingSeat` is recorded as the seat that placed or granted. It adds no rights. Pass the seat of the person who
  asked for the work, such as the administrator who starts an import. Leave it out when nobody asked.
- The last-administrator rule applies to system work in a tenant too.
- A scope is lower case letters, digits, `_` and `-`. Any other scope throws `ArgumentException`, and nothing
  starts.
- Put the `using` around the one call that needs system work. The rest of the handler then runs as the caller.
- System work does what it is told. Take its tenant, seat, role and unit from `access.CurrentTenant` or from your own
  records. A role taken from the request would let the sender choose any role, the administrators' role included.
- No method starts operator work. Only `[Operator]` on a request does, and the operator is the current user. So no
  code can record work as an operator who did not send the request.

### Which call for which situation

Eight situations follow. For each one, the chooser names the call or the attribute and says why it fits. It also
shows what the work holds, how Tenancy records it, and which database role it runs as on Postgres.

<div data-interactive="WhoActs">

| Situation | Use | Holds | Recorded as | On Postgres, runs as |
|---|---|---|---|---|
| A user signs up and creates an organization | `access.BeginSystem()` | Nothing in any tenant. Only `ProvisionAsync` accepts it. | `system` | `ddd_system_in`, scope `tenancy`, no tenant |
| Your staff create a tenant for a customer | `[Operator]` on the command | As `BeginSystem()`, for the whole handler | `operator`, with the operator's id | `ddd_system_in`, scope `tenancy`, no tenant |
| Your staff change a tenant | `[Operator]` and a `[ForTenant]` member | Every permission in that tenant, for the whole handler | `operator`, with the operator's id | `ddd_system_in`, scope `tenancy`, that tenant |
| Demo data is seeded at start-up | `access.BeginSystemIn(tenant, actingSeat: admin)` | Every permission in that tenant | `system`; the events name the acting seat | `ddd_system_in`, scope `tenancy`, that tenant |
| A nightly job visits every tenant | `access.TenantsToSweepAsync(scope)`, then `access.BeginSystemIn(tenant, scope: "inspections")` | Every permission on the module's rows in each tenant. It reads Tenancy's tables and writes none. | `system`, in the module's scope | `ddd_system_in`, the module's scope, each tenant in turn |
| Your code grants a role after a condition | `access.BeginSystemIn(tenant)`, with no acting seat | Every permission in that tenant | `system`, with the grant's reason | `ddd_system_in`, scope `tenancy`, that tenant |
| A link carries a token instead of a sign-in | `access.BeginTokenIn(tenant, seat, scope)` | Every row of the module in that tenant. Your code reads only the seat's rows. | `token`, with the seat the token stands for | `ddd_system_in`, the module's scope, that tenant |
| An administrator imports seats | Nothing. The handler runs as the caller. | What the caller's seat holds | `seat`, with the caller's seat | `authenticated`, the caller's own role |

</div>

### The scoped system caller

- Neither caller is `Caller.System`. `Caller.System` stands for system work, and row level security lets
  it past every policy. On Postgres the scoped caller runs as a database role that cannot bypass the policies, and
  the policies decide which rows it reaches
  ([The scoped system role](../row-level-security.md#the-scoped-system-role)).
- The scope names whose work the system work is:
  - `tenancy` is Tenancy's scope, for work that calls Tenancy's managers. On Postgres only work in this scope
    writes Tenancy's tables.
  - A module's name is the scope for that module's work in a tenant, and for its work on its resources with members
    ([Who holds a permission](../membership/permissions-and-roles.md#who-holds-a-permission)). Work in a module's
    scope can read Tenancy's tables and cannot write them.

### System work and request requirements

- A request cannot declare system work as its requirement. The requirement decides who may send the request. The
  handler then starts system work itself, in trusted code, when the work needs it. For example, a handler that
  provisions a tenant starts system work whether its request requires `[AllowAnonymous]` (a public registration
  form) or `[SignedIn]`. When the system work ends, the caller from before it is current again.
- `[Operator]` is the one requirement that also starts work. The access check lets only an operator through, and on a
  command it runs the handler as operator work. On a query it starts nothing.
- A request that system work sends inside a tenant declares who may send it with `[InTenant]`. A second check, after
  that requirement, refuses everything that only system work may do:
  - The handler checks the caller. The handler of the sample's `MarkTenantAsDemo` calls
    `access.CurrentTenant.Require()` and refuses the caller with `access.system-only` unless `BySystem` is true.
  - Tenancy's system-only manager methods refuse every other caller with `access.system-only`. These are
    `TenantManager.ProvisionAsync`, `SuspendAsync`, `ReactivateAsync` and `CloseAsync`.
- Code that runs without a started caller is nobody to Tenancy. This holds where the toolkit's caller is
  `Caller.System`, and in a host that does not [require explicit callers](../row-level-security.md#requiring-explicit-callers).
  Tenancy's managers refuse a caller that is nobody with `tenancy.not-seated`.

### System work outside any tenant

System work outside any tenant (`access.BeginSystem()`, or `[Operator]` on a command without a tenant) reads no
tenant's rows, because the tenant filter matches no row for it. The save check refuses each row of a tenant with
`tenancy.other-tenant`. So `ProvisionAsync` first makes the new tenant's id, and then checks the slug and saves as
system work inside the new tenant.

### Reads before any tenant is known

Two reads run before any tenant is known. Each one is a single query that returns ids or permissions only:

- The start-up check reads the permissions stored on the roles of every tenant.
- `access.TenantsToSweepAsync(scope, cancellationToken)` reads the tenants that a round of system work visits, the
  active and the suspended ones. A job reads them first, and then starts system work in each tenant in turn:

```csharp
public sealed class EndExpiredOrdersJob(IServiceScopeFactory scopes)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<TenancyAccessManager>();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderStore>();

        foreach (var tenant in await access.TenantsToSweepAsync("ordering", cancellationToken))
        {
            using (access.BeginSystemIn(tenant, scope: "ordering"))
            {
                await orders.EndWhatHasRunOutAsync(cancellationToken);
            }
        }
    }
}
```

- **The caller of the read.** Tenancy starts a caller for the read alone. On a database where only the tenant
  filter keeps tenants apart (SQLite), the read runs as `Caller.System` over the tables. With
  [row level security](postgres.md), the read runs as scoped system work in no tenant and calls a database function
  ([Reads across tenants](postgres.md#reads-across-tenants)).
- **A separate context.** The read runs on a new context from a new service scope, outside any transaction you
  started. So your transaction cannot change who the read runs as, and the read's wider access does not carry over
  into your work.
- **A separate connection.** The read opens its own connection unless your contexts share one. Call it before you
  start a transaction. Inside a transaction, your code holds one connection while the read waits for another. With
  few connections in the data source, the code can then wait for itself
  ([With a context pool](../row-level-security.md#with-a-context-pool)). Tenancy's
  managers run on one connection.
- **A read that belongs to no tenant.** Wrap a read of yours that belongs to no seat and no tenant in
  `TenancyCallers.BeginNone()` when it runs in the middle of somebody's work. Then the surrounding tenant does not
  apply to that read.

### Explicit callers

`services.RequireExplicitCallers()` makes work fail when the work does not state who it runs as. Without it, work
that states no caller runs as the application. The sample uses it
([Fail-closed callers](../row-level-security.md#requiring-explicit-callers)).
