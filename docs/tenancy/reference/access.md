---
title: "Tenancy API: Access manager and users"
sidebar_label: Access manager and users
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Tenancy API: Access manager and users

`TenancyAccessManager` says who acts (`CurrentUser`, the toolkit's `ICurrentUser`) and which tenant and seat the work acts in (`CurrentTenant`). Its `Begin` methods start system work. Below it: the Tenancy user it reads, the actor a change records, the selection of a seat in the tenant a request names, and the requirements a request states with an attribute, `[Operator]` among them.

Your code takes `TenancyAccessManager` with no type arguments. The generator writes it into the global namespace of the project that declares the application's Tenancy classes. A project of another module says which of its ids are Tenancy's with `[assembly: TenancyIds<TenantId, SeatId, OrganizationUnitId, RoleId>]`, and gets the class in its root namespace. The class derives from `TenancyAccessManager<…>` closed over the four ids:

| Type parameter | Filled with | Constraints |
|---|---|---|
| `TTenantId` | the id it is declared with, in `[TenantAggregate<TenantId>]` | `struct, IEntityId, IEquatable<TTenantId>` |
| `TSeatId` | the id it is declared with, in `[SeatAggregate<SeatId>]` | `struct, IEntityId, IEquatable<TSeatId>` |
| `TUnitId` | the id it is declared with, in `[OrganizationUnit<UnitId>]` | `struct, IEntityId, IEquatable<TUnitId>` |
| `TRoleId` | the id it is declared with, in `[RoleAggregate<RoleId>]` | `struct, IEntityId, IEquatable<TRoleId>` |

## Access manager

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `TenancyAccessManager<TTenantId, TSeatId, TUnitId, TRoleId>`

```csharp
public class TenancyAccessManager<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Who acts and in which tenant, and the work that is not the caller's own. Your code takes it as `TenancyAccessManager`, a class the generator writes, closed over your ids:

```csharp
public sealed class AddMemberHandler(IBoardRepository boards, TenancyAccessManager access) : ICommandHandler<AddMember>
{
    public async ValueTask<Unit> Handle(AddMember command, CancellationToken cancellationToken)
    {
        var tenant = access.CurrentTenant.Require();
        // ...
    }
}
```

The project that declares Tenancy's classes gets that class in the global namespace. A project of another module sees only Tenancy's ids, and says which of its ids they are with one line, `[assembly: TenancyIds<TenantId, SeatId, OrganizationUnitId, RoleId>]` ([`TenancyIdsAttribute<TTenantId, TSeatId, TUnitId, TRoleId>`](aggregates.md#tenancyidsattributettenantid-tseatid-tunitid-troleid)). It then gets the class in its root namespace. `AddTenancy` registers the class of the module that declares the classes, and `AddTenancyAccess` the class of the module it is called for. Both register it per scope.

It keeps no state of its own. [`CurrentUser`](#currentuser) and [`CurrentTenant`](#currenttenant) read the current flow of work each time they are asked, so they follow every `Begin` made after the manager was handed out.

Each `Begin` method starts work that is not the caller's own, for one `using`. It begins the tenant the work acts in and the kind of work together: the kind decides how the work is recorded, and which database role it runs as. When the result is disposed, the user before it is current again. Begin as narrow as possible: around the one call that needs it.

Tenancy's queries for the current caller come from the context you query: `db.Tenancy()`, which `DDDToolkit.Supporting.Tenancy.EntityFramework` writes for your ids. A read source that is not Entity Framework asks [`In`](#initenancyreadsourcettenantid-tseatid-tunitid-troleid-iqueryexecutor).

**Type parameters**

- `TTenantId`: The application's tenant id.
- `TSeatId`: The application's seat id.
- `TUnitId`: The application's unit id.
- `TRoleId`: The application's role id.

| Member | Summary |
|---|---|
| [`TenancyAccessManager(TenancyCatalogue, TimeProvider, IServiceProvider)`](#tenancyaccessmanagertenancycatalogue-timeprovider-iserviceprovider) | The access manager over the application's services. |
| [`CurrentUser`](#currentuser) | Who acts: a signed-in user, somebody who has not signed in, or system work. |
| [`CurrentTenant`](#currenttenant) | The tenant the current work acts in, and the seat in it. |
| [`BeginSystem()`](#beginsystem) | Begins system work outside any tenant, for provisioning a tenant: when no tenant exists yet, so there is no tenant to begin in and no seat whose rights could do it. |
| [`BeginSystemIn(TTenantId, TSeatId?, string)`](#beginsysteminttenantid-tseatid-string) | Begins system work inside one tenant: seeding, an import, a job per tenant, or a right your code grants after a condition. |
| [`BeginTokenIn(TTenantId, TSeatId, string)`](#begintokeninttenantid-tseatid-string) | Begins system work inside one tenant that answers a link carrying a token a seat made, such as a feed only its owner knows the address of. |
| [`TenantsToSweepAsync(string, CancellationToken)`](#tenantstosweepasyncstring-cancellationtoken) | The ids of the tenants a round of system work visits: the active and the suspended ones, in no order. |
| [`In(ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>, IQueryExecutor)`](#initenancyreadsourcettenantid-tseatid-tunitid-troleid-iqueryexecutor) | The access queries for the current caller over a read source that is not Entity Framework, at this moment. |

#### `TenancyAccessManager(TenancyCatalogue, TimeProvider, IServiceProvider)`

```csharp
public TenancyAccessManager(
    TenancyCatalogue catalogue,
    TimeProvider clock,
    IServiceProvider services)
```

The access manager over the application's services. The container makes it; your code takes it.

**Parameters**

- `catalogue`: The catalogue every permission asked about is checked against.
- `clock`: What "now" is, for whether a grant applies.
- `services`: The services of the scope: where [`CurrentUser`](#currentuser) comes from, the application's `ICurrentUser`, and the storage package's read of the tenants to visit.

**Exceptions**

- `ArgumentNullException`: An argument is null.

#### `CurrentUser`

```csharp
public ICurrentUser CurrentUser { get; }
```

Who acts: a signed-in user, somebody who has not signed in, or system work. It is the toolkit's `ICurrentUser`, the one code takes from dependency injection, and it reads the current flow of work each time it is asked. So inside [`BeginSystem`](#beginsystem) it answers system work, and after the `using` the user again.

**Example**

```csharp
var identity = access.CurrentUser.Id;   // null for system work and for somebody who has not signed in
```

#### `CurrentTenant`

```csharp
public CurrentTenant<TTenantId, TSeatId> CurrentTenant { get; }
```

The tenant the current work acts in, and the seat in it. It reads the current flow of work each time it is asked. `Require()` returns the tenant, or throws the refusal the request got.

**Example**

```csharp
var current = access.CurrentTenant.Require();   // current.Id is the tenant, current.Seat the caller's seat
```

#### `BeginSystem()`

```csharp
public IDisposable BeginSystem()
```

Begins system work outside any tenant, for provisioning a tenant: when no tenant exists yet, so there is no tenant to begin in and no seat whose rights could do it. Only provisioning accepts it. The work reads nothing inside a tenant, and is recorded as the system. It ends when the result is disposed.

**Returns**: The work, which ends when it is disposed. The user before it is current again then.

**Remarks**

It does what [`TenancyWork.BeginSystem<TTenantId, TSeatId>`](#beginsystem-1) does. On Postgres the work runs as the toolkit's scoped system work in the scope `tenancy`. Under Tenancy's policies that work reads and writes nothing until provisioning narrows it to the tenant it makes.

**Example**

Read the signed-in user's id before the work begins: inside it, [`CurrentUser`](#currentuser) reports system work and has no id.

```csharp
var ada = access.CurrentUser.Id ?? throw new InvalidOperationException("[SignedIn] lets only a signed-in user through.");
using (access.BeginSystem())
{
    var tenant = await tenants.ProvisionAsync(
        new TenantToProvision("harbor-works", "Harbor Works", TenantShape.Hierarchical, "Harbor Works", ada),
        cancellationToken);
}
```

#### `BeginSystemIn(TTenantId, TSeatId?, string)`

```csharp
public IDisposable BeginSystemIn(
    TTenantId tenant,
    TSeatId? actingSeat = null,
    string scope = "tenancy")
```

Begins system work inside one tenant: seeding, an import, a job per tenant, or a right your code grants after a condition. The work holds every permission in that tenant and nothing in another. The last-administrator rule still applies to it. It ends when the result is disposed.

Take the tenant, and what the work changes, from the current tenant or from your own records. Never take them from the request: system work holds every permission in the tenant, so it changes whatever the request names.

**Parameters**

- `tenant`: The tenant.
- `actingSeat`: The seat of the person who asked for the work. It is only recorded in the history, and adds no rights.
- `scope`: The scope of the work: Tenancy's own, which writes Tenancy's tables, unless a module's own work passes its name. Lower case letters, digits, `_` and `-`.

**Returns**: The work, which ends when it is disposed. The user before it is current again then.

**Exceptions**

- `ArgumentException`: `scope` is not a scope. Nothing is begun then.

**Remarks**

It does what [`TenancyWork.BeginSystemIn<TTenantId, TSeatId>`](#beginsysteminttenantid-tseatid-string-1) does. The work is recorded as the system in `scope`, and `actingSeat` as who placed or granted. On Postgres it runs as the toolkit's scoped system work, a database role that cannot bypass the policies, so it reads and writes the rows of that tenant alone.

**Example**

A seat that passed a course gets the role the course gives. The seat holds no `tenancy.grants.manage`, so the handler grants the role as system work, for the calling seat and with the role from the application's own records:

```csharp
var current = access.CurrentTenant.Require();
using (access.BeginSystemIn(current.Id, actingSeat: current.Seat))
{
    await seats.GrantAsync(current.Seat!.Value, course.Role, course.Unit, until: null, reason: "Course passed", cancellationToken);
}
```

#### `BeginTokenIn(TTenantId, TSeatId, string)`

```csharp
public IDisposable BeginTokenIn(TTenantId tenant, TSeatId seat, string scope)
```

Begins system work inside one tenant that answers a link carrying a token a seat made, such as a feed only its owner knows the address of. It is recorded as the token and the seat it stands for. It holds what system work in the tenant holds, so your code narrows what it reads to that seat. Begin it only once the token was checked and found to be that seat's, and the seat to be in use. It ends when the result is disposed.

**Parameters**

- `tenant`: The tenant.
- `seat`: The seat the token stands for.
- `scope`: The scope of the work, the module's name: lower case letters, digits, `_` and `-`.

**Returns**: The work, which ends when it is disposed. The user before it is current again then.

**Exceptions**

- `ArgumentException`: `scope` is not a scope. Nothing is begun then.

**Remarks**

It does what [`TenancyWork.BeginTokenIn<TTenantId, TSeatId>`](#begintokeninttenantid-tseatid-string-1) does. On Postgres it runs as the toolkit's scoped system work in `scope`, as [`BeginSystemIn`](#beginsysteminttenantid-tseatid-string) does.

**Example**

A calendar feed whose address carries a token Leo made. The handler checks the token first, with the application's own records, and then reads Leo's inspections alone:

```csharp
var link = await feeds.FindByTokenAsync(token, cancellationToken) ?? throw new FeedNotFoundException();
using (access.BeginTokenIn(link.Tenant, link.Seat, scope: "inspections"))
{
    var due = await db.Inspections.Where(inspection => inspection.Inspector == link.Seat).ToListAsync(cancellationToken);
}
```

#### `TenantsToSweepAsync(string, CancellationToken)`

```csharp
public Task<IReadOnlyList<TTenantId>> TenantsToSweepAsync(
    string scope,
    CancellationToken cancellationToken)
```

The ids of the tenants a round of system work visits: the active and the suspended ones, in no order. A job asks this first, and then begins system work in each tenant in turn, in its own scope. The read returns ids alone, which name no person and no row. It runs on a context of its own, outside the work around it, as the system in `scope`.

**Parameters**

- `scope`: The scope of the work that asks, the module's name: lower case letters, digits, `_` and `-`.
- `cancellationToken`: Cancels the read.

**Returns**: The ids of the active and the suspended tenants.

**Exceptions**

- `ArgumentException`: `scope` is not a scope. Nothing is read then.
- `InvalidOperationException`: No storage package registered the read: Tenancy is not registered with `AddTenancy` of `DDDToolkit.Supporting.Tenancy.EntityFramework`, or of another storage package that reads the tenants.

**Example**

A nightly job closes overdue inspections in every tenant:

```csharp
foreach (var tenant in await access.TenantsToSweepAsync("inspections", cancellationToken))
{
    using (access.BeginSystemIn(tenant, scope: "inspections"))
    {
        await inspections.CloseOverdueAsync(cancellationToken);
    }
}
```

#### `In(ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>, IQueryExecutor)`

```csharp
public ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId> In(
    ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> source,
    IQueryExecutor executor)
```

The access queries for the current caller over a read source that is not Entity Framework, at this moment. Over a context, ask `db.Tenancy()` instead, which `DDDToolkit.Supporting.Tenancy.EntityFramework` writes for your ids.

**Parameters**

- `source`: The rows to ask over.
- `executor`: Runs the queries that answer yes or no.

**Returns**: The queries, for the caller of the current flow of work at this moment. They are composed over `source` and run by whoever runs the query they end up in.

**Exceptions**

- `ArgumentNullException`: `source` or `executor` is null.

**Example**

```csharp
var queries = access.In(readSource, executor);
var editable = await executor.ListAsync(queries.UnitsWhereIHold("projects.edit"), cancellationToken);
```

### `CurrentTenant<TTenantId, TSeatId>`

```csharp
public sealed class CurrentTenant<TTenantId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

The tenant the current work acts in, and the seat in it. A request acts in the tenant it names, through the user's seat there. System work acts in the tenant it was begun in. It reads the current flow of work each time it is asked, so it follows every `Begin`.

```csharp
board.AddMember(command.Seat, command.Role, MemberPeriod.Open(now), now, addedBy: access.CurrentTenant.Seat);
```

**Type parameters**

- `TTenantId`: The application's tenant id.
- `TSeatId`: The application's seat id.

| Member | Summary |
|---|---|
| [`IsAvailable`](#isavailable) | Whether the current work acts in a tenant: through a seat, or as system work in the tenant. |
| [`Id`](#id) | The tenant the current work acts in; `null` when it acts in none. |
| [`Seat`](#seat) | The user's seat in the tenant. |
| [`Require()`](#require) | The tenant the current work acts in, with the seat, for work that only makes sense inside one. |

#### `IsAvailable`

```csharp
public bool IsAvailable { get; }
```

Whether the current work acts in a tenant: through a seat, or as system work in the tenant.

**Exceptions**

- `InvalidOperationException`: The current user was made with other id types: two Tenancy registrations with different ids.

**Example**

A read that shows nothing outside a tenant, where [`Require`](#require) would throw:

```csharp
if (!access.CurrentTenant.IsAvailable)
{
    return [];
}
```

#### `Id`

```csharp
public TTenantId? Id { get; }
```

The tenant the current work acts in; `null` when it acts in none.

**Exceptions**

- `InvalidOperationException`: The current user was made with other id types: two Tenancy registrations with different ids.

#### `Seat`

```csharp
public TSeatId? Seat { get; }
```

The user's seat in the tenant. For system work in the tenant, the seat it acts for, which is only recorded in the history and gives no rights; `null` when it acts for none. `null` as well when the work acts in no tenant.

**Exceptions**

- `InvalidOperationException`: The current user was made with other id types: two Tenancy registrations with different ids.

#### `Require()`

```csharp
public TenantInScope<TTenantId, TSeatId> Require()
```

The tenant the current work acts in, with the seat, for work that only makes sense inside one. It reads nothing. A user who is nobody gets the refusal that tenant selection gave the request.

**Returns**: The tenant and the seat: the user's seat, or for system work in the tenant the seat it acts for, if any, with [`TenantInScope.BySystem`](#tenantinscopettenantid-tseatid) set.

**Exceptions**

- `RefusalException`: The user is nobody, with the refusal tenant selection gave: `tenancy.tenant-required`, `tenancy.not-seated`, `tenancy.tenant-inactive` or `tenancy.seat-suspended`. Outside any request, `tenancy.not-seated`.
- `InvalidOperationException`: The work is system work outside any tenant; or the current user was made with other id types.

**Example**

```csharp
var current = access.CurrentTenant.Require();
var board = Board.Open(BoardId.Create(), current.Id, command.Name, openedBy: current.Seat);
```

### `TenantInScope<TTenantId, TSeatId>`

```csharp
public readonly record struct TenantInScope<TTenantId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

The tenant a caller acts in, as [`CurrentTenant.Require`](#require) answers it.

| Member | Summary |
|---|---|
| [`TenantInScope(TTenantId, TSeatId?, bool)`](#tenantinscopettenantid-tseatid-bool) | The primary constructor: the type's summary says what it makes. |
| `TTenantId Id { get; init; }` | The tenant. |
| `TSeatId? Seat { get; init; }` | The caller's seat, or the seat system work acts for, if any. |
| `bool BySystem { get; init; }` | Whether this is system work in the tenant rather than a seat. |

#### `TenantInScope(TTenantId, TSeatId?, bool)`

```csharp
public TenantInScope(TTenantId Id, TSeatId? Seat, bool BySystem)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Id`: The tenant.
- `Seat`: The caller's seat, or the seat system work acts for, if any.
- `BySystem`: Whether this is system work in the tenant rather than a seat.

### `ITenantsToSweep<TTenantId>`

```csharp
public interface ITenantsToSweep<TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

Reads the tenants a round of system work visits, for [`TenancyAccessManager.TenantsToSweepAsync`](#tenantstosweepasyncstring-cancellationtoken). The storage package implements it and registers it with its `AddTenancy`. An application asks the access manager, not this.

**Type parameters**

- `TTenantId`: The application's tenant id.

| Member | Summary |
|---|---|
| [`TenantsToSweepAsync(string, CancellationToken)`](#tenantstosweepasyncstring-cancellationtoken-1) | The ids of the active and the suspended tenants, in no order. |

#### `TenantsToSweepAsync(string, CancellationToken)`

```csharp
Task<IReadOnlyList<TTenantId>> TenantsToSweepAsync(
    string scope,
    CancellationToken cancellationToken)
```

The ids of the active and the suspended tenants, in no order. The read is no seat's and no tenant's: it runs on its own, outside the work around it, as the system in `scope`.

**Parameters**

- `scope`: The scope of the work that asks: lower case letters, digits, `_` and `-`.
- `cancellationToken`: Cancels the read.

**Exceptions**

- `ArgumentException`: `scope` is not a scope. Nothing is read then.

## Users

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `TenancyUser<TTenantId, TSeatId>`

```csharp
public sealed record TenancyUser<TTenantId, TSeatId> : ITenancyUser
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Who is asking Tenancy, with the application's ids. Tenancy never takes this from what a caller sends: a seat is found through the caller's verified identity, and system work is begun on purpose with [`TenancyWork`](#tenancywork).

**Type parameters**

- `TTenantId`: The application's tenant id.
- `TSeatId`: The application's seat id.

| Member | Summary |
|---|---|
| `static TenancyUser<TTenantId, TSeatId> System { get; }` | System work outside any tenant: provisioning only. Recorded as the system, in Tenancy's own scope. |
| `TenancyUserKind Kind { get; }` | Which kind of user this is. |
| `TTenantId? Tenant { get; }` | The tenant a seat or system work in a tenant acts in. |
| `TSeatId? Seat { get; }` | The caller's seat; for system work in a tenant, the seat it acts for, if any, which is only recorded as who placed or granted something and gives no rights of its own. |
| `string? Refusal { get; }` | For [`TenancyUserKind.Nobody`](#tenancyuserkind), the refusal code that says why; otherwise `null`. |
| `TenancyActor<TSeatId>? Actor { get; }` | Who a change this user makes is recorded as: a seat as itself; system work as the system with its scope, or as the operator or the token [`TenancyWork`](#tenancywork) began it for. `null` for nobody, who changes nothing. An event log's rows, the columns of `RecordsWhoChanged` and Tenancy's own records all read this one answer, so they cannot come to disagree about who acted. |
| `TTenantId? NamedTenant { get; }` | The tenant an operator's request names, as tenant selection found it: where a command marked `[Operator(Tenant = OperatorTenant.FromRequest)]` acts, and what a `[ForTenant]` member is held to. `null` for every other caller. The operator holds nothing there, and the caller is nobody. |
| [`Nobody(string)`](#nobodystring) | Nobody Tenancy lets in, and why. |
| [`OperatorNaming(TTenantId)`](#operatornamingttenantid) | One of the application's operators, whose request names `tenant`. |
| [`InSeat(TTenantId, TSeatId)`](#inseatttenantid-tseatid) | A person, through their seat in a tenant. |
| [`SystemIn(TTenantId, TSeatId?)`](#systeminttenantid-tseatid) | System work inside one tenant, holding every permission there. |
| [`SystemIn(TTenantId, TenancyActor<TSeatId>)`](#systeminttenantid-tenancyactortseatid) | System work inside one tenant, holding every permission there, recorded as `actor`: the system in a scope of its own, an operator the work is carried out for, or the token a link carried. |
| [`SystemBy(TenancyActor<TSeatId>)`](#systembytenancyactortseatid) | System work outside any tenant, recorded as `actor`: an operator a tenant is provisioned for. |

#### `Nobody(string)`

```csharp
public static TenancyUser<TTenantId, TSeatId> Nobody(string refusal)
```

Nobody Tenancy lets in, and why.

**Parameters**

- `refusal`: One of the codes of [`TenancyRefusals`](refusals.md#tenancyrefusals), such as [`TenancyRefusals.NotSeated`](refusals.md#tenancyrefusals).

**Exceptions**

- `ArgumentException`: `refusal` is not one of Tenancy's codes.

#### `OperatorNaming(TTenantId)`

```csharp
public static TenancyUser<TTenantId, TSeatId> OperatorNaming(TTenantId tenant)
```

One of the application's operators, whose request names `tenant`. To Tenancy the caller is nobody, refused as a person without a seat there. The tenant is kept as [`NamedTenant`](#tenancyuserttenantid-tseatid). Tenant selection answers it for an operator's token role.

**Parameters**

- `tenant`: The tenant the request names.

#### `InSeat(TTenantId, TSeatId)`

```csharp
public static TenancyUser<TTenantId, TSeatId> InSeat(TTenantId tenant, TSeatId seat)
```

A person, through their seat in a tenant.

**Parameters**

- `tenant`: The tenant.
- `seat`: The person's seat in it.

#### `SystemIn(TTenantId, TSeatId?)`

```csharp
public static TenancyUser<TTenantId, TSeatId> SystemIn(TTenantId tenant, TSeatId? actingSeat = null)
```

System work inside one tenant, holding every permission there. Recorded as the system, in Tenancy's own scope.

**Parameters**

- `tenant`: The tenant.
- `actingSeat`: The seat the work is done for, recorded as who placed or granted; it adds no rights.

#### `SystemIn(TTenantId, TenancyActor<TSeatId>)`

```csharp
public static TenancyUser<TTenantId, TSeatId> SystemIn(
    TTenantId tenant,
    TenancyActor<TSeatId> actor)
```

System work inside one tenant, holding every permission there, recorded as `actor`: the system in a scope of its own, an operator the work is carried out for, or the token a link carried. The seat the actor names, if any, is the seat the work is done for. [`TenancyWork`](#tenancywork) begins such a user together with the toolkit's own, which is how work is meant to begin it.

**Parameters**

- `tenant`: The tenant.
- `actor`: Who the work is recorded as: any kind but a seat, which acts through its own seat. Made with [`TenancyActor.OfSystem`](#ofsystemstring-tseatid), [`TenancyActor.OfOperator`](#ofoperatorguid-string) or [`TenancyActor.OfToken`](#oftokentseatid-string), which name each kind in full.

**Exceptions**

- `ArgumentException`: `actor` is a seat, or does not say who it is: an operator without an identity, a token without a seat, or any of them without a scope.

#### `SystemBy(TenancyActor<TSeatId>)`

```csharp
public static TenancyUser<TTenantId, TSeatId> SystemBy(TenancyActor<TSeatId> actor)
```

System work outside any tenant, recorded as `actor`: an operator a tenant is provisioned for. It only provisions, as [`System`](#tenancyuserttenantid-tseatid) does.

**Parameters**

- `actor`: Who the work is recorded as: any kind but a seat, which is in a tenant.

**Exceptions**

- `ArgumentException`: `actor` is a seat, or does not say who it is: an operator without an identity, a token without a seat, or any of them without a scope.

### `ITenancyUser`

```csharp
public interface ITenancyUser
```

A [`TenancyUser<TTenantId, TSeatId>`](#tenancyuserttenantid-tseatid) without its id types, for code that cannot name them: a query filter that only compares the tenant, and the save check.

| Member | Summary |
|---|---|
| `TenancyUserKind Kind { get; }` | Which kind of user this is. |
| `object? TenantId { get; }` | The tenant a seat or system work in a tenant acts in, boxed; `null` for the others. |
| `object? SeatId { get; }` | The caller's seat, or the seat system work acts for, boxed; `null` when there is none. |
| `string? Refusal { get; }` | For [`TenancyUserKind.Nobody`](#tenancyuserkind), the refusal code that says why; otherwise `null`. |
| `ITenancyActor? Actor { get; }` | Who a change this user makes is recorded as, without its id type; `null` for nobody, who changes nothing. |

### `TenancyUsers`

```csharp
public static class TenancyUsers
```

The Tenancy user of the current flow of work, which every manager, access question and tenant filter reads. It is kept apart from the toolkit's current user, `ICurrentUser`: that one says which person or which system is calling, this one which seat in which tenant, and system work there is never system work here.

It is stored without its id types, so a query filter that only compares the tenant can read it without knowing the application's classes. It follows the flow the way an `AsyncLocal<T>` does: into awaited calls and tasks started inside, not back out.

Nothing makes a user up. Outside any scope there is none, which every reader treats as nobody: [`TenantSelection<TTenantId, TSeatId>`](#tenantselectionttenantid-tseatid) begins a seat for a request, and [`TenancyWork`](#tenancywork) begins system work on purpose.

| Member | Summary |
|---|---|
| `static ITenancyUser? Ambient { get; }` | The user [`Begin`](#beginitenancyuser) made current for this flow of work, or `null` outside any. |
| [`Begin(ITenancyUser)`](#beginitenancyuser) | Makes `user` the Tenancy user for everything that runs until the result is disposed, and then the one before it again. |
| [`BeginNone()`](#beginnone) | Makes nobody the Tenancy user for everything that runs until the result is disposed, and then the one before it again: what runs inside acts in no tenant, whatever the flow of work around it began, as it would outside any scope. |
| [`Current()`](#current) | The current user with the application's ids; nobody, refused as not seated, outside any scope. |
| [`CurrentTenantOrNull()`](#currenttenantornull) | The tenant the current user acts in, boxed, for a seat or system work in a tenant; `null` for nobody and for system work outside any tenant, which a tenant filter then matches with nothing. |

#### `Begin(ITenancyUser)`

```csharp
public static IDisposable Begin(ITenancyUser user)
```

Makes `user` the Tenancy user for everything that runs until the result is disposed, and then the one before it again. Scopes nest.

**Exceptions**

- `ArgumentNullException`: `user` is null.

#### `BeginNone()`

```csharp
public static IDisposable BeginNone()
```

Makes nobody the Tenancy user for everything that runs until the result is disposed, and then the one before it again: what runs inside acts in no tenant, whatever the flow of work around it began, as it would outside any scope. For a read that is no seat's and no tenant's, made in the middle of somebody's work: the tenant of the user around it must not travel with it.

#### `Current()`

```csharp
public static TenancyUser<TTenantId, TSeatId> Current<TTenantId, TSeatId>()
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

The current user with the application's ids; nobody, refused as not seated, outside any scope. An application asks the access manager its code takes, `access.CurrentTenant`, which reads this closed over its ids. Code that needs only what kind of user it is reads [`Ambient`](#tenancyusers), which needs no id: none there is nobody, not seated.

**Exceptions**

- `InvalidOperationException`: The current user was made with other id types: two Tenancy registrations with different ids.

#### `CurrentTenantOrNull()`

```csharp
public static object? CurrentTenantOrNull()
```

The tenant the current user acts in, boxed, for a seat or system work in a tenant; `null` for nobody and for system work outside any tenant, which a tenant filter then matches with nothing.

### `TenancyWork`

```csharp
public static class TenancyWork
```

The ways system power in Tenancy is meant to be begun. Nothing becomes system work in Tenancy by itself, whatever the toolkit's current user says. A person reaches Tenancy through a seat, and system work is begun here, on purpose. Each method begins Tenancy's user together with the toolkit's scoped system work.

```csharp
using (TenancyWork.BeginSystemIn(tenant, actingSeat))
{
    await seats.PlaceAsync(seat, unit, primary: true, cancellationToken);
}
```

Every method is generic over the application's tenant and seat ids. C# infers both where both are arguments, as above. Where one is not, as for [`BeginSystem<TTenantId, TSeatId>`](#beginsystem-1) or system work in a tenant for no seat, the ids are written: `TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant)`. An application calls the same methods closed over its ids on the access manager its code takes, `access.BeginSystem()` and `access.BeginSystemIn(tenant)` ([`TenancyAccessManager<TTenantId, TSeatId, TUnitId, TRoleId>`](#tenancyaccessmanagerttenantid-tseatid-tunitid-troleid)), and writes no id.

Who may send a request is not what its handler runs with. A handler that provisions a tenant begins system work here itself, in trusted code, whatever its request requires: `[AllowAnonymous]` for a registration form anyone may fill in, `[SignedIn]` for a signed-in user who registers an organization. A tool only the application runs is trusted code itself: it begins the work here and calls the manager, or sends its request inside it. The requirement decides who gets as far as the handler; what the work may then do is what is begun here, and nothing of the caller's. System work is no requirement a request declares: it is this scope, and once its result is disposed the user before it is current again.

Each method begins two things, and ends both when its result is disposed. The Tenancy user says in which tenant the work acts. The toolkit's scoped system work, `ICurrentUser.BeginSystem(scope)`, says what kind of work it is. Keeping them in one scope keeps them from drifting apart. Neither begins system work without a scope, which row level security lets past every policy, while system work in a tenant must stay inside that tenant. On Postgres scoped system work runs as a role that cannot bypass the policies, and the tenant reaches them next to it. Tenancy's own policies come with `DDDToolkit.Supporting.Tenancy.Postgres`.

They are not the only way. [`TenancyUsers.Begin`](#beginitenancyuser) with a user made by [`TenancyUser.System`](#tenancyuserttenantid-tseatid), `SystemIn` or `SystemBy` begins Tenancy's user alone, a handle for tests and for code that begins the toolkit's current user itself. It leaves the toolkit's current user, and so the role the database runs as, as it is.

Each also says who the work is recorded as ([`TenancyUser.Actor`](#tenancyuserttenantid-tseatid)): the system in its scope, unless the work answers a link that carries a seat's token ([`BeginTokenIn<TTenantId, TSeatId>`](#begintokeninttenantid-tseatid-string-1)).

An operator's work is begun by none of them. A command marked [`OperatorAttribute`](#operatorattribute) runs as operator work. The access check begins it around the handler, recorded as the operator who sent the command. Nothing else begins it, so no code can record work as an operator who did not ask for it.

| Member | Summary |
|---|---|
| `SystemScope = "tenancy"` | The scope of Tenancy's own system work. Under the policies of `DDDToolkit.Supporting.Tenancy.Postgres`, only work in this scope writes Tenancy's tables; work in another scope may read them. |
| [`BeginSystem()`](#beginsystem-1) | Begins system work outside any tenant, for provisioning a tenant. |
| [`BeginSystemIn(TTenantId, TSeatId?, string)`](#beginsysteminttenantid-tseatid-string-1) | Begins system work inside one tenant, such as an import or seeding: it holds every permission there, and nothing in any other tenant. |
| [`BeginSystemIn(TTenantId, TSeatId, string)`](#beginsysteminttenantid-tseatid-string-2) | Begins system work inside one tenant for a seat: [`BeginSystemIn<TTenantId, TSeatId>`](#beginsysteminttenantid-tseatid-string-1) with the seat given, so C# infers both ids from the arguments and the call names neither, `TenancyWork.BeginSystemIn(tenant, seat, "projects")`, in a module that sees only the ids as much as in one that sees the classes. |
| [`BeginTokenIn(TTenantId, TSeatId, string)`](#begintokeninttenantid-tseatid-string-1) | Begins system work inside one tenant that answers a link carrying a token a seat made, such as a feed only its owner knows the address of: the users of [`BeginSystemIn<TTenantId, TSeatId>`](#beginsysteminttenantid-tseatid-string-1) in the module's own scope, recorded as the token and the seat it stands for. |

#### `BeginSystem()`

```csharp
public static IDisposable BeginSystem<TTenantId, TSeatId>()
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Begins system work outside any tenant, for provisioning a tenant. It reads nothing inside one: every tenant filter matches nothing for it. The toolkit's current user is scoped system work in [`SystemScope`](#tenancywork), which under the policies of `DDDToolkit.Supporting.Tenancy.Postgres` reads and writes nothing until provisioning narrows it to the tenant it makes.

#### `BeginSystemIn(TTenantId, TSeatId?, string)`

```csharp
public static IDisposable BeginSystemIn<TTenantId, TSeatId>(
    TTenantId tenant,
    TSeatId? actingSeat = null,
    string scope = "tenancy")
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Begins system work inside one tenant, such as an import or seeding: it holds every permission there, and nothing in any other tenant. The last-administrator rule still applies to it.

Work that calls Tenancy's managers keeps the default scope. A module's own system work in a tenant, that only reads Tenancy's rows next to its own, passes the module's name instead, and then cannot write Tenancy's tables under the policies of `DDDToolkit.Supporting.Tenancy.Postgres`.

**Parameters**

- `tenant`: The tenant.
- `actingSeat`: The seat the work is done for, recorded as who placed or granted; it adds no rights.
- `scope`: The scope of the system work: lower case letters, digits, `_` and `-`.

**Exceptions**

- `ArgumentException`: `scope` is not a scope; nothing is begun then.

#### `BeginSystemIn(TTenantId, TSeatId, string)`

```csharp
public static IDisposable BeginSystemIn<TTenantId, TSeatId>(
    TTenantId tenant,
    TSeatId actingSeat,
    string scope = "tenancy")
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Begins system work inside one tenant for a seat: [`BeginSystemIn<TTenantId, TSeatId>`](#beginsysteminttenantid-tseatid-string-1) with the seat given, so C# infers both ids from the arguments and the call names neither, `TenancyWork.BeginSystemIn(tenant, seat, "projects")`, in a module that sees only the ids as much as in one that sees the classes. A seat that may be missing, a `TSeatId?`, goes to that one.

It yields to that one wherever both apply, which is wherever the type arguments are written: there a bare `default` for the seat would otherwise make an empty id the seat the work is recorded for, where it has always meant no seat, as it does for `access.BeginSystemIn(tenant, default)`. Where they are inferred, this is the only one that applies.

**Parameters**

- `tenant`: The tenant.
- `actingSeat`: The seat the work is done for, recorded as who placed or granted; it adds no rights.
- `scope`: The scope of the system work: lower case letters, digits, `_` and `-`.

**Exceptions**

- `ArgumentException`: `scope` is not a scope; nothing is begun then.

#### `BeginTokenIn(TTenantId, TSeatId, string)`

```csharp
public static IDisposable BeginTokenIn<TTenantId, TSeatId>(
    TTenantId tenant,
    TSeatId seat,
    string scope)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Begins system work inside one tenant that answers a link carrying a token a seat made, such as a feed only its owner knows the address of: the users of [`BeginSystemIn<TTenantId, TSeatId>`](#beginsysteminttenantid-tseatid-string-1) in the module's own scope, recorded as the token and the seat it stands for.

It is system work, not the seat: it holds what system work in the tenant holds, so the module that begins it narrows what it reads to that seat itself. Begin it only once the token was checked and found to be that seat's, and the seat to be in use.

**Parameters**

- `tenant`: The tenant.
- `seat`: The seat the token stands for.
- `scope`: The scope of the system work, the module's name: lower case letters, digits, `_` and `-`.

**Exceptions**

- `ArgumentException`: `scope` is not a scope; nothing is begun then.

### `TenancyOperatorTokenRoles`

```csharp
public sealed class TenancyOperatorTokenRoles
```

The token roles of the application's operators, as Tenancy was registered with them (`TenancyOptions.OperatorTokenRoles`), for code that cannot name the application's id types: a start-up check that compares them with what the database has. Registered once, by `AddTenancy`.

| Member | Summary |
|---|---|
| [`TenancyOperatorTokenRoles(IEnumerable<string>)`](#tenancyoperatortokenrolesienumerablestring) | The primary constructor: the type's summary says what it makes. |
| `IReadOnlyList<string> TokenRoles { get; }` | The operators' token roles, each once, in ordinal order. Empty for an application without operators. |

#### `TenancyOperatorTokenRoles(IEnumerable<string>)`

```csharp
public TenancyOperatorTokenRoles(IEnumerable<string> tokenRoles)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `tokenRoles`: The operators' token roles, as the tokens spell them.

### `TenancyUserKind`

```csharp
public enum TenancyUserKind
```

Who is asking Tenancy, as Tenancy sees it.

| Member | Summary |
|---|---|
| `Nobody = 0` | Nobody Tenancy lets in: no tenant named, no seat in it, or a seat or tenant that is not active. Holds nothing and changes nothing; [`ITenancyUser.Refusal`](#itenancyuser) says why. |
| `Seat = 1` | A person, through a seat that is active, in a tenant that is too. Holds what the seat's roles grant. |
| `SystemInTenant = 2` | System work inside one tenant, such as an import, seeding or an operator's command. Holds every permission in that tenant and nothing in any other. |
| `System = 3` | System work outside any tenant. It only provisions tenants; it reads and changes nothing inside one. |

## Actors

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `ITenancyActor`

```csharp
public interface ITenancyActor
```

A [`TenancyActor<TSeatId>`](#tenancyactortseatid) without its id type, for code that cannot name it: the save that fills the columns of `RecordsWhoChanged`, and the accessor that tells an event log who acted.

| Member | Summary |
|---|---|
| `TenancyActorKind Kind { get; }` | What kind of actor it is. |
| `object? SeatId { get; }` | The seat, boxed: a seat's own, the seat a token stands for, or the seat system work is done for; `null` when there is none. |
| `Guid? Operator { get; }` | An operator's verified identity; `null` for every other kind. |
| `string? Scope { get; }` | The scope of the application's own work; `null` for a seat. |
| [`ToActedBy()`](#toactedby) | The actor as the toolkit's own records keep it: a kind and one id. |

#### `ToActedBy()`

```csharp
ActedBy ToActedBy()
```

The actor as the toolkit's own records keep it: a kind and one id.

### `TenancyActorKindJsonConverter`

```csharp
public sealed class TenancyActorKindJsonConverter : JsonConverter<TenancyActorKind>
```

Writes a kind as the word a record keeps it by. An event that names who made a change is stored for good, and a number would change its meaning with the order the members are declared in.

The kind of a [`TenancyActor<TSeatId>`](#tenancyactortseatid) names this converter itself, so the word does not depend on what the serializer is told about enums in general: an outbox whose options write every enum by its member's name still stores `seat`, the word the row of an event log keeps next to it. It is public so that a serializer context generated at compile time can name it too.

| Member | Summary |
|---|---|
| [`Read(ref Utf8JsonReader, Type, JsonSerializerOptions)`](#readref-utf8jsonreader-type-jsonserializeroptions) | Reads and converts the JSON to type `T`. |
| [`Write(Utf8JsonWriter, TenancyActorKind, JsonSerializerOptions)`](#writeutf8jsonwriter-tenancyactorkind-jsonserializeroptions) | Writes a specified value as JSON. |

#### `Read(ref Utf8JsonReader, Type, JsonSerializerOptions)`

```csharp
public override TenancyActorKind Read(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options)
```

Reads and converts the JSON to type `T`.

**Parameters**

- `reader`: The reader.
- `typeToConvert`: The type to convert.
- `options`: An object that specifies serialization options to use.

**Returns**: The converted value.

**Exceptions**

- `JsonException`: The value is not one of the four words.

#### `Write(Utf8JsonWriter, TenancyActorKind, JsonSerializerOptions)`

```csharp
public override void Write(
    Utf8JsonWriter writer,
    TenancyActorKind value,
    JsonSerializerOptions options)
```

Writes a specified value as JSON.

**Parameters**

- `writer`: The writer to write to.
- `value`: The value to convert to JSON.
- `options`: An object that specifies serialization options to use.

### `TenancyActorKinds`

```csharp
public static class TenancyActorKinds
```

The kinds Tenancy writes into a record that keeps who acted, next to the toolkit's own (`ActedByKinds`): an event log's row, and the columns of `RecordsWhoChanged`.

| Member | Summary |
|---|---|
| `Seat = "seat"` | A person, through their seat; the id is the seat's. |
| `Operator = "operator"` | An operator of the application; the id is the operator's verified identity. |
| `System = "system"` | The application's own work; the id is the scope it runs in. The toolkit's own kind. |
| `Token = "token"` | A link that carries a token; the id is the seat the token stands for. |
| [`From(TenancyActorKind)`](#fromtenancyactorkind) | The kind as a record keeps it. |

#### `From(TenancyActorKind)`

```csharp
public static string From(TenancyActorKind kind)
```

The kind as a record keeps it.

**Exceptions**

- `ArgumentOutOfRangeException`: `kind` is none of the four.

### `TenancyActor<TSeatId>`

```csharp
public readonly record struct TenancyActor<TSeatId> : ITenancyActor
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Who made a change Tenancy records. Ids only: a seat, an operator's verified identity, or the scope of the application's own work. Never a name, and never an address.

Make one with [`OfSeat`](#ofseattseatid), [`OfOperator`](#ofoperatorguid-string), [`OfSystem`](#ofsystemstring-tseatid) or [`OfToken`](#oftokentseatid-string), which give each kind what it is named by. The Tenancy user carries the actor of the work it stands for ([`TenancyUser.Actor`](#tenancyuserttenantid-tseatid)), so nothing works one out for itself: a request's seat, and what [`TenancyWork`](#tenancywork) began on purpose.

Every domain event of Tenancy's carries one as `By`, the actor of the caller whose command raised it, so an event that is stored, kept in an event log or published says who made the change without anything else being read.

**Type parameters**

- `TSeatId`: The application's seat id.

| Member | Summary |
|---|---|
| [`TenancyActor(TenancyActorKind, TSeatId?, Guid?, string?)`](#tenancyactortenancyactorkind-tseatid-guid-string) | The primary constructor: the type's summary says what it makes. |
| `TenancyActorKind Kind { get; init; }` | What kind of actor it is. |
| `TSeatId? Seat { get; init; }` | The seat: a seat's own, the seat a token stands for, or, for the application's own work, the seat it is done for, which is not who acted. |
| `Guid? Operator { get; init; }` | An operator's verified identity. |
| `string? Scope { get; init; }` | The scope the application's own work runs in. |
| [`OfSeat(TSeatId)`](#ofseattseatid) | A person, through `seat`. |
| [`OfOperator(Guid, string)`](#ofoperatorguid-string) | An operator of the application, by the verified identity they signed in with, in the scope of the work that carries out what they asked. |
| [`OfSystem(string, TSeatId?)`](#ofsystemstring-tseatid) | The application's own work in `scope`. |
| [`OfToken(TSeatId, string)`](#oftokentseatid-string) | A link that carries a token `seat` made, in the scope of the work that answers it. |
| [`ToActedBy()`](#toactedby-1) | The toolkit's own `ActedBy`: `seat` with the seat's id, `operator` with the identity, `system` with the scope, and `token` with the seat's id. |

#### `TenancyActor(TenancyActorKind, TSeatId?, Guid?, string?)`

```csharp
public TenancyActor(
    TenancyActorKind Kind,
    TSeatId? Seat = null,
    Guid? Operator = null,
    string? Scope = null)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Kind`: What kind of actor it is.
- `Seat`: The seat: a seat's own, the seat a token stands for, or, for the application's own work, the seat it is done for, which is not who acted.
- `Operator`: An operator's verified identity.
- `Scope`: The scope the application's own work runs in.

#### `OfSeat(TSeatId)`

```csharp
public static TenancyActor<TSeatId> OfSeat(TSeatId seat)
```

A person, through `seat`.

#### `OfOperator(Guid, string)`

```csharp
public static TenancyActor<TSeatId> OfOperator(Guid identity, string scope)
```

An operator of the application, by the verified identity they signed in with, in the scope of the work that carries out what they asked. An operator holds no seat, so there is none to name.

**Parameters**

- `identity`: The operator's verified identity.
- `scope`: The scope of the work that acts for them.

**Exceptions**

- `ArgumentException`: `identity` is empty, or `scope` is blank.

#### `OfSystem(string, TSeatId?)`

```csharp
public static TenancyActor<TSeatId> OfSystem(string scope, TSeatId? forSeat = null)
```

The application's own work in `scope`.

**Parameters**

- `scope`: The scope it runs in.
- `forSeat`: The seat it is done for, if any: recorded next to the scope, and not who acted.

**Exceptions**

- `ArgumentException`: `scope` is blank.

#### `OfToken(TSeatId, string)`

```csharp
public static TenancyActor<TSeatId> OfToken(TSeatId seat, string scope)
```

A link that carries a token `seat` made, in the scope of the work that answers it.

**Parameters**

- `seat`: The seat the token stands for.
- `scope`: The scope of the work that answers the link.

**Exceptions**

- `ArgumentException`: `scope` is blank.

#### `ToActedBy()`

```csharp
public ActedBy ToActedBy()
```

The toolkit's own `ActedBy`: `seat` with the seat's id, `operator` with the identity, `system` with the scope, and `token` with the seat's id. An id is written as its value, with the invariant culture and a `Guid` as `D`, which is how a database writes the same value as text: a policy can compare the two.

### `TenancyActorKind`

```csharp
public enum TenancyActorKind
```

What kind of actor made a change Tenancy records. As JSON it is the word of [`TenancyActorKinds`](#tenancyactorkinds), `seat`, `operator`, `system` or `token`: the word an event log and the columns of `RecordsWhoChanged` keep as well ([`TenancyActorKindJsonConverter`](#tenancyactorkindjsonconverter)).

| Member | Summary |
|---|---|
| `Seat = 0` | A person, through their seat. |
| `Operator = 1` | An operator of the application, named by their verified identity: never a seat of any tenant. |
| `System = 2` | The application's own work, named by the scope it runs in. |
| `Token = 3` | A link that carries a token a seat made: the seat it stands for. |

## Tenant selection

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `ISeatDirectory<TTenantId, TSeatId>`

```csharp
public interface ISeatDirectory<TTenantId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Finds a person's seats by their verified identity: the one lookup that looks across tenants, and only ever for the identity asking. It also finds the tenant an operator names. Tenancy's storage implements it.

| Member | Summary |
|---|---|
| [`FindAsync(Guid, string, CancellationToken)`](#findasyncguid-string-cancellationtoken) | The identity's seat in the tenant with this slug, or `null` when there is no such tenant or no seat in it for this identity; the two are not told apart. |
| [`FindTenantAsync(string, CancellationToken)`](#findtenantasyncstring-cancellationtoken) | The tenant with this slug, in any status, or `null` when there is none. |
| [`AllOfAsync(Guid, CancellationToken)`](#allofasyncguid-cancellationtoken) | Every seat the identity has, in every tenant and in any status, for a tenant picker: the application's own seats, whole, each beside its tenant ([`SeatInTenant<TSeat>`](#seatintenanttseat)). |

#### `FindAsync(Guid, string, CancellationToken)`

```csharp
Task<SeatOfUser<TTenantId, TSeatId>?> FindAsync(
    Guid identity,
    string tenantSlug,
    CancellationToken cancellationToken)
```

The identity's seat in the tenant with this slug, or `null` when there is no such tenant or no seat in it for this identity; the two are not told apart.

**Parameters**

- `identity`: The verified identity, the subject of the caller's token.
- `tenantSlug`: The tenant's slug, trimmed and lowercased.
- `cancellationToken`: Cancels the lookup.

#### `FindTenantAsync(string, CancellationToken)`

```csharp
Task<TTenantId?> FindTenantAsync(string tenantSlug, CancellationToken cancellationToken)
```

The tenant with this slug, in any status, or `null` when there is none. Tenant selection asks it for an operator alone, who reads every tenant, and never for a person who may hold a seat.

**Parameters**

- `tenantSlug`: The tenant's slug, trimmed and lowercased.
- `cancellationToken`: Cancels the lookup.

#### `AllOfAsync(Guid, CancellationToken)`

```csharp
Task<IReadOnlyList<SeatInTenant<TSeat>>> AllOfAsync<TSeat>(
    Guid identity,
    CancellationToken cancellationToken)
    where TSeat : class
```

Every seat the identity has, in every tenant and in any status, for a tenant picker: the application's own seats, whole, each beside its tenant ([`SeatInTenant<TSeat>`](#seatintenanttseat)). A seat has no name in Tenancy, so a picker shows one by what the application keeps on its seat class, such as the name it is shown by in that tenant, from the seats read here, with no statement more.

**Parameters**

- `identity`: The verified identity, the subject of the caller's token.
- `cancellationToken`: Cancels the lookup.

**Type parameters**

- `TSeat`: The application's seat class, which the storage keeps; or a class it derives from.

**Exceptions**

- `InvalidOperationException`: `TSeat` is no class the storage's seats are.

### `ITenantSelection`

```csharp
public interface ITenantSelection
```

[`TenantSelection<TTenantId, TSeatId>`](#tenantselectionttenantid-tseatid) without its id types, for the code that makes a request's Tenancy user current and needs no id to do it. A host's middleware takes the Tenancy user it is answered and begins it, and [`TenancyUsers.Begin`](#beginitenancyuser) takes any [`ITenancyUser`](#itenancyuser), so the middleware names neither of the application's ids, and reads the same in every application:

```csharp
var seat = await context.RequestServices.GetRequiredService<ITenantSelection>()
    .ResolveAsync(context.GetSupabaseUser(), context.Request.Headers["Tenant"], context.RequestAborted);
using (TenancyUsers.Begin(seat))
{
    await next(context);
}
```

`AddTenancy` registers it per scope as the very selection it registers closed over the ids, so the two answer alike, unless the host registered one of its own before, which then stays. What answers records with the ids in them, a person's own seats for a tenant picker ([`TenantSelection.SeatsOfAsync<TSeat>`](#seatsofasynccurrentusersnapshot-cancellationtoken)), is that one's alone.

| Member | Summary |
|---|---|
| [`ResolveAsync(CurrentUserSnapshot, string?, CancellationToken)`](#resolveasynccurrentusersnapshot-string-cancellationtoken) | The Tenancy user for `user` in the tenant `tenantSlug` names: its active seat there, or nobody and why, as [`TenantSelection.ResolveAsync`](#resolveasynccurrentusersnapshot-string-cancellationtoken-1) answers it. |

#### `ResolveAsync(CurrentUserSnapshot, string?, CancellationToken)`

```csharp
Task<ITenancyUser> ResolveAsync(
    CurrentUserSnapshot user,
    string? tenantSlug,
    CancellationToken cancellationToken)
```

The Tenancy user for `user` in the tenant `tenantSlug` names: its active seat there, or nobody and why, as [`TenantSelection.ResolveAsync`](#resolveasynccurrentusersnapshot-string-cancellationtoken-1) answers it. Every refusal comes back as nobody, with the refusal's code; nothing is thrown for a caller Tenancy does not let in.

**Parameters**

- `user`: Who is calling, as the toolkit's current user answers.
- `tenantSlug`: The tenant the request names, by its slug; trimmed and lowercased.
- `cancellationToken`: Cancels the lookup.

### `TenantSelection<TTenantId, TSeatId>`

```csharp
public sealed class TenantSelection<TTenantId, TSeatId> : ITenantSelection
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Turns the toolkit's current user and the tenant a request names into a Tenancy user: the caller's active seat in that tenant, or nobody and why.

The request names the tenant by its slug only. The tenant's id and the seat's id always come from the directory, found through the caller's verified identity, so nothing a caller sends picks a seat. The toolkit's system work is nobody here: system work in Tenancy is begun on purpose with [`TenancyWork`](#tenancywork), never inherited from a request.

A host's middleware, which only makes the Tenancy user it is answered current, asks the same of [`ITenantSelection`](#itenantselection), the very instance without its id types, and names no id.

Only a signed-in user whose token carries a seated role is looked up at all, [`TenantSelectionOptions.SeatedTokenRoles`](#tenantselectionoptions): `authenticated` unless the application lists others. A user with another token role is nobody in every tenant, and is told what a person without a seat is told, so the answer says nothing about the seats their identity has. The list of a person's own seats, [`SeatsOfAsync<TSeat>`](#seatsofasynccurrentusersnapshot-cancellationtoken), follows the same rule.

An operator of the application, a user whose token carries one of `operators`, holds no seat either. The tenant its request names is looked up by its slug alone, and the answer is nobody with that tenant kept ([`TenancyUser.OperatorNaming`](#operatornamingttenantid)). A command marked `[Operator(Tenant = OperatorTenant.FromRequest)]` acts in that tenant. A request that names no tenant is nobody with `tenancy.tenant-required`, and one that names a tenant nobody has is nobody with `tenancy.not-seated`.

| Member | Summary |
|---|---|
| [`TenantSelection(ISeatDirectory<TTenantId, TSeatId>, TenantSelectionOptions?, TenancyOperatorTokenRoles?)`](#tenantselectioniseatdirectoryttenantid-tseatid-tenantselectionoptions-tenancyoperatortokenroles) | The primary constructor: the type's summary says what it makes. |
| [`ResolveAsync(CurrentUserSnapshot, string?, CancellationToken)`](#resolveasynccurrentusersnapshot-string-cancellationtoken-1) | The Tenancy user for `user` in the tenant `tenantSlug` names. |
| [`SeatsOfAsync(CurrentUserSnapshot, CancellationToken)`](#seatsofasynccurrentusersnapshot-cancellationtoken) | Every seat `user` has, in every tenant and in any status, for a tenant picker: the application's own seats, whole, each beside its tenant ([`SeatInTenant<TSeat>`](#seatintenanttseat)), so a picker shows a seat by the name the application keeps on its seat class, say, as every other answer about the seat does. |

#### `TenantSelection(ISeatDirectory<TTenantId, TSeatId>, TenantSelectionOptions?, TenancyOperatorTokenRoles?)`

```csharp
public TenantSelection(
    ISeatDirectory<TTenantId, TSeatId> seats,
    TenantSelectionOptions? options = null,
    TenancyOperatorTokenRoles? operators = null)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `seats`: Finds the caller's seats, and the tenant an operator names.
- `options`: Which token roles are seated; signed-in users with the role `authenticated` when left out.
- `operators`: Which token roles are operators'; none when left out.

#### `ResolveAsync(CurrentUserSnapshot, string?, CancellationToken)`

```csharp
public Task<TenancyUser<TTenantId, TSeatId>> ResolveAsync(
    CurrentUserSnapshot user,
    string? tenantSlug,
    CancellationToken cancellationToken)
```

The Tenancy user for `user` in the tenant `tenantSlug` names. Every refusal comes back as nobody, with the refusal's code; nothing is thrown for a caller Tenancy does not let in.

**Parameters**

- `user`: Who is calling, as the toolkit's current user answers.
- `tenantSlug`: The tenant the request names, by its slug; trimmed and lowercased here.
- `cancellationToken`: Cancels the lookup.

#### `SeatsOfAsync(CurrentUserSnapshot, CancellationToken)`

```csharp
public Task<IReadOnlyList<SeatInTenant<TSeat>>> SeatsOfAsync<TSeat>(
    CurrentUserSnapshot user,
    CancellationToken cancellationToken)
    where TSeat : class
```

Every seat `user` has, in every tenant and in any status, for a tenant picker: the application's own seats, whole, each beside its tenant ([`SeatInTenant<TSeat>`](#seatintenanttseat)), so a picker shows a seat by the name the application keeps on its seat class, say, as every other answer about the seat does. Looked up by the verified identity of the caller's own token, and by nothing a request could supply. None for a caller that is no signed-in user, and none for a user whose token role holds no seat: nothing is looked up for them, and they are answered what a person without a seat is answered, as in [`ResolveAsync`](#resolveasynccurrentusersnapshot-string-cancellationtoken-1).

**Parameters**

- `user`: Who is calling, as the toolkit's current user answers.
- `cancellationToken`: Cancels the lookup.

**Type parameters**

- `TSeat`: The application's seat class, or a class it derives from.

**Exceptions**

- `InvalidOperationException`: `TSeat` is no class the storage's seats are.

### `TenantSelectionOptions`

```csharp
public sealed class TenantSelectionOptions
```

Which signed-in users may hold a seat: those whose token carries one of the listed roles. A host that gives some of its users a token role of their own, analysts who look across tenants say, leaves that role off the list, and such a user is nobody in every tenant, whatever seats their identity has.

```csharp
services.AddTenancy<ShopTenancyContext>(options =>
{
    ...
    options.TenantSelection.SeatedTokenRoles.Add("member");
});
```

| Member | Summary |
|---|---|
| `AuthenticatedTokenRole = "authenticated"` | The role of a signed-in user whose token says nothing else, and of a caller made without a token. |
| `IList<string> SeatedTokenRoles { get; }` | The token roles whose users are seated, `ICurrentUser.Role` as the token spells it: `authenticated` by default. A caller without a role counts as `authenticated`. |

### `SeatInTenant<TSeat>`

```csharp
public sealed record SeatInTenant<TSeat>
    where TSeat : class
```

One of a person's seats, for a tenant picker: the application's own seat, whole, and only what is not on the seat itself, the tenant it is in by what a picker shows of it. The seat carries its id, its tenant's id, its status and every field the application added, such as the name the person is shown by in that tenant, so a picker shows a seat by what the application chose, as every other answer about the seat does ([`TenantSelection.SeatsOfAsync<TSeat>`](#seatsofasynccurrentusersnapshot-cancellationtoken)).

**Type parameters**

- `TSeat`: The application's seat class, or a class it derives from.

**Remarks**

The seat was read for the picker and is tracked by nobody: nothing done to it is saved. It was read across tenants, before one is picked, so where it is placed and the roles it holds are its tenant's to answer, in that tenant: a database that keeps tenants apart hands them to nobody outside it, and its placements may be missing here. What leaves is what the application selects: the seat has its identity.

| Member | Summary |
|---|---|
| [`SeatInTenant(TSeat, string, string, TenantStatus)`](#seatintenanttseat-string-string-tenantstatus) | The primary constructor: the type's summary says what it makes. |
| `TSeat Seat { get; init; }` | The seat, the application's own class. |
| `string Slug { get; init; }` | The slug its tenant is selected by. |
| `string OrganizationName { get; init; }` | Its tenant's name. |
| `TenantStatus TenantStatus { get; init; }` | Where its tenant is in its life. |

#### `SeatInTenant(TSeat, string, string, TenantStatus)`

```csharp
public SeatInTenant(TSeat Seat, string Slug, string OrganizationName, TenantStatus TenantStatus)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Seat`: The seat, the application's own class.
- `Slug`: The slug its tenant is selected by.
- `OrganizationName`: Its tenant's name.
- `TenantStatus`: Where its tenant is in its life.

**Remarks**

The seat was read for the picker and is tracked by nobody: nothing done to it is saved. It was read across tenants, before one is picked, so where it is placed and the roles it holds are its tenant's to answer, in that tenant: a database that keeps tenants apart hands them to nobody outside it, and its placements may be missing here. What leaves is what the application selects: the seat has its identity.

### `SeatOfUser<TTenantId, TSeatId>`

```csharp
public sealed record SeatOfUser<TTenantId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A person's seat in one tenant, as the directory finds it for their verified identity when the caller's seat is selected ([`ISeatDirectory.FindAsync`](#findasyncguid-string-cancellationtoken)): the tenant and the seat, each by its id and status, which is all the selection decides by. A tenant picker is answered the application's own seats instead ([`ISeatDirectory.AllOfAsync<TSeat>`](#allofasyncguid-cancellationtoken)).

| Member | Summary |
|---|---|
| [`SeatOfUser(TTenantId, string, string, TenantStatus, TSeatId, SeatStatus)`](#seatofuserttenantid-string-string-tenantstatus-tseatid-seatstatus) | The primary constructor: the type's summary says what it makes. |
| `TTenantId Tenant { get; init; }` | The tenant's id. |
| `string Slug { get; init; }` | The slug the tenant is selected by. |
| `string OrganizationName { get; init; }` | The tenant's name. |
| `TenantStatus TenantStatus { get; init; }` | Where the tenant is in its life. |
| `TSeatId Seat { get; init; }` | The seat's id. |
| `SeatStatus SeatStatus { get; init; }` | Whether the seat counts. |

#### `SeatOfUser(TTenantId, string, string, TenantStatus, TSeatId, SeatStatus)`

```csharp
public SeatOfUser(
    TTenantId Tenant,
    string Slug,
    string OrganizationName,
    TenantStatus TenantStatus,
    TSeatId Seat,
    SeatStatus SeatStatus)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Tenant`: The tenant's id.
- `Slug`: The slug the tenant is selected by.
- `OrganizationName`: The tenant's name.
- `TenantStatus`: Where the tenant is in its life.
- `Seat`: The seat's id.
- `SeatStatus`: Whether the seat counts.

## Requirements

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `InTenantAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
public sealed class InTenantAttribute : Attribute
```

Says on a request that its caller works in the tenant the request was sent for: a seat there, or system work there. What [`TenancyAccess.InTenant`](#intenant) says, written where the request is declared:

```csharp
[InTenant]
public sealed record TenantRoles : IQuery<IReadOnlyList<RoleListing>>, ITenantsRequest;
```

**Remarks**

The toolkit's generator turns it into `TenancyAccess.InTenant()` at compile time, in the module's request interface (`[AccessRequests]`, declared `partial`), and the check that `services.AddTenancyAccess<TRequests, TContext>()` adds holds the caller to it. A request handed to a manager that asks more than this, a permission the manager reads where it acts, says this much and the manager asks the rest. Beside a permission of Tenancy's, `[RequiresPermission]` with or without `[AtUnit]`, it says nothing more, since only a caller in the tenant holds one, and the generator warns of it (DDD00084).

On the module's interface it is the module's default, said once where nearly every request reads the tenant's data: a request of the module that states nothing of its own requires a caller in the tenant, and one that states its own, a permission say, keeps only its own.

```csharp
[AccessRequests, InTenant]
public partial interface IProjectsRequest : IRequireAccess;
```

### `AtTenantAttribute`

```csharp
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class AtTenantAttribute : PermissionLevelAttribute
```

The level of a permission that is asked for the whole tenant: held by a role of the organization granted at its root, the way [`TenancyAccess.AtTenant`](#attenantstring) asks it, and what a request's `RequiresPermissionAttribute` without a marker requires. A constant of a `PermissionsAttribute` class carries it.

```csharp
[Permissions]
public static class BoardPermissions
{
    /// <summary>Make and change the roles of boards.</summary>
    [AtTenant]
    public const string ManageRoles = "boards.roles.manage";
}
```

**Remarks**

Tenancy's generator puts it into the catalogue, under the module the project declares, with what `PermissionLevelAttribute` says of it, so an organization's role can hold it. With `[AtUnit]` beside it, a permission asked both ways, for the whole tenant and at a unit, says so.

### `AtUnitAttribute`

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, Inherited = false)]
public sealed class AtUnitAttribute : PermissionLevelAttribute
```

At a unit: on a member of a request, the unit at which the permission of its `RequiresPermissionAttribute` is held, there or at a unit above it; on a constant of a `PermissionsAttribute` class, that the permission is asked at a unit. The one meaning, said where the permission is required and where it is declared.

```csharp
[Permissions]
public static class BoardPermissions
{
    /// <summary>Open a board at a unit.</summary>
    [AtUnit]
    public const string Create = "boards.create";
}
```

On a request it is what [`TenancyAccess.AtUnit<TUnitId>`](#atunitstring-tunitid) says, written on the member of the request it reads.

```csharp
[RequiresPermission(TenancyPermissions.GrantsManage)]
public sealed record MakeGrant(SeatId Seat, [AtUnit] OrganizationUnitId Unit, RoleId Role) : ICommand, ITenantsRequest;
```

is `TenancyAccess.AtUnit(TenancyPermissions.GrantsManage, request.Unit)`.

**Remarks**

It goes on a positional parameter of a record as it is, or on a property, and `[property: AtUnit]` is read as well. The member is the application's unit id, the id its class declared with `[OrganizationUnit<TUnitId>]` is declared over: on another id, a seat's say, it is DDD00081, where the project sees that class. A unit that may be left out, `OrganizationUnitId?`, is a requirement computed from whether it is given, so the request answers `RequiredAccess` itself: `Unit is { } unit ? TenancyAccess.AtUnit(permission, unit) : TenancyAccess.AtTenant(permission)`. A permission without any marker is held at the tenant.

On a permission it is its level, read by Tenancy's generator into the catalogue under the module, with what `PermissionLevelAttribute` says of it; the settings of a level say nothing on a request's member. A permission asked at a unit is held there by a role of the organization granted at that unit or above it, and reaches every unit below.

Not beside `[OrOwn]`: the caller would send the unit the permission is checked at for another subject, so a permission held at one unit would act on a seat anywhere (DDD00085). A permission at a unit is held by a seat in the tenant or by system work there, so `[InTenant]` beside it says nothing more, and is a warning (DDD00084).

### `OperatorAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class OperatorAttribute : Attribute
```

Says on a request that its caller is one of the application's operators, and nobody else is: no seat, whatever it holds, and no system work. On a command, the handler runs as operator work, recorded as the operator. What [`TenancyAccess.Operator`](#operatoroperatortenant-string) says, written where the request is declared:

```csharp
[Operator]
public sealed record SuspendTenant([ForTenant] TenantId Tenant, string Reason) : ICommand<Tenant>, ITenancyRequest;

[Operator]
public sealed record AllTenants(string? After) : IQuery<TenantManager.TenantPage>, ITenancyRequest;
```

**Remarks**

The access check refuses every other caller with `tenancy.operators-only` before the handler runs. An operator is a signed-in user whose token carries one of `TenancyOptions.OperatorTokenRoles`.

On a command, the pipeline runs the handler as operator work. The work is system work, recorded as the operator. The operator's identity is the current user's id, never an id a caller sends. With a [`ForTenantAttribute`](#fortenantattribute) member, the work acts in the tenant the command names, and a request that names another tenant is refused with `tenancy.tenant-mismatch`. With `Tenant = OperatorTenant.FromRequest`, it acts in the tenant the request names beside the command, as tenant selection reads it. With neither, it acts in no tenant, which is what provisioning needs, and a tenant the request names is ignored. [`Scope`](#scope) is the scope the work runs in.

The whole handler holds every permission in that tenant. Keep an operator's handler to the operator's act.

On a query nothing begins. An operator reads across tenants through the database role of its token. A message answered with a stream begins nothing either.

The toolkit's generator turns it into `TenancyAccess.Operator(...)` at compile time, in the module's request interface (`[AccessRequests]`, declared `partial`). Beside it, `[InTenant]` and `[RequiresPermission]` refuse every operator (DDD00104). `FromRequest` beside a `[ForTenant]` member names two tenants (DDD00103). `Tenant` or `Scope` on a query says nothing (DDD00105).

| Member | Summary |
|---|---|
| [`Tenant`](#tenant) | Where a command's operator work acts. |
| [`Scope`](#scope) | The scope a command's operator work runs in. |

#### `Tenant`

```csharp
public OperatorTenant Tenant { get; set; }
```

Where a command's operator work acts. [`OperatorTenant.FromCommand`](#operatortenant), the default, is the tenant the command names with a [`ForTenantAttribute`](#fortenantattribute) member, or no tenant where it names none. [`OperatorTenant.FromRequest`](#operatortenant) is the tenant the request names beside the command.

**Example**

A command that acts in the tenant the request names beside it, as tenant selection reads it:

```csharp
[Operator(Tenant = OperatorTenant.FromRequest)]
public sealed record ReopenTenant(string Note) : ICommand<Tenant>, ITenancyRequest;
```

#### `Scope`

```csharp
public string Scope { get; set; }
```

The scope a command's operator work runs in. It is Tenancy's own, `tenancy`, by default. A module's own command names its module's scope, `projects` say. A scope has lower case letters, digits, `_` and `-`.

**Example**

```csharp
[Operator(Scope = "projects")]
public sealed record RenameTenantProject([ForTenant] TenantId Tenant, ProjectId Project, string Name) : ICommand, IProjectsRequest;
```

### `ForTenantAttribute`

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
public sealed class ForTenantAttribute : Attribute
```

Marks the member of a request marked [`OperatorAttribute`](#operatorattribute) that names the tenant the request is about:

```csharp
[Operator]
public sealed record SuspendTenant([ForTenant] TenantId Tenant, string Reason) : ICommand<Tenant>, ITenancyRequest;
```

is `TenancyAccess.Operator(request.Tenant)`. On a command, the operator work acts in that tenant.

**Remarks**

A request that names another tenant beside the command, as tenant selection reads it, is refused with `tenancy.tenant-mismatch`. A request that names none is let through.

It goes on a positional parameter of a record as it is, or on a property. The member is the application's tenant id, the id its class declared with `[TenantAggregate<TTenantId>]` is declared over. On another id it is DDD00081. Without [`OperatorAttribute`](#operatorattribute) on the request it is DDD00102, and beside `[Operator(Tenant = OperatorTenant.FromRequest)]` it is DDD00103.

### `OperatorTenant`

```csharp
public enum OperatorTenant
```

Where the operator work of a command marked [`OperatorAttribute`](#operatorattribute) acts. Each command says which, so a client that always sends a tenant cannot move a command that acts in no tenant into one.

| Member | Summary |
|---|---|
| `FromCommand = 0` | The tenant the command names with a [`ForTenantAttribute`](#fortenantattribute) member, or no tenant where it names none. A tenant the request names beside a command without such a member is ignored. The default. |
| `FromRequest = 1` | The tenant the request names beside the command, as tenant selection reads it, the `Tenant` header in the sample host. The operator gets no seat there. A request that names no tenant is refused with `tenancy.tenant-required`, and one that names a tenant nobody has with `tenancy.not-seated`, as for a seat. |

### `TenancyAccess`

```csharp
public static class TenancyAccess
```

How a request spells what it requires where Tenancy is the one that knows, beside the core's own (`AccessRequirement.SignedIn()` and the like) and Membership's (`MemberAccess.On(permission, resource)`):

```csharp
AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.InTenant();
AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.AtTenant(TenancyPermissions.RolesManage);
AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.AtUnit(TenancyPermissions.GrantsManage, Unit);
AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.Operator();
```

Each answers one of [`TenancyRequirement`](#tenancyrequirement)'s cases, which the check that `services.AddTenancyAccess<TRequests, TContext>()` adds holds the caller to. Where the requirement reads no value of the request but the permission and a unit it carries, the request says it with an attribute instead, and the toolkit's generator writes the same call: [`InTenantAttribute`](#intenantattribute), `[RequiresPermission(permission)]` for the whole tenant, with [`AtUnitAttribute`](#atunitattribute) on the unit for a permission there, and [`OperatorAttribute`](#operatorattribute), with [`ForTenantAttribute`](#fortenantattribute) on the tenant. These methods stay for a requirement computed from the request's values: `Unit is { } unit ? TenancyAccess.AtUnit(permission, unit) : TenancyAccess.AtTenant(permission)`.

**Remarks**

They are the one spelling: the cases' constructors are internal, as the core's are, and these methods make them. Methods rather than constructors, so a case closed over the application's unit id takes it from the argument: `AtUnit(permission, unit)`.

| Member | Summary |
|---|---|
| [`InTenant()`](#intenant) | The caller works in the tenant the request was sent for: a seat there, or system work there ([`TenancyRequirement.InTenant`](#tenancyrequirementintenant)). |
| [`AtTenant(string)`](#attenantstring) | The caller holds `permission` for the whole tenant, at its root, now ([`TenancyRequirement.AtTenant`](#tenancyrequirementattenant)). |
| [`AtUnit(string, TUnitId)`](#atunitstring-tunitid) | The caller holds `permission` at `unit`, there or at a unit above it, now ([`TenancyRequirement.AtUnit<TUnitId>`](#tenancyrequirementatunittunitid)). |
| [`Operator(OperatorTenant, string)`](#operatoroperatortenant-string) | The caller is one of the application's operators, and nobody else is: no seat, whatever it holds, and no system work ([`TenancyRequirement.Operator`](#tenancyrequirementoperator)). |
| [`Operator(TTenantId, OperatorTenant, string)`](#operatorttenantid-operatortenant-string) | The caller is one of the application's operators, and the request names `forTenant` ([`TenancyRequirement.OperatorIn<TTenantId>`](#tenancyrequirementoperatorinttenantid)). |
| [`Own(TSeatId)`](#owntseatid) | The request is about the caller's own seat: `seat` is the caller's seat in the tenant of the request. |

#### `InTenant()`

```csharp
public static InTenant InTenant()
```

The caller works in the tenant the request was sent for: a seat there, or system work there ([`TenancyRequirement.InTenant`](#tenancyrequirementintenant)).

#### `AtTenant(string)`

```csharp
public static AtTenant AtTenant(string permission)
```

The caller holds `permission` for the whole tenant, at its root, now ([`TenancyRequirement.AtTenant`](#tenancyrequirementattenant)).

**Parameters**

- `permission`: The permission the request needs for the whole tenant: one of the catalogue's.

**Exceptions**

- `ArgumentException`: `permission` is blank.

#### `AtUnit(string, TUnitId)`

```csharp
public static AtUnit<TUnitId> AtUnit<TUnitId>(string permission, TUnitId unit)
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

The caller holds `permission` at `unit`, there or at a unit above it, now ([`TenancyRequirement.AtUnit<TUnitId>`](#tenancyrequirementatunittunitid)).

**Parameters**

- `permission`: The permission the request needs at the unit: one of the catalogue's.
- `unit`: The unit, from the request.

**Type parameters**

- `TUnitId`: The application's unit id.

**Exceptions**

- `ArgumentException`: `permission` is blank.

**Remarks**

The type is taken from the argument, so the compiler does not tell a unit from another id: on a request that also carries a seat, `AtUnit(permission, Seat)` compiles. It lets nobody through, since the check decides the case for the application's unit id alone and stops every send of another with an `InvalidOperationException` that names it. Where a request carries more ids than one, write the type, `AtUnit<OrganizationUnitId>(permission, Unit)`, and the compiler catches the wrong one; a test that holds every request to the requirement it should declare catches it as well.

#### `Operator(OperatorTenant, string)`

```csharp
public static Operator Operator(OperatorTenant tenant = FromCommand, string scope = "tenancy")
```

The caller is one of the application's operators, and nobody else is: no seat, whatever it holds, and no system work ([`TenancyRequirement.Operator`](#tenancyrequirementoperator)). On a command, the handler runs as operator work recorded as the operator, in no tenant, or with [`OperatorTenant.FromRequest`](#operatortenant) in the tenant the request names. On a query nothing begins.

**Parameters**

- `tenant`: Where a command's operator work acts: no tenant, the default, or the tenant the request names.
- `scope`: The scope a command's operator work runs in: lower case letters, digits, `_` and `-`.

**Exceptions**

- `ArgumentException`: `scope` is not a scope.

**Remarks**

Named for the one kind of caller it lets through. A request says it with [`OperatorAttribute`](#operatorattribute) as well.

#### `Operator(TTenantId, OperatorTenant, string)`

```csharp
public static OperatorIn<TTenantId> Operator<TTenantId>(
    TTenantId forTenant,
    OperatorTenant tenant = FromCommand,
    string scope = "tenancy")
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

The caller is one of the application's operators, and the request names `forTenant` ([`TenancyRequirement.OperatorIn<TTenantId>`](#tenancyrequirementoperatorinttenantid)). On a command, the handler runs as operator work recorded as the operator, in that tenant. A request that names another tenant beside it is refused with `tenancy.tenant-mismatch`. On a query nothing begins.

**Parameters**

- `forTenant`: The tenant the request names, from the request.
- `tenant`: With [`OperatorTenant.FromCommand`](#operatortenant), the request names its tenant. [`OperatorTenant.FromRequest`](#operatortenant) is for a request that names none, and is refused here.
- `scope`: The scope a command's operator work runs in: lower case letters, digits, `_` and `-`.

**Type parameters**

- `TTenantId`: The application's tenant id.

**Exceptions**

- `ArgumentException`: `tenant` is [`OperatorTenant.FromRequest`](#operatortenant), which would name a second tenant, or `scope` is not a scope.

**Remarks**

A request says it with [`OperatorAttribute`](#operatorattribute) and [`ForTenantAttribute`](#fortenantattribute) on the member as well.

#### `Own(TSeatId)`

```csharp
public static Owned<TSeatId> Own<TSeatId>(TSeatId seat)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

The request is about the caller's own seat: `seat` is the caller's seat in the tenant of the request. Anyone else is refused with `access.not-own`: another seat, a caller with no seat there, an operator, and system work, which holds every permission in its tenant but has no seat of its own. Spelled beside the other cases, for a requirement that combines them:

```csharp
AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.Any(
    TenancyAccess.Own(Seat),
    TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));
```

which a request says with attributes as well: `[RequiresPermission(TenancyPermissions.SeatsManage)]` and `[OrOwn] SeatId Seat`.

**Parameters**

- `seat`: The seat the request names.

**Type parameters**

- `TSeatId`: The application's seat id.

**Remarks**

The case is the core's, `AccessRequirement.Own<TValue>`, decided by the core's check of who is calling: what the caller's own seat is, Tenancy says with the handle `AddTenancy` registers, from the caller's verified identity and the tenant the request names. So it reads nothing, and it is the same requirement as `AccessRequirement.Own(seat)`.

### `TenancyRequirement`

```csharp
public abstract record TenancyRequirement : AccessRequirement
```

What a request requires of its caller where Tenancy is the one that knows: a seat in the tenant, a permission for the whole of it or at one of its units, or an operator. A request says which case through `IRequireAccess`, spelled with [`TenancyAccess`](#tenancyaccess):

```csharp
public sealed record CloseTheBooks(int Year) : IBillingRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.AtTenant(BillingPermissions.CloseTheBooks);
}
```

and the check the storage package registers for the module's request interface, with `services.AddTenancyAccess<IBillingRequest, BillingContext>()`, holds the caller to it before the handler runs. A caller that does not meet a case is refused with Tenancy's own codes, whichever module the request is of.

**Remarks**

A closed set: the constructor is private, so the cases nested here are all there are, and each is a record, so two requirements that say the same are equal and a test can hold every request to the one it is meant to declare. What is about who is calling and nothing else is the core's: anyone (`AccessRequirement.AllowAnonymous`) and a signed-in user who need not have a seat yet (`AccessRequirement.SignedIn`). The application's own work is no case: it is a scope trusted code begins around the work, `access.BeginSystem()` and its kin, and what only it may do the package's managers refuse everyone else, with `access.system-only`.

A case says the first thing a request requires, the one the request itself can name. What Tenancy's own managers ask beyond it, such as who may give a role that manages access, or whether a unit may move with what hangs below it, they ask themselves, whoever calls them, and the database's policies ask again: those rules are the package's, and a request does not spell them.

The cases are about the tenant: as a whole, or at one of its units ([`TenancyRequirement.AtUnit<TUnitId>`](#tenancyrequirementatunittunitid)). What is held on one thing an application keeps at a unit is that module's own to declare and to check, with the access questions asked inside its own statements.

### `TenancyRequirement.AtTenant`

```csharp
public sealed record AtTenant : TenancyRequirement
```

A permission is held by the caller for the whole tenant: at its root, now. Otherwise `tenancy.not-permitted`, naming the permission, exactly as the package's own managers refuse it; nobody is refused with its own reason first. System work in the tenant holds every permission there. A request declares it with [`TenancyAccess.AtTenant`](#attenantstring).

**Remarks**

For what changes or shows something of the tenant as a whole: a manager of the package that asks for the permission first, such as making a role, or the state an application adds to one of the package's aggregates. It costs one statement, on the context the check was registered over.

| Member | Summary |
|---|---|
| `string Permission { get; }` | The permission the request needs for the whole tenant. |

### `TenancyRequirement.AtUnit<TUnitId>`

```csharp
public sealed record AtUnit<TUnitId> : TenancyRequirement
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

A permission is held by the caller at a unit: there, or at a unit above it, now. Otherwise `tenancy.not-permitted`, with the permission and the unit, exactly as the package's own managers refuse it; nobody is refused with its own reason first. System work in the tenant holds every permission at every unit of its tenant. A unit of another tenant, or one that does not exist, is one where nobody holds anything, and is refused the same way. A request declares it with [`TenancyAccess.AtUnit<TUnitId>`](#atunitstring-tunitid).

**Type parameters**

- `TUnitId`: The application's unit id.

**Remarks**

For a command that acts at a unit the request names: one the package's manager decides, such as placing a seat or giving a role there, and one that makes something at a unit, where there is nothing yet a permission could be held on, such as opening a project there: the check a module would otherwise write in its handler, or as a case of its own. The unit is the request's own, so the unit checked is the unit the handler acts at. It costs one statement, on the context the check was registered over, as a permission for the whole tenant does.

```csharp
AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.AtUnit(BillingPermissions.OpenLedger, UnitId);
```

The unit's id is the application's own type, so the case is closed over it: the check registered with `AddTenancyAccess` decides it for the application's unit id, and stops a request that declares it over another type rather than letting it through.

| Member | Summary |
|---|---|
| `string Permission { get; }` | The permission the request needs at the unit. |
| `TUnitId Unit { get; }` | The unit the permission is needed at, from the request. |

### `TenancyRequirement.InTenant`

```csharp
public sealed record InTenant : TenancyRequirement
```

The caller works in the tenant the request was sent for: a seat there, or system work there. Nobody is refused with its own reason, such as `tenancy.not-seated`. A request declares it with [`TenancyAccess.InTenant`](#intenant).

**Remarks**

For a request that answers only what the caller itself holds or may see, where holding nothing is an answer and not a refusal; for one whose handler asks for whatever else it takes in plain sight; and for one handed to a manager of the package whose first rule needs something read, such as the unit an invitation is for, or the parent a unit hangs under now. That manager asks the rest itself.

### `TenancyRequirement.Operator`

```csharp
public sealed record Operator : TenancyRequirement
```

The caller is one of the application's operators: a signed-in user whose token carries one of [`TenancyOptions.OperatorTokenRoles`](managers.md#operatortokenroles). Anyone else is refused with `tenancy.operators-only`, a seat and system work included. A request declares it with [`TenancyAccess.Operator`](#operatoroperatortenant-string), or with [`OperatorAttribute`](#operatorattribute).

**Remarks**

On a command, the pipeline runs the handler as operator work recorded as the operator. The work acts in no tenant, or, with [`OperatorTenant.FromRequest`](#operatortenant), in the tenant the request names. On a query nothing begins. It is the toolkit's current user that is asked, never the Tenancy user, which is nobody for an operator in every tenant.

| Member | Summary |
|---|---|
| `OperatorTenant Tenant { get; }` | Where a command's operator work acts: no tenant ([`OperatorTenant.FromCommand`](#operatortenant)), or the tenant the request names ([`OperatorTenant.FromRequest`](#operatortenant)). |
| `string Scope { get; }` | The scope a command's operator work runs in. |

### `TenancyRequirement.OperatorIn<TTenantId>`

```csharp
public sealed record OperatorIn<TTenantId> : TenancyRequirement
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

The caller is one of the application's operators, and the request names the tenant it is about. On a command, the operator work acts in [`Tenant`](#tenancyrequirementoperatorinttenantid). A request that names another tenant beside it, as tenant selection reads it, is refused with `tenancy.tenant-mismatch`. A request declares it with [`TenancyAccess.Operator<TTenantId>`](#operatorttenantid-operatortenant-string), or with [`OperatorAttribute`](#operatorattribute) and [`ForTenantAttribute`](#fortenantattribute) on the member that names the tenant.

**Type parameters**

- `TTenantId`: The application's tenant id.

**Remarks**

The check decides the case for the application's tenant id alone. A case over another id stops the request with an `InvalidOperationException` that names it.

| Member | Summary |
|---|---|
| `TTenantId Tenant { get; }` | The tenant the request names, which a command's operator work acts in. |
| `string Scope { get; }` | The scope a command's operator work runs in. |
