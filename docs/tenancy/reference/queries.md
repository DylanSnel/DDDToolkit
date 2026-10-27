---
title: "Tenancy API: ITenancy, its queries and row access"
sidebar_label: ITenancy, its queries and row access
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Tenancy API: ITenancy, its queries and row access

`ITenancy`, through which a module asks about the caller, and the queries it gives in a context; the read model they read, the projection that keeps it, and the row access rules a database enforces.

## Queries

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `ITenancy<TTenantId, TSeatId, TUnitId, TRoleId>`

```csharp
public interface ITenancy<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

The door a module goes through to ask Tenancy about the current caller: who it is, which tenant it acts in, and, over a read source the module supplies, what it may do.

```csharp
var queries = tenancy.In(source, executor);
var mine = projects.Where(project => queries.UnitsWhereIHold("projects.edit").Contains(project.UnitId));
```

| Member | Summary |
|---|---|
| `TenancyCaller<TTenantId, TSeatId> Caller { get; }` | The current Tenancy caller. |
| [`RequireTenant()`](#requiretenant) | The tenant the current caller acts in, for work that only makes sense inside one. |
| [`In(ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>, IQueryExecutor)`](#initenancyreadsourcettenantid-tseatid-tunitid-troleid-iqueryexecutor) | The access queries for the current caller, over `source`, at this moment. |

#### `RequireTenant()`

```csharp
TenantInScope<TTenantId, TSeatId> RequireTenant()
```

The tenant the current caller acts in, for work that only makes sense inside one.

**Exceptions**

- `RefusalException`: The caller is nobody; the refusal is the one it was given.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `In(ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>, IQueryExecutor)`

```csharp
ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId> In(
    ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> source,
    IQueryExecutor executor)
```

The access queries for the current caller, over `source`, at this moment.

**Parameters**

- `source`: The rows to ask over, usually in the module's own context.
- `executor`: Runs the queries that answer yes or no.

### `ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId>`

```csharp
public interface ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

What the current caller may do, asked over one read source. Made by [`ITenancy.In`](#initenancyreadsourcettenantid-tseatid-tunitid-troleid-iqueryexecutor), which fixes the caller and the moment "now" once, so every answer of one set of queries agrees with the others.

The queries are not run here: a module composes them into its own query, where they become subqueries, or runs them itself. A permission granted at the root holds for the whole tenant; one granted lower down covers its unit and every unit below it. A grant counts from its start until its end, and a permission the catalogue has retired holds nowhere.

A seat is answered from its own rights. System work in a tenant holds every permission in that tenant and nothing in another. Nobody holds nothing. System work outside any tenant has nothing to ask about here, and asking is a programming error.

| Member | Summary |
|---|---|
| [`UnitsWhereIHold(string)`](#unitswhereiholdstring) | Every unit where the caller holds `permission` now: the units it is held at, and every unit below them. |
| [`WhereIHold(IReadOnlyCollection<string>)`](#whereiholdireadonlycollectionstring) | Every pair of a unit and a permission of `permissions` the caller holds there now: for each permission, the units it is held at and every unit below them. |
| [`ReadableUnits()`](#readableunits) | The units the caller belongs to: each unit it is placed in, and every unit below it. |
| [`UnitsUnder(TUnitId)`](#unitsundertunitid) | `unit` and every unit below it, in the caller's tenant: the part of the tree that hangs under one unit, read from the closure. |
| [`RolesWithPermission(string)`](#roleswithpermissionstring) | The tenant's active roles that grant `permission`. |
| [`RolePermissions(IReadOnlyCollection<string>)`](#rolepermissionsireadonlycollectionstring) | Every pair of one of the tenant's active roles and a permission of `permissions` it grants: [`RolesWithPermission`](#roleswithpermissionstring) for several permissions in one query. |
| [`PermissionsIHoldAt(TUnitId)`](#permissionsiholdattunitid) | Every live permission the caller holds now that reaches `unit`, each once. |
| [`SeatsHoldingPermissionAt(string, TUnitId)`](#seatsholdingpermissionatstring-tunitid) | Who holds `permission` at `unit` now: the active seats with a right for it that applies now, held at the unit or at a unit above it. |
| [`Units()`](#units) | The tenant's units, archived ones included, as access facts, without a name: where each hangs and whether it is in use. |
| [`Roles()`](#roles) | The tenant's roles, archived ones included, as access facts, without a name: the pack each was copied from, its status and the permissions it grants. |
| [`Seats()`](#seats) | The tenant's seats the caller reads, in any status, as access facts, without a name: whether each counts. |
| [`HoldsAtTenantAsync(string, CancellationToken)`](#holdsattenantasyncstring-cancellationtoken) | Whether the caller holds `permission` now for the whole tenant: at its root. |
| [`HoldsAtAsync(string, TUnitId, CancellationToken)`](#holdsatasyncstring-tunitid-cancellationtoken) | Whether the caller holds `permission` now at `unit`, held there or above it. |

#### `UnitsWhereIHold(string)`

```csharp
IQueryable<TUnitId> UnitsWhereIHold(string permission)
```

Every unit where the caller holds `permission` now: the units it is held at, and every unit below them.

**Parameters**

- `permission`: A permission of the catalogue.

**Exceptions**

- `ArgumentException`: The catalogue does not know `permission`: a typo in code.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `WhereIHold(IReadOnlyCollection<string>)`

```csharp
IQueryable<UnitPermission<TUnitId>> WhereIHold(IReadOnlyCollection<string> permissions)
```

Every pair of a unit and a permission of `permissions` the caller holds there now: for each permission, the units it is held at and every unit below them. It is [`UnitsWhereIHold`](#unitswhereiholdstring) for several permissions in one query, so a list that shows what the caller may do with each row asks once, not once per permission:

```csharp
var held = queries.WhereIHold(["projects.edit", "projects.close"]);
var abilities = from project in projects
                join pair in held on project.UnitId equals pair.Unit
                select new { project.Id, pair.Key };
```

The answer is for showing what a caller can do, a button or a menu. Whether a command may run is still asked when it runs, by the use case that runs it: a permission set read a moment ago decides nothing.

**Parameters**

- `permissions`: Permissions of the catalogue. A retired permission is held nowhere, and none at all answers nothing.

**Exceptions**

- `ArgumentNullException`: `permissions` is null.
- `ArgumentException`: The catalogue does not know one of `permissions`: a typo in code.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `ReadableUnits()`

```csharp
IQueryable<TUnitId> ReadableUnits()
```

The units the caller belongs to: each unit it is placed in, and every unit below it.

**Exceptions**

- `InvalidOperationException`: The caller is system work outside any tenant.

#### `UnitsUnder(TUnitId)`

```csharp
IQueryable<TUnitId> UnitsUnder(TUnitId unit)
```

`unit` and every unit below it, in the caller's tenant: the part of the tree that hangs under one unit, read from the closure. A unit of another tenant, or of none, answers nothing.

**Parameters**

- `unit`: The unit.

**Exceptions**

- `InvalidOperationException`: The caller is system work outside any tenant.

#### `RolesWithPermission(string)`

```csharp
IQueryable<TRoleId> RolesWithPermission(string permission)
```

The tenant's active roles that grant `permission`.

**Parameters**

- `permission`: A permission of the catalogue.

**Exceptions**

- `ArgumentException`: The catalogue does not know `permission`.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `RolePermissions(IReadOnlyCollection<string>)`

```csharp
IQueryable<RoleWithPermission<TRoleId>> RolePermissions(IReadOnlyCollection<string> permissions)
```

Every pair of one of the tenant's active roles and a permission of `permissions` it grants: [`RolesWithPermission`](#roleswithpermissionstring) for several permissions in one query.

**Parameters**

- `permissions`: Permissions of the catalogue. A retired permission is granted by no role here, and none at all answers nothing.

**Exceptions**

- `ArgumentNullException`: `permissions` is null.
- `ArgumentException`: The catalogue does not know one of `permissions`.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `PermissionsIHoldAt(TUnitId)`

```csharp
IQueryable<string> PermissionsIHoldAt(TUnitId unit)
```

Every live permission the caller holds now that reaches `unit`, each once. A retired permission is never among them.

**Exceptions**

- `InvalidOperationException`: The caller is system work outside any tenant.

#### `SeatsHoldingPermissionAt(string, TUnitId)`

```csharp
IQueryable<TSeatId> SeatsHoldingPermissionAt(string permission, TUnitId unit)
```

Who holds `permission` at `unit` now: the active seats with a right for it that applies now, held at the unit or at a unit above it. A seat learns about another seat's right only where it may read the grant that gives it: at a unit where it manages grants, seats or units, held there or above it, or anywhere when it manages roles for the whole tenant. So a seat that manages one part of the tree learns who holds the permission from within that part, and not who holds it from above; a seat that manages none learns only whether it holds the permission there itself. System work in a tenant learns about every seat of that tenant, and nobody about none.

**Parameters**

- `permission`: A permission of the catalogue.
- `unit`: The unit.

**Exceptions**

- `ArgumentException`: The catalogue does not know `permission`: a typo in code.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `Units()`

```csharp
IQueryable<OrganizationUnitRow<TTenantId, TUnitId>> Units()
```

The tenant's units, archived ones included, as access facts, without a name: where each hangs and whether it is in use. What a unit is called is the directory's to answer, by id.

**Exceptions**

- `InvalidOperationException`: The caller is system work outside any tenant.

#### `Roles()`

```csharp
IQueryable<RoleRow<TTenantId, TRoleId>> Roles()
```

The tenant's roles, archived ones included, as access facts, without a name: the pack each was copied from, its status and the permissions it grants. What a role is called is the directory's to answer, by id.

**Exceptions**

- `InvalidOperationException`: The caller is system work outside any tenant.

#### `Seats()`

```csharp
IQueryable<SeatRow<TTenantId, TSeatId>> Seats()
```

The tenant's seats the caller reads, in any status, as access facts, without a name: whether each counts. What a seat is shown by is the application's, selected from its own seat, which the directory answers whole, by id.

A read of the seats, unlike the queries about rights: by default every seat of the tenant, and on Postgres the seats a read rule of the application's on its seat class lets the caller read, where it writes one (`tenant_seats()` runs as its caller). A module that admits a seat by asking here, as Membership's admission does, then admits only seats the caller reads.

**Exceptions**

- `InvalidOperationException`: The caller is system work outside any tenant.

#### `HoldsAtTenantAsync(string, CancellationToken)`

```csharp
Task<bool> HoldsAtTenantAsync(string permission, CancellationToken cancellationToken)
```

Whether the caller holds `permission` now for the whole tenant: at its root.

**Parameters**

- `permission`: A permission of the catalogue.
- `cancellationToken`: Cancels the query.

**Exceptions**

- `ArgumentException`: The catalogue does not know `permission`.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `HoldsAtAsync(string, TUnitId, CancellationToken)`

```csharp
Task<bool> HoldsAtAsync(string permission, TUnitId unit, CancellationToken cancellationToken)
```

Whether the caller holds `permission` now at `unit`, held there or above it.

**Parameters**

- `permission`: A permission of the catalogue.
- `unit`: The unit.
- `cancellationToken`: Cancels the query.

**Exceptions**

- `ArgumentException`: The catalogue does not know `permission`.
- `InvalidOperationException`: The caller is system work outside any tenant.

### `CurrentTenancy<TTenantId, TSeatId, TUnitId, TRoleId>`

```csharp
public sealed class CurrentTenancy<TTenantId, TSeatId, TUnitId, TRoleId> : ITenancy<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Answers what the current caller may do, over any read source. Registered once for the application; it keeps no state of its own, and reads the caller and the time each time it is asked.

| Member | Summary |
|---|---|
| [`CurrentTenancy(TenancyCatalogue, TimeProvider)`](#currenttenancytenancycatalogue-timeprovider) | The primary constructor: the type's summary says what it makes. |
| `TenancyCaller<TTenantId, TSeatId> Caller { get; }` | The current Tenancy caller. |
| [`RequireTenant()`](#requiretenant-1) | The tenant the current caller acts in, for work that only makes sense inside one. |
| [`In(ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>, IQueryExecutor)`](#initenancyreadsourcettenantid-tseatid-tunitid-troleid-iqueryexecutor-1) | The access queries for the current caller, over `source`, at this moment. |

#### `CurrentTenancy(TenancyCatalogue, TimeProvider)`

```csharp
public CurrentTenancy(TenancyCatalogue catalogue, TimeProvider clock)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `catalogue`: The catalogue every permission asked about is checked against.
- `clock`: What "now" is, for whether a grant applies.

#### `RequireTenant()`

```csharp
public TenantInScope<TTenantId, TSeatId> RequireTenant()
```

The tenant the current caller acts in, for work that only makes sense inside one.

**Exceptions**

- `RefusalException`: The caller is nobody; the refusal is the one it was given.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `In(ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>, IQueryExecutor)`

```csharp
public ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId> In(
    ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> source,
    IQueryExecutor executor)
```

The access queries for the current caller, over `source`, at this moment.

**Parameters**

- `source`: The rows to ask over, usually in the module's own context.
- `executor`: Runs the queries that answer yes or no.

### `MoveReach<TUnitId>`

```csharp
public sealed record MoveReach<TUnitId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

A right a move of a unit would change: where it is held, the permission, until when, which of the two parents it reaches, the one the unit hangs under or the one it would, and whether it is the moving seat's own. What a move's check reads of the rights of every seat, and no more of them: no seat, and no role.

| Member | Summary |
|---|---|
| [`MoveReach(TUnitId, string, DateTimeOffset?, TUnitId, bool)`](#movereachtunitid-string-datetimeoffset-tunitid-bool) | The primary constructor: the type's summary says what it makes. |
| `TUnitId UnitId { get; init; }` | The unit the right is held at: a parent, or a unit above one. |
| `string Permission { get; init; }` | The permission. |
| `DateTimeOffset? EndsAt { get; init; }` | The first moment the right no longer applies, or `null` when it has no end. |
| `TUnitId Parent { get; init; }` | The parent it reaches. A right held above both parents comes once for each. |
| `bool OfCaller { get; init; }` | Whether the right is the moving seat's own. |

#### `MoveReach(TUnitId, string, DateTimeOffset?, TUnitId, bool)`

```csharp
public MoveReach(
    TUnitId UnitId,
    string Permission,
    DateTimeOffset? EndsAt,
    TUnitId Parent,
    bool OfCaller)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `UnitId`: The unit the right is held at: a parent, or a unit above one.
- `Permission`: The permission.
- `EndsAt`: The first moment the right no longer applies, or `null` when it has no end.
- `Parent`: The parent it reaches. A right held above both parents comes once for each.
- `OfCaller`: Whether the right is the moving seat's own.

### `NamedUnit<TUnitId>`

```csharp
public sealed record NamedUnit<TUnitId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

What a unit is called, and nothing more: its name, its path from the root and how deep it is. What `TenancyDirectory.UnitsByIdAsync` answers about the units a caller works on, whether or not it reads the rest of the organization: a seat on a project at a unit it is not placed under, or one an application's restrictive rule keeps out of the organization, still reads what that unit is called.

| Member | Summary |
|---|---|
| [`NamedUnit(TUnitId, string, string, int)`](#namedunittunitid-string-string-int) | The primary constructor: the type's summary says what it makes. |
| `TUnitId Id { get; init; }` | The unit. |
| `string Name { get; init; }` | The unit's name. |
| `string Path { get; init; }` | The names of the unit and the units above it, the root first, joined with `" / "`. |
| `int Depth { get; init; }` | How deep the unit is, the root being 1. |

#### `NamedUnit(TUnitId, string, string, int)`

```csharp
public NamedUnit(TUnitId Id, string Name, string Path, int Depth)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Id`: The unit.
- `Name`: The unit's name.
- `Path`: The names of the unit and the units above it, the root first, joined with `" / "`.
- `Depth`: How deep the unit is, the root being 1.

### `RoleWithPermission<TRoleId>`

```csharp
public readonly record struct RoleWithPermission<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

One of the tenant's active roles and a permission it grants: one answer of [`ITenancyQueries.RolePermissions`](#rolepermissionsireadonlycollectionstring).

| Member | Summary |
|---|---|
| [`RoleWithPermission(TRoleId, string)`](#rolewithpermissiontroleid-string) | The primary constructor: the type's summary says what it makes. |
| `TRoleId Role { get; init; }` | The role. |
| `string Permission { get; init; }` | The permission it grants. |

#### `RoleWithPermission(TRoleId, string)`

```csharp
public RoleWithPermission(TRoleId Role, string Permission)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Role`: The role.
- `Permission`: The permission it grants.

### `TenantInScope<TTenantId, TSeatId>`

```csharp
public readonly record struct TenantInScope<TTenantId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

The tenant a caller acts in.

| Member | Summary |
|---|---|
| [`TenantInScope(TTenantId, TSeatId?, bool)`](#tenantinscopettenantid-tseatid-bool) | The primary constructor: the type's summary says what it makes. |
| `TTenantId Tenant { get; init; }` | The tenant. |
| `TSeatId? Seat { get; init; }` | The caller's seat, or the seat system work acts for, if any. |
| `bool BySystem { get; init; }` | Whether this is system work in the tenant rather than a seat. |

#### `TenantInScope(TTenantId, TSeatId?, bool)`

```csharp
public TenantInScope(TTenantId Tenant, TSeatId? Seat, bool BySystem)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Tenant`: The tenant.
- `Seat`: The caller's seat, or the seat system work acts for, if any.
- `BySystem`: Whether this is system work in the tenant rather than a seat.

### `UnitPermission<TUnitId>`

```csharp
public readonly record struct UnitPermission<TUnitId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

A unit and a permission the caller holds there now: one answer of [`ITenancyQueries.WhereIHold`](#whereiholdireadonlycollectionstring), which answers such a pair for every unit a permission reaches, the units it is held at and every unit below them.

| Member | Summary |
|---|---|
| [`UnitPermission(TUnitId, string)`](#unitpermissiontunitid-string) | The primary constructor: the type's summary says what it makes. |
| `TUnitId Unit { get; init; }` | The unit. |
| `string Permission { get; init; }` | The permission held there. |

#### `UnitPermission(TUnitId, string)`

```csharp
public UnitPermission(TUnitId Unit, string Permission)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Unit`: The unit.
- `Permission`: The permission held there.

## Read model

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `IQueryExecutor`

```csharp
public interface IQueryExecutor
```

Runs a query asynchronously without the package knowing the storage. Entity Framework's asynchronous operators live in Entity Framework, which this package does not reference; the storage that supplies the read source supplies the way to run what is built over it.

| Member | Summary |
|---|---|
| [`AnyAsync(IQueryable<T>, CancellationToken)`](#anyasynciqueryablet-cancellationtoken) | Whether the query has any row. |
| [`ListAsync(IQueryable<T>, CancellationToken)`](#listasynciqueryablet-cancellationtoken) | Every row of the query. |
| [`FirstOrDefaultAsync(IQueryable<T>, CancellationToken)`](#firstordefaultasynciqueryablet-cancellationtoken) | The first row of the query, or the default when it has none. |

#### `AnyAsync(IQueryable<T>, CancellationToken)`

```csharp
Task<bool> AnyAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
```

Whether the query has any row.

#### `ListAsync(IQueryable<T>, CancellationToken)`

```csharp
Task<IReadOnlyList<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
```

Every row of the query.

#### `FirstOrDefaultAsync(IQueryable<T>, CancellationToken)`

```csharp
Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
```

The first row of the query, or the default when it has none.

### `ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>`

```csharp
public interface ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Where the access questions read their rows. The storage supplies it over whichever context the question is composed into, so a module asks Tenancy inside its own query, and the question becomes a subquery of it rather than a second round trip.

A source may already keep every set to the current tenant; the questions keep to the caller's tenant themselves as well.

| Member | Summary |
|---|---|
| `IQueryable<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>> SeatRights { get; }` | The permissions seats hold at units. |
| `IQueryable<OrganizationUnitPath<TTenantId, TUnitId>> UnitPaths { get; }` | The closure of every organization's tree. |
| `IQueryable<OrganizationUnitRow<TTenantId, TUnitId>> Units { get; }` | The units. |
| `IQueryable<RoleRow<TTenantId, TRoleId>> Roles { get; }` | The roles. |
| `IQueryable<PlacementRow<TSeatId, TUnitId>> Placements { get; }` | Where seats are placed. |
| `IQueryable<SeatRow<TTenantId, TSeatId>> Seats { get; }` | The seats. |
| [`SeatsHoldingPermissionAt(string, TUnitId)`](#seatsholdingpermissionatstring-tunitid-1) | The seats that hold `permission` at `unit` now, as the storage itself answers the calling seat, or `null` when it has no answer of its own and the question is worked out over the rows above. |

#### `SeatsHoldingPermissionAt(string, TUnitId)`

```csharp
IQueryable<TSeatId>? SeatsHoldingPermissionAt(string permission, TUnitId unit)
```

The seats that hold `permission` at `unit` now, as the storage itself answers the calling seat, or `null` when it has no answer of its own and the question is worked out over the rows above. A storage that shows a seat only its own [`SeatRights`](#itenancyreadsourcettenantid-tseatid-tunitid-troleid) has one: over those rows the question would find nobody but the seat itself, whoever asks. Its answer keeps to what [`ITenancyQueries.SeatsHoldingPermissionAt`](#seatsholdingpermissionatstring-tunitid) says a seat learns, and is a query of the same context, so it still composes into one statement.

**Parameters**

- `permission`: A live permission of the catalogue.
- `unit`: The unit.

### `OrganizationUnitPath<TTenantId, TUnitId>`

```csharp
public sealed class OrganizationUnitPath<TTenantId, TUnitId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

One pair of a unit and a unit at or below it, and how many levels apart they are: the closure of the organization's tree, so "every unit below this one" is a join rather than a walk. Every unit is paired with itself at distance 0. Written by [`TenancyProjection.ClosureOf<TTenantId, TUnit, TUnitId>`](#closureoforganizationaggregatettenantid-tunit-tunitid).

| Member | Summary |
|---|---|
| `TTenantId TenantId { get; set; }` | The organization's tenant. |
| `TUnitId AncestorId { get; set; }` | The unit higher up, or the unit itself. |
| `TUnitId DescendantId { get; set; }` | The unit at or below [`AncestorId`](#organizationunitpathttenantid-tunitid). |
| `int Distance { get; set; }` | How many levels apart they are: 0 for a unit and itself, 1 for a unit and its parent. |

### `OrganizationUnitRow<TTenantId, TUnitId>`

```csharp
public sealed class OrganizationUnitRow<TTenantId, TUnitId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

A unit of a tenant's organization, as a row to read: where it hangs and whether it is in use, never its name.

| Member | Summary |
|---|---|
| `TUnitId Id { get; set; }` | The unit's id. |
| `TTenantId TenantId { get; set; }` | The organization's tenant. |
| `TUnitId? ParentId { get; set; }` | The unit it hangs under, or `null` for the root. |
| `UnitStatus Status { get; set; }` | Whether the unit is in use. |

### `PlacementRow<TSeatId, TUnitId>`

```csharp
public sealed class PlacementRow<TSeatId, TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

Where a seat is placed, as a row to read. It has no tenant of its own: it is reached through the seat's id, and the seat belongs to one tenant.

| Member | Summary |
|---|---|
| `TSeatId SeatId { get; set; }` | The seat. |
| `TUnitId UnitId { get; set; }` | The unit it is placed in. |
| `bool IsPrimary { get; set; }` | Whether this is the seat's primary placement. |

### `RoleRow<TTenantId, TRoleId>`

```csharp
public sealed class RoleRow<TTenantId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

A tenant's role, as a row to read: its pack, its status and the permissions it grants, never its name.

| Member | Summary |
|---|---|
| `TRoleId Id { get; set; }` | The role's id. |
| `TTenantId TenantId { get; set; }` | The role's tenant. |
| `string? FromPack { get; set; }` | The key of the pack it was copied from, or `null`. |
| `RoleStatus Status { get; set; }` | Whether the role is in use. |
| `IReadOnlyList<string> Permissions { get; set; }` | The permissions it grants. |

### `SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>`

```csharp
public sealed class SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

One permission a seat holds at one unit through one role, for the period of the grant. Written by [`TenancyProjection.RightsOf<TTenantId, TSeatId, TUnitId, TRoleId>`](#rightsofttenantid-tseatid-seatstatus-ienumerablegrantfacttunitid-troleid-functroleid-rolefacts-tenancycatalogue) whenever the seat or the role changes; whether the period applies is asked when the row is read, so a grant expires without a write. A seat that is not active, an archived role and a permission that is not live have no rows.

| Member | Summary |
|---|---|
| `TTenantId TenantId { get; set; }` | The seat's tenant. |
| `TSeatId SeatId { get; set; }` | The seat that holds the permission. |
| `TUnitId UnitId { get; set; }` | Where the role was granted; the permission covers that unit and every unit below it. |
| `TRoleId RoleId { get; set; }` | The role that grants it there. |
| `string Permission { get; set; }` | The permission. |
| `DateTimeOffset StartsAt { get; set; }` | The first moment the grant applies. |
| `DateTimeOffset? EndsAt { get; set; }` | The first moment it no longer applies, or `null` when it has no end. |

### `SeatRow<TTenantId, TSeatId>`

```csharp
public sealed class SeatRow<TTenantId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A tenant's seat, as a row to read: whether it counts, never its identity, nor a field the application added to its seat class, such as the name it shows the seat by.

| Member | Summary |
|---|---|
| `TSeatId Id { get; set; }` | The seat's id. |
| `TTenantId TenantId { get; set; }` | The seat's tenant. |
| `SeatStatus Status { get; set; }` | Whether the seat counts. |

## Projection

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `TenancyProjection`

```csharp
public static class TenancyProjection
```

Works out the rows the access questions read from the aggregates they come from: the closure of an organization's tree, and the permissions a seat holds where. Pure functions, with no storage: the storage writes what they return in the same save as the change it follows from, and the tests call them directly.

| Member | Summary |
|---|---|
| [`ClosureOf(OrganizationAggregate<TTenantId, TUnit, TUnitId>)`](#closureoforganizationaggregatettenantid-tunit-tunitid) | Every pair of a unit and a unit at or below it, with the levels between them; every unit paired with itself at 0. |
| [`RightsOf(TTenantId, TSeatId, SeatStatus, IEnumerable<GrantFact<TUnitId, TRoleId>>, Func<TRoleId, RoleFacts?>, TenancyCatalogue)`](#rightsofttenantid-tseatid-seatstatus-ienumerablegrantfacttunitid-troleid-functroleid-rolefacts-tenancycatalogue) | The permissions a seat holds, one row for each unit, role and permission, with the period of the grant it comes from. |
| [`GrantsOf(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>)`](#grantsofseataggregatetseatid-ttenantid-tunitid-troleid) | Every grant of a seat in memory, with the unit of the placement it belongs to. |

#### `ClosureOf(OrganizationAggregate<TTenantId, TUnit, TUnitId>)`

```csharp
public static IReadOnlyList<OrganizationUnitPath<TTenantId, TUnitId>> ClosureOf<TTenantId, TUnit, TUnitId>(OrganizationAggregate<TTenantId, TUnit, TUnitId> organization)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

Every pair of a unit and a unit at or below it, with the levels between them; every unit paired with itself at 0. Archived units are included: rights held at an archived unit keep working.

**Parameters**

- `organization`: The organization.

**Exceptions**

- `InvalidOperationException`: The tree is broken: a unit is above itself, is deeper than `MaxDepth`, or hangs under a unit the organization does not have. Its invariants refuse to save such a tree, so this is never reached through the package's methods, and it stops rather than loops when it is.

#### `RightsOf(TTenantId, TSeatId, SeatStatus, IEnumerable<GrantFact<TUnitId, TRoleId>>, Func<TRoleId, RoleFacts?>, TenancyCatalogue)`

```csharp
public static IReadOnlyList<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>> RightsOf<TTenantId, TSeatId, TUnitId, TRoleId>(
    TTenantId tenant,
    TSeatId seat,
    SeatStatus status,
    IEnumerable<GrantFact<TUnitId, TRoleId>> grants,
    Func<TRoleId, RoleFacts?> roles,
    TenancyCatalogue catalogue)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

The permissions a seat holds, one row for each unit, role and permission, with the period of the grant it comes from. A seat that is not active holds nothing. A grant of a role that is archived, or that `roles` does not know, holds nothing. A permission the catalogue has retired, or no longer knows, holds nowhere. Whether the period applies is left to whoever reads the rows.

**Parameters**

- `tenant`: The seat's tenant.
- `seat`: The seat.
- `status`: The seat's status.
- `grants`: The seat's grants, from [`GrantsOf<TSeatId, TTenantId, TUnitId, TRoleId>`](#grantsofseataggregatetseatid-ttenantid-tunitid-troleid) or from a query.
- `roles`: What is known about each role, or `null` for a role that is not found.
- `catalogue`: The catalogue that says which permissions are live.

#### `GrantsOf(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>)`

```csharp
public static IEnumerable<GrantFact<TUnitId, TRoleId>> GrantsOf<TSeatId, TTenantId, TUnitId, TRoleId>(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> seat)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Every grant of a seat in memory, with the unit of the placement it belongs to.

**Parameters**

- `seat`: The seat.

### `GrantFact<TUnitId, TRoleId>`

```csharp
public sealed record GrantFact<TUnitId, TRoleId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

One role a seat holds at one unit, for a period: what [`TenancyProjection.RightsOf<TTenantId, TSeatId, TUnitId, TRoleId>`](#rightsofttenantid-tseatid-seatstatus-ienumerablegrantfacttunitid-troleid-functroleid-rolefacts-tenancycatalogue) needs of a grant, whether it comes from a seat in memory or from a query that did not load the seat.

| Member | Summary |
|---|---|
| [`GrantFact(TUnitId, TRoleId, DateTimeOffset, DateTimeOffset?)`](#grantfacttunitid-troleid-datetimeoffset-datetimeoffset) | The primary constructor: the type's summary says what it makes. |
| `TUnitId UnitId { get; init; }` | The unit of the placement the grant belongs to. |
| `TRoleId RoleId { get; init; }` | The role granted. |
| `DateTimeOffset StartsAt { get; init; }` | The first moment the grant applies. |
| `DateTimeOffset? EndsAt { get; init; }` | The first moment it no longer applies, or `null`. |

#### `GrantFact(TUnitId, TRoleId, DateTimeOffset, DateTimeOffset?)`

```csharp
public GrantFact(TUnitId UnitId, TRoleId RoleId, DateTimeOffset StartsAt, DateTimeOffset? EndsAt)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `UnitId`: The unit of the placement the grant belongs to.
- `RoleId`: The role granted.
- `StartsAt`: The first moment the grant applies.
- `EndsAt`: The first moment it no longer applies, or `null`.

## Row access

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `TenancyFunctions`

```csharp
public static class TenancyFunctions
```

Tenancy's functions in a database that answers the questions itself, as another package names them: the rules of a resource with members, say, whose own functions ask Tenancy's.

```csharp
MembershipRules.For<Board>(roles, above: new ReachFromAbove(TenancyFunctions.UnitsWhereIHold))
```

Each is named by its owner, [`TenancyRowAccess.Owner`](#tenancyrowaccess), and its name, whatever schema the application gives Tenancy's tables: the script that writes a function that asks one finds it where it is defined. `DDDToolkit.Supporting.Tenancy.Postgres` defines them.

| Member | Summary |
|---|---|
| `static DatabaseFunction CallerSeat { get; }` | Answers the calling seat: the active seat of the caller's verified identity in the tenant the connection names, or `NULL` for a caller without one. As [`TenancyRowAccess.CallerSeatId<TSeatId>`](#callerseatid). |
| `static DatabaseFunction UnitsWhereIHold { get; }` | Answers, for a permission, the units the calling seat's hold of it reaches: where it is held, and every unit below. As [`TenancyRowAccess.UnitsWhereIHold<TUnitId>`](#unitswhereiholdstring-1). |
| `static DatabaseFunction RolesWithPermission { get; }` | Answers, for a permission, the roles of the caller's tenant in use that give it. As [`TenancyRowAccess.RolesWithPermission<TRoleId>`](#roleswithpermissionstring-1). |

### `TenancyRowAccess`

```csharp
public static class TenancyRowAccess
```

Tenancy's questions as a row access rule asks them, so a module writes its rules on the seats, units and roles Tenancy keeps without referencing anything of a database:

```csharp
[RowAccess<Report>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class ReportsAreReadWhereThePermissionIsHeld
{
    public static bool Allows(Report report, Caller caller)
        => TenancyRowAccess.UnitsWhereIHold<OrganizationUnitId>("reports.read").Contains(report.UnitId);
}
```

Only the database answers them: the SQL functions of the same names, which `DDDToolkit.Supporting.Tenancy.Postgres` writes into the schema of the context that maps Tenancy's tables, for the caller whose identity and tenant the connection carries. Called in C#, each throws. In C#, ask [`ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId>`](#itenancyqueriesttenantid-tseatid-tunitid-troleid), which gives the same answers to a seat.

The functions answer for a seat: a signed-in user's, found from the verified identity and the tenant. A suspended or deactivated seat holds nothing, and neither does any seat of a suspended or closed tenant; a question about the caller answers `NULL` for someone without an active seat there, which no comparison passes. A rule for anonymous callers that asks one is refused when the policies are written. System work in a tenant has no seat, so every question answers nothing for it, where the C# questions answer the whole tenant: the policies Tenancy writes keep system work to its tenant themselves, so a rule for it asks none of these.

The same questions say who reads Tenancy's own seats and invitations, where the application replaces Tenancy's default with a read rule on its class: `seat.TenantId == CallerTenant<TenantId>()` for the caller's tenant, `seat.Id == CallerSeatId<SeatId>()` for the caller itself, `UnitsWhereIHold<OrganizationUnitId>(permission).Contains(invitation.UnitId)` for where it holds a permission, [`HoldsPermission`](#holdspermissionstring) and [`HoldsAtTenant`](#holdsattenantstring), and [`SeatsInMyUnits<TSeatId>`](#seatsinmyunits) for the seats placed where it is placed, or below.

The questions named `...InTenant` take the tenant as an argument, and read no setting of the connection: for a policy that runs where the application's connection is not the one asking, such as one on the path of a stored file or on a channel, whose tenant is in the path or the channel's name. They answer for the seat the caller's verified identity has in that tenant, while that seat and the tenant are active, and nothing to anyone else. A set-shaped one is worked out once per statement, so its tenant comes from the parameters of an access function of the module's own, which a policy then asks about each row.

| Member | Summary |
|---|---|
| `Owner = "tenancy"` | The owner the functions are named relative to: `tenancy/units_where_i_hold_permission`, whatever schema the application gives Tenancy's tables. |
| [`UnitsWhereIHold(string)`](#unitswhereiholdstring-1) | Answered by the SQL function `tenancy/units_where_i_hold_permission`, a set a row access rule asks with `Contains`, once per statement. |
| [`ReadableUnits()`](#readableunits-1) | Answered by the SQL function `tenancy/readable_units`, a set a row access rule asks with `Contains`, once per statement. |
| [`SeatsInMyUnits()`](#seatsinmyunits) | Answered by the SQL function `tenancy/seats_in_my_units`, a set a row access rule asks with `Contains`, once per statement. |
| [`RolesWithPermission(string)`](#roleswithpermissionstring-1) | Answered by the SQL function `tenancy/roles_with_permission`, a set a row access rule asks with `Contains`, once per statement. |
| [`CallerSeatId()`](#callerseatid) | Answered by the SQL function `tenancy/caller_seat`, one value a row access rule compares, once per statement when no argument reads the row. |
| [`CallerSeat()`](#callerseat) | Answered by the SQL function `tenancy/caller_seat_row`, one row whose properties a row access rule reads, once per statement. |
| [`CallerTenant()`](#callertenant) | Answered by the SQL function `tenancy/caller_tenant`, one value a row access rule compares, once per statement when no argument reads the row. |
| [`HoldsPermission(string)`](#holdspermissionstring) | Answered by the SQL function `tenancy/holds_permission`, one value a row access rule compares, once per statement when no argument reads the row. |
| [`HoldsAtTenant(string)`](#holdsattenantstring) | Answered by the SQL function `tenancy/holds_at_tenant`, one value a row access rule compares, once per statement when no argument reads the row. |
| [`SeatInTenant(TTenantId)`](#seatintenantttenantid) | Answered by the SQL function `tenancy/seat_in_tenant`, one value a row access rule compares, once per statement when no argument reads the row. |
| [`SeatedInTenant(TTenantId)`](#seatedintenantttenantid) | Answered by the SQL function `tenancy/seated_in_tenant`, one value a row access rule compares, once per statement when no argument reads the row. |
| [`HoldsPermissionInTenant(TTenantId, string)`](#holdspermissionintenantttenantid-string) | Answered by the SQL function `tenancy/holds_permission_in_tenant`, one value a row access rule compares, once per statement when no argument reads the row. |
| [`UnitsWhereIHoldInTenant(TTenantId, string)`](#unitswhereiholdintenantttenantid-string) | Answered by the SQL function `tenancy/units_where_i_hold_permission_in_tenant`, a set a row access rule asks with `Contains`, once per statement. |
| [`RolesWithPermissionInTenant(TTenantId, string)`](#roleswithpermissionintenantttenantid-string) | Answered by the SQL function `tenancy/roles_with_permission_in_tenant`, a set a row access rule asks with `Contains`, once per statement. |

#### `UnitsWhereIHold(string)`

```csharp
public static AccessSet<TUnitId> UnitsWhereIHold<TUnitId>(string permission)
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

Answered by the SQL function `tenancy/units_where_i_hold_permission`, a set a row access rule asks with `Contains`, once per statement. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `ReadableUnits()`

```csharp
public static AccessSet<TUnitId> ReadableUnits<TUnitId>()
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

Answered by the SQL function `tenancy/readable_units`, a set a row access rule asks with `Contains`, once per statement. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `SeatsInMyUnits()`

```csharp
public static AccessSet<TSeatId> SeatsInMyUnits<TSeatId>()
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Answered by the SQL function `tenancy/seats_in_my_units`, a set a row access rule asks with `Contains`, once per statement. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `RolesWithPermission(string)`

```csharp
public static AccessSet<TRoleId> RolesWithPermission<TRoleId>(string permission)
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Answered by the SQL function `tenancy/roles_with_permission`, a set a row access rule asks with `Contains`, once per statement. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `CallerSeatId()`

```csharp
public static TSeatId CallerSeatId<TSeatId>()
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Answered by the SQL function `tenancy/caller_seat`, one value a row access rule compares, once per statement when no argument reads the row. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `CallerSeat()`

```csharp
public static TSeat CallerSeat<TSeat>()
    where TSeat : class
```

Answered by the SQL function `tenancy/caller_seat_row`, one row whose properties a row access rule reads, once per statement. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `CallerTenant()`

```csharp
public static TTenantId CallerTenant<TTenantId>()
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

Answered by the SQL function `tenancy/caller_tenant`, one value a row access rule compares, once per statement when no argument reads the row. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `HoldsPermission(string)`

```csharp
public static bool HoldsPermission(string permission)
```

Answered by the SQL function `tenancy/holds_permission`, one value a row access rule compares, once per statement when no argument reads the row. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `HoldsAtTenant(string)`

```csharp
public static bool HoldsAtTenant(string permission)
```

Answered by the SQL function `tenancy/holds_at_tenant`, one value a row access rule compares, once per statement when no argument reads the row. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `SeatInTenant(TTenantId)`

```csharp
public static TSeatId SeatInTenant<TSeatId, TTenantId>(TTenantId tenant)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

Answered by the SQL function `tenancy/seat_in_tenant`, one value a row access rule compares, once per statement when no argument reads the row. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `SeatedInTenant(TTenantId)`

```csharp
public static bool SeatedInTenant<TTenantId>(TTenantId tenant)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

Answered by the SQL function `tenancy/seated_in_tenant`, one value a row access rule compares, once per statement when no argument reads the row. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `HoldsPermissionInTenant(TTenantId, string)`

```csharp
public static bool HoldsPermissionInTenant<TTenantId>(TTenantId tenant, string permission)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

Answered by the SQL function `tenancy/holds_permission_in_tenant`, one value a row access rule compares, once per statement when no argument reads the row. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `UnitsWhereIHoldInTenant(TTenantId, string)`

```csharp
public static AccessSet<TUnitId> UnitsWhereIHoldInTenant<TUnitId, TTenantId>(
    TTenantId tenant,
    string permission)
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

Answered by the SQL function `tenancy/units_where_i_hold_permission_in_tenant`, a set a row access rule asks with `Contains`, once per statement. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

#### `RolesWithPermissionInTenant(TTenantId, string)`

```csharp
public static AccessSet<TRoleId> RolesWithPermissionInTenant<TRoleId, TTenantId>(
    TTenantId tenant,
    string permission)
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

Answered by the SQL function `tenancy/roles_with_permission_in_tenant`, a set a row access rule asks with `Contains`, once per statement. Only the database can answer it, so called in C# it throws.

**Exceptions**

- `DatabaseOnlyException`: Always: the question is the database's.

### `TenancySaveCheck`

```csharp
public static class TenancySaveCheck
```

The rule every write of a tenant's row passes, whatever wrote it: a seat and system work in a tenant only write their own tenant's rows, and nobody writes any. The storage applies it to every changed row that belongs to a tenant, before anything is written, so a use case that loaded the wrong row cannot save it.

System work outside any tenant writes no tenant's rows either. It acts in no tenant, so every tenant's row is another tenant's to it. Provisioning, the one thing it starts, saves as system work inside the tenant it makes, once it knows that tenant's id, and so passes as that; nothing is left that system work outside any tenant has to save.

| Member | Summary |
|---|---|
| [`Check(object)`](#checkobject) | Checks one changed row against the current Tenancy caller. |

#### `Check(object)`

```csharp
public static void Check(object entryTenant)
```

Checks one changed row against the current Tenancy caller.

**Parameters**

- `entryTenant`: The tenant the row belongs to, boxed: its tenant id.

**Exceptions**

- `ArgumentNullException`: `entryTenant` is null.
- `RefusalException`: `tenancy.other-tenant` when the row belongs to another tenant than the caller's, and for every row when the caller is system work outside any tenant; `tenancy.not-seated` when there is no caller Tenancy lets in.
