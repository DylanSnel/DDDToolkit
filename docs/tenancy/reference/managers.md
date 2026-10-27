---
title: "Tenancy API: Managers"
sidebar_label: Managers
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Tenancy API: Managers

The managers, one per aggregate: `TenantManager`, `OrganizationManager`, `SeatManager`, `RoleManager` and `InvitationManager`. Your code takes each one by that name, with no type arguments. The generator writes each one into the project that declares the application's Tenancy classes, as a sealed class that derives from the package's class closed over those classes: `SeatManager` derives from `SeatManager<…>`. A type nested in the package's class is named through the generated class, such as `SeatManager.SeatOverview`. This page names them that way. `InvitationManager` is written only where the project declares an invitation class. `AddTenancy<TContext>()` registers the first four per scope, and `AddTenancyInvitations<TContext>()` registers `InvitationManager`.

A command that works on an aggregate has two overloads: one takes the domain objects, the other their ids. Both load the aggregate and check the caller's permissions in the same way. A command that creates an aggregate returns it, and a command that changes one returns it as changed. A read returns the application's own classes.

The nine type parameters are filled with:

| Type parameter | Filled with | Constraints |
|---|---|---|
| `TTenant` | your Tenant class, declared with `[TenantAggregate<TenantId>]` | `TenantAggregate<TTenantId>` |
| `TTenantId` | the id it is declared with, in `[TenantAggregate<TenantId>]` | `struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>` |
| `TOrganization` | your Organization class, declared with `[OrganizationAggregate<TenantId>]` | `OrganizationAggregate<TTenantId, TUnit, TUnitId>` |
| `TUnit` | your OrganizationUnit class, declared with `[OrganizationUnit<UnitId>]` | `OrganizationUnitEntity<TUnitId>` |
| `TUnitId` | the id it is declared with, in `[OrganizationUnit<UnitId>]` | `struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>` |
| `TSeat` | your Seat class, declared with `[SeatAggregate<SeatId>]` | `SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>` |
| `TSeatId` | the id it is declared with, in `[SeatAggregate<SeatId>]` | `struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>` |
| `TRole` | your Role class, declared with `[RoleAggregate<RoleId>]` | `RoleAggregate<TRoleId, TTenantId>` |
| `TRoleId` | the id it is declared with, in `[RoleAggregate<RoleId>]` | `struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>` |

The stores a storage package implements are under [Stores](#stores). The registration a host calls, from all three packages, is under [Registration](#registration).

## Tenants

Namespace `DDDToolkit.Supporting.Tenancy.Managers`.

### `TenantManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`

```csharp
public class TenantManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
```

A tenant's life and settings: provisioning it with everything it needs to be used, changing its shape, and suspending, reactivating or closing it; and, for the application's operators, the tenants there are. Your code takes it as `TenantManager`, a class the generator writes, closed over your classes and ids, and the container makes per scope. Each command returns the tenant as it changed it, the one the unit of work keeps.

Provisioning is the one thing system work outside any tenant does. Suspending, reactivating and closing are system work inside the tenant, and act on that tenant: whoever carries them out begins `access.BeginSystemIn(tenant)` first, or sends a command marked `[Operator]` with the tenant on a `[ForTenant]` member, which runs as operator work in that tenant, so acting on another tenant cannot be expressed.

The organization's name is the tenant's name, and `OrganizationManager.RenameAsync` changes it.

| Member | Summary |
|---|---|
| `MostPerPage = 200` | How many tenants a page of [`ListAsync`](#listasyncstring-int-cancellationtoken) holds at most. |
| [`TenantManager(ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>, TenancyCatalogue, TimeProvider, TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>, ICurrentUser, IStarterRoleTexts?)`](#tenantmanageritenancystorettenant-ttenantid-torganization-tunit-tunitid-tseat-tseatid-trole-troleid-tenancycatalogue-timeprovider-tenancyoptionsttenantid-tseatid-tunitid-troleid-icurrentuser-istarterroletexts) | The primary constructor: the type's summary says what it makes. |
| [`ProvisionAsync(TenantToProvision<TTenant, TTenantId, TUnit, TUnitId, TSeat, TSeatId, TRoleId>, CancellationToken)`](#provisionasynctenanttoprovisionttenant-ttenantid-tunit-tunitid-tseat-tseatid-troleid-cancellationtoken) | Provisions a tenant, in one save: the tenant, its organization with the root, a role copied from every starter role seeded for its shape, and a first seat for the administrator's identity, placed at the root and granted the administrators' role there with no end. |
| [`ChangeShapeAsync(TenantShape, IReadOnlyDictionary<string, TRoleId>?, CultureInfo?, CancellationToken)`](#changeshapeasynctenantshape-ireadonlydictionarystring-troleid-cultureinfo-cancellationtoken) | Changes a flat tenant into a hierarchical one, and gives it a copy of every starter role seeded for the new shape it has no copy of yet. |
| [`SuspendAsync(string, CancellationToken)`](#suspendasyncstring-cancellationtoken) | Suspends the caller's tenant: system work in that tenant only. |
| [`ReactivateAsync(CancellationToken)`](#reactivateasynccancellationtoken) | Puts the caller's suspended tenant back in use: system work in that tenant only. |
| [`CloseAsync(string, CancellationToken)`](#closeasyncstring-cancellationtoken) | Closes the caller's tenant for good: system work in that tenant only. |
| [`ListAsync(string?, int, CancellationToken)`](#listasyncstring-int-cancellationtoken) | A page of the tenants of the application, for its operators, by slug: those after the last tenant of the page before, or the first ones. |

#### `TenantManager(ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>, TenancyCatalogue, TimeProvider, TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>, ICurrentUser, IStarterRoleTexts?)`

```csharp
public TenantManager(
    ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> store,
    TenancyCatalogue catalogue,
    TimeProvider clock,
    TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> options,
    ICurrentUser user,
    IStarterRoleTexts? starterRoleTexts = null)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `store`: Where the tenant is loaded and saved.
- `catalogue`: The starter roles a new tenant is given.
- `clock`: What "now" is.
- `options`: Which token roles are operators', for the tenants an operator lists.
- `user`: Who is calling, as the toolkit says: the operator that lists the tenants.
- `starterRoleTexts`: The starter roles' names and descriptions in a tenant's language, when the application registered some; otherwise every role made from a starter role gets the catalogue's texts, but the role of the default administrators' starter role, which the package adds, gets the package's own texts in that language, English or Dutch.

#### `ProvisionAsync(TenantToProvision<TTenant, TTenantId, TUnit, TUnitId, TSeat, TSeatId, TRoleId>, CancellationToken)`

```csharp
public Task<TTenant> ProvisionAsync(
    TenantToProvision<TTenant, TTenantId, TUnit, TUnitId, TSeat, TSeatId, TRoleId> command,
    CancellationToken cancellationToken)
```

Provisions a tenant, in one save: the tenant, its organization with the root, a role copied from every starter role seeded for its shape, and a first seat for the administrator's identity, placed at the root and granted the administrators' role there with no end. That role holds what the administrators' starter role seeded for the shape holds, which is every live permission unless the starter role lists its own. The tenant is active when the save is done.

The roles are named as the catalogue names its starter roles, or, with a language and an [`IStarterRoleTexts`](catalogue.md#istarterroletexts) registered, in that language. The role of [`TenancyStarterRoles.DefaultAdministrators`](catalogue.md#defaultadministrators), for an application that declares no administrators' starter role, is named in that language by the application's texts, or else by the package's own, in English or Dutch. The fields the application added to its tenant, unit and seat classes are set by the command's callbacks, before the tenant is activated and in the same save.

Only system work outside any tenant provisions one, and it does not do so with that power: once it knows the new tenant's id, everything else, the check that the slug is free and the save included, runs as system work inside the new tenant ([`TenancyWork.BeginSystemIn<TTenantId, TSeatId>`](callers.md#beginsysteminttenantid-tseatid-string-1)), so it writes that tenant's rows and nothing else. On a database that keeps tenants apart by itself, such as Postgres with row level security, the slug check then sees no other tenant, and the unique index on the slug is what refuses one that is taken.

The organization, the first seat and the roles belong to the tenant, and are read through the other managers by a request that names the new tenant. Imports and seeding that need fixed ids pass them in the command.

**Parameters**

- `command`: What to provision.
- `cancellationToken`: Cancels the work.

**Returns**: The new tenant, active.

**Exceptions**

- `RefusalException`: `access.system-only` for a seat, `tenancy.slug-taken`, `tenancy.role-name-taken` when two starter roles are named alike in the tenant's language, and what the aggregates refuse: an invalid name, a starter role's translated one included, or an empty identity.
- `InvalidValueObjectException`: The slug does not follow [`TenantSlug.Pattern`](aggregates.md#tenantslug).
- `ArgumentException`: The command's role ids name a starter role the tenant is not given, or give one id twice.
- `InvalidOperationException`: The caller is system work in a tenant rather than outside any.

**Example**

An operator's command that names no tenant runs as operator work in no tenant, which provisioning needs. A sign-up form begins `access.BeginSystem()` around the call instead.

```csharp
[Operator]
public sealed record ProvisionTenant(string Slug, string Name, Guid Administrator) : ICommand<Tenant>, ITenancyRequest;

// In its handler:
return await tenants.ProvisionAsync(
    new TenantToProvision(command.Slug, command.Name, TenantShape.Hierarchical, RootName: command.Name, AdminIdentity: command.Administrator),
    cancellationToken);
```

#### `ChangeShapeAsync(TenantShape, IReadOnlyDictionary<string, TRoleId>?, CultureInfo?, CancellationToken)`

```csharp
public Task<TTenant> ChangeShapeAsync(
    TenantShape to,
    IReadOnlyDictionary<string, TRoleId>? roleIds,
    CultureInfo? language,
    CancellationToken cancellationToken)
```

Changes a flat tenant into a hierarchical one, and gives it a copy of every starter role seeded for the new shape it has no copy of yet. No grant changes: who held what at the root still does. Where the two shapes have administrators' starter roles of their own, the tenant gets the role of the starter role seeded for the new shape with the permissions that starter role holds, and nobody is given it: the administrators keep the role they have, and give the new one as they see fit.

**Parameters**

- `to`: The new shape.
- `roleIds`: The ids of the new roles, by starter role key, for imports and seeding; the rest get new ids.
- `language`: The language the new roles are named in, usually the tenant's, which the application keeps: each starter role's texts are asked of the application's [`IStarterRoleTexts`](catalogue.md#istarterroletexts) in it. `null`, or an application that registered no texts, keeps the catalogue's own; the default administrators' starter role, which the package adds, gets the package's own texts in that language, English or Dutch, where the application's have none for it. The roles the tenant has already keep their names.
- `cancellationToken`: Cancels the work.

**Returns**: The tenant, in its new shape.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SettingsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.shape-change`, `tenancy.tenant-state` for a closed tenant, `tenancy.role-name-taken` when a role of the tenant, or another new role, already has a new role's name, `tenancy.name-invalid` for a starter role's translated name or description.
- `ArgumentException`: `roleIds` names a starter role of no role that would be made, or gives one id twice.

**Example**

```csharp
var tenant = await tenants.ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, cancellationToken);
```

#### `SuspendAsync(string, CancellationToken)`

```csharp
public Task<TTenant> SuspendAsync(string reason, CancellationToken cancellationToken)
```

Suspends the caller's tenant: system work in that tenant only.

**Parameters**

- `reason`: Why; required.
- `cancellationToken`: Cancels the work.

**Returns**: The tenant, suspended.

**Exceptions**

- `RefusalException`: `access.system-only` for a seat, `tenancy.tenant-state`, `tenancy.reason-required`.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Example**

An operator's command. `[Operator]` runs its handler as operator work in the tenant of the `[ForTenant]` member:

```csharp
[Operator]
public sealed record SuspendTenant([ForTenant] TenantId Tenant, string Reason) : ICommand<Tenant>, ITenancyRequest;

// In its handler:
return await tenants.SuspendAsync(command.Reason, cancellationToken);
```

#### `ReactivateAsync(CancellationToken)`

```csharp
public Task<TTenant> ReactivateAsync(CancellationToken cancellationToken)
```

Puts the caller's suspended tenant back in use: system work in that tenant only.

**Parameters**

- `cancellationToken`: Cancels the work.

**Returns**: The tenant, active again.

**Exceptions**

- `RefusalException`: `access.system-only` for a seat, `tenancy.tenant-state`.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Example**

A job that puts a tenant back in use once its invoice is paid:

```csharp
using (access.BeginSystemIn(invoice.Tenant))
{
    await tenants.ReactivateAsync(cancellationToken);
}
```

#### `CloseAsync(string, CancellationToken)`

```csharp
public Task<TTenant> CloseAsync(string reason, CancellationToken cancellationToken)
```

Closes the caller's tenant for good: system work in that tenant only.

**Parameters**

- `reason`: Why; required.
- `cancellationToken`: Cancels the work.

**Returns**: The tenant, closed.

**Exceptions**

- `RefusalException`: `access.system-only` for a seat, `tenancy.tenant-state`, `tenancy.reason-required`.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Example**

In the handler of an operator's command marked `[Operator]`, with the tenant on a `[ForTenant]` member:

```csharp
return await tenants.CloseAsync(command.Reason, cancellationToken);
```

#### `ListAsync(string?, int, CancellationToken)`

```csharp
public Task<TenantPage> ListAsync(string? after, int size, CancellationToken cancellationToken)
```

A page of the tenants of the application, for its operators, by slug: those after the last tenant of the page before, or the first ones. Each comes with its name and how many of its seats are in use. It reads, and changes nothing.

Only an operator asks: a signed-in user whose token carries one of [`TenancyOptions.OperatorTokenRoles`](#operatortokenroles). An operator holds no seat and acts in no tenant, so this is the one read that asks the toolkit's current user rather than the Tenancy caller. It reads as that caller, never as the system: on a database that keeps tenants apart by itself, such as Postgres with row level security, the operator's own database role and its policies decide what it reads, and a host that mapped no such role reads nothing.

The application's own work that has to visit every tenant, a sweep say, does not go through here: it asks which tenants there are in a scope of its own, `access.TenantsToSweepAsync(scope)`, and then works in each.

**Parameters**

- `after`: [`TenantManager.TenantPage.Next`](#tenantmanagertenantpage) of the page before, or `null` for the first page. It is a marker the manager made, and means nothing to anyone else.
- `size`: How many tenants the page holds: 1 to [`MostPerPage`](#tenantmanagerttenant-ttenantid-torganization-tunit-tunitid-tseat-tseatid-trole-troleid).
- `cancellationToken`: Cancels the read.

**Returns**: The page: each tenant with its name and its number of active seats, and the marker of the next page, or `null` on the last page.

**Exceptions**

- `RefusalException`: `tenancy.operators-only` for anyone but an operator, checked before anything else, so nobody else learns what a page holds; `tenancy.page-size-invalid`, with `Max`; `tenancy.cursor-invalid` for a marker the manager did not make. Each of the last two names its input in the argument `Field`: `size` and `after`.

**Example**

The handler of an operator's query marked `[Operator]`:

```csharp
var page = await tenants.ListAsync(query.After, size: 50, cancellationToken);
// page.Items: each listing's Tenant, Name and ActiveSeats; page.Next: the marker of the next page
```

### `TenantToProvision<TTenant, TTenantId, TUnit, TUnitId, TSeat, TSeatId, TRoleId>`

```csharp
public record TenantToProvision<TTenant, TTenantId, TUnit, TUnitId, TSeat, TSeatId, TRoleId>
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
```

What a new tenant is provisioned with, for `TenantManager.ProvisionAsync`. Your code makes it as `TenantToProvision`, a record the generator writes, closed over your classes and ids:

```csharp
var tenant = await tenants.ProvisionAsync(new TenantToProvision(
    Slug: command.Slug,
    Name: command.Name,
    Shape: TenantShape.Hierarchical,
    RootName: command.Name,
    AdminIdentity: admin), cancellationToken);
```

Imports and seeding that need fixed ids pass them in: `TenantId`, `RootId`, `AdminSeatId` and `RoleIds`.

| Member | Summary |
|---|---|
| [`TenantToProvision(string, string, TenantShape, string, Guid, TTenantId?, TUnitId?, TSeatId?, IReadOnlyDictionary<string, TRoleId>?, CultureInfo?, Action<TTenant>?, Action<TUnit>?, Action<TSeat>?)`](#tenanttoprovisionstring-string-tenantshape-string-guid-ttenantid-tunitid-tseatid-ireadonlydictionarystring-troleid-cultureinfo-actionttenant-actiontunit-actiontseat) | The primary constructor: the type's summary says what it makes. |
| `string Slug { get; init; }` | The slug it is selected by; trimmed and lowercased. |
| `string Name { get; init; }` | The tenant's name, which is its organization's. |
| `TenantShape Shape { get; init; }` | Whether its organization is flat or a tree. |
| `string RootName { get; init; }` | The root unit's name. |
| `Guid AdminIdentity { get; init; }` | The verified identity of the first administrator. What their seat is shown by is the application's, set in `ConfigureFirstSeat` when its seat class keeps a name. |
| `TTenantId? TenantId { get; init; }` | The tenant's id, for imports and seeding; a new one otherwise. |
| `TUnitId? RootId { get; init; }` | The root's id, for imports and seeding; a new one otherwise. |
| `TSeatId? AdminSeatId { get; init; }` | The first seat's id, for imports and seeding; a new one otherwise. |
| `IReadOnlyDictionary<string, TRoleId>? RoleIds { get; init; }` | The ids of the roles copied from the starter roles, by starter role key; the others get new ids. |
| `CultureInfo? Language { get; init; }` | The language the tenant's roles are named in: each starter role's name and description are asked of the application's [`IStarterRoleTexts`](catalogue.md#istarterroletexts) in it. `null`, or an application that registered no texts, keeps the catalogue's own; the default administrators' starter role, which the package adds, falls back to the package's own texts in it, English or Dutch. The package keeps no language itself: where the tenant remembers its language is a field of the application's tenant class, set in `ConfigureTenant`. |
| `Action<TTenant>? ConfigureTenant { get; init; }` | Sets the fields the application added to its tenant class, on the new tenant before it is activated, so they are saved in the same transaction and a domain event the class raises there goes out with the provisioning. When it throws, nothing is saved. |
| `Action<TUnit>? ConfigureRoot { get; init; }` | The same for the fields the application added to its unit class, on the root: what kind of unit it is, say, when the class keeps one. A unit raises no events of its own; the organization raises the root's. |
| `Action<TSeat>? ConfigureFirstSeat { get; init; }` | The same for the first administrator's seat, which is placed at the root and holds the administrators' role by then: the name it is shown by, say, when the class keeps one. |

#### `TenantToProvision(string, string, TenantShape, string, Guid, TTenantId?, TUnitId?, TSeatId?, IReadOnlyDictionary<string, TRoleId>?, CultureInfo?, Action<TTenant>?, Action<TUnit>?, Action<TSeat>?)`

```csharp
public TenantToProvision(
    string Slug,
    string Name,
    TenantShape Shape,
    string RootName,
    Guid AdminIdentity,
    TTenantId? TenantId = null,
    TUnitId? RootId = null,
    TSeatId? AdminSeatId = null,
    IReadOnlyDictionary<string, TRoleId>? RoleIds = null,
    CultureInfo? Language = null,
    Action<TTenant>? ConfigureTenant = null,
    Action<TUnit>? ConfigureRoot = null,
    Action<TSeat>? ConfigureFirstSeat = null)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Slug`: The slug it is selected by; trimmed and lowercased.
- `Name`: The tenant's name, which is its organization's.
- `Shape`: Whether its organization is flat or a tree.
- `RootName`: The root unit's name.
- `AdminIdentity`: The verified identity of the first administrator. What their seat is shown by is the application's, set in `ConfigureFirstSeat` when its seat class keeps a name.
- `TenantId`: The tenant's id, for imports and seeding; a new one otherwise.
- `RootId`: The root's id, for imports and seeding; a new one otherwise.
- `AdminSeatId`: The first seat's id, for imports and seeding; a new one otherwise.
- `RoleIds`: The ids of the roles copied from the starter roles, by starter role key; the others get new ids.
- `Language`: The language the tenant's roles are named in: each starter role's name and description are asked of the application's [`IStarterRoleTexts`](catalogue.md#istarterroletexts) in it. `null`, or an application that registered no texts, keeps the catalogue's own; the default administrators' starter role, which the package adds, falls back to the package's own texts in it, English or Dutch. The package keeps no language itself: where the tenant remembers its language is a field of the application's tenant class, set in `ConfigureTenant`.
- `ConfigureTenant`: Sets the fields the application added to its tenant class, on the new tenant before it is activated, so they are saved in the same transaction and a domain event the class raises there goes out with the provisioning. When it throws, nothing is saved.
- `ConfigureRoot`: The same for the fields the application added to its unit class, on the root: what kind of unit it is, say, when the class keeps one. A unit raises no events of its own; the organization raises the root's.
- `ConfigureFirstSeat`: The same for the first administrator's seat, which is placed at the root and holds the administrators' role by then: the name it is shown by, say, when the class keeps one.

### `TenantManager.TenantListing`

```csharp
public sealed record TenantListing
```

A tenant as [`TenantManager.ListAsync`](#listasyncstring-int-cancellationtoken) lists it for an operator: the tenant, the application's own class, with what is not on it, its name and how many of its seats are in use. It names no person, and no row of the tenant's.

| Member | Summary |
|---|---|
| [`TenantListing(TTenant, string, int)`](#tenantlistingttenant-string-int) | The primary constructor: the type's summary says what it makes. |
| `TTenant Tenant { get; init; }` | The tenant, the application's own class, read for the answer and tracked by nobody. |
| `string Name { get; init; }` | Its name, which is its organization's. |
| `int ActiveSeats { get; init; }` | How many of its seats are active now. |

#### `TenantListing(TTenant, string, int)`

```csharp
public TenantListing(TTenant Tenant, string Name, int ActiveSeats)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Tenant`: The tenant, the application's own class, read for the answer and tracked by nobody.
- `Name`: Its name, which is its organization's.
- `ActiveSeats`: How many of its seats are active now.

### `TenantManager.TenantPage`

```csharp
public sealed record TenantPage
```

One page of the tenants an operator lists.

| Member | Summary |
|---|---|
| [`TenantPage(IReadOnlyList<TenantListing>, string?)`](#tenantpageireadonlylisttenantlisting-string) | The primary constructor: the type's summary says what it makes. |
| `IReadOnlyList<TenantListing> Items { get; init; }` | The tenants of the page, by slug. |
| `string? Next { get; init; }` | What to ask the next page with, or `null` when this is the last one. |

#### `TenantPage(IReadOnlyList<TenantListing>, string?)`

```csharp
public TenantPage(IReadOnlyList<TenantListing> Items, string? Next)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Items`: The tenants of the page, by slug.
- `Next`: What to ask the next page with, or `null` when this is the last one.

## Organization

Namespace `DDDToolkit.Supporting.Tenancy.Managers`.

### `OrganizationManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`

```csharp
public class OrganizationManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
```

The organization and its tree: renaming the organization, adding, renaming, moving and archiving units, and reading the units. Your code takes it as `OrganizationManager`, a class the generator writes, closed over your classes and ids, and the container makes per scope.

Each change of a unit takes the unit, or its id. Both check the same: the manager loads the organization itself and asks the caller's rights over what it loaded, never over the state of a unit it was handed. Each returns the unit as it changed it, the one the unit of work keeps.

Each change of a unit needs [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) where the change is made, so a seat that manages a region manages the units below it and nothing beside or above it. Two edits of the tree at the same time are kept apart by the organization's version. A move also changes what every permission held above the moved unit reaches, so it takes the access revision too.

The reads answer units as the application's own class, whole, with every field of its own, beside the path and depth that are not on it ([`OrganizationManager.UnitInTree`](#organizationmanagerunitintree)). Each is read for the answer and tracked by nobody. Paths are written from the root down, joined with `" / "`, and come from the closure of the tree, so a unit's path names the units above it even where the caller is not placed under those.

| Member | Summary |
|---|---|
| `MostIds = 200` | How many ids one question by id takes. |
| [`OrganizationManager(ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>, TenancyCatalogue, TimeProvider)`](#organizationmanageritenancystorettenant-ttenantid-torganization-tunit-tunitid-tseat-tseatid-trole-troleid-tenancycatalogue-timeprovider) | The primary constructor: the type's summary says what it makes. |
| [`RenameAsync(string, CancellationToken)`](#renameasyncstring-cancellationtoken) | Renames the organization, which is the tenant's name. |
| [`AddUnitAsync(TUnitId, string, CancellationToken, TUnitId?, Action<TUnit>?)`](#addunitasynctunitid-string-cancellationtoken-tunitid-actiontunit) | Adds a unit below `parent`. |
| [`AddUnitAsync(TUnit, string, CancellationToken, TUnitId?, Action<TUnit>?)`](#addunitasynctunit-string-cancellationtoken-tunitid-actiontunit) | Adds a unit below `parent`, as [`AddUnitAsync`](#addunitasynctunitid-string-cancellationtoken-tunitid-actiontunit) does with its id. |
| [`RenameUnitAsync(TUnitId, string, CancellationToken)`](#renameunitasynctunitid-string-cancellationtoken) | Renames a unit. |
| [`RenameUnitAsync(TUnit, string, CancellationToken)`](#renameunitasynctunit-string-cancellationtoken) | Renames a unit, as [`RenameUnitAsync`](#renameunitasynctunitid-string-cancellationtoken) does with its id. |
| [`MoveUnitAsync(TUnitId, TUnitId, CancellationToken)`](#moveunitasynctunitid-tunitid-cancellationtoken) | Moves a unit, and everything below it, under another unit. |
| [`MoveUnitAsync(TUnit, TUnit, CancellationToken)`](#moveunitasynctunit-tunit-cancellationtoken) | Moves a unit under another unit, as [`MoveUnitAsync`](#moveunitasynctunitid-tunitid-cancellationtoken) does with their ids. |
| [`ArchiveUnitAsync(TUnitId, CancellationToken)`](#archiveunitasynctunitid-cancellationtoken) | Archives a unit. |
| [`ArchiveUnitAsync(TUnit, CancellationToken)`](#archiveunitasynctunit-cancellationtoken) | Archives a unit, as [`ArchiveUnitAsync`](#archiveunitasynctunitid-cancellationtoken) does with its id. |
| [`ListUnitsAsync(CancellationToken)`](#listunitsasynccancellationtoken) | The units the caller reads, by path: for a seat, the units it is placed in and every unit below them; for system work in the tenant, every unit. |
| [`UnitsByIdAsync(IReadOnlyCollection<TUnitId>, CancellationToken)`](#unitsbyidasyncireadonlycollectiontunitid-cancellationtoken) | What the units among `ids` that are the caller's tenant's are called, by path, whichever of them the caller is placed under: a seat that works on something at a unit it is not placed under still reads what that unit is called, and so does one an application's restrictive rule keeps out of the organization. |

#### `OrganizationManager(ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>, TenancyCatalogue, TimeProvider)`

```csharp
public OrganizationManager(
    ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> store,
    TenancyCatalogue catalogue,
    TimeProvider clock)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `store`: Where the organization is loaded and saved.
- `catalogue`: The permissions asked for.
- `clock`: What "now" is.

#### `RenameAsync(string, CancellationToken)`

```csharp
public Task<TOrganization> RenameAsync(string name, CancellationToken cancellationToken)
```

Renames the organization, which is the tenant's name.

**Parameters**

- `name`: Its new name.
- `cancellationToken`: Cancels the work.

**Returns**: The organization, renamed.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SettingsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.name-invalid`.

**Example**

```csharp
var renamed = await organization.RenameAsync("Harbor Works Group", cancellationToken);
```

#### `AddUnitAsync(TUnitId, string, CancellationToken, TUnitId?, Action<TUnit>?)`

```csharp
public Task<TUnit> AddUnitAsync(
    TUnitId parent,
    string name,
    CancellationToken cancellationToken,
    TUnitId? id = null,
    Action<TUnit>? configure = null)
```

Adds a unit below `parent`.

**Parameters**

- `parent`: The unit it hangs under.
- `name`: Its name.
- `cancellationToken`: Cancels the work.
- `id`: Its id, for imports and seeding; a new one otherwise.
- `configure`: Sets the fields the application added to its unit class, such as what kind of unit it is, on the new unit once the caller and the tree are checked and before the organization takes the unit in, so they are saved in the same transaction and the class's own rules judge them there. When it throws, the unit is not added: nothing is saved, by this call or by a later save in the same scope.

**Returns**: The new unit.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) at the parent, and what the organization refuses, `tenancy.flat-tenant` first.
- `ArgumentException`: `id` is already a unit's: a mistake in the import or seeding that gave it, not a refusal.

**Example**

A handler that adds a unit from a request, and sets a field the application added to its unit class:

```csharp
var unit = await organization.AddUnitAsync(command.Parent, command.Name, cancellationToken, configure: added => added.SetKind(command.Kind));
```

#### `AddUnitAsync(TUnit, string, CancellationToken, TUnitId?, Action<TUnit>?)`

```csharp
public Task<TUnit> AddUnitAsync(
    TUnit parent,
    string name,
    CancellationToken cancellationToken,
    TUnitId? id = null,
    Action<TUnit>? configure = null)
```

Adds a unit below `parent`, as [`AddUnitAsync`](#addunitasynctunitid-string-cancellationtoken-tunitid-actiontunit) does with its id.

**Parameters**

- `parent`: The unit it hangs under.
- `name`: Its name.
- `cancellationToken`: Cancels the work.
- `id`: Its id, for imports and seeding; a new one otherwise.
- `configure`: Sets the fields the application added to its unit class, such as what kind of unit it is, on the new unit once the caller and the tree are checked and before the organization takes the unit in, so they are saved in the same transaction and the class's own rules judge them there. When it throws, the unit is not added: nothing is saved, by this call or by a later save in the same scope.

**Returns**: The new unit.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) at the parent, and what the organization refuses, `tenancy.flat-tenant` first.
- `ArgumentException`: `id` is already a unit's: a mistake in the import or seeding that gave it, not a refusal.

**Example**

```csharp
var northCoast = await organization.AddUnitAsync(north, "North Coast", cancellationToken);
```

#### `RenameUnitAsync(TUnitId, string, CancellationToken)`

```csharp
public Task<TUnit> RenameUnitAsync(TUnitId unit, string name, CancellationToken cancellationToken)
```

Renames a unit.

**Parameters**

- `unit`: The unit.
- `name`: Its new name.
- `cancellationToken`: Cancels the work.

**Returns**: The unit, renamed.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) at the unit, and what the organization refuses.

**Example**

```csharp
var unit = await organization.RenameUnitAsync(command.Unit, command.Name, cancellationToken);
```

#### `RenameUnitAsync(TUnit, string, CancellationToken)`

```csharp
public Task<TUnit> RenameUnitAsync(TUnit unit, string name, CancellationToken cancellationToken)
```

Renames a unit, as [`RenameUnitAsync`](#renameunitasynctunitid-string-cancellationtoken) does with its id.

**Parameters**

- `unit`: The unit.
- `name`: Its new name.
- `cancellationToken`: Cancels the work.

**Returns**: The unit, renamed.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) at the unit, and what the organization refuses.

**Example**

```csharp
var renamed = await organization.RenameUnitAsync(northCoast, "North Coast Yard", cancellationToken);
```

#### `MoveUnitAsync(TUnitId, TUnitId, CancellationToken)`

```csharp
public Task<TUnit> MoveUnitAsync(
    TUnitId unit,
    TUnitId newParent,
    CancellationToken cancellationToken)
```

Moves a unit, and everything below it, under another unit. The caller needs the permission where the unit hangs now and where it goes, so a unit only moves between parts of the tree the caller manages both of. A move changes which grants reach the unit, so a seat moves it only as far as it could give and take away what that changes: the move gives the seat itself no permission it does not hold at the unit already, and none for longer than it holds it there; and it gives or takes away no permission that manages access, from anyone, that the seat does not hold at the unit for at least as long as that grant runs. The second part is containment, which goes when the application turns it off ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)): then a permission that manages access follows a move as any other does, and the move still gives the seat itself nothing.

**Parameters**

- `unit`: The unit.
- `newParent`: The unit it hangs under from now on.
- `cancellationToken`: Cancels the work.

**Returns**: The unit, moved.

**Exceptions**

- `RefusalException`: `tenancy.unit-not-found`, `tenancy.not-permitted` without [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) at the current parent and at the new one, what the organization refuses, and `tenancy.grant-exceeds-own` naming the permissions the move would give the calling seat, or, while containment is on, give or take away from anyone, that it lacks at the unit, or lacks for long enough.

**Example**

```csharp
var moved = await organization.MoveUnitAsync(command.Unit, command.NewParent, cancellationToken);
```

#### `MoveUnitAsync(TUnit, TUnit, CancellationToken)`

```csharp
public Task<TUnit> MoveUnitAsync(TUnit unit, TUnit newParent, CancellationToken cancellationToken)
```

Moves a unit under another unit, as [`MoveUnitAsync`](#moveunitasynctunitid-tunitid-cancellationtoken) does with their ids.

**Parameters**

- `unit`: The unit.
- `newParent`: The unit it hangs under from now on.
- `cancellationToken`: Cancels the work.

**Returns**: The unit, moved.

**Exceptions**

- `RefusalException`: `tenancy.unit-not-found`, `tenancy.not-permitted` without [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) at the current parent and at the new one, what the organization refuses, and `tenancy.grant-exceeds-own` naming the permissions the move would give the calling seat, or, while containment is on, give or take away from anyone, that it lacks at the unit, or lacks for long enough.

**Example**

North Coast moves from North to South Bay. The grants held at North no longer reach it:

```csharp
var moved = await organization.MoveUnitAsync(northCoast, southBay, cancellationToken);
```

#### `ArchiveUnitAsync(TUnitId, CancellationToken)`

```csharp
public Task<TUnit> ArchiveUnitAsync(TUnitId unit, CancellationToken cancellationToken)
```

Archives a unit. The caller needs the permission at its parent: archiving takes the unit out of the part of the tree above it. Seats placed there and rights held there keep working.

**Parameters**

- `unit`: The unit.
- `cancellationToken`: Cancels the work.

**Returns**: The unit, archived.

**Exceptions**

- `RefusalException`: `tenancy.unit-not-found`, `tenancy.not-permitted` without [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) at the parent, and what the organization refuses.

**Example**

```csharp
var archived = await organization.ArchiveUnitAsync(command.Unit, cancellationToken);
```

#### `ArchiveUnitAsync(TUnit, CancellationToken)`

```csharp
public Task<TUnit> ArchiveUnitAsync(TUnit unit, CancellationToken cancellationToken)
```

Archives a unit, as [`ArchiveUnitAsync`](#archiveunitasynctunitid-cancellationtoken) does with its id.

**Parameters**

- `unit`: The unit.
- `cancellationToken`: Cancels the work.

**Returns**: The unit, archived.

**Exceptions**

- `RefusalException`: `tenancy.unit-not-found`, `tenancy.not-permitted` without [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) at the parent, and what the organization refuses.

**Example**

```csharp
var archived = await organization.ArchiveUnitAsync(northCoast, cancellationToken);
```

#### `ListUnitsAsync(CancellationToken)`

```csharp
public Task<IReadOnlyList<UnitInTree>> ListUnitsAsync(CancellationToken cancellationToken)
```

The units the caller reads, by path: for a seat, the units it is placed in and every unit below them; for system work in the tenant, every unit. Each is the application's own unit, whole, with its path and depth beside it ([`OrganizationManager.UnitInTree`](#organizationmanagerunitintree)), so a field the application added to its unit class, such as what kind of unit it is, is shown from the units read here, with no statement more.

**Parameters**

- `cancellationToken`: Cancels the read.

**Returns**: The units, each with its path and depth, by path. A seat that reads no unit of the organization gets none.

**Exceptions**

- `RefusalException`: The caller is nobody.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Example**

```csharp
foreach (var (unit, path, depth) in await organization.ListUnitsAsync(cancellationToken))
{
    // path: "Harbor Works / North / North Coast", depth: 3
}
```

#### `UnitsByIdAsync(IReadOnlyCollection<TUnitId>, CancellationToken)`

```csharp
public Task<IReadOnlyList<NamedUnit<TUnitId>>> UnitsByIdAsync(
    IReadOnlyCollection<TUnitId> ids,
    CancellationToken cancellationToken)
```

What the units among `ids` that are the caller's tenant's are called, by path, whichever of them the caller is placed under: a seat that works on something at a unit it is not placed under still reads what that unit is called, and so does one an application's restrictive rule keeps out of the organization. So the answer is each unit's name, path and depth, and nothing more of it: on a database that keeps its own policies it comes from Tenancy's function that runs as its owner and answers the ids asked alone. An id of another tenant, or of no unit, is left out without a word.

**Parameters**

- `ids`: The units asked about, at most [`MostIds`](#organizationmanagerttenant-ttenantid-torganization-tunit-tunitid-tseat-tseatid-trole-troleid) different ones.
- `cancellationToken`: Cancels the read.

**Returns**: Each unit's id, name, path and depth, by path.

**Exceptions**

- `RefusalException`: The caller is nobody, with its own code; or `tenancy.too-many-ids`.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Example**

The paths of the units a page of projects sits at, in one read:

```csharp
var named = await organization.UnitsByIdAsync([.. projects.Select(project => project.UnitId)], cancellationToken);
```

### `OrganizationManager.UnitInTree`

```csharp
public sealed record UnitInTree
```

A unit, the application's own and whole, with where it sits in the tree: its path from the root and how deep it is. The unit carries its id, its parent, its name, its status and every field the application added, such as what kind of unit it is; the path and the depth are not on it. They come from the closure and the units above it, which the answer does not carry, as a seat lists only the units under its placements, so the application would need a read more to work them out. So that is all this adds, and an application shows a unit by what it selects ([`OrganizationManager.ListUnitsAsync`](#listunitsasynccancellationtoken)).

**Remarks**

The unit was read for this answer and is tracked by nobody: nothing done to it is saved, by this question or by a save later in the same unit of work.

| Member | Summary |
|---|---|
| [`UnitInTree(TUnit, string, int)`](#unitintreetunit-string-int) | The primary constructor: the type's summary says what it makes. |
| `TUnit Unit { get; init; }` | The unit, the application's own class. |
| `string Path { get; init; }` | The names of the unit and the units above it, the root first, joined with `" / "`. |
| `int Depth { get; init; }` | How deep the unit is, the root being 1. |

#### `UnitInTree(TUnit, string, int)`

```csharp
public UnitInTree(TUnit Unit, string Path, int Depth)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Unit`: The unit, the application's own class.
- `Path`: The names of the unit and the units above it, the root first, joined with `" / "`.
- `Depth`: How deep the unit is, the root being 1.

**Remarks**

The unit was read for this answer and is tracked by nobody: nothing done to it is saved, by this question or by a save later in the same unit of work.

## Seats

Namespace `DDDToolkit.Supporting.Tenancy.Managers`.

### `SeatManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`

```csharp
public class SeatManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
```

People's seats: adding them, placing them in units, granting them roles there, and suspending, reactivating or deactivating them; and what the screens show of them, who the caller is and what any seat may do where. Your code takes it as `SeatManager`, a class the generator writes, closed over your classes and ids, and the container makes per scope.

Each command takes the domain objects, `GrantAsync(seat, role, at, ...)`, or their ids, as a request carries them. Both check the same: the manager loads the seat, the role and the unit itself, and asks the caller's rights over what it loaded, never over the state of an object it was handed. Each command returns the seat as it changed it, the one the unit of work keeps.

Holding [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at a unit is enough to give roles there. Its holder gives any active role of the tenant that manages no access to a seat placed there or below, and takes any such role away, without holding the role's permissions: to another seat for as long as it says, to itself for no longer than it holds [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) there. A role that manages access (see [`TenancyCatalogue.AccessManagingPermissionsOf`](catalogue.md#accessmanagingpermissionsofrolefacts)) stays contained: a seat gives it, and takes it away, only while holding there each of its permissions that manage access, for at least as long as the grant runs, and never gives it to itself. Withdrawing a placement takes its grants away, by the same rule, and so does suspending or deactivating a seat, while reactivating one gives its grants back by that rule too. System work in a tenant is not held to any of that. When the application turns containment off ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), a role that manages access goes as one that manages none: a grants manager gives every role, to another seat for as long as it says and to itself for no longer than it holds [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) there, and the permission a command asks where it acts is all a seat's status takes. Nothing leaves a tenant that has an administrator without one, whoever asks, and that is decided before the seat changes, so a refused command leaves nothing for a later save to write. A seat of another tenant is not found.

Nothing here names a seat: a seat has no name in Tenancy. What the application keeps on its seat class, a name it is shown by among it, it sets in the callback of [`AddAsync`](#addasyncguid-cancellationtoken-tseatid-actiontseat), and changes with a use case of its own, under a rule of its own.

The reads answer seats as the application's own class, whole: the seat with its identity, its placements and their grants and every field the application added. Each is read for the answer and tracked by nobody, so nothing done to one is saved, by the read or by a save later in the same unit of work. Whoever works in a tenant reads it: a seat of it, or system work in it. No permission is asked, so the request that asks says who may: what another seat holds where, above all, is a choice of the application's. On Postgres a read rule of the application's on its seat class may narrow which seats the caller reads.

| Member | Summary |
|---|---|
| `MostIds = 200` | How many ids one question by id takes. |
| [`SeatManager(ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>, TenancyCatalogue, TimeProvider)`](#seatmanageritenancystorettenant-ttenantid-torganization-tunit-tunitid-tseat-tseatid-trole-troleid-tenancycatalogue-timeprovider) | The primary constructor: the type's summary says what it makes. |
| [`AddAsync(Guid, CancellationToken, TSeatId?, Action<TSeat>?)`](#addasyncguid-cancellationtoken-tseatid-actiontseat) | Adds a seat for a verified identity, placed nowhere yet. |
| [`PlaceAsync(TSeatId, TUnitId, bool, CancellationToken)`](#placeasynctseatid-tunitid-bool-cancellationtoken) | Places a seat in an active unit. |
| [`PlaceAsync(TSeat, TUnit, bool, CancellationToken)`](#placeasynctseat-tunit-bool-cancellationtoken) | Places a seat in an active unit, as [`PlaceAsync`](#placeasynctseatid-tunitid-bool-cancellationtoken) does with their ids. |
| [`WithdrawAsync(TSeatId, TUnitId, CancellationToken)`](#withdrawasynctseatid-tunitid-cancellationtoken) | Withdraws a seat from a unit, revoking every role it holds there. |
| [`WithdrawAsync(TSeat, TUnit, CancellationToken)`](#withdrawasynctseat-tunit-cancellationtoken) | Withdraws a seat from a unit, as [`WithdrawAsync`](#withdrawasynctseatid-tunitid-cancellationtoken) does with their ids. |
| [`MakePrimaryAsync(TSeatId, TUnitId, CancellationToken)`](#makeprimaryasynctseatid-tunitid-cancellationtoken) | Makes a seat's placement in a unit its primary one. |
| [`MakePrimaryAsync(TSeat, TUnit, CancellationToken)`](#makeprimaryasynctseat-tunit-cancellationtoken) | Makes a seat's placement in a unit its primary one, as [`MakePrimaryAsync`](#makeprimaryasynctseatid-tunitid-cancellationtoken) does with their ids. |
| [`GrantAsync(TSeatId, TRoleId, TUnitId, DateTimeOffset?, string?, CancellationToken, DateTimeOffset?)`](#grantasynctseatid-troleid-tunitid-datetimeoffset-string-cancellationtoken-datetimeoffset) | Grants a seat a role at an active unit where it is placed, from now until `until`. |
| [`GrantAsync(TSeat, TRole, TUnit, DateTimeOffset?, string?, CancellationToken, DateTimeOffset?)`](#grantasynctseat-trole-tunit-datetimeoffset-string-cancellationtoken-datetimeoffset) | Grants a seat a role at a unit, as [`GrantAsync`](#grantasynctseatid-troleid-tunitid-datetimeoffset-string-cancellationtoken-datetimeoffset) does with their ids. |
| [`RevokeAsync(TSeatId, TRoleId, TUnitId, CancellationToken)`](#revokeasynctseatid-troleid-tunitid-cancellationtoken) | Revokes a role a seat holds at a unit. |
| [`RevokeAsync(TSeat, TRole, TUnit, CancellationToken)`](#revokeasynctseat-trole-tunit-cancellationtoken) | Revokes a role a seat holds at a unit, as [`RevokeAsync`](#revokeasynctseatid-troleid-tunitid-cancellationtoken) does with their ids. |
| [`SuspendAsync(TSeatId, CancellationToken)`](#suspendasynctseatid-cancellationtoken) | Suspends a seat: its grants give it nothing until it is reactivated. |
| [`SuspendAsync(TSeat, CancellationToken)`](#suspendasynctseat-cancellationtoken) | Suspends a seat, as [`SuspendAsync`](#suspendasynctseatid-cancellationtoken) does with its id. |
| [`ReactivateAsync(TSeatId, CancellationToken)`](#reactivateasynctseatid-cancellationtoken) | Makes a suspended seat active again. |
| [`ReactivateAsync(TSeat, CancellationToken)`](#reactivateasynctseat-cancellationtoken) | Makes a suspended seat active again, as [`ReactivateAsync`](#reactivateasynctseatid-cancellationtoken) does with its id. |
| [`DeactivateAsync(TSeatId, CancellationToken)`](#deactivateasynctseatid-cancellationtoken) | Deactivates a seat for good, which takes away every grant that has not ended, by the rule [`SuspendAsync`](#suspendasynctseatid-cancellationtoken) follows. |
| [`DeactivateAsync(TSeat, CancellationToken)`](#deactivateasynctseat-cancellationtoken) | Deactivates a seat for good, as [`DeactivateAsync`](#deactivateasynctseatid-cancellationtoken) does with its id. |
| [`WhoAmIAsync(CancellationToken)`](#whoamiasynccancellationtoken) | Who the calling seat is, and what it may do where: its own seat, whole, as the application's class, with the tenant, the paths of the units it is placed at, the roles its grants name, and every live permission it holds now with where it is granted and every unit it reaches ([`SeatManager.SeatOverview`](#seatmanagerseatoverview)). |
| [`OverviewAsync(TSeatId, CancellationToken)`](#overviewasynctseatid-cancellationtoken) | Any seat of the caller's tenant and what it may do where, as [`WhoAmIAsync`](#whoamiasynccancellationtoken) answers the caller's own: the seat, whole, as the application's class, with the tenant, the paths of the units it is placed at, the roles its grants name, and every permission it holds now with where it is granted and every unit it reaches ([`SeatManager.SeatOverview`](#seatmanagerseatoverview)). |
| [`OverviewAsync(TSeat, CancellationToken)`](#overviewasynctseat-cancellationtoken) | Any seat of the caller's tenant and what it may do where, as [`OverviewAsync`](#overviewasynctseatid-cancellationtoken) answers it by its id. |
| [`ListAsync(CancellationToken)`](#listasynccancellationtoken) | The tenant's seats, the application's own class, each whole and in the order of their ids: a seat has nothing of Tenancy's a person would order it by, so a screen orders what it shows by what it shows. |
| [`ByIdAsync(IReadOnlyCollection<TSeatId>, CancellationToken)`](#byidasyncireadonlycollectiontseatid-cancellationtoken) | The seats among `ids` that are in the caller's tenant, as [`ListAsync`](#listasynccancellationtoken) answers them: the application's own, whole, in the order of their ids. |

#### `SeatManager(ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>, TenancyCatalogue, TimeProvider)`

```csharp
public SeatManager(
    ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> store,
    TenancyCatalogue catalogue,
    TimeProvider clock)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `store`: Where seats and roles are loaded and saved.
- `catalogue`: The permissions asked for, and which permissions are live.
- `clock`: What "now" is.

#### `AddAsync(Guid, CancellationToken, TSeatId?, Action<TSeat>?)`

```csharp
public Task<TSeat> AddAsync(
    Guid identity,
    CancellationToken cancellationToken,
    TSeatId? id = null,
    Action<TSeat>? configure = null)
```

Adds a seat for a verified identity, placed nowhere yet.

**Parameters**

- `identity`: The person's verified identity, the subject of their token.
- `cancellationToken`: Cancels the work.
- `id`: Its id, for imports and seeding; a new one otherwise. An id that is already a seat's is a mistake in the import, which the save reports.
- `configure`: Sets the fields the application added to its seat class, such as the name it is shown by, on the new seat once the caller is checked and before the seat is handed to the store, so they are saved in the same transaction and the class's own rules judge them there. When it throws, the seat is not added: nothing is saved, by this call or by a later save in the same scope.

**Returns**: The new seat.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.identity-has-seat`, `tenancy.identity-required`.

**Example**

A seat for Leo, with the name the application's seat class keeps:

```csharp
var leo = await seats.AddAsync(command.Identity, cancellationToken, configure: seat => seat.Rename("Leo"));
```

#### `PlaceAsync(TSeatId, TUnitId, bool, CancellationToken)`

```csharp
public Task<TSeat> PlaceAsync(
    TSeatId seat,
    TUnitId unit,
    bool primary,
    CancellationToken cancellationToken)
```

Places a seat in an active unit.

**Parameters**

- `seat`: The seat.
- `unit`: The unit.
- `primary`: Whether this becomes the seat's primary placement.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, placed.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.seat-not-found`, `tenancy.unit-not-found`, `tenancy.unit-not-active`, and what the seat refuses: placing itself, a second placement in the unit, a second primary.

**Example**

```csharp
var seat = await seats.PlaceAsync(command.Seat, command.Unit, command.Primary, cancellationToken);
```

#### `PlaceAsync(TSeat, TUnit, bool, CancellationToken)`

```csharp
public Task<TSeat> PlaceAsync(
    TSeat seat,
    TUnit unit,
    bool primary,
    CancellationToken cancellationToken)
```

Places a seat in an active unit, as [`PlaceAsync`](#placeasynctseatid-tunitid-bool-cancellationtoken) does with their ids.

**Parameters**

- `seat`: The seat.
- `unit`: The unit.
- `primary`: Whether this becomes the seat's primary placement.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, placed.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.seat-not-found`, `tenancy.unit-not-found`, `tenancy.unit-not-active`, and what the seat refuses: placing itself, a second placement in the unit, a second primary.

**Example**

```csharp
leo = await seats.PlaceAsync(leo, northCoast, primary: true, cancellationToken);
```

#### `WithdrawAsync(TSeatId, TUnitId, CancellationToken)`

```csharp
public Task<TSeat> WithdrawAsync(TSeatId seat, TUnitId unit, CancellationToken cancellationToken)
```

Withdraws a seat from a unit, revoking every role it holds there. When it holds any, the caller needs [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) there too, and, while containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), for each of those roles that manages access, its permissions that do, held there until at least that grant's end: taking a placement away takes its grants away. A role that manages no access needs none of its permissions.

**Parameters**

- `seat`: The seat.
- `unit`: The unit of one of its placements.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, withdrawn.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted`, `tenancy.seat-not-found`, `tenancy.placement-not-found`, `tenancy.grant-exceeds-own`, `tenancy.last-admin`.

**Example**

```csharp
var seat = await seats.WithdrawAsync(command.Seat, command.Unit, cancellationToken);
```

#### `WithdrawAsync(TSeat, TUnit, CancellationToken)`

```csharp
public Task<TSeat> WithdrawAsync(TSeat seat, TUnit unit, CancellationToken cancellationToken)
```

Withdraws a seat from a unit, as [`WithdrawAsync`](#withdrawasynctseatid-tunitid-cancellationtoken) does with their ids.

**Parameters**

- `seat`: The seat.
- `unit`: The unit of one of its placements.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, withdrawn.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted`, `tenancy.seat-not-found`, `tenancy.placement-not-found`, `tenancy.grant-exceeds-own`, `tenancy.last-admin`.

**Example**

Leo leaves North Coast, with the roles he held there:

```csharp
leo = await seats.WithdrawAsync(leo, northCoast, cancellationToken);
```

#### `MakePrimaryAsync(TSeatId, TUnitId, CancellationToken)`

```csharp
public Task<TSeat> MakePrimaryAsync(TSeatId seat, TUnitId unit, CancellationToken cancellationToken)
```

Makes a seat's placement in a unit its primary one.

**Parameters**

- `seat`: The seat.
- `unit`: The unit of one of its placements.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, with its new primary placement.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.seat-not-found`, `tenancy.placement-not-found`.

**Example**

```csharp
var seat = await seats.MakePrimaryAsync(command.Seat, command.Unit, cancellationToken);
```

#### `MakePrimaryAsync(TSeat, TUnit, CancellationToken)`

```csharp
public Task<TSeat> MakePrimaryAsync(TSeat seat, TUnit unit, CancellationToken cancellationToken)
```

Makes a seat's placement in a unit its primary one, as [`MakePrimaryAsync`](#makeprimaryasynctseatid-tunitid-cancellationtoken) does with their ids.

**Parameters**

- `seat`: The seat.
- `unit`: The unit of one of its placements.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, with its new primary placement.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.seat-not-found`, `tenancy.placement-not-found`.

**Example**

```csharp
leo = await seats.MakePrimaryAsync(leo, southBay, cancellationToken);
```

#### `GrantAsync(TSeatId, TRoleId, TUnitId, DateTimeOffset?, string?, CancellationToken, DateTimeOffset?)`

```csharp
public Task<TSeat> GrantAsync(
    TSeatId seat,
    TRoleId role,
    TUnitId at,
    DateTimeOffset? until,
    string? reason,
    CancellationToken cancellationToken,
    DateTimeOffset? from = null)
```

Grants a seat a role at an active unit where it is placed, from now until `until`. With [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, a seat gives another seat any role that manages no access, for any period, and itself one for no longer than it holds [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) there. While containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), a role that manages access it gives only while it holds there each of the role's permissions that manage access, for at least as long as the grant lasts, and never to itself: a seat whose own grant ends next week does not give such a role for good. Off, such a role goes as any other.

**Parameters**

- `seat`: The seat.
- `role`: The role, which must be active.
- `at`: The unit of one of the seat's placements.
- `until`: When the grant ends, or `null` for no end.
- `reason`: Why, or `null`.
- `cancellationToken`: Cancels the work.
- `from`: When the grant starts, for imports and seeding: system work only. A grant a seat makes starts now, and a seat that passes this is refused rather than silently ignored.

**Returns**: The seat, with the grant.

**Exceptions**

- `RefusalException`: `tenancy.start-system-only`, `tenancy.not-permitted` without [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.role-not-found`, `tenancy.self-appointment` for a role that manages access given to the caller itself while containment is on, `tenancy.grant-exceeds-own`, `tenancy.seat-not-found`, `tenancy.unit-not-found`, `tenancy.unit-not-active` for an archived unit, `tenancy.invalid-period`, and what the seat refuses.

**Example**

```csharp
var seat = await seats.GrantAsync(command.Seat, command.Role, command.Unit, command.Until, command.Reason, cancellationToken);
```

#### `GrantAsync(TSeat, TRole, TUnit, DateTimeOffset?, string?, CancellationToken, DateTimeOffset?)`

```csharp
public Task<TSeat> GrantAsync(
    TSeat seat,
    TRole role,
    TUnit at,
    DateTimeOffset? until,
    string? reason,
    CancellationToken cancellationToken,
    DateTimeOffset? from = null)
```

Grants a seat a role at a unit, as [`GrantAsync`](#grantasynctseatid-troleid-tunitid-datetimeoffset-string-cancellationtoken-datetimeoffset) does with their ids.

**Parameters**

- `seat`: The seat.
- `role`: The role, which must be active.
- `at`: The unit of one of the seat's placements.
- `until`: When the grant ends, or `null` for no end.
- `reason`: Why, or `null`.
- `cancellationToken`: Cancels the work.
- `from`: When the grant starts, for imports and seeding: system work only. A grant a seat makes starts now, and a seat that passes this is refused rather than silently ignored.

**Returns**: The seat, with the grant.

**Exceptions**

- `RefusalException`: `tenancy.start-system-only`, `tenancy.not-permitted` without [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.role-not-found`, `tenancy.self-appointment` for a role that manages access given to the caller itself while containment is on, `tenancy.grant-exceeds-own`, `tenancy.seat-not-found`, `tenancy.unit-not-found`, `tenancy.unit-not-active` for an archived unit, `tenancy.invalid-period`, and what the seat refuses.

**Example**

Rhea gives Leo the role Surveyor at North, with no end:

```csharp
leo = await seats.GrantAsync(leo, surveyor, at: north, until: null, reason: "Passed the survey course", cancellationToken);
```

#### `RevokeAsync(TSeatId, TRoleId, TUnitId, CancellationToken)`

```csharp
public Task<TSeat> RevokeAsync(
    TSeatId seat,
    TRoleId role,
    TUnitId at,
    CancellationToken cancellationToken)
```

Revokes a role a seat holds at a unit. With [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, a seat takes away any role that manages no access, an archived role included, which manages nothing. While containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), a role that manages access it takes away only while it holds there each of the role's permissions that manage access, until at least the grant's end, or for good for a grant with none; off, such a role goes as any other. A seat's own grant that applies now is its own hold, so a seat may take away its current roles.

**Parameters**

- `seat`: The seat.
- `role`: The role it holds there.
- `at`: The unit it holds the role at.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, without the grant.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.role-not-found`, `tenancy.seat-not-found`, `tenancy.grant-exceeds-own`, `tenancy.last-admin`, `tenancy.grant-not-found`.

**Example**

```csharp
var seat = await seats.RevokeAsync(command.Seat, command.Role, command.Unit, cancellationToken);
```

#### `RevokeAsync(TSeat, TRole, TUnit, CancellationToken)`

```csharp
public Task<TSeat> RevokeAsync(
    TSeat seat,
    TRole role,
    TUnit at,
    CancellationToken cancellationToken)
```

Revokes a role a seat holds at a unit, as [`RevokeAsync`](#revokeasynctseatid-troleid-tunitid-cancellationtoken) does with their ids.

**Parameters**

- `seat`: The seat.
- `role`: The role it holds there.
- `at`: The unit it holds the role at.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, without the grant.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.role-not-found`, `tenancy.seat-not-found`, `tenancy.grant-exceeds-own`, `tenancy.last-admin`, `tenancy.grant-not-found`.

**Example**

```csharp
leo = await seats.RevokeAsync(leo, surveyor, at: north, cancellationToken);
```

#### `SuspendAsync(TSeatId, CancellationToken)`

```csharp
public Task<TSeat> SuspendAsync(TSeatId seat, CancellationToken cancellationToken)
```

Suspends a seat: its grants give it nothing until it is reactivated. Suspending takes away every grant that has not ended, so, while containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), each of a role that manages access needs its permissions that do, held by the caller at that grant's unit until at least its end, as taking the role away would. A seat may suspend itself: its own grants that apply now are its own hold.

**Parameters**

- `seat`: The seat.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, suspended.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.seat-not-found`, `tenancy.grant-exceeds-own`, `tenancy.last-admin`, `tenancy.seat-state`.

**Example**

```csharp
var seat = await seats.SuspendAsync(command.Seat, cancellationToken);
```

#### `SuspendAsync(TSeat, CancellationToken)`

```csharp
public Task<TSeat> SuspendAsync(TSeat seat, CancellationToken cancellationToken)
```

Suspends a seat, as [`SuspendAsync`](#suspendasynctseatid-cancellationtoken) does with its id.

**Parameters**

- `seat`: The seat.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, suspended.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.seat-not-found`, `tenancy.grant-exceeds-own`, `tenancy.last-admin`, `tenancy.seat-state`.

**Example**

```csharp
tove = await seats.SuspendAsync(tove, cancellationToken);
```

#### `ReactivateAsync(TSeatId, CancellationToken)`

```csharp
public Task<TSeat> ReactivateAsync(TSeatId seat, CancellationToken cancellationToken)
```

Makes a suspended seat active again. Reactivating gives back every grant that has not ended, so, while containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), each of a role that manages access needs its permissions that do, held by the caller at that grant's unit until at least its end, as giving the role would.

**Parameters**

- `seat`: The seat.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, active again.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.seat-not-found`, `tenancy.grant-exceeds-own`, `tenancy.seat-state`.

**Example**

```csharp
var seat = await seats.ReactivateAsync(command.Seat, cancellationToken);
```

#### `ReactivateAsync(TSeat, CancellationToken)`

```csharp
public Task<TSeat> ReactivateAsync(TSeat seat, CancellationToken cancellationToken)
```

Makes a suspended seat active again, as [`ReactivateAsync`](#reactivateasynctseatid-cancellationtoken) does with its id.

**Parameters**

- `seat`: The seat.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, active again.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.seat-not-found`, `tenancy.grant-exceeds-own`, `tenancy.seat-state`.

**Example**

```csharp
tove = await seats.ReactivateAsync(tove, cancellationToken);
```

#### `DeactivateAsync(TSeatId, CancellationToken)`

```csharp
public Task<TSeat> DeactivateAsync(TSeatId seat, CancellationToken cancellationToken)
```

Deactivates a seat for good, which takes away every grant that has not ended, by the rule [`SuspendAsync`](#suspendasynctseatid-cancellationtoken) follows. A seat may deactivate itself.

**Parameters**

- `seat`: The seat.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, deactivated.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.seat-not-found`, `tenancy.grant-exceeds-own`, `tenancy.last-admin`, `tenancy.seat-state`.

**Example**

```csharp
var seat = await seats.DeactivateAsync(command.Seat, cancellationToken);
```

#### `DeactivateAsync(TSeat, CancellationToken)`

```csharp
public Task<TSeat> DeactivateAsync(TSeat seat, CancellationToken cancellationToken)
```

Deactivates a seat for good, as [`DeactivateAsync`](#deactivateasynctseatid-cancellationtoken) does with its id.

**Parameters**

- `seat`: The seat.
- `cancellationToken`: Cancels the work.

**Returns**: The seat, deactivated.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.seat-not-found`, `tenancy.grant-exceeds-own`, `tenancy.last-admin`, `tenancy.seat-state`.

**Example**

```csharp
tove = await seats.DeactivateAsync(tove, cancellationToken);
```

#### `WhoAmIAsync(CancellationToken)`

```csharp
public Task<SeatOverview> WhoAmIAsync(CancellationToken cancellationToken)
```

Who the calling seat is, and what it may do where: its own seat, whole, as the application's class, with the tenant, the paths of the units it is placed at, the roles its grants name, and every live permission it holds now with where it is granted and every unit it reaches ([`SeatManager.SeatOverview`](#seatmanagerseatoverview)). A seat asks this about itself; any seat of the tenant, its own included, is [`OverviewAsync`](#overviewasynctseatid-cancellationtoken).

**Parameters**

- `cancellationToken`: Cancels the read.

**Returns**: The calling seat's overview.

**Exceptions**

- `RefusalException`: `tenancy.not-seated` for every caller that is not a seat: nobody, whatever it was refused for, and system work, which has no seat of its own to show.

**Remarks**

The seat is read as the lists read seats, untracked, and not loaded as a command loads one: nothing done to it is saved, by this question or by a save later in the same unit of work.

**Example**

Whether to show the button that edits projects at North Coast:

```csharp
var me = await seats.WhoAmIAsync(cancellationToken);
var canEdit = me.Permissions.Any(held => held.Permission == "projects.edit" && held.Reaches.Any(unit => unit.Id == northCoast));
```

#### `OverviewAsync(TSeatId, CancellationToken)`

```csharp
public Task<SeatOverview> OverviewAsync(TSeatId seat, CancellationToken cancellationToken)
```

Any seat of the caller's tenant and what it may do where, as [`WhoAmIAsync`](#whoamiasynccancellationtoken) answers the caller's own: the seat, whole, as the application's class, with the tenant, the paths of the units it is placed at, the roles its grants name, and every permission it holds now with where it is granted and every unit it reaches ([`SeatManager.SeatOverview`](#seatmanagerseatoverview)). Asked about the caller's own seat, it answers what [`WhoAmIAsync`](#whoamiasynccancellationtoken) answers.

**Parameters**

- `seat`: The seat asked about.
- `cancellationToken`: Cancels the read.

**Returns**: The seat's overview, as the caller reads it.

**Exceptions**

- `RefusalException`: The caller is nobody, with its own code; or `tenancy.seat-not-found` for a seat the caller does not read: one of another tenant, one that does not exist, or one a read rule of the application's leaves out, which the answer does not tell apart.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Remarks**

It asks nothing of the caller beyond what every read asks, a seat of the tenant or system work in it. Who may see another person's access is the application's choice, so the request that asks this states it (`IRequireAccess.RequiredAccess`), as a request states it for everything else: `TenancyAccess.AtTenant(TenancyPermissions.SeatsManage)` for a tenant's administration, say, or `TenancyAccess.InTenant()` where the database's read rules are the rule.

What comes back is what the caller reads, through the store, as the lists read seats. On Postgres Tenancy's policies give a seat another seat's grants only where it may read them: at the units where it manages grants, seats or units and below them, or all of them when it manages roles for the whole tenant; and a read rule of the application's on its seat class decides which seats it reads at all. The permissions are worked out from the grants that were read, by the rule the rights are written by, since a database that keeps the rights shows a seat its own alone: a permission is shown where the caller reads the grant that gives it, and a seat that is not active holds none. Where no policy applies, on another database or in a context without row level security, every seat comes with all its grants and permissions, and the request's requirement is the only rule: choose it as the rule you mean.

The seat is read untracked, as the lists read seats: nothing done to it is saved, by this question or by a save later in the same unit of work.

**Example**

The units a seat is placed at, by path:

```csharp
var overview = await seats.OverviewAsync(command.Seat, cancellationToken);
var paths = overview.Seat.Placements.Select(placement => overview.UnitOf(placement.UnitId).Path);
```

#### `OverviewAsync(TSeat, CancellationToken)`

```csharp
public Task<SeatOverview> OverviewAsync(TSeat seat, CancellationToken cancellationToken)
```

Any seat of the caller's tenant and what it may do where, as [`OverviewAsync`](#overviewasynctseatid-cancellationtoken) answers it by its id.

**Parameters**

- `seat`: The seat asked about.
- `cancellationToken`: Cancels the read.

**Returns**: The seat's overview, as the caller reads it.

**Exceptions**

- `RefusalException`: The caller is nobody, with its own code; or `tenancy.seat-not-found` for a seat the caller does not read: one of another tenant, one that does not exist, or one a read rule of the application's leaves out, which the answer does not tell apart.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Remarks**

It asks nothing of the caller beyond what every read asks, a seat of the tenant or system work in it. Who may see another person's access is the application's choice, so the request that asks this states it (`IRequireAccess.RequiredAccess`), as a request states it for everything else: `TenancyAccess.AtTenant(TenancyPermissions.SeatsManage)` for a tenant's administration, say, or `TenancyAccess.InTenant()` where the database's read rules are the rule.

What comes back is what the caller reads, through the store, as the lists read seats. On Postgres Tenancy's policies give a seat another seat's grants only where it may read them: at the units where it manages grants, seats or units and below them, or all of them when it manages roles for the whole tenant; and a read rule of the application's on its seat class decides which seats it reads at all. The permissions are worked out from the grants that were read, by the rule the rights are written by, since a database that keeps the rights shows a seat its own alone: a permission is shown where the caller reads the grant that gives it, and a seat that is not active holds none. Where no policy applies, on another database or in a context without row level security, every seat comes with all its grants and permissions, and the request's requirement is the only rule: choose it as the rule you mean.

The seat is read untracked, as the lists read seats: nothing done to it is saved, by this question or by a save later in the same unit of work.

**Example**

```csharp
var overview = await seats.OverviewAsync(leo, cancellationToken);
```

#### `ListAsync(CancellationToken)`

```csharp
public Task<IReadOnlyList<TSeat>> ListAsync(CancellationToken cancellationToken)
```

The tenant's seats, the application's own class, each whole and in the order of their ids: a seat has nothing of Tenancy's a person would order it by, so a screen orders what it shows by what it shows. The application selects what it shows, a name its seat class keeps, or a profile of its own found by the seat's identity, from the seats read here, with no statement more.

**Parameters**

- `cancellationToken`: Cancels the read.

**Returns**: The seats the caller reads, the application's own class, in the order of their ids.

**Exceptions**

- `RefusalException`: The caller is nobody.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Remarks**

One statement, which reads every seat with its identity, its placements and their grants, and every field the application added; tracked by nobody, so nothing done to a seat is saved. The identity is the application's to keep: what leaves is what it selects.

No permission is asked, and only a database's own read rules narrow what of another seat is read: on Postgres, Tenancy's policies answer another seat's grants only where the caller may read them, and elsewhere, or in a context without row level security, every seat comes with all its placements and grants. Where another seat's grants are shown, the request that asks says who may see them, as a request that answers from [`OverviewAsync`](#overviewasynctseatid-cancellationtoken) does; select a list's seats into what every member may see.

**Example**

```csharp
var people = (await seats.ListAsync(cancellationToken)).Select(seat => new { seat.Id, seat.DisplayName, seat.Status });
```

#### `ByIdAsync(IReadOnlyCollection<TSeatId>, CancellationToken)`

```csharp
public Task<IReadOnlyList<TSeat>> ByIdAsync(
    IReadOnlyCollection<TSeatId> ids,
    CancellationToken cancellationToken)
```

The seats among `ids` that are in the caller's tenant, as [`ListAsync`](#listasynccancellationtoken) answers them: the application's own, whole, in the order of their ids. An id of another tenant, or of no seat, is left out without a word: the answer never says which.

**Parameters**

- `ids`: The seats asked about, at most [`MostIds`](#seatmanagerttenant-ttenantid-torganization-tunit-tunitid-tseat-tseatid-trole-troleid) different ones.
- `cancellationToken`: Cancels the read.

**Returns**: The seats found, the application's own class, in the order of their ids.

**Exceptions**

- `RefusalException`: The caller is nobody, with its own code; or `tenancy.too-many-ids`.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Remarks**

Each seat comes with its placements and grants as [`ListAsync`](#listasynccancellationtoken) reads them, narrowed by the database's own read rules alone: select what the caller is shown.

**Example**

The seats of a project's crew, to show their names:

```csharp
var crew = await seats.ByIdAsync([.. project.Crew.Select(member => member.Seat)], cancellationToken);
```

### `SeatManager.PermissionReach`

```csharp
public sealed record PermissionReach
```

A permission a seat holds now: the units it is granted at, every unit it reaches (those and every unit below them), and whether it holds for the whole tenant, which is being granted at the root.

| Member | Summary |
|---|---|
| [`PermissionReach(string, bool, IReadOnlyList<UnitRef>, IReadOnlyList<UnitRef>)`](#permissionreachstring-bool-ireadonlylistunitref-ireadonlylistunitref) | The primary constructor: the type's summary says what it makes. |
| `string Permission { get; init; }` | The permission. |
| `bool AtTenant { get; init; }` | Whether the seat holds it for the whole tenant: one of its grants is at the root. |
| `IReadOnlyList<UnitRef> GrantedAt { get; init; }` | The units a grant of the seat gives it at, each once, by its path from the root. |
| `IReadOnlyList<UnitRef> Reaches { get; init; }` | Every unit it holds the permission at: those, and every unit below them, each once, by path. |

#### `PermissionReach(string, bool, IReadOnlyList<UnitRef>, IReadOnlyList<UnitRef>)`

```csharp
public PermissionReach(
    string Permission,
    bool AtTenant,
    IReadOnlyList<UnitRef> GrantedAt,
    IReadOnlyList<UnitRef> Reaches)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Permission`: The permission.
- `AtTenant`: Whether the seat holds it for the whole tenant: one of its grants is at the root.
- `GrantedAt`: The units a grant of the seat gives it at, each once, by its path from the root.
- `Reaches`: Every unit it holds the permission at: those, and every unit below them, each once, by path.

### `SeatManager.SeatOverview`

```csharp
public sealed record SeatOverview
```

A seat and what it may do where: the application's own seat, whole, with only what is not on the seat itself beside it. The seat carries its id, its identity, its status, where it is placed with the roles granted there for which period, and every field the application added; Tenancy adds the tenant it is in and that tenant's name, the path of each unit it is placed at, the roles its grants name, and every permission it holds at [`AsOf`](#seatmanagerseatoverview) with where that permission reaches. [`SeatManager.WhoAmIAsync`](#whoamiasynccancellationtoken) answers it for the calling seat, and [`SeatManager.OverviewAsync`](#overviewasynctseatid-cancellationtoken) for any seat of the tenant.

Nothing of it is the seat a second time, so an application shows the seat by what it chose, a name its seat class keeps or a profile found by the identity, from the seat itself, and puts its placements beside the paths and roles with [`UnitOf`](#unitoftunitid) and [`RoleOf`](#roleoftroleid). What leaves is what the application selects: the seat has its identity.

The seat and the roles were read for the overview and are tracked by nobody: nothing done to them is saved, by this question or by a save later in the same unit of work. The tenant is loaded as a command loads it. Nothing in the record is the caller's own but the question that made it, so it shows any seat the same way. What it holds of a seat is what the caller reads: where a database's policies give the caller only some of another seat's grants, as Tenancy's do on Postgres, the seat comes with those, and the roles and permissions are those that they name and give.

| Member | Summary |
|---|---|
| [`SeatOverview(TTenant, string, TSeat, IReadOnlyList<UnitRef>, IReadOnlyList<TRole>, IReadOnlyList<PermissionReach>, DateTimeOffset)`](#seatoverviewttenant-string-tseat-ireadonlylistunitref-ireadonlylisttrole-ireadonlylistpermissionreach-datetimeoffset) | The primary constructor: the type's summary says what it makes. |
| `TTenant Tenant { get; init; }` | The tenant the seat is in, the application's own class. |
| `string TenantName { get; init; }` | The tenant's name, which is its organization's. |
| `TSeat Seat { get; init; }` | The seat, the application's own class, with its placements and the grants the caller reads. |
| `IReadOnlyList<UnitRef> Units { get; init; }` | Each unit the seat is placed at, once, by its path from the root. |
| `IReadOnlyList<TRole> Roles { get; init; }` | Each role the seat's grants name, once, the application's own class, by name, whether or not the grant applies now; a role the store does not answer, one a filter of the application's own hides, is left out rather than made up. |
| `IReadOnlyList<PermissionReach> Permissions { get; init; }` | Every permission the seat holds at `AsOf` from those grants, where it is granted and every unit it reaches: none while the seat is not active. |
| `DateTimeOffset AsOf { get; init; }` | The moment the overview holds for: the permissions are those held then, and a grant of the seat applies then when `grant.AppliesAt(AsOf)`. |
| [`UnitOf(TUnitId)`](#unitoftunitid) | A unit the seat is placed at, by its path: what [`Units`](#seatmanagerseatoverview) holds for it. |
| [`RoleOf(TRoleId)`](#roleoftroleid) | A role the seat's grants name: what [`Roles`](#seatmanagerseatoverview) holds for it. |

#### `SeatOverview(TTenant, string, TSeat, IReadOnlyList<UnitRef>, IReadOnlyList<TRole>, IReadOnlyList<PermissionReach>, DateTimeOffset)`

```csharp
public SeatOverview(
    TTenant Tenant,
    string TenantName,
    TSeat Seat,
    IReadOnlyList<UnitRef> Units,
    IReadOnlyList<TRole> Roles,
    IReadOnlyList<PermissionReach> Permissions,
    DateTimeOffset AsOf)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Tenant`: The tenant the seat is in, the application's own class.
- `TenantName`: The tenant's name, which is its organization's.
- `Seat`: The seat, the application's own class, with its placements and the grants the caller reads.
- `Units`: Each unit the seat is placed at, once, by its path from the root.
- `Roles`: Each role the seat's grants name, once, the application's own class, by name, whether or not the grant applies now; a role the store does not answer, one a filter of the application's own hides, is left out rather than made up.
- `Permissions`: Every permission the seat holds at `AsOf` from those grants, where it is granted and every unit it reaches: none while the seat is not active.
- `AsOf`: The moment the overview holds for: the permissions are those held then, and a grant of the seat applies then when `grant.AppliesAt(AsOf)`.

#### `UnitOf(TUnitId)`

```csharp
public UnitRef UnitOf(TUnitId unit)
```

A unit the seat is placed at, by its path: what [`Units`](#seatmanagerseatoverview) holds for it.

**Parameters**

- `unit`: The unit of one of the seat's placements.

**Exceptions**

- `KeyNotFoundException`: The seat is placed at no such unit.

#### `RoleOf(TRoleId)`

```csharp
public TRole? RoleOf(TRoleId role)
```

A role the seat's grants name: what [`Roles`](#seatmanagerseatoverview) holds for it.

**Parameters**

- `role`: The role of one of the seat's grants.

**Returns**: The role; or `null` when a grant of the seat names it and the store answered no such role, as a filter of the application's own on its role class does, a soft delete say. The grant is the seat's own all the same, so a screen shows it with what the application chooses for a role it cannot name.

**Exceptions**

- `KeyNotFoundException`: No grant of the seat names such a role.

### `SeatManager.UnitRef`

```csharp
public sealed record UnitRef
```

A unit, with its path from the root, such as `Harbor Works / North / North Coast`: the units an overview names, whichever of them the caller reads whole.

| Member | Summary |
|---|---|
| [`UnitRef(TUnitId, string)`](#unitreftunitid-string) | The primary constructor: the type's summary says what it makes. |
| `TUnitId Id { get; init; }` | The unit. |
| `string Path { get; init; }` | The names of the unit and the units above it, the root first, joined with `" / "`. |

#### `UnitRef(TUnitId, string)`

```csharp
public UnitRef(TUnitId Id, string Path)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Id`: The unit.
- `Path`: The names of the unit and the units above it, the root first, joined with `" / "`.

## Roles

Namespace `DDDToolkit.Supporting.Tenancy.Managers`.

### `RoleManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`

```csharp
public class RoleManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
```

The tenant's roles: making them, renaming them, changing their permissions and archiving them, and reading them. Your code takes it as `RoleManager`, a class the generator writes, closed over your classes and ids, and the container makes per scope.

Each change of a role takes the role, or its id. Both check the same: the manager loads the role itself and asks the caller's rights over what it loaded, never over the state of a role it was handed. Each returns the role as it changed it, the one the unit of work keeps.

Each change needs [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant. Following the starter roles the roles were made from, once the application changed them, is system work's alone ([`FollowStarterRolesAsync`](#followstarterrolesasynccancellationtoken)). A holder of that permission may put any live permission into any role, permissions it does not hold included. That is on purpose: it is the administrator's permission, and an administrator can give themselves anything already. One exception: an administrator alone, holding it at the root with no end, changes which permissions that manage access a role holds, by adding one, taking one out or archiving a role that holds one. A role that gains one gives it to every seat that holds the role, seats that gave themselves the role included, and a role that loses one takes it from every holder at once, so someone holding the permission for a week neither gives lasting power over access that way nor takes away what it could not revoke grant by grant. A change of a role's permissions reaches every seat that holds the role at the next save. Whether a change leaves the tenant an administrator is decided before the role changes, so a refused command leaves nothing for a later save to write.

That exception is containment, and goes when the application turns containment off ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)): then the role permission for the whole tenant is all any change of a role takes. The administrator a tenant keeps stays either way.

The reads answer roles as the application's own class, whole, and read the roles of the caller's tenant: a seat of it, or system work in it, asks no permission. Whether a role manages access is [`ManagesAccess`](#managesaccesstrole).

| Member | Summary |
|---|---|
| `MostIds = 200` | How many ids one question by id takes. |
| [`RoleManager(ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>, TenancyCatalogue, TimeProvider)`](#rolemanageritenancystorettenant-ttenantid-torganization-tunit-tunitid-tseat-tseatid-trole-troleid-tenancycatalogue-timeprovider) | The primary constructor: the type's summary says what it makes. |
| [`CreateAsync(string, string, IReadOnlyCollection<string>, CancellationToken, TRoleId?)`](#createasyncstring-string-ireadonlycollectionstring-cancellationtoken-troleid) | Makes a role by hand; it comes from no starter role. |
| [`RenameAsync(TRoleId, string, string, CancellationToken)`](#renameasynctroleid-string-string-cancellationtoken) | Renames a role and describes it again. |
| [`RenameAsync(TRole, string, string, CancellationToken)`](#renameasynctrole-string-string-cancellationtoken) | Renames a role and describes it again, as [`RenameAsync`](#renameasynctroleid-string-string-cancellationtoken) does with its id. |
| [`SetPermissionsAsync(TRoleId, IReadOnlyCollection<string>, CancellationToken)`](#setpermissionsasynctroleid-ireadonlycollectionstring-cancellationtoken) | Sets the permissions a role grants, for every seat that holds it. |
| [`SetPermissionsAsync(TRole, IReadOnlyCollection<string>, CancellationToken)`](#setpermissionsasynctrole-ireadonlycollectionstring-cancellationtoken) | Sets the permissions a role grants, as [`SetPermissionsAsync`](#setpermissionsasynctroleid-ireadonlycollectionstring-cancellationtoken) does with its id. |
| [`ArchiveAsync(TRoleId, CancellationToken)`](#archiveasynctroleid-cancellationtoken) | Archives a role: it stays where it was granted, and grants nothing from now on. |
| [`ArchiveAsync(TRole, CancellationToken)`](#archiveasynctrole-cancellationtoken) | Archives a role, as [`ArchiveAsync`](#archiveasynctroleid-cancellationtoken) does with its id. |
| [`FollowStarterRolesAsync(CancellationToken)`](#followstarterrolesasynccancellationtoken) | Every active role of the caller's tenant that was made from a starter role follows that starter role, as the catalogue the application runs with builds it now ([`RoleAggregate.FollowStarterRole<TSeatId>`](aggregates.md#followstarterroletenancycatalogue-tenancyactortseatid)): a permission the starter role gained since is added, a permission it lost is taken out, and what the tenant changed itself stays. |
| [`ListAsync(CancellationToken)`](#listasynccancellationtoken-1) | The tenant's roles, the application's own class, the active ones first, each by name. |
| [`ByIdAsync(IReadOnlyCollection<TRoleId>, CancellationToken)`](#byidasyncireadonlycollectiontroleid-cancellationtoken) | The roles among `ids` that are the caller's tenant's, the application's own class, the active ones first, each by name. |
| [`ManagesAccess(TRole)`](#managesaccesstrole) | Whether a role manages access: whether the catalogue marks one of its live permissions as managing access, which an archived role never does. |

#### `RoleManager(ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>, TenancyCatalogue, TimeProvider)`

```csharp
public RoleManager(
    ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> store,
    TenancyCatalogue catalogue,
    TimeProvider clock)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `store`: Where roles are loaded and saved.
- `catalogue`: The permissions a role may hold.
- `clock`: What "now" is.

#### `CreateAsync(string, string, IReadOnlyCollection<string>, CancellationToken, TRoleId?)`

```csharp
public Task<TRole> CreateAsync(
    string name,
    string description,
    IReadOnlyCollection<string> permissions,
    CancellationToken cancellationToken,
    TRoleId? id = null)
```

Makes a role by hand; it comes from no starter role.

**Parameters**

- `name`: Its name, unique in the tenant, ignoring case.
- `description`: What it is for.
- `permissions`: The permissions it grants; the permissions they imply are added.
- `cancellationToken`: Cancels the work.
- `id`: Its id, for imports and seeding; a new one otherwise. An id that is already a role's is a mistake in the import, which the save reports.

**Returns**: The new role.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.role-name-taken`, `tenancy.name-invalid`, `tenancy.unknown-permission`.

**Example**

```csharp
var planner = await roles.CreateAsync("Planner", "Opens and edits projects", ["projects.open", "projects.edit"], cancellationToken);
```

#### `RenameAsync(TRoleId, string, string, CancellationToken)`

```csharp
public Task<TRole> RenameAsync(
    TRoleId role,
    string name,
    string description,
    CancellationToken cancellationToken)
```

Renames a role and describes it again.

**Parameters**

- `role`: The role.
- `name`: Its new name, unique in the tenant, ignoring case.
- `description`: What it is for.
- `cancellationToken`: Cancels the work.

**Returns**: The role, renamed.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.role-not-found`, `tenancy.role-name-taken`, `tenancy.role-archived`, `tenancy.name-invalid`.

**Example**

```csharp
var role = await roles.RenameAsync(command.Role, command.Name, command.Description, cancellationToken);
```

#### `RenameAsync(TRole, string, string, CancellationToken)`

```csharp
public Task<TRole> RenameAsync(
    TRole role,
    string name,
    string description,
    CancellationToken cancellationToken)
```

Renames a role and describes it again, as [`RenameAsync`](#renameasynctroleid-string-string-cancellationtoken) does with its id.

**Parameters**

- `role`: The role.
- `name`: Its new name, unique in the tenant, ignoring case.
- `description`: What it is for.
- `cancellationToken`: Cancels the work.

**Returns**: The role, renamed.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.role-not-found`, `tenancy.role-name-taken`, `tenancy.role-archived`, `tenancy.name-invalid`.

**Example**

```csharp
planner = await roles.RenameAsync(planner, "Project planner", "Opens and edits projects", cancellationToken);
```

#### `SetPermissionsAsync(TRoleId, IReadOnlyCollection<string>, CancellationToken)`

```csharp
public Task<TRole> SetPermissionsAsync(
    TRoleId role,
    IReadOnlyCollection<string> permissions,
    CancellationToken cancellationToken)
```

Sets the permissions a role grants, for every seat that holds it. While containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), a seat that adds or takes out a permission that manages access must be an administrator: hold [`TenancyPermissions.AdministratorPermission`](catalogue.md#tenancypermissions) at the root with no end.

**Parameters**

- `role`: The role.
- `permissions`: The permissions it grants from now on; the permissions they imply are added.
- `cancellationToken`: Cancels the work.

**Returns**: The role, with its new permissions.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.role-not-found`, `tenancy.role-archived`, `tenancy.unknown-permission`, `tenancy.grant-exceeds-own` when a seat that is not an administrator adds or takes out a permission that manages access while containment is on, naming [`TenancyPermissions.AdministratorPermission`](catalogue.md#tenancypermissions), and `tenancy.last-admin` when the last administrators' role would lose the administrator permission.

**Example**

```csharp
var role = await roles.SetPermissionsAsync(command.Role, command.Permissions, cancellationToken);
```

#### `SetPermissionsAsync(TRole, IReadOnlyCollection<string>, CancellationToken)`

```csharp
public Task<TRole> SetPermissionsAsync(
    TRole role,
    IReadOnlyCollection<string> permissions,
    CancellationToken cancellationToken)
```

Sets the permissions a role grants, as [`SetPermissionsAsync`](#setpermissionsasynctroleid-ireadonlycollectionstring-cancellationtoken) does with its id.

**Parameters**

- `role`: The role.
- `permissions`: The permissions it grants from now on; the permissions they imply are added.
- `cancellationToken`: Cancels the work.

**Returns**: The role, with its new permissions.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.role-not-found`, `tenancy.role-archived`, `tenancy.unknown-permission`, `tenancy.grant-exceeds-own` when a seat that is not an administrator adds or takes out a permission that manages access while containment is on, naming [`TenancyPermissions.AdministratorPermission`](catalogue.md#tenancypermissions), and `tenancy.last-admin` when the last administrators' role would lose the administrator permission.

**Example**

```csharp
planner = await roles.SetPermissionsAsync(planner, ["projects.open", "projects.edit", "projects.close"], cancellationToken);
```

#### `ArchiveAsync(TRoleId, CancellationToken)`

```csharp
public Task<TRole> ArchiveAsync(TRoleId role, CancellationToken cancellationToken)
```

Archives a role: it stays where it was granted, and grants nothing from now on. While containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), a seat that archives a role that manages access must be an administrator: hold [`TenancyPermissions.AdministratorPermission`](catalogue.md#tenancypermissions) at the root with no end.

**Parameters**

- `role`: The role.
- `cancellationToken`: Cancels the work.

**Returns**: The role, archived.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.role-not-found`, `tenancy.role-archived`, `tenancy.grant-exceeds-own` when a seat that is not an administrator archives a role that manages access while containment is on, naming [`TenancyPermissions.AdministratorPermission`](catalogue.md#tenancypermissions), and `tenancy.last-admin`.

**Example**

```csharp
var role = await roles.ArchiveAsync(command.Role, cancellationToken);
```

#### `ArchiveAsync(TRole, CancellationToken)`

```csharp
public Task<TRole> ArchiveAsync(TRole role, CancellationToken cancellationToken)
```

Archives a role, as [`ArchiveAsync`](#archiveasynctroleid-cancellationtoken) does with its id.

**Parameters**

- `role`: The role.
- `cancellationToken`: Cancels the work.

**Returns**: The role, archived.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.role-not-found`, `tenancy.role-archived`, `tenancy.grant-exceeds-own` when a seat that is not an administrator archives a role that manages access while containment is on, naming [`TenancyPermissions.AdministratorPermission`](catalogue.md#tenancypermissions), and `tenancy.last-admin`.

**Example**

```csharp
planner = await roles.ArchiveAsync(planner, cancellationToken);
```

#### `FollowStarterRolesAsync(CancellationToken)`

```csharp
public Task<StarterRolesFollowed> FollowStarterRolesAsync(CancellationToken cancellationToken)
```

Every active role of the caller's tenant that was made from a starter role follows that starter role, as the catalogue the application runs with builds it now ([`RoleAggregate.FollowStarterRole<TSeatId>`](aggregates.md#followstarterroletenancycatalogue-tenancyactortseatid)): a permission the starter role gained since is added, a permission it lost is taken out, and what the tenant changed itself stays. Each role whose permissions change raises [`RoleFollowedItsStarterRole<TTenantId, TRoleId, TSeatId>`](aggregates.md#rolefolloweditsstarterrolettenantid-troleid-tseatid), and the change reaches every seat that holds the role at the save, the one save of the command.

System work in the tenant alone runs it, as `services.SyncStarterRoles()` does for every tenant once the host has started, through [`IStarterRoleSync`](catalogue.md#istarterrolesync); an operator's command marked `[Operator]` runs it for one tenant, as operator work there, and the events name the operator. No seat runs it: what a starter role holds is the application's decision, permissions that manage access included, and an administrator changes a role with [`SetPermissionsAsync`](#setpermissionsasynctroleid-ireadonlycollectionstring-cancellationtoken).

It takes the tenant's access revision first, as every change of rights does, and saves only when a role changed: a second run in a row reads, and writes nothing. Two runs at the same time cannot both commit; the one that fails on the revision changes nothing when it runs again. The last-administrator rule holds for it as for anyone: a role whose following would take the administrator permission from the tenant's last administrators is left as it is, and named in the answer.

**Parameters**

- `cancellationToken`: Cancels the work.

**Returns**: The roles that changed, the roles kept for an administrator, and the roles whose starter role is gone.

**Exceptions**

- `RefusalException`: `access.system-only` for a seat.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Example**

```csharp
using (access.BeginSystemIn(tenant))
{
    var followed = await roles.FollowStarterRolesAsync(cancellationToken);
}
```

#### `ListAsync(CancellationToken)`

```csharp
public Task<IReadOnlyList<TRole>> ListAsync(CancellationToken cancellationToken)
```

The tenant's roles, the application's own class, the active ones first, each by name.

**Parameters**

- `cancellationToken`: Cancels the read.

**Returns**: The roles, archived ones included.

**Exceptions**

- `RefusalException`: The caller is nobody.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Example**

```csharp
var active = (await roles.ListAsync(cancellationToken)).Where(role => role.Status == RoleStatus.Active);
```

#### `ByIdAsync(IReadOnlyCollection<TRoleId>, CancellationToken)`

```csharp
public Task<IReadOnlyList<TRole>> ByIdAsync(
    IReadOnlyCollection<TRoleId> ids,
    CancellationToken cancellationToken)
```

The roles among `ids` that are the caller's tenant's, the application's own class, the active ones first, each by name. An id of another tenant, or of no role, is left out without a word.

**Parameters**

- `ids`: The roles asked about, at most [`MostIds`](#rolemanagerttenant-ttenantid-torganization-tunit-tunitid-tseat-tseatid-trole-troleid) different ones.
- `cancellationToken`: Cancels the read.

**Returns**: The roles found.

**Exceptions**

- `RefusalException`: The caller is nobody, with its own code; or `tenancy.too-many-ids`.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Example**

The names of the roles that grant `projects.edit`:

```csharp
var ids = await db.Tenancy().RolesWithPermission("projects.edit").ToListAsync(cancellationToken);
var names = (await roles.ByIdAsync(ids, cancellationToken)).Select(role => role.Name);
```

#### `ManagesAccess(TRole)`

```csharp
public bool ManagesAccess(TRole role)
```

Whether a role manages access: whether the catalogue marks one of its live permissions as managing access, which an archived role never does. While containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), such a role is given only by a seat that holds those permissions, rather than by whoever holds `tenancy.grants.manage` where the seat is placed. Once the application turns it off, it is given as any other role, and this still says it manages access. It reads nothing.

**Parameters**

- `role`: The role.

**Returns**: `true` when one of the role's live permissions manages access.

**Exceptions**

- `ArgumentNullException`: `role` is null.

**Example**

A role list that marks the roles only some seats may give:

```csharp
var list = (await roles.ListAsync(cancellationToken)).Select(role => new { role.Id, role.Name, Restricted = roles.ManagesAccess(role) });
```

### `RoleManager.StarterRolesFollowed`

```csharp
public sealed record StarterRolesFollowed
```

What following the starter roles did in one tenant ([`RoleManager.FollowStarterRolesAsync`](#followstarterrolesasynccancellationtoken)). A role that follows its starter role already, one made by hand and one that is archived are in none of the lists.

| Member | Summary |
|---|---|
| [`StarterRolesFollowed(IReadOnlyList<TRoleId>, IReadOnlyList<TRoleId>, IReadOnlyList<TRoleId>)`](#starterrolesfollowedireadonlylisttroleid-ireadonlylisttroleid-ireadonlylisttroleid) | The primary constructor: the type's summary says what it makes. |
| `IReadOnlyList<TRoleId> Changed { get; init; }` | The roles whose permissions changed, each with a [`RoleFollowedItsStarterRole<TTenantId, TRoleId, TSeatId>`](aggregates.md#rolefolloweditsstarterrolettenantid-troleid-tseatid) raised. |
| `IReadOnlyList<TRoleId> KeptForAnAdministrator { get; init; }` | The roles left as they were, permissions and record, because following their starter role would take the administrator permission from the tenant's last administrators. Each follows at a later run, once the tenant has an administrator through another role. |
| `IReadOnlyList<TRoleId> WithoutTheirStarterRole { get; init; }` | The roles made from a starter role the catalogue no longer has. They keep their permissions, and follow the starter role again if the application declares it again under the same permission. |

#### `StarterRolesFollowed(IReadOnlyList<TRoleId>, IReadOnlyList<TRoleId>, IReadOnlyList<TRoleId>)`

```csharp
public StarterRolesFollowed(
    IReadOnlyList<TRoleId> Changed,
    IReadOnlyList<TRoleId> KeptForAnAdministrator,
    IReadOnlyList<TRoleId> WithoutTheirStarterRole)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Changed`: The roles whose permissions changed, each with a [`RoleFollowedItsStarterRole<TTenantId, TRoleId, TSeatId>`](aggregates.md#rolefolloweditsstarterrolettenantid-troleid-tseatid) raised.
- `KeptForAnAdministrator`: The roles left as they were, permissions and record, because following their starter role would take the administrator permission from the tenant's last administrators. Each follows at a later run, once the tenant has an administrator through another role.
- `WithoutTheirStarterRole`: The roles made from a starter role the catalogue no longer has. They keep their permissions, and follow the starter role again if the application declares it again under the same permission.

## Invitations

Namespace `DDDToolkit.Supporting.Tenancy.Managers`.

### `InvitationManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TInvitation, TInvitationId>`

```csharp
public class InvitationManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TInvitation, TInvitationId>
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
    where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TInvitationId : struct, ICreatableEntityId<TInvitationId>, IEquatable<TInvitationId>
```

Invitations into a tenant: issuing one for an address, listing the open ones, cancelling one, and accepting one, which is what makes the seat. Your code takes it as `InvitationManager`, a class the generator writes, closed over your classes and ids, in a project that declares an invitation class; the container makes it per scope.

An invitation offers what adding a seat, placing it and granting it a role would give, so issuing one takes what those take: [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, as adding a seat does, and [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, with the rule for giving a role there. A role that manages no access is offered freely, and may outlast the issuer's own grants. A role that manages access is offered only by a seat that holds each of its permissions that do at the unit, until at least the end of the grant, unless the application turns containment off ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), and then as freely as any other. Nobody invites themself into one: an identity that has a seat in the tenant cannot accept.

An invitation never gives more than its issuer could give at the moment it is used. Accepting asks the issuer's rights again, as they are then, with the role as it is then, and an invitation whose issuer has lost them is refused with one answer that says nothing of which. System work is not held to a seat, and neither is an invitation it issued.

The token is returned once, by [`IssueAsync`](#issueasyncstring-tunitid-troleid-datetimeoffset-timespan-cancellationtoken-tinvitationid), and kept nowhere: the store holds its digest. It is in no event, no refusal and no log of the package. Sending it to the address is the application's to do.

| Member | Summary |
|---|---|
| [`InvitationManager(ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>, IInvitationStore<TInvitation, TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>, TenancyCatalogue, TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>, TenancyInvitationOptions<TInvitationId>, ICurrentUser, TimeProvider)`](#invitationmanageritenancystorettenant-ttenantid-torganization-tunit-tunitid-tseat-tseatid-trole-troleid-iinvitationstoretinvitation-tinvitationid-ttenantid-tunitid-troleid-tseatid-tenancycatalogue-tenancyoptionsttenantid-tseatid-tunitid-troleid-tenancyinvitationoptionstinvitationid-icurrentuser-timeprovider) | The primary constructor: the type's summary says what it makes. |
| [`IssueAsync(string, TUnitId, TRoleId, DateTimeOffset?, TimeSpan?, CancellationToken, TInvitationId?)`](#issueasyncstring-tunitid-troleid-datetimeoffset-timespan-cancellationtoken-tinvitationid) | Issues an invitation for an address: a seat placed in `unit` as its primary placement, with `role` there until `grantUntil`, for whoever accepts it with the token this returns. |
| [`IssueAsync(string, TUnit, TRole, DateTimeOffset?, TimeSpan?, CancellationToken, TInvitationId?)`](#issueasyncstring-tunit-trole-datetimeoffset-timespan-cancellationtoken-tinvitationid) | Issues an invitation for an address, as [`IssueAsync`](#issueasyncstring-tunitid-troleid-datetimeoffset-timespan-cancellationtoken-tinvitationid) does with the ids of the unit and the role. |
| [`CancelAsync(TInvitationId, CancellationToken)`](#cancelasynctinvitationid-cancellationtoken) | Cancels an open invitation, so its token no longer works. |
| [`CancelAsync(TInvitation, CancellationToken)`](#cancelasynctinvitation-cancellationtoken) | Cancels an open invitation, as [`CancelAsync`](#cancelasynctinvitationid-cancellationtoken) does with its id. |
| [`AcceptAsync(string, string?, CancellationToken, TSeatId?, Action<TSeat>?)`](#acceptasyncstring-string-cancellationtoken-tseatid-actiontseat) | Accepts the invitation `token` is for, as the signed-in identity that is calling: in one save it makes that identity's seat in the invitation's tenant, places it in the invitation's unit as its primary placement, grants it the invitation's role there, and marks the invitation as accepted. |
| [`ListOpenAsync(CancellationToken)`](#listopenasynccancellationtoken) | The tenant's invitations that can still be accepted, the soonest to end first, the application's own class: for a seat, those into the units where it holds [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions), which may be none; for system work in the tenant, all of them. |

#### `InvitationManager(ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>, IInvitationStore<TInvitation, TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>, TenancyCatalogue, TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>, TenancyInvitationOptions<TInvitationId>, ICurrentUser, TimeProvider)`

```csharp
public InvitationManager(
    ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> store,
    IInvitationStore<TInvitation, TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId> invitations,
    TenancyCatalogue catalogue,
    TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> options,
    TenancyInvitationOptions<TInvitationId> invitationOptions,
    ICurrentUser user,
    TimeProvider clock)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `store`: Where seats, roles and the rest are loaded, and where the unit of work is saved.
- `invitations`: Where invitations are loaded and kept, in that unit of work.
- `catalogue`: The permissions asked for, and which roles manage access.
- `options`: Which signed-in users may hold a seat: whom an acceptance seats.
- `invitationOptions`: How long an invitation stays open.
- `user`: Who is calling, as the toolkit's current user answers: the signed-in identity that accepts.
- `clock`: What "now" is.

#### `IssueAsync(string, TUnitId, TRoleId, DateTimeOffset?, TimeSpan?, CancellationToken, TInvitationId?)`

```csharp
public Task<IssuedInvitation> IssueAsync(
    string address,
    TUnitId unit,
    TRoleId role,
    DateTimeOffset? grantUntil,
    TimeSpan? lifetime,
    CancellationToken cancellationToken,
    TInvitationId? id = null)
```

Issues an invitation for an address: a seat placed in `unit` as its primary placement, with `role` there until `grantUntil`, for whoever accepts it with the token this returns. Nothing is made but the invitation.

**Parameters**

- `address`: The address the invitation is for. The package sends nothing to it.
- `unit`: The unit the seat is placed in, which must be active.
- `role`: The role the seat is granted there, which must be active.
- `grantUntil`: When the grant ends, later than the invitation itself, or `null` for no end.
- `lifetime`: How long the invitation stays open, within [`TenancyInvitationOptions.MinLifetime`](#tenancyinvitationoptionstinvitationid) and [`TenancyInvitationOptions.MaxLifetime`](#tenancyinvitationoptionstinvitationid), or `null` for [`TenancyInvitationOptions.DefaultLifetime`](#tenancyinvitationoptionstinvitationid).
- `cancellationToken`: Cancels the work.
- `id`: Its id, for imports and seeding; a new one otherwise.

**Returns**: The new invitation, and its token, which is shown this once.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant or [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.invitation-lifetime`, `tenancy.tenant-inactive`, `tenancy.role-not-found`, `tenancy.grant-exceeds-own` for a role that manages access the caller could not give there for that long, while containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), `tenancy.unit-not-found`, `tenancy.unit-not-active`, `tenancy.role-not-active`, `tenancy.address-invalid`, `tenancy.invitation-grant-ends-first`.

**Example**

```csharp
var issued = await invitations.IssueAsync(command.Address, command.Unit, command.Role, command.Until, lifetime: null, cancellationToken);
// Send issued.Token to the address. Tenancy keeps only its digest.
```

#### `IssueAsync(string, TUnit, TRole, DateTimeOffset?, TimeSpan?, CancellationToken, TInvitationId?)`

```csharp
public Task<IssuedInvitation> IssueAsync(
    string address,
    TUnit unit,
    TRole role,
    DateTimeOffset? grantUntil,
    TimeSpan? lifetime,
    CancellationToken cancellationToken,
    TInvitationId? id = null)
```

Issues an invitation for an address, as [`IssueAsync`](#issueasyncstring-tunitid-troleid-datetimeoffset-timespan-cancellationtoken-tinvitationid) does with the ids of the unit and the role.

**Parameters**

- `address`: The address the invitation is for. The package sends nothing to it.
- `unit`: The unit the seat is placed in, which must be active.
- `role`: The role the seat is granted there, which must be active.
- `grantUntil`: When the grant ends, later than the invitation itself, or `null` for no end.
- `lifetime`: How long the invitation stays open, within [`TenancyInvitationOptions.MinLifetime`](#tenancyinvitationoptionstinvitationid) and [`TenancyInvitationOptions.MaxLifetime`](#tenancyinvitationoptionstinvitationid), or `null` for [`TenancyInvitationOptions.DefaultLifetime`](#tenancyinvitationoptionstinvitationid).
- `cancellationToken`: Cancels the work.
- `id`: Its id, for imports and seeding; a new one otherwise.

**Returns**: The new invitation, and its token, which is shown this once.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant or [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.invitation-lifetime`, `tenancy.tenant-inactive`, `tenancy.role-not-found`, `tenancy.grant-exceeds-own` for a role that manages access the caller could not give there for that long, while containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), `tenancy.unit-not-found`, `tenancy.unit-not-active`, `tenancy.role-not-active`, `tenancy.address-invalid`, `tenancy.invitation-grant-ends-first`.

**Example**

Tove is invited as Surveyor at North Coast. The invitation stays open for a week:

```csharp
var issued = await invitations.IssueAsync("tove@example.com", northCoast, surveyor, grantUntil: null, lifetime: TimeSpan.FromDays(7), cancellationToken);
```

#### `CancelAsync(TInvitationId, CancellationToken)`

```csharp
public Task<TInvitation> CancelAsync(TInvitationId invitation, CancellationToken cancellationToken)
```

Cancels an open invitation, so its token no longer works. A seat needs [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) at the invitation's unit, whoever issued it; an invitation into a unit where it does not manage seats is not found, as it is not among the ones it lists.

**Parameters**

- `invitation`: The invitation.
- `cancellationToken`: Cancels the work.

**Returns**: The invitation, cancelled.

**Exceptions**

- `RefusalException`: `tenancy.invitation-not-found`, `tenancy.invitation-state` for one that was accepted or cancelled already.

**Example**

```csharp
var cancelled = await invitations.CancelAsync(command.Invitation, cancellationToken);
```

#### `CancelAsync(TInvitation, CancellationToken)`

```csharp
public Task<TInvitation> CancelAsync(TInvitation invitation, CancellationToken cancellationToken)
```

Cancels an open invitation, as [`CancelAsync`](#cancelasynctinvitationid-cancellationtoken) does with its id.

**Parameters**

- `invitation`: The invitation.
- `cancellationToken`: Cancels the work.

**Returns**: The invitation, cancelled.

**Exceptions**

- `RefusalException`: `tenancy.invitation-not-found`, `tenancy.invitation-state` for one that was accepted or cancelled already.

**Example**

```csharp
var cancelled = await invitations.CancelAsync(issued.Invitation, cancellationToken);
```

#### `AcceptAsync(string, string?, CancellationToken, TSeatId?, Action<TSeat>?)`

```csharp
public Task<AcceptedInvitation> AcceptAsync(
    string token,
    string? verifiedAddress,
    CancellationToken cancellationToken,
    TSeatId? seat = null,
    Action<TSeat>? configure = null)
```

Accepts the invitation `token` is for, as the signed-in identity that is calling: in one save it makes that identity's seat in the invitation's tenant, places it in the invitation's unit as its primary placement, grants it the invitation's role there, and marks the invitation as accepted.

The caller is the toolkit's current user, read here and never handed in: a signed-in user with a verified identity, whose token role may hold a seat, and who did not sign in anonymously. It names no tenant and needs no seat: the token says which invitation, and so which tenant. Nothing is found by an address.

From there the work is the application's own, inside that tenant alone, for the seat that issued the invitation: that seat is who the placement and the grant keep as their giver, and the events name the system acting for it.

Accepting again as the same identity answers the same seat. Two acceptances at the same moment are kept apart by the invitation's own version and by the tenant's access revision: one is saved, and the other fails as any save that came second does, and is answered what the first made of the invitation when it is sent again.

**Parameters**

- `token`: The token, as `IssueAsync` returned it.
- `verifiedAddress`: The address the application knows the calling identity to have, verified by its identity provider, or `null` when the application does not hold an invitation to its address. Given, it only narrows: an invitation for another address is refused. It never finds an invitation or a person.
- `cancellationToken`: Cancels the work.
- `seat`: The new seat's id, for imports and seeding; a new one otherwise.
- `configure`: Sets the fields the application added to its seat class, such as the name it is shown by, on the new seat once the invitation is found to hold, and the seat is placed and holds the role, and before the invitation is marked as accepted and the seat is handed to the store: so they are saved in the same transaction, and the class's own rules judge them there. When it throws, nothing is accepted: no seat is saved, by this call or by a later save in the same scope, and the invitation stays open. It is not run for an invitation the same identity accepted before, which answers the seat it made then.

**Returns**: The tenant, whose slug the next request selects it by, and the seat the caller now has in it.

**Exceptions**

- `RefusalException`: `tenancy.identity-required` for a caller that is no signed-in identity that may hold a seat; `tenancy.invitation-not-found`, for a text that is no token too; `tenancy.invitation-cancelled`; `tenancy.invitation-used` when somebody else accepted it; `tenancy.invitation-lapsed` when its time ran out; `tenancy.address-mismatch`; `tenancy.tenant-inactive`; `tenancy.identity-has-seat`; `tenancy.unit-not-found` and `tenancy.unit-not-active`; `tenancy.role-not-found` and `tenancy.role-not-active`; `tenancy.invitation-unbacked` when its issuer may no longer give what it offers.
- `ConcurrencyConflictException`: Another change of access in the tenant, another acceptance included, was saved first.

**Example**

The handler of a request marked `[SignedIn]`, which names no tenant:

```csharp
var accepted = await invitations.AcceptAsync(command.Token, verifiedAddress: null, cancellationToken, configure: seat => seat.Rename(command.DisplayName));
// accepted.Tenant.Slug selects the tenant on the next request
```

#### `ListOpenAsync(CancellationToken)`

```csharp
public Task<IReadOnlyList<TInvitation>> ListOpenAsync(CancellationToken cancellationToken)
```

The tenant's invitations that can still be accepted, the soonest to end first, the application's own class: for a seat, those into the units where it holds [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions), which may be none; for system work in the tenant, all of them. They are read for the answer, and tracked by nobody. An invitation holds no token: the store keeps its digest apart.

**Parameters**

- `cancellationToken`: Cancels the read.

**Returns**: The open invitations the caller manages, the soonest to end first.

**Exceptions**

- `RefusalException`: The caller is nobody.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Example**

```csharp
var open = await invitations.ListOpenAsync(cancellationToken);
```

### `InvitationManager.AcceptedInvitation`

```csharp
public sealed record AcceptedInvitation
```

What accepting an invitation gives the person who accepted.

| Member | Summary |
|---|---|
| [`AcceptedInvitation(TTenant, TSeat)`](#acceptedinvitationttenant-tseat) | The primary constructor: the type's summary says what it makes. |
| `TTenant Tenant { get; init; }` | The tenant they now have a seat in, whose slug the next request selects it by. |
| `TSeat Seat { get; init; }` | Their seat, the application's own class. |

#### `AcceptedInvitation(TTenant, TSeat)`

```csharp
public AcceptedInvitation(TTenant Tenant, TSeat Seat)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Tenant`: The tenant they now have a seat in, whose slug the next request selects it by.
- `Seat`: Their seat, the application's own class.

### `InvitationManager.IssuedInvitation`

```csharp
public sealed record IssuedInvitation
```

An invitation as `IssueAsync` returns it: the one time its token is shown.

| Member | Summary |
|---|---|
| [`IssuedInvitation(TInvitation, string)`](#issuedinvitationtinvitation-string) | The primary constructor: the type's summary says what it makes. |
| `TInvitation Invitation { get; init; }` | The new invitation, the application's own class: its id, its address, and when it ends. |
| `string Token { get; init; }` | The token whoever accepts needs. Send it to the person, in the fragment of a link or in a message's text, and keep it nowhere. |
| [`ToString()`](#tostring) | Says which invitation and not its token: a record prints its members, and this one must not end up in a log. |

#### `IssuedInvitation(TInvitation, string)`

```csharp
public IssuedInvitation(TInvitation Invitation, string Token)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Invitation`: The new invitation, the application's own class: its id, its address, and when it ends.
- `Token`: The token whoever accepts needs. Send it to the person, in the fragment of a link or in a message's text, and keep it nowhere.

#### `ToString()`

```csharp
public override string ToString()
```

Says which invitation and not its token: a record prints its members, and this one must not end up in a log.

## Stores

Where the managers load and save. A storage package implements these interfaces and registers them: `DDDToolkit.Supporting.Tenancy.EntityFramework` does, in `AddTenancy` and `AddTenancyInvitations`. Your code takes the managers. A command of yours that changes a field of your own on one of the aggregates takes a store by the name the generator gives it, `TenancyStore` or, where you declare an invitation class, `InvitationStore`. Each name is an alias closed over your classes.

Namespace `DDDToolkit.Supporting.Tenancy.Managers`.

### `ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`

```csharp
public interface ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
```

Where the managers load and save: one unit of work per command. The storage package implements it, closed over the application's classes and ids, and registers it with `AddTenancy`.

Every lookup keeps to the current caller's tenant, as the storage's tenant filter does: a seat or a role of another tenant is simply not found. Two lookups go across tenants: [`SlugTakenAsync`](#slugtakenasyncstring-cancellationtoken), which says no more than yes or no, and [`ListTenantsAsync`](#listtenantsasyncstring-int-cancellationtoken), which is an operator's and reads what the operator may read.

The project that declares the application's classes, and every project above it that sees them, names it `TenancyStore`, a global alias the toolkit's generator writes, closed over those classes. A command of the application's that changes a field of its own on one of Tenancy's aggregates loads and saves it there, in the unit of work the managers save through.

| Member | Summary |
|---|---|
| `ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> Reads { get; }` | The rows the access questions read, as last saved. |
| `IQueryExecutor Queries { get; }` | Runs queries over [`Reads`](#itenancystorettenant-ttenantid-torganization-tunit-tunitid-tseat-tseatid-trole-troleid). |
| [`FindTenantAsync(TTenantId, CancellationToken)`](#findtenantasyncttenantid-cancellationtoken) | The tenant, or `null` when it is not the current caller's. |
| [`SlugTakenAsync(string, CancellationToken)`](#slugtakenasyncstring-cancellationtoken) | Whether any tenant, the caller's or not, has the slug. |
| [`ListTenantsAsync(string?, int, CancellationToken)`](#listtenantsasyncstring-int-cancellationtoken) | The tenants there are, past the tenant filter, in the order of their slugs: those whose slug comes after `afterSlug`, at most `take` of them, each with its organization's name and the number of its active seats. |
| [`FindOrganizationAsync(TTenantId, CancellationToken)`](#findorganizationasyncttenantid-cancellationtoken) | The tenant's organization with all its units, or `null` when it is not the current caller's. |
| [`ReadOrganizationAsync(TTenantId, CancellationToken)`](#readorganizationasyncttenantid-cancellationtoken) | The tenant's organization with all its units, for the managers' reads to answer, or `null` when it is not the current caller's. |
| [`FindSeatAsync(TSeatId, CancellationToken)`](#findseatasynctseatid-cancellationtoken) | The seat, or `null` when it is not in the current caller's tenant. |
| [`ListSeatsAsync(TTenantId, IReadOnlyCollection<TSeatId>?, CancellationToken)`](#listseatsasyncttenantid-ireadonlycollectiontseatid-cancellationtoken) | The tenant's seats, for the managers' reads to answer: all of them, or those among `only`; an id that is no seat of the tenant is simply not among the answer. |
| [`IdentityHasSeatAsync(TTenantId, Guid, CancellationToken)`](#identityhasseatasyncttenantid-guid-cancellationtoken) | Whether the identity already has a seat in the tenant. |
| [`FindRoleAsync(TRoleId, CancellationToken)`](#findroleasynctroleid-cancellationtoken) | The role, or `null` when it is not in the current caller's tenant. |
| [`ListRolesAsync(TTenantId, CancellationToken)`](#listrolesasyncttenantid-cancellationtoken) | Every role of the tenant, archived ones included. |
| [`RoleNameTakenAsync(TTenantId, string, TRoleId?, CancellationToken)`](#rolenametakenasyncttenantid-string-troleid-cancellationtoken) | Whether another role of the tenant than `except` has the name, ignoring case. |
| [`AdministratorsAsync(TTenantId, DateTimeOffset, CancellationToken)`](#administratorsasyncttenantid-datetimeoffset-cancellationtoken) | The tenant's administrators as last saved, as (seat, role) pairs: a right at the root for `tenancy.roles.manage`, with no end, applying at `now`. |
| [`RightsAMoveChangesAsync(TTenantId, TSeatId, TUnitId, TUnitId, IReadOnlyCollection<string>, DateTimeOffset, CancellationToken)`](#rightsamovechangesasyncttenantid-tseatid-tunitid-tunitid-ireadonlycollectionstring-datetimeoffset-cancellationtoken) | Every right that has not ended at `now`, one still to start included, held at a unit at or above `parent` or `newParent`, of `seat` or of a permission in `managing`, once for each of the two it reaches. |
| [`UnitsNamedAsync(TTenantId, IReadOnlyCollection<TUnitId>, CancellationToken)`](#unitsnamedasyncttenantid-ireadonlycollectiontunitid-cancellationtoken) | The units among `ids` that are `tenant`'s, by name and path and nothing more, whatever the calling seat reads of the organization: on a database that keeps its own policies, where an application's restrictive rule may keep the seat out of the units, through Tenancy's function that runs as its owner and answers those ids alone. |
| [`TenantNameAsync(TTenantId, CancellationToken)`](#tenantnameasyncttenantid-cancellationtoken) | What `tenant`, the calling seat's, is called, whatever the seat reads of its organization: on a database that keeps its own policies, where an application's restrictive rule may keep the seat out of the organization, through Tenancy's function that runs as its owner and answers the tenants of the caller's identity, as the tenant picker reads them. |
| [`Add(TTenant)`](#addttenant) | Adds a new tenant to the unit of work. |
| [`Add(TOrganization)`](#addtorganization) | Adds a new organization to the unit of work. |
| [`Add(TSeat)`](#addtseat) | Adds a new seat to the unit of work. |
| [`Add(TRole)`](#addtrole) | Adds a new role to the unit of work. |
| [`AddAccessRevision(TTenantId)`](#addaccessrevisionttenantid) | Adds a new tenant's access revision, which every later change of rights in it takes first. |
| [`SerializeAccessChangesAsync(TTenantId, CancellationToken)`](#serializeaccesschangesasyncttenantid-cancellationtoken) | Takes the tenant's access revision for this unit of work: reads it and bumps it, so the save fails when another change of rights in the tenant was saved after it was read. |
| [`SaveAsync(CancellationToken)`](#saveasynccancellationtoken) | Saves the unit of work, in one transaction. |

#### `FindTenantAsync(TTenantId, CancellationToken)`

```csharp
Task<TTenant?> FindTenantAsync(TTenantId id, CancellationToken cancellationToken)
```

The tenant, or `null` when it is not the current caller's.

#### `SlugTakenAsync(string, CancellationToken)`

```csharp
Task<bool> SlugTakenAsync(string slug, CancellationToken cancellationToken)
```

Whether any tenant, the caller's or not, has the slug.

#### `ListTenantsAsync(string?, int, CancellationToken)`

```csharp
Task<IReadOnlyList<TenantListing>> ListTenantsAsync(
    string? afterSlug,
    int take,
    CancellationToken cancellationToken)
```

The tenants there are, past the tenant filter, in the order of their slugs: those whose slug comes after `afterSlug`, at most `take` of them, each with its organization's name and the number of its active seats. What `TenantManager.ListAsync` shows an operator, and read as that operator: a storage that keeps tenants apart by itself answers what the caller may read there, which for anyone else is nothing, or their own tenant.

**Parameters**

- `afterSlug`: The slug the page comes after, or `null` for the first tenants.
- `take`: How many tenants to answer at most.
- `cancellationToken`: Cancels the read.

#### `FindOrganizationAsync(TTenantId, CancellationToken)`

```csharp
Task<TOrganization?> FindOrganizationAsync(TTenantId id, CancellationToken cancellationToken)
```

The tenant's organization with all its units, or `null` when it is not the current caller's.

#### `ReadOrganizationAsync(TTenantId, CancellationToken)`

```csharp
Task<TOrganization?> ReadOrganizationAsync(TTenantId id, CancellationToken cancellationToken)
```

The tenant's organization with all its units, for the managers' reads to answer, or `null` when it is not the current caller's. Read as the application's own classes, with every field it added to its unit class, since the reads hand the units to the application; and read only, never for a save, as [`ListSeatsAsync`](#listseatsasyncttenantid-ireadonlycollectiontseatid-cancellationtoken) reads seats: a change made to a unit is not written, by this unit of work or another.

**Parameters**

- `id`: The tenant, which is the current caller's.
- `cancellationToken`: Cancels the read.

#### `FindSeatAsync(TSeatId, CancellationToken)`

```csharp
Task<TSeat?> FindSeatAsync(TSeatId id, CancellationToken cancellationToken)
```

The seat, or `null` when it is not in the current caller's tenant. A storage that keeps grants to the seats that may read them, as Tenancy's policies on Postgres do, loads it with the grants the caller may read: all of its own seat's, and another seat's at the units where the caller manages grants, seats or units, or every one when it manages roles for the whole tenant. Each command acts only on grants at a unit where it asked one of those permissions first, or for the whole tenant, so they are among them.

#### `ListSeatsAsync(TTenantId, IReadOnlyCollection<TSeatId>?, CancellationToken)`

```csharp
Task<IReadOnlyList<TSeat>> ListSeatsAsync(
    TTenantId tenant,
    IReadOnlyCollection<TSeatId>? only,
    CancellationToken cancellationToken)
```

The tenant's seats, for the managers' reads to answer: all of them, or those among `only`; an id that is no seat of the tenant is simply not among the answer. Read as the application's own seats, with every field it added, since the reads hand them to the application whole; and read only, never for a save: a change made to one is not written, by this unit of work or another. A storage that keeps grants to the seats that may read them loads each seat's grants as [`FindSeatAsync`](#findseatasynctseatid-cancellationtoken) does.

**Parameters**

- `tenant`: The tenant, which is the current caller's.
- `only`: The seats asked about, or `null` for every seat of the tenant.
- `cancellationToken`: Cancels the read.

#### `IdentityHasSeatAsync(TTenantId, Guid, CancellationToken)`

```csharp
Task<bool> IdentityHasSeatAsync(
    TTenantId tenant,
    Guid identity,
    CancellationToken cancellationToken)
```

Whether the identity already has a seat in the tenant.

#### `FindRoleAsync(TRoleId, CancellationToken)`

```csharp
Task<TRole?> FindRoleAsync(TRoleId id, CancellationToken cancellationToken)
```

The role, or `null` when it is not in the current caller's tenant.

#### `ListRolesAsync(TTenantId, CancellationToken)`

```csharp
Task<IReadOnlyList<TRole>> ListRolesAsync(TTenantId tenant, CancellationToken cancellationToken)
```

Every role of the tenant, archived ones included.

#### `RoleNameTakenAsync(TTenantId, string, TRoleId?, CancellationToken)`

```csharp
Task<bool> RoleNameTakenAsync(
    TTenantId tenant,
    string name,
    TRoleId? except,
    CancellationToken cancellationToken)
```

Whether another role of the tenant than `except` has the name, ignoring case.

#### `AdministratorsAsync(TTenantId, DateTimeOffset, CancellationToken)`

```csharp
Task<IReadOnlyList<(TSeatId Seat, TRoleId Role)>> AdministratorsAsync(
    TTenantId tenant,
    DateTimeOffset now,
    CancellationToken cancellationToken)
```

The tenant's administrators as last saved, as (seat, role) pairs: a right at the root for `tenancy.roles.manage`, with no end, applying at `now`. What the rule that a tenant keeps an administrator reads. It is about every seat's rights, which a storage may keep from a seat that reads [`Reads`](#itenancystorettenant-ttenantid-torganization-tunit-tunitid-tseat-tseatid-trole-troleid): the storage answers it from wherever it can, as far as the caller may read the grants at the root. That is whole for every command that asks: each one that could take an administrator away asks a permission at the root first, and one that acts below the root does not ask.

**Parameters**

- `tenant`: The tenant, which is the current caller's.
- `now`: The moment a right must apply at.
- `cancellationToken`: Cancels the read.

#### `RightsAMoveChangesAsync(TTenantId, TSeatId, TUnitId, TUnitId, IReadOnlyCollection<string>, DateTimeOffset, CancellationToken)`

```csharp
Task<IReadOnlyList<MoveReach<TUnitId>>> RightsAMoveChangesAsync(
    TTenantId tenant,
    TSeatId seat,
    TUnitId parent,
    TUnitId newParent,
    IReadOnlyCollection<string> managing,
    DateTimeOffset now,
    CancellationToken cancellationToken)
```

Every right that has not ended at `now`, one still to start included, held at a unit at or above `parent` or `newParent`, of `seat` or of a permission in `managing`, once for each of the two it reaches. What the check of a move reads. Another seat's right is answered only where it reaches one of the two and not the other, which is a right the move changes: one that reaches both reaches the unit wherever it hangs, so the check never weighs it, and a seat that manages only part of the tree learns nothing of the rights above that part that stay. Like [`AdministratorsAsync`](#administratorsasyncttenantid-datetimeoffset-cancellationtoken), it is about every seat's rights, and the storage answers it whole. The manager asks once it has found `seat` to hold `tenancy.units.manage` at both parents, so the seat's own rights for that permission are always among the rows: a storage that cannot tell so much has no answer, and fails rather than answer with less, which would read as a move that changes nothing.

**Parameters**

- `tenant`: The tenant, which is the current caller's.
- `seat`: The seat that moves the unit, whose own rights count whatever their permission.
- `parent`: The unit the moved unit hangs under.
- `newParent`: The unit it would hang under.
- `managing`: The permissions that manage access, whose rights count whoever holds them.
- `now`: The moment a right must not have ended at.
- `cancellationToken`: Cancels the read.

#### `UnitsNamedAsync(TTenantId, IReadOnlyCollection<TUnitId>, CancellationToken)`

```csharp
Task<IReadOnlyList<NamedUnit<TUnitId>>?> UnitsNamedAsync(
    TTenantId tenant,
    IReadOnlyCollection<TUnitId> ids,
    CancellationToken cancellationToken)
```

The units among `ids` that are `tenant`'s, by name and path and nothing more, whatever the calling seat reads of the organization: on a database that keeps its own policies, where an application's restrictive rule may keep the seat out of the units, through Tenancy's function that runs as its owner and answers those ids alone. `null` from a storage that keeps no policies of its own, where the caller reads every unit of its tenant, so the manager reads them whole.

**Parameters**

- `tenant`: The tenant, which is the calling seat's.
- `ids`: The units asked about, at least one.
- `cancellationToken`: Cancels the read.

#### `TenantNameAsync(TTenantId, CancellationToken)`

```csharp
Task<string?> TenantNameAsync(TTenantId tenant, CancellationToken cancellationToken)
```

What `tenant`, the calling seat's, is called, whatever the seat reads of its organization: on a database that keeps its own policies, where an application's restrictive rule may keep the seat out of the organization, through Tenancy's function that runs as its owner and answers the tenants of the caller's identity, as the tenant picker reads them. `null` from a storage that keeps no policies of its own, where the caller reads the organization, and for a tenant without a name.

**Parameters**

- `tenant`: The tenant, which is the calling seat's.
- `cancellationToken`: Cancels the read.

#### `Add(TTenant)`

```csharp
void Add(TTenant tenant)
```

Adds a new tenant to the unit of work.

#### `Add(TOrganization)`

```csharp
void Add(TOrganization organization)
```

Adds a new organization to the unit of work.

#### `Add(TSeat)`

```csharp
void Add(TSeat seat)
```

Adds a new seat to the unit of work.

#### `Add(TRole)`

```csharp
void Add(TRole role)
```

Adds a new role to the unit of work.

#### `AddAccessRevision(TTenantId)`

```csharp
void AddAccessRevision(TTenantId tenant)
```

Adds a new tenant's access revision, which every later change of rights in it takes first.

#### `SerializeAccessChangesAsync(TTenantId, CancellationToken)`

```csharp
Task SerializeAccessChangesAsync(TTenantId tenant, CancellationToken cancellationToken)
```

Takes the tenant's access revision for this unit of work: reads it and bumps it, so the save fails when another change of rights in the tenant was saved after it was read.

#### `SaveAsync(CancellationToken)`

```csharp
Task SaveAsync(CancellationToken cancellationToken)
```

Saves the unit of work, in one transaction.

### `IInvitationStore<TInvitation, TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>`

```csharp
public interface IInvitationStore<TInvitation, TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Where the invitation manager loads and keeps invitations, in the unit of work of [`ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`](#itenancystorettenant-ttenantid-torganization-tunit-tunitid-tseat-tseatid-trole-troleid): what is added or changed here is saved by that store's save, with everything else the command changed. The storage package implements it.

A token's digest is handed over with its invitation and never comes back. The store keeps it where reading invitations does not read it, and answers one question about it: which invitation it belongs to.

Where the application declares an invitation class, the project that declares it, and every project above that sees it, names this interface `InvitationStore`, a global alias the toolkit's generator writes, closed over the application's classes.

**Type parameters**

- `TInvitation`: The application's invitation class.
- `TInvitationId`: The application's invitation id.
- `TTenantId`: The application's tenant id.
- `TUnitId`: The application's unit id.
- `TRoleId`: The application's role id.
- `TSeatId`: The application's seat id.

| Member | Summary |
|---|---|
| [`FindAsync(TInvitationId, CancellationToken)`](#findasynctinvitationid-cancellationtoken) | The invitation, or `null` when it is not in the current caller's tenant. |
| [`ListOpenAsync(TTenantId, DateTimeOffset, CancellationToken)`](#listopenasyncttenantid-datetimeoffset-cancellationtoken) | The tenant's invitations that can still be accepted at `now`: open, and not run out. |
| [`FindByDigestAsync(byte[], CancellationToken)`](#findbydigestasyncbyte-cancellationtoken) | Which invitation the token with this digest is for, in whatever tenant, as ids only; or `null` when there is none. |
| [`Add(TInvitation, byte[])`](#addtinvitation-byte) | Adds a new invitation to the unit of work, with the digest of its token. |

#### `FindAsync(TInvitationId, CancellationToken)`

```csharp
Task<TInvitation?> FindAsync(TInvitationId id, CancellationToken cancellationToken)
```

The invitation, or `null` when it is not in the current caller's tenant.

#### `ListOpenAsync(TTenantId, DateTimeOffset, CancellationToken)`

```csharp
Task<IReadOnlyList<TInvitation>> ListOpenAsync(
    TTenantId tenant,
    DateTimeOffset now,
    CancellationToken cancellationToken)
```

The tenant's invitations that can still be accepted at `now`: open, and not run out. For reading only: they are not part of the unit of work.

**Parameters**

- `tenant`: The tenant, which is the current caller's.
- `now`: The moment an invitation must still be open at.
- `cancellationToken`: Cancels the read.

#### `FindByDigestAsync(byte[], CancellationToken)`

```csharp
Task<InvitationOfToken<TInvitationId, TTenantId, TSeatId>?> FindByDigestAsync(
    byte[] digest,
    CancellationToken cancellationToken)
```

Which invitation the token with this digest is for, in whatever tenant, as ids only; or `null` when there is none. It is the one read of invitations across tenants: whoever accepts an invitation names no tenant, the token does. It runs apart from the unit of work, as nobody in particular, and answers nothing else about the invitation.

**Parameters**

- `digest`: The digest of the token somebody sent.
- `cancellationToken`: Cancels the read.

#### `Add(TInvitation, byte[])`

```csharp
void Add(TInvitation invitation, byte[] digest)
```

Adds a new invitation to the unit of work, with the digest of its token.

**Parameters**

- `invitation`: The invitation.
- `digest`: The digest of its token: `BearerTokens.DigestLength` bytes.

### `InvitationOfToken<TInvitationId, TTenantId, TSeatId>`

```csharp
public sealed record InvitationOfToken<TInvitationId, TTenantId, TSeatId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Which invitation a token is for, as the invitation store answers it across tenants: ids, and nothing else of it.

**Type parameters**

- `TInvitationId`: The application's invitation id.
- `TTenantId`: The application's tenant id.
- `TSeatId`: The application's seat id.

| Member | Summary |
|---|---|
| [`InvitationOfToken(TTenantId, TInvitationId, TSeatId?)`](#invitationoftokenttenantid-tinvitationid-tseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId Tenant { get; init; }` | The tenant the invitation is into. |
| `TInvitationId Invitation { get; init; }` | The invitation. |
| `TSeatId? IssuedBy { get; init; }` | The seat that issued it, or that system work issued it for; `null` for none. |

#### `InvitationOfToken(TTenantId, TInvitationId, TSeatId?)`

```csharp
public InvitationOfToken(TTenantId Tenant, TInvitationId Invitation, TSeatId? IssuedBy)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Tenant`: The tenant the invitation is into.
- `Invitation`: The invitation.
- `IssuedBy`: The seat that issued it, or that system work issued it for; `null` for none.

## Registration

What a host registers, from all three packages: Tenancy's services and options, the access checks of a module's requests, and Postgres. The calls in a context's model that map Tenancy's tables are with Entity Framework, under [Mapping](entity-framework.md#mapping).

### `TenancyInvitationOptions<TInvitationId>`

```csharp
public sealed class TenancyInvitationOptions<TInvitationId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
```

Namespace `DDDToolkit.Supporting.Tenancy`.

What an application that invites people may tell Tenancy: how long an invitation stays open, which has defaults. A new invitation's id is made by the id itself, with its `Create()`, as every id of Tenancy's is.

**Type parameters**

- `TInvitationId`: The application's invitation id.

| Member | Summary |
|---|---|
| `TimeSpan DefaultLifetime { get; set; }` | How long an invitation stays open when whoever issues it does not say: seven days. |
| `TimeSpan MinLifetime { get; set; }` | The shortest an invitation may stay open: ten minutes. |
| `TimeSpan MaxLifetime { get; set; }` | The longest an invitation may stay open: thirty days. A token that is never used should not stay good for ever. |

### `TenancyOperatorsAttribute`

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class TenancyOperatorsAttribute : Attribute
```

Namespace `DDDToolkit.Supporting.Tenancy`.

Marks the token roles of the application's operators, the ones it adds to `TenancyOptions.OperatorTokenRoles`, so the program that exports the database's policies writes them for the same roles:

```csharp
public static class ShopOperators
{
    [TenancyOperators]
    public static IReadOnlyList<string> TokenRoles { get; } = ["operator"];
}

// Where Tenancy is registered
options.OperatorTokenRoles.UnionWith(ShopOperators.TokenRoles);
```

Tenancy on Postgres gives the database role of each a policy that reads every tenant and writes nothing, and the host's start-up check stops a host that runs with other operators than its database was written for. An application without operators marks nothing, and nothing is written for them.

The member is a static property or field of a collection of `string`, readable from the project that runs the export: in a library it is public, in public types, which that library's own build checks (DDD00070). One member is marked in all the projects it references: the export reports two, and one of another type (DDD00071).

### `TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>`

```csharp
public sealed class TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Namespace `DDDToolkit.Supporting.Tenancy`.

What the application tells Tenancy, all of it with a default: what it adds to the catalogue ([`Catalogue`](#tenancyoptionsttenantid-tseatid-tunitid-troleid)), which signed-in users may hold a seat ([`TenantSelection`](#tenancyoptionsttenantid-tseatid-tunitid-troleid)), and who its operators are ([`OperatorTokenRoles`](#operatortokenroles), empty unless it has any).

How a new id is made is no option: each id says it itself, with its `Create()`, which Tenancy calls as `TTenantId.Create()`, in code and before the save. The generator writes it for an id over a `Guid`, a time-ordered one, and an id over anything else declares its own (DDD00067 says so where it has none). A command that is given an id, an import or fixed seed data, uses that one instead.

| Member | Summary |
|---|---|
| `ApplicationCatalogue? Catalogue { get; set; }` | What the application adds to the catalogue: starter roles, permissions of its own and marks on permissions that manage access. Left unset, it adds none: the permissions are Tenancy's and what the modules contribute, and every tenant starts with the default administrators' role ([`TenancyStarterRoles.DefaultAdministrators`](catalogue.md#defaultadministrators)). |
| `TenantSelectionOptions TenantSelection { get; }` | Which signed-in users may hold a seat, by the role their token carries: those with `authenticated` unless the application lists others in [`TenantSelectionOptions.SeatedTokenRoles`](callers.md#tenantselectionoptions). |
| [`OperatorTokenRoles`](#operatortokenroles) | The token roles whose users are operators of the application: staff who look across tenants, by the role their token carries (`ICurrentUser.Role`, as the host's token role map spells it). |
| [`IsOperator(CurrentUserSnapshot?)`](#isoperatorcurrentusersnapshot) | Whether `user` is one of the application's operators: a signed-in user whose token carries one of [`OperatorTokenRoles`](#operatortokenroles). |

#### `OperatorTokenRoles`

```csharp
public ISet<string> OperatorTokenRoles { get; }
```

The token roles whose users are operators of the application: staff who look across tenants, by the role their token carries (`ICurrentUser.Role`, as the host's token role map spells it). Empty by default: an application without operators has none.

```csharp
options.OperatorTokenRoles.Add("operator");
```

An operator holds no seat. Its token role is never one of [`TenantSelectionOptions.SeatedTokenRoles`](callers.md#tenantselectionoptions), so tenant selection answers nobody for it whatever tenant the request names, keeping that tenant, and registration refuses a role that is on both lists. An operator reads: the tenants, through `TenantManager`, and on Postgres whatever the policies of its own database role let it read. It changes nothing as itself. A command marked [`OperatorAttribute`](callers.md#operatorattribute) runs as operator work, which is system work that names it.

#### `IsOperator(CurrentUserSnapshot?)`

```csharp
public bool IsOperator(CurrentUserSnapshot? user)
```

Whether `user` is one of the application's operators: a signed-in user whose token carries one of [`OperatorTokenRoles`](#operatortokenroles). The one place that says so: `TenantManager.ListAsync` and the check of [`OperatorAttribute`](callers.md#operatorattribute) both ask here, so they mean the same caller by it. System work is never one, whoever it is recorded as, and neither is anyone without an identity.

**Parameters**

- `user`: Who is calling, as the toolkit's current user answers: the user of the request, not the Tenancy caller.

### `TenancyServiceCollectionExtensions`

```csharp
public static class TenancyServiceCollectionExtensions
```

Namespace `DDDToolkit.Supporting.Tenancy`.

Registration of Tenancy's storage-agnostic parts. The storage package registers the rest, the store and the seat directory, and calls [`AddTenancyCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`](#addtenancycoreiservicecollection-actiontenancyoptionsttenantid-tseatid-tunitid-troleid) itself; an application calls the storage package's registration, and adds its modules' permissions once, from the host: `services.AddTenancyPermissionsOfModules()`, which Tenancy's generator writes from the lists the modules mark with `TenancyPermissionsAttribute`. [`AddTenancyPermissions`](#addtenancypermissionsiservicecollection-ienumerablepermission) adds a list by hand.

| Member | Summary |
|---|---|
| [`AddTenancyCore(IServiceCollection, Action<TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>>?)`](#addtenancycoreiservicecollection-actiontenancyoptionsttenantid-tseatid-tunitid-troleid) | Registers the catalogue, the access manager, tenant selection and the managers, everything except the store and the seat directory. |
| [`AddTenancyInvitationsCore(IServiceCollection, Action<TenancyInvitationOptions<TInvitationId>>?)`](#addtenancyinvitationscoreiservicecollection-actiontenancyinvitationoptionstinvitationid) | Registers the invitation manager, closed over the application's invitation class, for an application that invites people into a tenant: everything of invitations except their store, which the storage package registers; that package calls this itself. |
| [`AddTenancyOperatorAccess(IServiceCollection)`](#addtenancyoperatoraccessiservicecollection) | Registers the check of Tenancy's operator cases for the requests of `TRequests`: [`TenancyRequirement.Operator`](callers.md#tenancyrequirementoperator) and [`TenancyRequirement.OperatorIn<TTenantId>`](callers.md#tenancyrequirementoperatorinttenantid), which a request declares with [`OperatorAttribute`](callers.md#operatorattribute). |
| [`AddTenancyPermissions(IServiceCollection, IEnumerable<Permission>)`](#addtenancypermissionsiservicecollection-ienumerablepermission) | Adds permissions to the catalogue by hand. |
| [`SyncStarterRoles(IServiceCollection)`](#syncstarterrolesiservicecollection) | Brings every tenant's roles up to the starter roles they were made from, in the background, once the host has started: after its start-up checks, when it runs them, and after every hosted service has started. |

#### `AddTenancyCore(IServiceCollection, Action<TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>>?)`

```csharp
public static IServiceCollection AddTenancyCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>(
    this IServiceCollection services,
    Action<TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>>? configure = null)
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
```

Registers the catalogue, the access manager, tenant selection and the managers, everything except the store and the seat directory.

- The catalogue is built once, from [`TenancyOptions.Catalogue`](#tenancyoptionsttenantid-tseatid-tunitid-troleid), or from none when it is not set, and every [`PermissionContribution`](catalogue.md#permissioncontribution), the first time it is asked for; resolve it at start-up to have a catalogue that does not hold together stop the application there.
- The access manager, [`TenancyAccessManager<TTenantId, TSeatId, TUnitId, TRoleId>`](callers.md#tenancyaccessmanagerttenantid-tseatid-tunitid-troleid), selection and the managers are made per scope. The storage package's `AddTenancy` registers the application's own `TenancyAccessManager`, `TenantManager`, `OrganizationManager`, `SeatManager` and `RoleManager` beside them. Selection is registered without its id types as well, [`ITenantSelection`](callers.md#itenantselection), the same instance, unless a host registered its own before: that one stays.
- The toolkit's `ICurrentUser` a host registered before stays; otherwise `CurrentUser` is registered.
- What the caller's own seat is, for a request about it (`[OrOwn] SeatId Seat`, `TenancyAccess.Own(seat)`): the caller's seat in the tenant, unless a host registered its own before with `AddCallersOwn`.
- A `TimeProvider` registered before stays; otherwise the system clock is used.
- A new id is made by the id itself, `TTenantId.Create()`, which every id of the application's classes has: the generator writes it for an id over a `Guid`, and DDD00067 asks for it on any other.

**Parameters**

- `services`: The service collection.
- `configure`: Changes the options, every one of which has a default; leave it out to keep them all.

**Exceptions**

- `InvalidOperationException`: A token role is an operator's and seated as well.

#### `AddTenancyInvitationsCore(IServiceCollection, Action<TenancyInvitationOptions<TInvitationId>>?)`

```csharp
public static IServiceCollection AddTenancyInvitationsCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TInvitation, TInvitationId>(
    this IServiceCollection services,
    Action<TenancyInvitationOptions<TInvitationId>>? configure = null)
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
    where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TInvitationId : struct, ICreatableEntityId<TInvitationId>, IEquatable<TInvitationId>
```

Registers the invitation manager, closed over the application's invitation class, for an application that invites people into a tenant: everything of invitations except their store, which the storage package registers; that package calls this itself. Call it after [`AddTenancyCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`](#addtenancycoreiservicecollection-actiontenancyoptionsttenantid-tseatid-tunitid-troleid), which registers what the managers share: the catalogue, the options, the clock and who is calling.

**Parameters**

- `services`: The service collection.
- `configure`: Changes how long an invitation stays open, which has defaults; leave it out to keep them.

**Exceptions**

- `InvalidOperationException`: Tenancy itself is not registered yet; or the lifetimes are out of order.

#### `AddTenancyOperatorAccess(IServiceCollection)`

```csharp
public static IServiceCollection AddTenancyOperatorAccess<TTenantId, TSeatId, TUnitId, TRoleId, TRequests>(this IServiceCollection services)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TRequests : class, IRequireAccess
```

Registers the check of Tenancy's operator cases for the requests of `TRequests`: [`TenancyRequirement.Operator`](callers.md#tenancyrequirementoperator) and [`TenancyRequirement.OperatorIn<TTenantId>`](callers.md#tenancyrequirementoperatorinttenantid), which a request declares with [`OperatorAttribute`](callers.md#operatorattribute). It refuses every caller who is no operator, and runs a command an operator sends as operator work. The storage package's `AddTenancyAccess` calls it beside the check of Tenancy's other cases, so an application calls that one. Adding it twice adds it once.

**Parameters**

- `services`: The service collection.

**Type parameters**

- `TTenantId`: The application's tenant id.
- `TSeatId`: The application's seat id.
- `TUnitId`: The application's unit id.
- `TRoleId`: The application's role id.
- `TRequests`: The module's request interface.

#### `AddTenancyPermissions(IServiceCollection, IEnumerable<Permission>)`

```csharp
public static IServiceCollection AddTenancyPermissions(
    this IServiceCollection services,
    IEnumerable<Permission> permissions)
```

Adds permissions to the catalogue by hand. Contributions add up. A permission may be declared only once in the whole catalogue, and never under `tenancy.`.

A module declares its permissions once, as the constants of a static class marked `[Permissions]`, and the host adds every module's with the call Tenancy's generator writes into it, `services.AddTenancyPermissionsOfModules()`, which calls this; an export builds the same catalogue from `TenancyPermissionsOfModules.All`. So a module's permissions are not added with this as well: the same list added twice stops the catalogue when it is built. This is for permissions no generator collects, such as those of a module the host composes by hand.

**Parameters**

- `services`: The service collection.
- `permissions`: The module's permissions.

#### `SyncStarterRoles(IServiceCollection)`

```csharp
public static IServiceCollection SyncStarterRoles(this IServiceCollection services)
```

Brings every tenant's roles up to the starter roles they were made from, in the background, once the host has started: after its start-up checks, when it runs them, and after every hosted service has started. A role made from a starter role gets the permissions the starter role gained since the role was made or last followed it, a module's new permissions in an administrators' starter role that lists none among them, and loses the permissions the starter role lost; what the tenant changed itself stays. Each role that changes raises [`RoleFollowedItsStarterRole<TTenantId, TRoleId, TSeatId>`](aggregates.md#rolefolloweditsstarterrolettenantid-troleid-tseatid), for the host to tell the tenant's administrators; the package sends no message itself.

```csharp
builder.Services.RunStartupChecks();
builder.Services.SyncStarterRoles();
```

It runs [`IStarterRoleSync`](catalogue.md#istarterrolesync), which Tenancy's storage package registers with `AddTenancy`, once per start of the host, as Tenancy's system work in each tenant, one tenant after the other. A second run changes nothing, and so does a run on another instance of the host at the same time as this one: whichever commits second finds the roles following their starter roles already. A tenant it cannot sync is logged and left for the next start, and the host keeps serving meanwhile. A host that wants it elsewhere, in a deployment step or behind an operator's endpoint, leaves this call out and runs [`IStarterRoleSync`](catalogue.md#istarterrolesync) itself.

It is a call of its own, and not a start-up check: a check reads and changes nothing, and stops a start that would go wrong, while this changes the tenants' roles, and the host may serve without it. Calling it more than once runs it once.

**Parameters**

- `services`: The application's services.

**Exceptions**

- `ArgumentNullException`: `services` is null.

### `TenancyDbContextOptionsExtensions`

```csharp
public static class TenancyDbContextOptionsExtensions
```

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

Adds Tenancy's part of every save to a context.

| Member | Summary |
|---|---|
| [`UseTenancy(DbContextOptionsBuilder, IServiceProvider)`](#usetenancydbcontextoptionsbuilder-iserviceprovider) | Adds the [`TenancySaveInterceptor`](entity-framework.md#tenancysaveinterceptor) that `AddTenancy` registered. |

#### `UseTenancy(DbContextOptionsBuilder, IServiceProvider)`

```csharp
public static DbContextOptionsBuilder UseTenancy(
    this DbContextOptionsBuilder optionsBuilder,
    IServiceProvider serviceProvider)
```

Adds the [`TenancySaveInterceptor`](entity-framework.md#tenancysaveinterceptor) that `AddTenancy` registered. `UseDDDToolkit` calls it for every context once Tenancy is registered, so a context wired with that one call needs nothing more:

```csharp
services.AddDbContext<ProjectsContext>((serviceProvider, options) => options
    .UseNpgsql(connectionString)
    .UseDDDToolkit(serviceProvider));
```

The interceptor goes on every context the one call wires, Tenancy's own and a module's alike: the options do not know the model yet, and the interceptor does, at every save. It checks the saves of a context whose model keeps rows to a tenant, Tenancy's own or those of `ScopeToTenant`, writes the rights where the model maps Tenancy's own classes, and does nothing where the model has neither.

Call it yourself after `UseDDDToolkitCore`, for a context that takes the parts it wants one by one, and last: after the toolkit's interceptors, the save check sees what the domain event handlers changed, and only aggregates that passed their invariants; and a row an interceptor after it added would be saved unchecked. [`TenancyChecks.EnsureWired`](entity-framework.md#ensurewireddbcontext) refuses a context that has it the other way round, or not at all. It adds nothing to options that already have the interceptor, so a chain that calls it after `UseDDDToolkit` gives it once.

The same call serves a context pool (`AddPooledDbContextFactory`, `AddDbContextPool`): the save check reads the caller of each save, not of the options, so a context taken from a pool checks each renter's saves against that renter's tenant.

**Parameters**

- `optionsBuilder`: The context's options.
- `serviceProvider`: The provider handed to the options callback, of `AddDbContext` or of a context pool: the interceptor is one instance for the application, so the root provider a pool hands its callback serves as well as a scope's.

**Exceptions**

- `ArgumentNullException`: `optionsBuilder` or `serviceProvider` is null.
- `InvalidOperationException`: Tenancy is not registered in `serviceProvider`.

### `TenancyEntityFrameworkServiceCollectionExtensions`

```csharp
public static class TenancyEntityFrameworkServiceCollectionExtensions
```

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

Registers Tenancy, stored with Entity Framework.

| Member | Summary |
|---|---|
| [`AddTenancy(IServiceCollection, Action<TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>>?)`](#addtenancyiservicecollection-actiontenancyoptionsttenantid-tseatid-tunitid-troleid) | Registers Tenancy over `TContext`, the context whose model calls `AddTenancy`: everything [`TenancyServiceCollectionExtensions.AddTenancyCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`](#addtenancycoreiservicecollection-actiontenancyoptionsttenantid-tseatid-tunitid-troleid) registers, the store the managers load and save through, the seat directory tenant selection asks, the save interceptor, brought to every context `UseDDDToolkit` wires as Tenancy's part ([`TenancySaveInterceptor.PartName`](entity-framework.md#tenancysaveinterceptor)), the query executor, and what an event log asks: who acted, as Tenancy knows the caller ([`TenancyActedByAccessor`](entity-framework.md#tenancyactedbyaccessor), put around the accessor registered before this call, or around the toolkit's default), and the tenant of each row of the log `AddTenancyEventLogTable` maps. |
| [`AddTenancyInvitations(IServiceCollection, Action<TenancyInvitationOptions<TInvitationId>>?)`](#addtenancyinvitationsiservicecollection-actiontenancyinvitationoptionstinvitationid) | Registers invitations over `TContext`, the context whose model calls `AddTenancyInvitations`, for an application that invites people into a tenant: the invitation manager, `TInvitationManager`, the application's `InvitationManager`, and the store it loads and keeps invitations through. |
| [`AddTenancyAccess(IServiceCollection)`](#addtenancyaccessiservicecollection) | Adds Tenancy's access check to the checks the requests of one module are held to (`AccessChecks<TRequests>`): what decides the cases of [`TenancyRequirement`](callers.md#tenancyrequirement) a request of that module declares, before its handler runs. |

#### `AddTenancy(IServiceCollection, Action<TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>>?)`

```csharp
public static IServiceCollection AddTenancy<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TAccessManager, TTenantManager, TOrganizationManager, TSeatManager, TRoleManager, TContext>(
    this IServiceCollection services,
    Action<TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>>? configure = null)
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
    where TAccessManager : TenancyAccessManager<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantManager : TenantManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
    where TOrganizationManager : OrganizationManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
    where TSeatManager : SeatManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
    where TRoleManager : RoleManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
    where TContext : DbContext
```

Registers Tenancy over `TContext`, the context whose model calls `AddTenancy`: everything [`TenancyServiceCollectionExtensions.AddTenancyCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`](#addtenancycoreiservicecollection-actiontenancyoptionsttenantid-tseatid-tunitid-troleid) registers, the store the managers load and save through, the seat directory tenant selection asks, the save interceptor, brought to every context `UseDDDToolkit` wires as Tenancy's part ([`TenancySaveInterceptor.PartName`](entity-framework.md#tenancysaveinterceptor)), the query executor, and what an event log asks: who acted, as Tenancy knows the caller ([`TenancyActedByAccessor`](entity-framework.md#tenancyactedbyaccessor), put around the accessor registered before this call, or around the toolkit's default), and the tenant of each row of the log `AddTenancyEventLogTable` maps.

```csharp
services.AddTenancy<TenancyContext>(options =>
    options.Catalogue = ShopCatalogue.Application);   // optional: starter roles, permissions of its own, marks
services.AddDbContext<TenancyContext>((serviceProvider, options) => options
    .UseNpgsql(connectionString)
    .UseDDDToolkit(serviceProvider));
```

Every option has a default, so `services.AddTenancy<TenancyContext>()` is enough. A new id is made by the id itself, `TSeatId.Create()`, in code and before the save: the generator writes it for an id over a `Guid`, and an id over anything else declares its own. The store and the directory are scoped, like the context they use. The store offers a save that fails to every registered [`ITenancySaveFailures`](entity-framework.md#itenancysavefailures), which a package for one database adds, and leaves to the database what [`TenancyStoreOptions`](entity-framework.md#tenancystoreoptions) says, which such a package sets.

`AddTenancy<TenancyContext>` is generated into the project that declares the classes, closed over them (`TemplateRegistrationAttribute`), or into a project of the same `[assembly: Module]` that declares none, such as the module's infrastructure project next to its domain project. It calls this method with the classes and ids filled in; the context is still the caller's to name. This method can be called as well, with all fifteen.

It registers the access manager per scope: `TAccessManager`, the application's `TenancyAccessManager`, which the generator writes into the project that declares the classes and fills in here, or the package's class itself where a call names it; and the package's [`TenancyAccessManager<TTenantId, TSeatId, TUnitId, TRoleId>`](callers.md#tenancyaccessmanagerttenantid-tseatid-tunitid-troleid) it derives from. It registers the read of the tenants a round of system work visits over the context as well, which the access manager asks.

It registers the managers per scope the same way: `TTenantManager`, `TOrganizationManager`, `TSeatManager` and `TRoleManager`, the application's `TenantManager`, `OrganizationManager`, `SeatManager` and `RoleManager`, and the package's classes they derive from.

It brings the start-up checks of [`TenancyChecks`](entity-framework.md#tenancychecks), which a host runs with `services.RunStartupChecks()`: the catalogue builds, every context that keeps rows to a tenant checks its saves, and a permission a role holds that the catalogue has lost is logged.

And it registers [`IStarterRoleSync`](catalogue.md#istarterrolesync) over the context, which brings every tenant's roles up to the starter roles they were made from: `services.SyncStarterRoles()` runs it once the host has started, and a host may run it itself.

**Parameters**

- `services`: The service collection.
- `configure`: Changes Tenancy's options, every one of which has a default; leave it out to keep them all.

**Exceptions**

- `ArgumentNullException`: `services` is null.
- `InvalidOperationException`: A token role is an operator's and seated as well.

#### `AddTenancyInvitations(IServiceCollection, Action<TenancyInvitationOptions<TInvitationId>>?)`

```csharp
public static IServiceCollection AddTenancyInvitations<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TInvitation, TInvitationId, TInvitationManager, TContext>(
    this IServiceCollection services,
    Action<TenancyInvitationOptions<TInvitationId>>? configure = null)
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
    where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TInvitationId : struct, ICreatableEntityId<TInvitationId>, IEquatable<TInvitationId>
    where TInvitationManager : InvitationManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TInvitation, TInvitationId>
    where TContext : DbContext
```

Registers invitations over `TContext`, the context whose model calls `AddTenancyInvitations`, for an application that invites people into a tenant: the invitation manager, `TInvitationManager`, the application's `InvitationManager`, and the store it loads and keeps invitations through. Call it after `AddTenancy`, with the context:

```csharp
services.AddTenancy<TenancyContext>();
services.AddTenancyInvitations<TenancyContext>();
```

That call is generated into the project that declares Tenancy's classes, closed over them, as `AddTenancy<TenancyContext>` is (`TemplateRegistrationAttribute`), invitation class included. An application may have no invitation class: then no project gets the call, and none is told of it. This method can be called as well, with all thirteen. A new invitation's id is made by the id itself, `InvitationId.Create()`.

The store is scoped, and works in the unit of work of Tenancy's own store: an invitation is saved, and its acceptance with the seat it makes, by that store's one save.

**Parameters**

- `services`: The service collection.
- `configure`: Changes how long an invitation stays open, which has defaults; leave it out to keep them.

**Exceptions**

- `ArgumentNullException`: `services` is null.
- `InvalidOperationException`: Tenancy itself is not registered yet; or the lifetimes are out of order.

#### `AddTenancyAccess(IServiceCollection)`

```csharp
public static IServiceCollection AddTenancyAccess<TTenantId, TSeatId, TUnitId, TRoleId, TAccessManager, TRequests, TContext>(this IServiceCollection services)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TAccessManager : TenancyAccessManager<TTenantId, TSeatId, TUnitId, TRoleId>
    where TRequests : class, IRequireAccess
    where TContext : DbContext
```

Adds Tenancy's access check to the checks the requests of one module are held to (`AccessChecks<TRequests>`): what decides the cases of [`TenancyRequirement`](callers.md#tenancyrequirement) a request of that module declares, before its handler runs.

```csharp
services.AddTenancyAccess<IBillingRequest, BillingContext>();
```

A module calls it for its own request interface and its own context, once, next to the checks for the cases only it has. The check asks a permission for the whole tenant over `TContext`, so that is Tenancy's own context or one that maps Tenancy's read model (`AddTenancyReadModel`, or `AddTenancyReadFunctions` where the database keeps the rights), and so is a permission at a unit ([`TenancyRequirement.AtUnit<TUnitId>`](callers.md#tenancyrequirementatunittunitid), closed over `TUnitId`); a module whose requests declare neither is never read for, whatever its context maps. The statement runs on a context of its own where the application registered a factory for the context (`IDbContextFactory<TContext>`), a pooled one say, and on the scope's own context otherwise.

The check is free of any dispatcher, as the package is: whatever calls `AccessChecks.RequireAsync` in front of the module's handlers holds the requests to it. In an application that uses the Mediator library that is the pipeline behavior the generator writes for an interface marked `[AccessRequests]`.

The operator cases, `[Operator]`, are decided by the check `AddTenancyOperatorAccess` adds, which this adds as well. That check reads nothing, and begins the operator work a command an operator sends runs in, through `AccessChecks.BeginWork`.

`AddTenancyAccess<IBillingRequest, BillingContext>` is generated into the project that declares Tenancy's classes, closed over their four ids (`TemplateRegistrationAttribute`), or into a project of the same `[assembly: Module]` that declares none. A module that only asks Tenancy declares none of the classes, and says which of its ids are Tenancy's once, with `[assembly: TenancyIds<TenantId, SeatId, OrganizationUnitId, RoleId>]` in its application project: the project of the module that holds its context then gets it too. Tenancy itself is registered by `AddTenancy`, in the same container; adding the check for an interface more than once is harmless.

It registers the module's access manager per scope as well: `TAccessManager`, the `TenancyAccessManager` the generator writes for the module and fills in here.

**Parameters**

- `services`: The service collection.

**Type parameters**

- `TTenantId`: The application's tenant id.
- `TSeatId`: The application's seat id.
- `TUnitId`: The application's unit id.
- `TRoleId`: The application's role id.
- `TAccessManager`: The access manager the module's code takes: the module's `TenancyAccessManager`, or the package's class itself.
- `TRequests`: The module's request interface, which every command and query of the module implements.
- `TContext`: The module's context, which a permission for the whole tenant is asked over.

**Exceptions**

- `ArgumentNullException`: `services` is null.

### `TenancyOutboxExtensions`

```csharp
public static class TenancyOutboxExtensions
```

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

Registers Tenancy's domain events with the outbox of Tenancy's context.

| Member | Summary |
|---|---|
| [`AddTenancyDomainEvents(OutboxOptions)`](#addtenancydomaineventsoutboxoptions) | Registers every domain event Tenancy raises, closed over the application's ids, under the name its `[DomainEventName]` gives it (`tenancy.seat-placed` and the like), so the outbox stores and reads them. |
| [`AddTenancyInvitationEvents(OutboxOptions)`](#addtenancyinvitationeventsoutboxoptions) | Registers the domain events of invitations, closed over the application's ids, under the names their `[DomainEventName]` gives them: `tenancy.invitation-issued`, `tenancy.invitation-accepted` and `tenancy.invitation-cancelled`. |

#### `AddTenancyDomainEvents(OutboxOptions)`

```csharp
public static OutboxOptions AddTenancyDomainEvents<TTenantId, TSeatId, TUnitId, TRoleId>(this OutboxOptions outbox)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Registers every domain event Tenancy raises, closed over the application's ids, under the name its `[DomainEventName]` gives it (`tenancy.seat-placed` and the like), so the outbox stores and reads them. The package's events are generic, so no generated registration can find them.

They are domain events, not contracts: each one the application has not mapped to an integration event is kept off the sinks. To publish one, map it before this call:

```csharp
options.UseOutbox<TenancyContext>(outbox => outbox
    .PublishAs<SeatAdded<TenantId, SeatId>, SeatJoined>(added => new SeatJoined(added.TenantId.Value, added.SeatId.Value))
    .AddTenancyDomainEvents());
```

Mapped after it, an event is already kept off the sinks, and the mapping throws.

The call without type arguments is generated into the project that declares the application's classes, closed over their four ids, or into a project of the same module that declares none, such as the module's infrastructure project, where the context is (`TemplateRegistrationAttribute`). This is the method it calls, which can be called as well, with the four ids written out.

Every event carries who made the change as `By`, so every one of them is closed over the seat id as well, an event about a tenant, a unit or a role included: `TenantSuspended<TenantId, SeatId>`. To keep the ones that change access as a history, see [`TenancyEventLogExtensions.AddTenancyEventLog<TTenantId, TSeatId, TUnitId, TRoleId>`](entity-framework.md#addtenancyeventlogeventlogoptions).

**Parameters**

- `outbox`: The outbox of Tenancy's context.

**Exceptions**

- `ArgumentNullException`: `outbox` is null.

#### `AddTenancyInvitationEvents(OutboxOptions)`

```csharp
public static OutboxOptions AddTenancyInvitationEvents<TTenantId, TInvitationId, TUnitId, TRoleId, TSeatId>(this OutboxOptions outbox)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Registers the domain events of invitations, closed over the application's ids, under the names their `[DomainEventName]` gives them: `tenancy.invitation-issued`, `tenancy.invitation-accepted` and `tenancy.invitation-cancelled`. Call it next to [`AddTenancyDomainEvents<TTenantId, TSeatId, TUnitId, TRoleId>`](#addtenancydomaineventsoutboxoptions) in an application that maps invitations:

```csharp
options.UseOutbox<TenancyContext>(outbox => outbox
    .AddTenancyDomainEvents()
    .AddTenancyInvitationEvents<InvitationId>());
```

That call is generated as [`AddTenancyDomainEvents<TTenantId, TSeatId, TUnitId, TRoleId>`](#addtenancydomaineventsoutboxoptions)'s is, closed over the four ids of the application's classes; the invitation's id is named, since an application may have no invitations, as it is named where invitations are registered. As the others, each one the application has not mapped to an integration event before this call is kept off the sinks. They carry ids and dates, and who made the change: never a token, a digest or an address.

None of them is among the events the access history keeps: an invitation changes nobody's access until it is accepted, and then the seat's own events, added, placed and granted its role, say what changed.

**Parameters**

- `outbox`: The outbox of Tenancy's context.

**Exceptions**

- `ArgumentNullException`: `outbox` is null.

### `TenancyPostgresServiceCollectionExtensions`

```csharp
public static class TenancyPostgresServiceCollectionExtensions
```

Namespace `DDDToolkit.Supporting.Tenancy.Postgres`.

Registers what Tenancy needs on Postgres with row level security.

| Member | Summary |
|---|---|
| [`AddTenancyPostgres(IServiceCollection)`](#addtenancypostgresiservicecollection) | Sets [`TenancyRowLevelSecurity.TenantSetting`](postgres.md#tenantsetting) to the current Tenancy caller's tenant whenever a context under row level security connects, next to the caller's role and claims and in the same statement; turns the last administrator the trigger keeps at commit, which no index states, into the refusal the manager gives for the same rule (a slug another tenant has, which the policies hide from the manager's own check, is refused by its unique index on any database, with no help from here); requires an explicit current user (`CurrentUserServiceCollectionExtensions.RequireExplicitCurrentUser`), so work nobody said anything about fails rather than running as the login role, past every policy; and leaves the rights to the database ([`TenancyStoreOptions.DatabaseKeepsRights`](entity-framework.md#tenancystoreoptions)), whose trigger writes them and whose policies let a seat read only its own, so the save writes none, the store asks the database's functions about other seats, and Tenancy's few reads across tenants ask its functions as scoped system work in no tenant, rather than run as the application itself, past the policies. |

#### `AddTenancyPostgres(IServiceCollection)`

```csharp
public static IServiceCollection AddTenancyPostgres(this IServiceCollection services)
```

Sets [`TenancyRowLevelSecurity.TenantSetting`](postgres.md#tenantsetting) to the current Tenancy caller's tenant whenever a context under row level security connects, next to the caller's role and claims and in the same statement; turns the last administrator the trigger keeps at commit, which no index states, into the refusal the manager gives for the same rule (a slug another tenant has, which the policies hide from the manager's own check, is refused by its unique index on any database, with no help from here); requires an explicit current user (`CurrentUserServiceCollectionExtensions.RequireExplicitCurrentUser`), so work nobody said anything about fails rather than running as the login role, past every policy; and leaves the rights to the database ([`TenancyStoreOptions.DatabaseKeepsRights`](entity-framework.md#tenancystoreoptions)), whose trigger writes them and whose policies let a seat read only its own, so the save writes none, the store asks the database's functions about other seats, and Tenancy's few reads across tenants ask its functions as scoped system work in no tenant, rather than run as the application itself, past the policies. Call it next to `AddSupabaseRowLevelSecurity` or `AddPostgresRowLevelSecurity`:

```csharp
builder.Services.AddSupabaseRowLevelSecurity();
builder.Services.AddTenancyPostgres();
```

The policies themselves come with the access files the export writes, which makes [`TenancyRowAccessContribution`](postgres.md#tenancyrowaccesscontribution) from the catalogue the application marks; [`TenancyPostgresChecks`](postgres.md#tenancypostgreschecks) checks at start-up that the database and the host are set up as they rely on. This registers those checks as start-up checks, which the host runs with `services.RunStartupChecks()`. Calling it more than once is harmless.

**Parameters**

- `services`: The application's services.

**Exceptions**

- `ArgumentNullException`: `services` is null.
