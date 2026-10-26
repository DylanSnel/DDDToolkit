---
title: "Tenancy API: Use cases"
sidebar_label: Use cases
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Tenancy API: Use cases

The management classes. `TenancyUseCases` is a class the generator writes in the project that declares the application's classes: it derives from `TenancyUseCases<…>` closed over them, so every type nested there is named through it, `TenancyUseCases.SeatCommands`, `TenancyUseCases.SeatOverview`, and every static member is called through it, `TenancyUseCases.BeginSystem()`. This page names them that way. What fills the nine type parameters:

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

The registration a host calls, from all three packages, is at the end, under [Registration](#registration).

## TenancyUseCases

Namespace `DDDToolkit.Supporting.Tenancy.UseCases`.

### `TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`

```csharp
public abstract class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
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

Tenancy's use cases, closed over the application's classes and ids. They are the one door to the rules that span aggregates: who may do what, where (the caller's permissions at a unit); that a role that manages access is given or taken away, and its seat suspended, deactivated or reactivated, only by someone who holds its permissions that manage access, there and for at least as long, and never given by a seat to itself, while other roles are given by whoever holds `tenancy.grants.manage` where the seat is placed; that a move gains the mover nothing, and gives or takes away from anyone else no permission that manages access the mover could not; and that a tenant always keeps an administrator.

An application does not close the class itself. The toolkit's generator closes it over the classes a module declares with Tenancy's templates, in the project that declares them, as a class of this one's name without its type parameters:

```csharp
public abstract class TenancyUseCases : global::DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<
    global::Shop.Domain.ShopTenant, global::Shop.Contracts.TenantId, ..., global::Shop.Contracts.RoleId>
{
    private TenancyUseCases() { }
}
```

whatever the module is called; C# tells the two apart by their type parameters. A type nested in a class is found through every class derived from it, so every project that sees that one, the module's application and API projects and the host among them, takes a `TenancyUseCases.SeatCommands` and answers a `TenancyUseCases.SeatOverview`: the types nested here, closed over the module's classes, which the container registered and whose documentation shows. Each type parameter's `[TemplateType]` says which class fills it, as `AddTenancy`'s do, and the package's `[assembly: TemplateFacade]` asks for the class. That is why this class is abstract rather than static. An application whose two modules both declare Tenancy's classes names one of the two with `[assembly: TemplateFacadeName("TenancyUseCases", "CustomersTenancy")]` in that module's project.

A static member is found through a derived class as a nested type is, so the same class closes over the ids what is called rather than named: system work, `TenancyUseCases.BeginSystem()` and `TenancyUseCases.BeginSystemIn(tenant)`, and the current caller, `TenancyUseCases.CurrentCaller()`. [`TenancyWork`](callers.md#tenancywork) and [`TenancyCallers`](callers.md#tenancycallers) keep them generic over the ids, for code that sees only those.

They are plain services, not handlers: each method checks the Tenancy caller, loads what it needs through [`TenancyUseCases.IStore`](#tenancyusecasesistore), calls the aggregates, and saves once. Every command starts by asking who is calling. Nobody is refused, a seat is asked for its permissions, and system work in a tenant holds every permission there. System work outside any tenant only provisions.

A new tenant, unit, seat, role or invitation gets its id from the id itself, `TSeatId.Create()`, in code and before the save, so the change and every event of it know the id from the start; a command given an id, for an import or fixed seed data, uses that one. That is why each id is an `ICreatableEntityId<TSelf>`: the generator writes `Create()` for an id over a `Guid`, and an id over anything else declares it.

A command that changes rights, or what they reach, first takes the tenant's access revision, before it reads anything, so that two such commands committed at the same time cannot both have decided on what the other changed. Placing a seat and archiving a unit take it as well: a placement widens what a seat may read, and an archived unit takes no new placement or grant, so a placement or a grant that found the unit active cannot commit next to the archive that ended it. Every command decides everything before it changes an aggregate, so a refused command leaves nothing behind that a later save in the same unit of work would write.

The rules that span aggregates hold for what goes through these use cases, and only for that. The aggregates' public methods check the aggregate's own state; code of the application that calls `Seat.Revoke` or `Role.Archive` itself skips the permission checks, containment and self-appointment, and the last-administrator rule, and answers for them. A closed tenant is closed to its seats, because no seat is selected in it; system work begun in it is not stopped.

| Member | Summary |
|---|---|
| [`TenancyUseCases()`](#tenancyusecases-1) | For the class the generator writes for an application, which derives from this one closed over its classes so that every nested type and static member is named through it. |
| [`BeginSystem()`](#beginsystem) | Begins system work outside any tenant, for provisioning a tenant: [`TenancyWork.BeginSystem<TTenantId, TSeatId>`](callers.md#beginsystem) closed over the application's ids. |
| [`BeginSystemIn(TTenantId, TSeatId?, string)`](#beginsysteminttenantid-tseatid-string) | Begins system work inside one tenant, such as an import, seeding or an operator's command: it holds every permission there, and nothing in any other tenant. |
| [`BeginOperator(Guid)`](#beginoperatorguid) | Begins system work outside any tenant that provisions a tenant for an operator, recorded as that operator: [`TenancyWork.BeginOperator<TTenantId, TSeatId>`](callers.md#beginoperatorguid) closed over the application's ids. |
| [`BeginOperatorIn(TTenantId, Guid, string)`](#beginoperatorinttenantid-guid-string) | Begins system work inside one tenant that carries out what an operator asked for, recorded as that operator: [`TenancyWork.BeginOperatorIn<TTenantId, TSeatId>`](callers.md#beginoperatorinttenantid-guid-string) closed over the application's ids. |
| [`BeginTokenIn(TTenantId, TSeatId, string)`](#begintokeninttenantid-tseatid-string) | Begins system work inside one tenant that answers a link carrying a token a seat made, recorded as the token and that seat: [`TenancyWork.BeginTokenIn<TTenantId, TSeatId>`](callers.md#begintokeninttenantid-tseatid-string) closed over the application's ids. |
| [`CurrentCaller()`](#currentcaller) | The current Tenancy caller with the application's ids; nobody, refused as not seated, outside any scope: [`TenancyCallers.Current<TTenantId, TSeatId>`](callers.md#current) closed over them. |

#### `TenancyUseCases()`

```csharp
protected TenancyUseCases()
```

For the class the generator writes for an application, which derives from this one closed over its classes so that every nested type and static member is named through it. Nothing makes an instance: everything here is a nested type or a static member.

#### `BeginSystem()`

```csharp
public static IDisposable BeginSystem()
```

Begins system work outside any tenant, for provisioning a tenant: [`TenancyWork.BeginSystem<TTenantId, TSeatId>`](callers.md#beginsystem) closed over the application's ids. Ends when the result is disposed.

#### `BeginSystemIn(TTenantId, TSeatId?, string)`

```csharp
public static IDisposable BeginSystemIn(
    TTenantId tenant,
    TSeatId? actingSeat = null,
    string scope = "tenancy")
```

Begins system work inside one tenant, such as an import, seeding or an operator's command: it holds every permission there, and nothing in any other tenant. [`TenancyWork.BeginSystemIn<TTenantId, TSeatId>`](callers.md#beginsysteminttenantid-tseatid-string) closed over the application's ids. Ends when the result is disposed.

**Parameters**

- `tenant`: The tenant.
- `actingSeat`: The seat the work is done for, recorded as who placed or granted; it adds no rights.
- `scope`: The scope of the toolkit's scoped system caller: Tenancy's own unless a module's own work passes its name.

**Exceptions**

- `ArgumentException`: `scope` is not a scope; nothing is begun then.

#### `BeginOperator(Guid)`

```csharp
public static IDisposable BeginOperator(Guid operatorIdentity)
```

Begins system work outside any tenant that provisions a tenant for an operator, recorded as that operator: [`TenancyWork.BeginOperator<TTenantId, TSeatId>`](callers.md#beginoperatorguid) closed over the application's ids.

**Parameters**

- `operatorIdentity`: The operator's verified identity: the `sub` of the token they signed in with.

**Exceptions**

- `ArgumentException`: `operatorIdentity` is empty; nothing is begun then.

#### `BeginOperatorIn(TTenantId, Guid, string)`

```csharp
public static IDisposable BeginOperatorIn(
    TTenantId tenant,
    Guid operatorIdentity,
    string scope = "tenancy")
```

Begins system work inside one tenant that carries out what an operator asked for, recorded as that operator: [`TenancyWork.BeginOperatorIn<TTenantId, TSeatId>`](callers.md#beginoperatorinttenantid-guid-string) closed over the application's ids.

**Parameters**

- `tenant`: The tenant.
- `operatorIdentity`: The operator's verified identity: the `sub` of the token they signed in with.
- `scope`: The scope of the toolkit's scoped system caller.

**Exceptions**

- `ArgumentException`: `operatorIdentity` is empty, or `scope` is not a scope; nothing is begun then.

#### `BeginTokenIn(TTenantId, TSeatId, string)`

```csharp
public static IDisposable BeginTokenIn(TTenantId tenant, TSeatId seat, string scope)
```

Begins system work inside one tenant that answers a link carrying a token a seat made, recorded as the token and that seat: [`TenancyWork.BeginTokenIn<TTenantId, TSeatId>`](callers.md#begintokeninttenantid-tseatid-string) closed over the application's ids. Begin it only once the token was checked and found to be that seat's, and the seat to be in use.

**Parameters**

- `tenant`: The tenant.
- `seat`: The seat the token stands for.
- `scope`: The scope of the toolkit's scoped system caller, the module's name.

**Exceptions**

- `ArgumentException`: `scope` is not a scope; nothing is begun then.

#### `CurrentCaller()`

```csharp
public static TenancyCaller<TTenantId, TSeatId> CurrentCaller()
```

The current Tenancy caller with the application's ids; nobody, refused as not seated, outside any scope: [`TenancyCallers.Current<TTenantId, TSeatId>`](callers.md#current) closed over them.

**Exceptions**

- `InvalidOperationException`: The current caller was made with other id types: two Tenancy registrations with different ids.

### `TenancyUseCases.IStore`

```csharp
public interface IStore
```

Where the use cases load and save: one unit of work per command. The storage package implements it.

Every lookup keeps to the current caller's tenant, as the storage's tenant filter does: a seat or a role of another tenant is simply not found. Two lookups go across tenants: [`SlugTakenAsync`](#slugtakenasyncstring-cancellationtoken), which says no more than yes or no, and [`ListTenantsAsync`](#listtenantsasyncstring-int-cancellationtoken), which is an operator's and reads what the operator may read.

| Member | Summary |
|---|---|
| `ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> Reads { get; }` | The rows the access questions read, as last saved. |
| `IQueryExecutor Queries { get; }` | Runs queries over [`Reads`](#tenancyusecasesistore). |
| [`FindTenantAsync(TTenantId, CancellationToken)`](#findtenantasyncttenantid-cancellationtoken) | The tenant, or `null` when it is not the current caller's. |
| [`SlugTakenAsync(string, CancellationToken)`](#slugtakenasyncstring-cancellationtoken) | Whether any tenant, the caller's or not, has the slug. |
| [`ListTenantsAsync(string?, int, CancellationToken)`](#listtenantsasyncstring-int-cancellationtoken) | The tenants there are, past the tenant filter, in the order of their slugs: those whose slug comes after `afterSlug`, at most `take` of them, each with its organization's name and the number of its active seats. |
| [`FindOrganizationAsync(TTenantId, CancellationToken)`](#findorganizationasyncttenantid-cancellationtoken) | The tenant's organization with all its units, or `null` when it is not the current caller's. |
| [`ReadOrganizationAsync(TTenantId, CancellationToken)`](#readorganizationasyncttenantid-cancellationtoken) | The tenant's organization with all its units, for the directory to answer, or `null` when it is not the current caller's. |
| [`FindSeatAsync(TSeatId, CancellationToken)`](#findseatasynctseatid-cancellationtoken) | The seat, or `null` when it is not in the current caller's tenant. |
| [`ListSeatsAsync(TTenantId, IReadOnlyCollection<TSeatId>?, CancellationToken)`](#listseatsasyncttenantid-ireadonlycollectiontseatid-cancellationtoken) | The tenant's seats, for the directory to answer: all of them, or those among `only`; an id that is no seat of the tenant is simply not among the answer. |
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

The tenants there are, past the tenant filter, in the order of their slugs: those whose slug comes after `afterSlug`, at most `take` of them, each with its organization's name and the number of its active seats. What the tenants' directory shows an operator, and read as that operator: a storage that keeps tenants apart by itself answers what the caller may read there, which for anyone else is nothing, or their own tenant.

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

The tenant's organization with all its units, for the directory to answer, or `null` when it is not the current caller's. Read as the application's own classes, with every field it added to its unit class, since the directory hands the units to the application; and read only, never for a save, as [`ListSeatsAsync`](#listseatsasyncttenantid-ireadonlycollectiontseatid-cancellationtoken) reads seats: a change made to a unit is not written, by this unit of work or another.

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

The tenant's seats, for the directory to answer: all of them, or those among `only`; an id that is no seat of the tenant is simply not among the answer. Read as the application's own seats, with every field it added, since the directory hands them to the application whole; and read only, never for a save: a change made to one is not written, by this unit of work or another. A storage that keeps grants to the seats that may read them loads each seat's grants as [`FindSeatAsync`](#findseatasynctseatid-cancellationtoken) does.

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

The tenant's administrators as last saved, as (seat, role) pairs: a right at the root for `tenancy.roles.manage`, with no end, applying at `now`. What the rule that a tenant keeps an administrator reads. It is about every seat's rights, which a storage may keep from a seat that reads [`Reads`](#tenancyusecasesistore): the storage answers it from wherever it can, as far as the caller may read the grants at the root. That is whole for every command that asks: each one that could take an administrator away asks a permission at the root first, and one that acts below the root does not ask.

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

Every right that has not ended at `now`, one still to start included, held at a unit at or above `parent` or `newParent`, of `seat` or of a permission in `managing`, once for each of the two it reaches. What the check of a move reads. Another seat's right is answered only where it reaches one of the two and not the other, which is a right the move changes: one that reaches both reaches the unit wherever it hangs, so the check never weighs it, and a seat that manages only part of the tree learns nothing of the rights above that part that stay. Like [`AdministratorsAsync`](#administratorsasyncttenantid-datetimeoffset-cancellationtoken), it is about every seat's rights, and the storage answers it whole. The use case asks once it has found `seat` to hold `tenancy.units.manage` at both parents, so the seat's own rights for that permission are always among the rows: a storage that cannot tell so much has no answer, and fails rather than answer with less, which would read as a move that changes nothing.

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

The units among `ids` that are `tenant`'s, by name and path and nothing more, whatever the calling seat reads of the organization: on a database that keeps its own policies, where an application's restrictive rule may keep the seat out of the units, through Tenancy's function that runs as its owner and answers those ids alone. `null` from a storage that keeps no policies of its own, where the caller reads every unit of its tenant, so the use case reads them whole.

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

## Tenants

Namespace `DDDToolkit.Supporting.Tenancy.UseCases`.

### `TenancyUseCases.TenantCommands`

```csharp
public sealed class TenantCommands
```

A tenant's life and settings: provisioning it with everything it needs to be used, changing its shape and name, and suspending, reactivating or closing it.

Provisioning is the one thing system work outside any tenant does. Suspending, reactivating and closing are system work inside the tenant, and act on that tenant: whoever carries them out begins `TenancyUseCases.BeginSystemIn(tenant)` first, or `TenancyUseCases.BeginOperatorIn(tenant, identity)` for an operator, so acting on another tenant cannot be expressed.

| Member | Summary |
|---|---|
| [`TenantCommands(IStore, TenancyCatalogue, TimeProvider, IRolePackTexts?)`](#tenantcommandsistore-tenancycatalogue-timeprovider-irolepacktexts) | The primary constructor: the type's summary says what it makes. |
| [`ProvisionAsync(TenantToProvision, CancellationToken)`](#provisionasynctenanttoprovision-cancellationtoken) | Provisions a tenant, in one save: the tenant, its organization with the root, a role copied from every pack seeded for its shape, and a first seat for [`TenancyUseCases.TenantToProvision.AdminIdentity`](#tenancyusecasestenanttoprovision), placed at the root and granted the administrators' role there with no end. |
| [`ChangeShapeAsync(TenantShape, IReadOnlyDictionary<string, TRoleId>?, CultureInfo?, CancellationToken)`](#changeshapeasynctenantshape-ireadonlydictionarystring-troleid-cultureinfo-cancellationtoken) | Changes a flat tenant into a hierarchical one, and gives it a copy of every pack seeded for the new shape it has no copy of yet. |
| [`RenameOrganizationAsync(string, CancellationToken)`](#renameorganizationasyncstring-cancellationtoken) | Renames the organization, which is the tenant's name. |
| [`SuspendAsync(string, CancellationToken)`](#suspendasyncstring-cancellationtoken) | Suspends the caller's tenant: system work in that tenant only. |
| [`ReactivateAsync(CancellationToken)`](#reactivateasynccancellationtoken) | Puts the caller's suspended tenant back in use: system work in that tenant only. |
| [`CloseAsync(string, CancellationToken)`](#closeasyncstring-cancellationtoken) | Closes the caller's tenant for good: system work in that tenant only. |

#### `TenantCommands(IStore, TenancyCatalogue, TimeProvider, IRolePackTexts?)`

```csharp
public TenantCommands(
    IStore store,
    TenancyCatalogue catalogue,
    TimeProvider clock,
    IRolePackTexts? packTexts = null)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `store`: Where the tenant is loaded and saved.
- `catalogue`: The packs a new tenant is given.
- `clock`: What "now" is.
- `packTexts`: The packs' names and descriptions in a tenant's language, when the application registered some; otherwise every role made from a pack gets the catalogue's texts, but the role of the default administrators' pack, which the package adds, gets the package's own texts in that language, English or Dutch.

#### `ProvisionAsync(TenantToProvision, CancellationToken)`

```csharp
public Task<ProvisionedTenant> ProvisionAsync(
    TenantToProvision command,
    CancellationToken cancellationToken)
```

Provisions a tenant, in one save: the tenant, its organization with the root, a role copied from every pack seeded for its shape, and a first seat for [`TenancyUseCases.TenantToProvision.AdminIdentity`](#tenancyusecasestenanttoprovision), placed at the root and granted the administrators' role there with no end. That role holds what the administrators' pack seeded for the shape holds, which is every live permission unless the pack lists its own. The tenant is active when the save is done.

The roles are named as the catalogue names its packs, or, with a [`TenancyUseCases.TenantToProvision.Language`](#tenancyusecasestenanttoprovision) and an [`IRolePackTexts`](catalogue.md#irolepacktexts) registered, in that language. The role of [`TenancyPacks.DefaultAdministrators`](catalogue.md#defaultadministrators), for an application that declares no administrators' pack, is named in that language by the application's texts, or else by the package's own, in English or Dutch. The fields the application added to its tenant, unit and seat classes are set by [`TenancyUseCases.TenantToProvision.ConfigureTenant`](#tenancyusecasestenanttoprovision), [`TenancyUseCases.TenantToProvision.ConfigureRoot`](#tenancyusecasestenanttoprovision) and [`TenancyUseCases.TenantToProvision.ConfigureFirstSeat`](#tenancyusecasestenanttoprovision), before the tenant is activated and in the same save.

Only system work outside any tenant provisions one, and it does not do so with that power: once it knows the new tenant's id, everything else, the check that the slug is free and the save included, runs as system work inside the new tenant ([`TenancyWork.BeginSystemIn<TTenantId, TSeatId>`](callers.md#beginsysteminttenantid-tseatid-string)), so it writes that tenant's rows and nothing else. On a database that keeps tenants apart by itself, such as Postgres with row level security, the slug check then sees no other tenant, and the unique index on the slug is what refuses one that is taken.

**Parameters**

- `command`: What to provision.
- `cancellationToken`: Cancels the work.

**Exceptions**

- `RefusalException`: `access.system-only` for a seat, `tenancy.slug-taken`, `tenancy.role-name-taken` when two packs are named alike in the tenant's language, and what the aggregates refuse: an invalid name, a pack's translated one included, or an empty identity.
- `InvalidValueObjectException`: The slug does not follow [`TenantSlug.Pattern`](aggregates.md#tenantslug).
- `ArgumentException`: [`TenancyUseCases.TenantToProvision.RoleIds`](#tenancyusecasestenanttoprovision) names a pack the tenant is not given, or gives one id twice.
- `InvalidOperationException`: The caller is system work in a tenant rather than outside any.

#### `ChangeShapeAsync(TenantShape, IReadOnlyDictionary<string, TRoleId>?, CultureInfo?, CancellationToken)`

```csharp
public Task ChangeShapeAsync(
    TenantShape to,
    IReadOnlyDictionary<string, TRoleId>? roleIds,
    CultureInfo? language,
    CancellationToken cancellationToken)
```

Changes a flat tenant into a hierarchical one, and gives it a copy of every pack seeded for the new shape it has no copy of yet. No grant changes: who held what at the root still does. Where the two shapes have administrators' packs of their own, the tenant gets the role of the pack seeded for the new shape with the permissions that pack holds, and nobody is given it: the administrators keep the role they have, and give the new one as they see fit.

**Parameters**

- `to`: The new shape.
- `roleIds`: The ids of the new roles, by pack key, for imports and seeding; the rest get new ids.
- `language`: The language the new roles are named in, usually the tenant's, which the application keeps: each pack's texts are asked of the application's [`IRolePackTexts`](catalogue.md#irolepacktexts) in it. `null`, or an application that registered no texts, keeps the catalogue's own; the default administrators' pack, which the package adds, gets the package's own texts in that language, English or Dutch, where the application's have none for it. The roles the tenant has already keep their names.
- `cancellationToken`: Cancels the work.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SettingsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.shape-change`, `tenancy.tenant-state` for a closed tenant, `tenancy.role-name-taken` when a role of the tenant, or another new role, already has a new role's name, `tenancy.name-invalid` for a pack's translated name or description.
- `ArgumentException`: `roleIds` names a pack of no role that would be made, or gives one id twice.

#### `RenameOrganizationAsync(string, CancellationToken)`

```csharp
public Task RenameOrganizationAsync(string name, CancellationToken cancellationToken)
```

Renames the organization, which is the tenant's name.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SettingsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.name-invalid`.

#### `SuspendAsync(string, CancellationToken)`

```csharp
public Task SuspendAsync(string reason, CancellationToken cancellationToken)
```

Suspends the caller's tenant: system work in that tenant only.

**Parameters**

- `reason`: Why; required.
- `cancellationToken`: Cancels the work.

**Exceptions**

- `RefusalException`: `access.system-only` for a seat, `tenancy.tenant-state`, `tenancy.reason-required`.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `ReactivateAsync(CancellationToken)`

```csharp
public Task ReactivateAsync(CancellationToken cancellationToken)
```

Puts the caller's suspended tenant back in use: system work in that tenant only.

**Exceptions**

- `RefusalException`: `access.system-only` for a seat, `tenancy.tenant-state`.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `CloseAsync(string, CancellationToken)`

```csharp
public Task CloseAsync(string reason, CancellationToken cancellationToken)
```

Closes the caller's tenant for good: system work in that tenant only.

**Parameters**

- `reason`: Why; required.
- `cancellationToken`: Cancels the work.

**Exceptions**

- `RefusalException`: `access.system-only` for a seat, `tenancy.tenant-state`, `tenancy.reason-required`.
- `InvalidOperationException`: The caller is system work outside any tenant.

### `TenancyUseCases.TenantDirectory`

```csharp
public sealed class TenantDirectory
```

The tenants of the application, for its operators: every tenant by slug, a page at a time, each with how many of its seats are in use. It reads, and changes nothing.

Only an operator asks: a signed-in user whose token carries one of [`TenancyOptions.OperatorTokenRoles`](#operatortokenroles). An operator holds no seat and acts in no tenant, so this is the one use case that asks the toolkit's own caller rather than the Tenancy caller. It reads as that caller, never as the system: on a database that keeps tenants apart by itself, such as Postgres with row level security, the operator's own database role and its policies decide what it reads, and a host that mapped no such role reads nothing.

The application's own work that has to visit every tenant, a sweep say, does not go through here: it asks which tenants there are in a scope of its own, and then works in each.

| Member | Summary |
|---|---|
| `MostPerPage = 200` | How many tenants a page holds at most. |
| [`TenantDirectory(IStore, TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>, ICallerAccessor)`](#tenantdirectoryistore-tenancyoptionsttenantid-tseatid-tunitid-troleid-icalleraccessor) | The primary constructor: the type's summary says what it makes. |
| [`ListAsync(string?, int, CancellationToken)`](#listasyncstring-int-cancellationtoken) | A page of the tenants, by slug: those after the last tenant of the page before, or the first ones. |

#### `TenantDirectory(IStore, TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>, ICallerAccessor)`

```csharp
public TenantDirectory(
    IStore store,
    TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> options,
    ICallerAccessor callers)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `store`: Where it reads.
- `options`: Which token roles are operators'.
- `callers`: Who is calling, as the toolkit says.

#### `ListAsync(string?, int, CancellationToken)`

```csharp
public Task<TenantDirectoryPage> ListAsync(
    string? after,
    int size,
    CancellationToken cancellationToken)
```

A page of the tenants, by slug: those after the last tenant of the page before, or the first ones.

**Parameters**

- `after`: [`TenancyUseCases.TenantDirectoryPage.Next`](#tenancyusecasestenantdirectorypage) of the page before, or `null` for the first page. It is a marker the directory made, and means nothing to anyone else.
- `size`: How many tenants the page holds: 1 to [`MostPerPage`](#tenancyusecasestenantdirectory).
- `cancellationToken`: Cancels the read.

**Exceptions**

- `RefusalException`: `tenancy.operators-only` for anyone but an operator, asked before anything else, so nobody else learns what a page holds; `tenancy.page-size-invalid`, with `Max`; `tenancy.cursor-invalid` for a marker the directory did not make. Each of the last two names its input in the argument `Field`: `size` and `after`.

### `TenancyUseCases.ProvisionedTenant`

```csharp
public sealed record ProvisionedTenant
```

The ids a provisioned tenant was given.

| Member | Summary |
|---|---|
| [`ProvisionedTenant(TTenantId, TUnitId, TSeatId, TRoleId, IReadOnlyDictionary<string, TRoleId>)`](#provisionedtenantttenantid-tunitid-tseatid-troleid-ireadonlydictionarystring-troleid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId Tenant { get; init; }` | The tenant, and its organization. |
| `TUnitId RootUnit { get; init; }` | The root unit. |
| `TSeatId AdminSeat { get; init; }` | The first administrator's seat. |
| `TRoleId AdministratorRole { get; init; }` | The role copied from the administrators' pack. |
| `IReadOnlyDictionary<string, TRoleId> RolesByPack { get; init; }` | Every role copied from a pack, by pack key. |

#### `ProvisionedTenant(TTenantId, TUnitId, TSeatId, TRoleId, IReadOnlyDictionary<string, TRoleId>)`

```csharp
public ProvisionedTenant(
    TTenantId Tenant,
    TUnitId RootUnit,
    TSeatId AdminSeat,
    TRoleId AdministratorRole,
    IReadOnlyDictionary<string, TRoleId> RolesByPack)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Tenant`: The tenant, and its organization.
- `RootUnit`: The root unit.
- `AdminSeat`: The first administrator's seat.
- `AdministratorRole`: The role copied from the administrators' pack.
- `RolesByPack`: Every role copied from a pack, by pack key.

### `TenancyUseCases.TenantDirectoryPage`

```csharp
public sealed record TenantDirectoryPage
```

One page of the tenants' directory.

| Member | Summary |
|---|---|
| [`TenantDirectoryPage(IReadOnlyList<TenantListing>, string?)`](#tenantdirectorypageireadonlylisttenantlisting-string) | The primary constructor: the type's summary says what it makes. |
| `IReadOnlyList<TenantListing> Items { get; init; }` | The tenants of the page, by slug. |
| `string? Next { get; init; }` | What to ask the next page with, or `null` when this is the last one. |

#### `TenantDirectoryPage(IReadOnlyList<TenantListing>, string?)`

```csharp
public TenantDirectoryPage(IReadOnlyList<TenantListing> Items, string? Next)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Items`: The tenants of the page, by slug.
- `Next`: What to ask the next page with, or `null` when this is the last one.

### `TenancyUseCases.TenantListing`

```csharp
public sealed record TenantListing
```

A tenant as the tenants' directory lists it for an operator: no person, and no row of it.

| Member | Summary |
|---|---|
| [`TenantListing(TTenantId, string, string, TenantStatus, int)`](#tenantlistingttenantid-string-string-tenantstatus-int) | The primary constructor: the type's summary says what it makes. |
| `TTenantId Id { get; init; }` | The tenant. |
| `string Slug { get; init; }` | The slug it is selected by. |
| `string Name { get; init; }` | Its name, which is its organization's. |
| `TenantStatus Status { get; init; }` | Whether it is in use. |
| `int ActiveSeats { get; init; }` | How many of its seats are active now. |

#### `TenantListing(TTenantId, string, string, TenantStatus, int)`

```csharp
public TenantListing(TTenantId Id, string Slug, string Name, TenantStatus Status, int ActiveSeats)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Id`: The tenant.
- `Slug`: The slug it is selected by.
- `Name`: Its name, which is its organization's.
- `Status`: Whether it is in use.
- `ActiveSeats`: How many of its seats are active now.

### `TenancyUseCases.TenantToProvision`

```csharp
public sealed record TenantToProvision
```

What a new tenant is provisioned with.

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
| `IReadOnlyDictionary<string, TRoleId>? RoleIds { get; init; }` | The ids of the roles copied from the packs, by pack key; the others get new ids. |
| `CultureInfo? Language { get; init; }` | The language the tenant's roles are named in: each pack's name and description are asked of the application's [`IRolePackTexts`](catalogue.md#irolepacktexts) in it. `null`, or an application that registered no texts, keeps the catalogue's own; the default administrators' pack, which the package adds, falls back to the package's own texts in it, English or Dutch. The package keeps no language itself: where the tenant remembers its language is a field of the application's tenant class, set in `ConfigureTenant`. |
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
- `RoleIds`: The ids of the roles copied from the packs, by pack key; the others get new ids.
- `Language`: The language the tenant's roles are named in: each pack's name and description are asked of the application's [`IRolePackTexts`](catalogue.md#irolepacktexts) in it. `null`, or an application that registered no texts, keeps the catalogue's own; the default administrators' pack, which the package adds, falls back to the package's own texts in it, English or Dutch. The package keeps no language itself: where the tenant remembers its language is a field of the application's tenant class, set in `ConfigureTenant`.
- `ConfigureTenant`: Sets the fields the application added to its tenant class, on the new tenant before it is activated, so they are saved in the same transaction and a domain event the class raises there goes out with the provisioning. When it throws, nothing is saved.
- `ConfigureRoot`: The same for the fields the application added to its unit class, on the root: what kind of unit it is, say, when the class keeps one. A unit raises no events of its own; the organization raises the root's.
- `ConfigureFirstSeat`: The same for the first administrator's seat, which is placed at the root and holds the administrators' role by then: the name it is shown by, say, when the class keeps one.

## Organization

Namespace `DDDToolkit.Supporting.Tenancy.UseCases`.

### `TenancyUseCases.OrganizationCommands`

```csharp
public sealed class OrganizationCommands
```

The organization's tree: adding, renaming, moving and archiving units. Each needs [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) where the change is made, so a seat that manages a region manages the units below it and nothing beside or above it.

Two edits of the tree at the same time are kept apart by the organization's version. A move also changes what every permission held above the moved unit reaches, so it takes the access revision too.

| Member | Summary |
|---|---|
| [`OrganizationCommands(IStore, TenancyCatalogue, TimeProvider)`](#organizationcommandsistore-tenancycatalogue-timeprovider) | The primary constructor: the type's summary says what it makes. |
| [`AddUnitAsync(TUnitId, string, CancellationToken, TUnitId?, Action<TUnit>?)`](#addunitasynctunitid-string-cancellationtoken-tunitid-actiontunit) | Adds a unit below `parent`. |
| [`RenameUnitAsync(TUnitId, string, CancellationToken)`](#renameunitasynctunitid-string-cancellationtoken) | Renames a unit. |
| [`MoveUnitAsync(TUnitId, TUnitId, CancellationToken)`](#moveunitasynctunitid-tunitid-cancellationtoken) | Moves a unit, and everything below it, under another unit. |
| [`ArchiveUnitAsync(TUnitId, CancellationToken)`](#archiveunitasynctunitid-cancellationtoken) | Archives a unit. |

#### `OrganizationCommands(IStore, TenancyCatalogue, TimeProvider)`

```csharp
public OrganizationCommands(IStore store, TenancyCatalogue catalogue, TimeProvider clock)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `store`: Where the organization is loaded and saved.
- `catalogue`: The permissions asked for.
- `clock`: What "now" is.

#### `AddUnitAsync(TUnitId, string, CancellationToken, TUnitId?, Action<TUnit>?)`

```csharp
public Task<TUnitId> AddUnitAsync(
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

**Returns**: The new unit's id.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) at the parent, and what the organization refuses, `tenancy.flat-tenant` first.
- `ArgumentException`: `id` is already a unit's: a mistake in the import or seeding that gave it, not a refusal.

#### `RenameUnitAsync(TUnitId, string, CancellationToken)`

```csharp
public Task RenameUnitAsync(TUnitId unit, string name, CancellationToken cancellationToken)
```

Renames a unit.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) at the unit, and what the organization refuses.

#### `MoveUnitAsync(TUnitId, TUnitId, CancellationToken)`

```csharp
public Task MoveUnitAsync(TUnitId unit, TUnitId newParent, CancellationToken cancellationToken)
```

Moves a unit, and everything below it, under another unit. The caller needs the permission where the unit hangs now and where it goes, so a unit only moves between parts of the tree the caller manages both of. A move changes which grants reach the unit, so a seat moves it only as far as it could give and take away what that changes: the move gives the seat itself no permission it does not hold at the unit already, and none for longer than it holds it there; and it gives or takes away no permission that manages access, from anyone, that the seat does not hold at the unit for at least as long as that grant runs. The second part is containment, which goes when the application turns it off ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)): then a permission that manages access follows a move as any other does, and the move still gives the seat itself nothing.

**Exceptions**

- `RefusalException`: `tenancy.unit-not-found`, `tenancy.not-permitted` without [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) at the current parent and at the new one, what the organization refuses, and `tenancy.grant-exceeds-own` naming the permissions the move would give the calling seat, or, while containment is on, give or take away from anyone, that it lacks at the unit, or lacks for long enough.

#### `ArchiveUnitAsync(TUnitId, CancellationToken)`

```csharp
public Task ArchiveUnitAsync(TUnitId unit, CancellationToken cancellationToken)
```

Archives a unit. The caller needs the permission at its parent: archiving takes the unit out of the part of the tree above it. Seats placed there and rights held there keep working.

**Exceptions**

- `RefusalException`: `tenancy.unit-not-found`, `tenancy.not-permitted` without [`TenancyPermissions.UnitsManage`](catalogue.md#tenancypermissions) at the parent, and what the organization refuses.

## Seats

Namespace `DDDToolkit.Supporting.Tenancy.UseCases`.

### `TenancyUseCases.SeatCommands`

```csharp
public sealed class SeatCommands
```

People's seats: adding them, placing them in units, granting them roles there, and suspending, reactivating or deactivating them.

Holding [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at a unit is enough to give roles there. Its holder gives any active role of the tenant that manages no access to a seat placed there or below, and takes any such role away, without holding the role's permissions: to another seat for as long as it says, to itself for no longer than it holds [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) there. A role that manages access (see [`TenancyCatalogue.AccessManagingPermissionsOf`](catalogue.md#accessmanagingpermissionsofrolefacts)) stays contained: a seat gives it, and takes it away, only while holding there each of its permissions that manage access, for at least as long as the grant runs, and never gives it to itself. Withdrawing a placement takes its grants away, by the same rule, and so does suspending or deactivating a seat, while reactivating one gives its grants back by that rule too. System work in a tenant is not held to any of that. When the application turns containment off ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), a role that manages access goes as one that manages none: a grants manager gives every role, to another seat for as long as it says and to itself for no longer than it holds [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) there, and the permission a command asks where it acts is all a seat's status takes. Nothing leaves a tenant that has an administrator without one, whoever asks, and that is decided before the seat changes, so a refused command leaves nothing for a later save to write. A seat of another tenant is not found.

Nothing here names a seat: a seat has no name in Tenancy. What the application keeps on its seat class, a name it is shown by among it, it sets in the callback of [`AddSeatAsync`](#addseatasyncguid-cancellationtoken-tseatid-actiontseat), and changes with a use case of its own, under a rule of its own.

| Member | Summary |
|---|---|
| [`SeatCommands(IStore, TenancyCatalogue, TimeProvider)`](#seatcommandsistore-tenancycatalogue-timeprovider) | The primary constructor: the type's summary says what it makes. |
| [`AddSeatAsync(Guid, CancellationToken, TSeatId?, Action<TSeat>?)`](#addseatasyncguid-cancellationtoken-tseatid-actiontseat) | Adds a seat for a verified identity, placed nowhere yet. |
| [`PlaceAsync(TSeatId, TUnitId, bool, CancellationToken)`](#placeasynctseatid-tunitid-bool-cancellationtoken) | Places a seat in an active unit. |
| [`WithdrawAsync(TSeatId, TUnitId, CancellationToken)`](#withdrawasynctseatid-tunitid-cancellationtoken) | Withdraws a seat from a unit, revoking every role it holds there. |
| [`MakePrimaryAsync(TSeatId, TUnitId, CancellationToken)`](#makeprimaryasynctseatid-tunitid-cancellationtoken) | Makes a seat's placement in a unit its primary one. |
| [`GrantAsync(TSeatId, TUnitId, TRoleId, DateTimeOffset?, string?, CancellationToken, DateTimeOffset?)`](#grantasynctseatid-tunitid-troleid-datetimeoffset-string-cancellationtoken-datetimeoffset) | Grants a seat a role at an active unit where it is placed, from now until `until`. |
| [`RevokeAsync(TSeatId, TUnitId, TRoleId, CancellationToken)`](#revokeasynctseatid-tunitid-troleid-cancellationtoken) | Revokes a role a seat holds at a unit. |
| [`SuspendAsync(TSeatId, CancellationToken)`](#suspendasynctseatid-cancellationtoken) | Suspends a seat: its grants give it nothing until it is reactivated. |
| [`ReactivateAsync(TSeatId, CancellationToken)`](#reactivateasynctseatid-cancellationtoken) | Makes a suspended seat active again. |
| [`DeactivateAsync(TSeatId, CancellationToken)`](#deactivateasynctseatid-cancellationtoken) | Deactivates a seat for good, which takes away every grant that has not ended, by the rule [`SuspendAsync`](#suspendasynctseatid-cancellationtoken) follows. |

#### `SeatCommands(IStore, TenancyCatalogue, TimeProvider)`

```csharp
public SeatCommands(IStore store, TenancyCatalogue catalogue, TimeProvider clock)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `store`: Where seats and roles are loaded and saved.
- `catalogue`: The permissions asked for, and which permissions are live.
- `clock`: What "now" is.

#### `AddSeatAsync(Guid, CancellationToken, TSeatId?, Action<TSeat>?)`

```csharp
public Task<TSeatId> AddSeatAsync(
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

**Returns**: The new seat's id.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.identity-has-seat`, `tenancy.identity-required`.

#### `PlaceAsync(TSeatId, TUnitId, bool, CancellationToken)`

```csharp
public Task PlaceAsync(
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

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.seat-not-found`, `tenancy.unit-not-found`, `tenancy.unit-not-active`, and what the seat refuses: placing itself, a second placement in the unit, a second primary.

#### `WithdrawAsync(TSeatId, TUnitId, CancellationToken)`

```csharp
public Task WithdrawAsync(TSeatId seat, TUnitId unit, CancellationToken cancellationToken)
```

Withdraws a seat from a unit, revoking every role it holds there. When it holds any, the caller needs [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) there too, and, while containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), for each of those roles that manages access, its permissions that do, held there until at least that grant's end: taking a placement away takes its grants away. A role that manages no access needs none of its permissions.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted`, `tenancy.seat-not-found`, `tenancy.placement-not-found`, `tenancy.grant-exceeds-own`, `tenancy.last-admin`.

#### `MakePrimaryAsync(TSeatId, TUnitId, CancellationToken)`

```csharp
public Task MakePrimaryAsync(TSeatId seat, TUnitId unit, CancellationToken cancellationToken)
```

Makes a seat's placement in a unit its primary one.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.seat-not-found`, `tenancy.placement-not-found`.

#### `GrantAsync(TSeatId, TUnitId, TRoleId, DateTimeOffset?, string?, CancellationToken, DateTimeOffset?)`

```csharp
public Task GrantAsync(
    TSeatId seat,
    TUnitId unit,
    TRoleId role,
    DateTimeOffset? until,
    string? reason,
    CancellationToken cancellationToken,
    DateTimeOffset? from = null)
```

Grants a seat a role at an active unit where it is placed, from now until `until`. With [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, a seat gives another seat any role that manages no access, for any period, and itself one for no longer than it holds [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) there. While containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), a role that manages access it gives only while it holds there each of the role's permissions that manage access, for at least as long as the grant lasts, and never to itself: a seat whose own grant ends next week does not give such a role for good. Off, such a role goes as any other.

**Parameters**

- `seat`: The seat.
- `unit`: The unit of one of its placements.
- `role`: The role, which must be active.
- `until`: When the grant ends, or `null` for no end.
- `reason`: Why, or `null`.
- `cancellationToken`: Cancels the work.
- `from`: When the grant starts, for imports and seeding: system work only. A grant a seat makes starts now, and a seat that passes this is refused rather than silently ignored.

**Exceptions**

- `RefusalException`: `tenancy.start-system-only`, `tenancy.not-permitted` without [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.role-not-found`, `tenancy.self-appointment` for a role that manages access given to the caller itself while containment is on, `tenancy.grant-exceeds-own`, `tenancy.seat-not-found`, `tenancy.unit-not-found`, `tenancy.unit-not-active` for an archived unit, `tenancy.invalid-period`, and what the seat refuses.

#### `RevokeAsync(TSeatId, TUnitId, TRoleId, CancellationToken)`

```csharp
public Task RevokeAsync(
    TSeatId seat,
    TUnitId unit,
    TRoleId role,
    CancellationToken cancellationToken)
```

Revokes a role a seat holds at a unit. With [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, a seat takes away any role that manages no access, an archived role included, which manages nothing. While containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), a role that manages access it takes away only while it holds there each of the role's permissions that manage access, until at least the grant's end, or for good for a grant with none; off, such a role goes as any other. A seat's own grant that applies now is its own hold, so a seat may take away its current roles.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.role-not-found`, `tenancy.seat-not-found`, `tenancy.grant-exceeds-own`, `tenancy.last-admin`, `tenancy.grant-not-found`.

#### `SuspendAsync(TSeatId, CancellationToken)`

```csharp
public Task SuspendAsync(TSeatId seat, CancellationToken cancellationToken)
```

Suspends a seat: its grants give it nothing until it is reactivated. Suspending takes away every grant that has not ended, so, while containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), each of a role that manages access needs its permissions that do, held by the caller at that grant's unit until at least its end, as taking the role away would. A seat may suspend itself: its own grants that apply now are its own hold.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.seat-not-found`, `tenancy.grant-exceeds-own`, `tenancy.last-admin`, `tenancy.seat-state`.

#### `ReactivateAsync(TSeatId, CancellationToken)`

```csharp
public Task ReactivateAsync(TSeatId seat, CancellationToken cancellationToken)
```

Makes a suspended seat active again. Reactivating gives back every grant that has not ended, so, while containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), each of a role that manages access needs its permissions that do, held by the caller at that grant's unit until at least its end, as giving the role would.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.seat-not-found`, `tenancy.grant-exceeds-own`, `tenancy.seat-state`.

#### `DeactivateAsync(TSeatId, CancellationToken)`

```csharp
public Task DeactivateAsync(TSeatId seat, CancellationToken cancellationToken)
```

Deactivates a seat for good, which takes away every grant that has not ended, by the rule [`SuspendAsync`](#suspendasynctseatid-cancellationtoken) follows. A seat may deactivate itself.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.seat-not-found`, `tenancy.grant-exceeds-own`, `tenancy.last-admin`, `tenancy.seat-state`.

## Roles

Namespace `DDDToolkit.Supporting.Tenancy.UseCases`.

### `TenancyUseCases.RoleCommands`

```csharp
public sealed class RoleCommands
```

The tenant's roles: making them, renaming them, changing their permissions and archiving them. Each needs [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant. Following the packs the roles were made from, once the application changed them, is system work's alone ([`FollowPacksAsync`](#followpacksasynccancellationtoken)).

A holder of that permission may put any live permission into any role, permissions it does not hold included. That is on purpose: it is the administrator's permission, and an administrator can give themselves anything already. One exception: an administrator alone, holding it at the root with no end, changes which permissions that manage access a role holds, by adding one, taking one out or archiving a role that holds one. A role that gains one gives it to every seat that holds the role, seats that gave themselves the role included, and a role that loses one takes it from every holder at once, so someone holding the permission for a week neither gives lasting power over access that way nor takes away what it could not revoke grant by grant. A change of a role's permissions reaches every seat that holds the role at the next save. Whether a change leaves the tenant an administrator is decided before the role changes, so a refused command leaves nothing for a later save to write.

That exception is containment, and goes when the application turns containment off ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)): then the role permission for the whole tenant is all any change of a role takes. The administrator a tenant keeps stays either way.

| Member | Summary |
|---|---|
| [`RoleCommands(IStore, TenancyCatalogue, TimeProvider)`](#rolecommandsistore-tenancycatalogue-timeprovider) | The primary constructor: the type's summary says what it makes. |
| [`CreateAsync(string, string, IReadOnlyCollection<string>, CancellationToken, TRoleId?)`](#createasyncstring-string-ireadonlycollectionstring-cancellationtoken-troleid) | Makes a role by hand; it comes from no pack. |
| [`RenameAsync(TRoleId, string, string, CancellationToken)`](#renameasynctroleid-string-string-cancellationtoken) | Renames a role and describes it again. |
| [`SetPermissionsAsync(TRoleId, IReadOnlyCollection<string>, CancellationToken)`](#setpermissionsasynctroleid-ireadonlycollectionstring-cancellationtoken) | Sets the permissions a role grants, for every seat that holds it. |
| [`ArchiveAsync(TRoleId, CancellationToken)`](#archiveasynctroleid-cancellationtoken) | Archives a role: it stays where it was granted, and grants nothing from now on. |
| [`FollowPacksAsync(CancellationToken)`](#followpacksasynccancellationtoken) | Every active role of the caller's tenant that was made from a pack follows that pack, as the catalogue the application runs with builds it now ([`RoleAggregate.FollowPack<TSeatId>`](aggregates.md#followpacktenancycatalogue-tenancyactortseatid)): a permission the pack gained since is added, a permission it lost is taken out, and what the tenant changed itself stays. |

#### `RoleCommands(IStore, TenancyCatalogue, TimeProvider)`

```csharp
public RoleCommands(IStore store, TenancyCatalogue catalogue, TimeProvider clock)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `store`: Where roles are loaded and saved.
- `catalogue`: The permissions a role may hold.
- `clock`: What "now" is.

#### `CreateAsync(string, string, IReadOnlyCollection<string>, CancellationToken, TRoleId?)`

```csharp
public Task<TRoleId> CreateAsync(
    string name,
    string description,
    IReadOnlyCollection<string> permissions,
    CancellationToken cancellationToken,
    TRoleId? id = null)
```

Makes a role by hand; it comes from no pack.

**Parameters**

- `name`: Its name, unique in the tenant, ignoring case.
- `description`: What it is for.
- `permissions`: The permissions it grants; the permissions they imply are added.
- `cancellationToken`: Cancels the work.
- `id`: Its id, for imports and seeding; a new one otherwise. An id that is already a role's is a mistake in the import, which the save reports.

**Returns**: The new role's id.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.role-name-taken`, `tenancy.name-invalid`, `tenancy.unknown-permission`.

#### `RenameAsync(TRoleId, string, string, CancellationToken)`

```csharp
public Task RenameAsync(
    TRoleId role,
    string name,
    string description,
    CancellationToken cancellationToken)
```

Renames a role and describes it again.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.role-not-found`, `tenancy.role-name-taken`, `tenancy.role-archived`, `tenancy.name-invalid`.

#### `SetPermissionsAsync(TRoleId, IReadOnlyCollection<string>, CancellationToken)`

```csharp
public Task SetPermissionsAsync(
    TRoleId role,
    IReadOnlyCollection<string> permissions,
    CancellationToken cancellationToken)
```

Sets the permissions a role grants, for every seat that holds it. While containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), a seat that adds or takes out a permission that manages access must be an administrator: hold [`TenancyPermissions.AdministratorPermission`](catalogue.md#tenancypermissions) at the root with no end.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.role-not-found`, `tenancy.role-archived`, `tenancy.unknown-permission`, `tenancy.grant-exceeds-own` when a seat that is not an administrator adds or takes out a permission that manages access while containment is on, naming [`TenancyPermissions.AdministratorPermission`](catalogue.md#tenancypermissions), and `tenancy.last-admin` when the last administrators' role would lose the administrator permission.

#### `ArchiveAsync(TRoleId, CancellationToken)`

```csharp
public Task ArchiveAsync(TRoleId role, CancellationToken cancellationToken)
```

Archives a role: it stays where it was granted, and grants nothing from now on. While containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), a seat that archives a role that manages access must be an administrator: hold [`TenancyPermissions.AdministratorPermission`](catalogue.md#tenancypermissions) at the root with no end.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) for the whole tenant, `tenancy.role-not-found`, `tenancy.role-archived`, `tenancy.grant-exceeds-own` when a seat that is not an administrator archives a role that manages access while containment is on, naming [`TenancyPermissions.AdministratorPermission`](catalogue.md#tenancypermissions), and `tenancy.last-admin`.

#### `FollowPacksAsync(CancellationToken)`

```csharp
public Task<PacksFollowed> FollowPacksAsync(CancellationToken cancellationToken)
```

Every active role of the caller's tenant that was made from a pack follows that pack, as the catalogue the application runs with builds it now ([`RoleAggregate.FollowPack<TSeatId>`](aggregates.md#followpacktenancycatalogue-tenancyactortseatid)): a permission the pack gained since is added, a permission it lost is taken out, and what the tenant changed itself stays. Each role whose permissions change raises [`RoleFollowedItsPack<TTenantId, TRoleId, TSeatId>`](aggregates.md#rolefolloweditspackttenantid-troleid-tseatid), and the change reaches every seat that holds the role at the save, the one save of the command.

System work in the tenant alone runs it, as `services.SyncRolePacks()` does for every tenant once the host has started, through [`IRolePackSync`](catalogue.md#irolepacksync); an operator's request begins `TenancyWork.BeginOperatorIn(tenant, operator)` for one tenant, and the events name the operator. No seat runs it: what a pack holds is the application's decision, permissions that manage access included, and an administrator changes a role with [`SetPermissionsAsync`](#setpermissionsasynctroleid-ireadonlycollectionstring-cancellationtoken).

It takes the tenant's access revision first, as every change of rights does, and saves only when a role changed: a second run in a row reads, and writes nothing. Two runs at the same time cannot both commit; the one that fails on the revision changes nothing when it runs again. The last-administrator rule holds for it as for anyone: a role whose following would take the administrator permission from the tenant's last administrators is left as it is, and named in the answer.

**Parameters**

- `cancellationToken`: Cancels the work.

**Returns**: The roles that changed, the roles kept for an administrator, and the roles whose pack is gone.

**Exceptions**

- `RefusalException`: `access.system-only` for a seat.
- `InvalidOperationException`: The caller is system work outside any tenant.

### `TenancyUseCases.PacksFollowed`

```csharp
public sealed record PacksFollowed
```

What following the packs did in one tenant ([`TenancyUseCases.RoleCommands.FollowPacksAsync`](#followpacksasynccancellationtoken)). A role that follows its pack already, one made by hand and one that is archived are in none of the lists.

| Member | Summary |
|---|---|
| [`PacksFollowed(IReadOnlyList<TRoleId>, IReadOnlyList<TRoleId>, IReadOnlyList<TRoleId>)`](#packsfollowedireadonlylisttroleid-ireadonlylisttroleid-ireadonlylisttroleid) | The primary constructor: the type's summary says what it makes. |
| `IReadOnlyList<TRoleId> Changed { get; init; }` | The roles whose permissions changed, each with a [`RoleFollowedItsPack<TTenantId, TRoleId, TSeatId>`](aggregates.md#rolefolloweditspackttenantid-troleid-tseatid) raised. |
| `IReadOnlyList<TRoleId> KeptForAnAdministrator { get; init; }` | The roles left as they were, permissions and record, because following their pack would take the administrator permission from the tenant's last administrators. Each follows at a later run, once the tenant has an administrator through another role. |
| `IReadOnlyList<TRoleId> WithoutTheirPack { get; init; }` | The roles made from a pack the catalogue no longer has. They keep their permissions, and follow the pack again if the application declares it again under the same permission. |

#### `PacksFollowed(IReadOnlyList<TRoleId>, IReadOnlyList<TRoleId>, IReadOnlyList<TRoleId>)`

```csharp
public PacksFollowed(
    IReadOnlyList<TRoleId> Changed,
    IReadOnlyList<TRoleId> KeptForAnAdministrator,
    IReadOnlyList<TRoleId> WithoutTheirPack)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Changed`: The roles whose permissions changed, each with a [`RoleFollowedItsPack<TTenantId, TRoleId, TSeatId>`](aggregates.md#rolefolloweditspackttenantid-troleid-tseatid) raised.
- `KeptForAnAdministrator`: The roles left as they were, permissions and record, because following their pack would take the administrator permission from the tenant's last administrators. Each follows at a later run, once the tenant has an administrator through another role.
- `WithoutTheirPack`: The roles made from a pack the catalogue no longer has. They keep their permissions, and follow the pack again if the application declares it again under the same permission.

## Invitations

Namespace `DDDToolkit.Supporting.Tenancy.UseCases`.

### `TenancyUseCases.IInvitationStore<TInvitation, TInvitationId>`

```csharp
public interface IInvitationStore<TInvitation, TInvitationId>
    where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
```

Where the invitation use cases load and keep invitations, in the unit of work of [`TenancyUseCases.IStore`](#tenancyusecasesistore): what is added or changed here is saved by [`TenancyUseCases.IStore.SaveAsync`](#saveasynccancellationtoken), with everything else the command changed. The storage package implements it.

A token's digest is handed over with its invitation and never comes back. The store keeps it where reading invitations does not read it, and answers one question about it: which invitation it belongs to.

**Type parameters**

- `TInvitation`: The application's invitation class.
- `TInvitationId`: The application's invitation id.

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
Task<InvitationOfToken<TInvitationId>?> FindByDigestAsync(
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

### `TenancyUseCases.InvitationCommands<TInvitation, TInvitationId>`

```csharp
public sealed class InvitationCommands<TInvitation, TInvitationId>
    where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TInvitationId : struct, ICreatableEntityId<TInvitationId>, IEquatable<TInvitationId>
```

Invitations into a tenant: issuing one for an address, listing the open ones, cancelling one, and accepting one, which is what makes the seat.

An invitation offers what adding a seat, placing it and granting it a role would give, so issuing one takes what those take: [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant, as adding a seat does, and [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, with the rule for giving a role there. A role that manages no access is offered freely, and may outlast the issuer's own grants. A role that manages access is offered only by a seat that holds each of its permissions that do at the unit, until at least the end of the grant, unless the application turns containment off ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), and then as freely as any other. Nobody invites themself into one: an identity that has a seat in the tenant cannot accept.

An invitation never gives more than its issuer could give at the moment it is used. Accepting asks the issuer's rights again, as they are then, with the role as it is then, and an invitation whose issuer has lost them is refused with one answer that says nothing of which. System work is not held to a seat, and neither is an invitation it issued.

The token is returned once, by [`IssueAsync`](#issueasyncstring-tunitid-troleid-datetimeoffset-timespan-cancellationtoken-tinvitationid), and kept nowhere: the store holds its digest. It is in no event, no refusal and no log of the package. Sending it to the address is the application's to do.

**Type parameters**

- `TInvitation`: The application's invitation class.
- `TInvitationId`: The application's invitation id.

| Member | Summary |
|---|---|
| [`InvitationCommands(IStore, IInvitationStore<TInvitation, TInvitationId>, TenancyCatalogue, TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>, TenancyInvitationOptions<TInvitationId>, ICallerAccessor, TimeProvider)`](#invitationcommandsistore-iinvitationstoretinvitation-tinvitationid-tenancycatalogue-tenancyoptionsttenantid-tseatid-tunitid-troleid-tenancyinvitationoptionstinvitationid-icalleraccessor-timeprovider) | The primary constructor: the type's summary says what it makes. |
| [`IssueAsync(string, TUnitId, TRoleId, DateTimeOffset?, TimeSpan?, CancellationToken, TInvitationId?)`](#issueasyncstring-tunitid-troleid-datetimeoffset-timespan-cancellationtoken-tinvitationid) | Issues an invitation for an address: a seat placed in `unit` as its primary placement, with `role` there until `grantUntil`, for whoever accepts it with the token this returns. |
| [`ListOpenAsync(CancellationToken)`](#listopenasynccancellationtoken) | The tenant's invitations that can still be accepted, the soonest to end first: for a seat, those into the units where it holds [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions), which may be none; for system work in the tenant, all of them. |
| [`CancelAsync(TInvitationId, CancellationToken)`](#cancelasynctinvitationid-cancellationtoken) | Cancels an open invitation, so its token no longer works. |
| [`AcceptAsync(string, string?, CancellationToken, TSeatId?, Action<TSeat>?)`](#acceptasyncstring-string-cancellationtoken-tseatid-actiontseat) | Accepts the invitation `token` is for, as the signed-in identity that is calling: in one save it makes that identity's seat in the invitation's tenant, places it in the invitation's unit as its primary placement, grants it the invitation's role there, and marks the invitation as accepted. |

#### `InvitationCommands(IStore, IInvitationStore<TInvitation, TInvitationId>, TenancyCatalogue, TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>, TenancyInvitationOptions<TInvitationId>, ICallerAccessor, TimeProvider)`

```csharp
public InvitationCommands(
    IStore store,
    IInvitationStore<TInvitation, TInvitationId> invitations,
    TenancyCatalogue catalogue,
    TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> options,
    TenancyInvitationOptions<TInvitationId> invitationOptions,
    ICallerAccessor callers,
    TimeProvider clock)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `store`: Where seats, roles and the rest are loaded, and where the unit of work is saved.
- `invitations`: Where invitations are loaded and kept, in that unit of work.
- `catalogue`: The permissions asked for, and which roles manage access.
- `options`: Which signed-in users may hold a seat: whom an acceptance seats.
- `invitationOptions`: How long an invitation stays open.
- `callers`: Who is calling, as the toolkit says: the signed-in identity that accepts.
- `clock`: What "now" is.

#### `IssueAsync(string, TUnitId, TRoleId, DateTimeOffset?, TimeSpan?, CancellationToken, TInvitationId?)`

```csharp
public Task<IssuedInvitation<TInvitationId>> IssueAsync(
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

**Returns**: The invitation's id, its token, which is shown this once, and when it ends.

**Exceptions**

- `RefusalException`: `tenancy.not-permitted` without [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) for the whole tenant or [`TenancyPermissions.GrantsManage`](catalogue.md#tenancypermissions) at the unit, `tenancy.invitation-lifetime`, `tenancy.tenant-inactive`, `tenancy.role-not-found`, `tenancy.grant-exceeds-own` for a role that manages access the caller could not give there for that long, while containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), `tenancy.unit-not-found`, `tenancy.unit-not-active`, `tenancy.role-not-active`, `tenancy.address-invalid`, `tenancy.invitation-grant-ends-first`.

#### `ListOpenAsync(CancellationToken)`

```csharp
public Task<IReadOnlyList<OpenInvitation<TInvitationId>>> ListOpenAsync(CancellationToken cancellationToken)
```

The tenant's invitations that can still be accepted, the soonest to end first: for a seat, those into the units where it holds [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions), which may be none; for system work in the tenant, all of them.

**Parameters**

- `cancellationToken`: Cancels the read.

#### `CancelAsync(TInvitationId, CancellationToken)`

```csharp
public Task CancelAsync(TInvitationId invitation, CancellationToken cancellationToken)
```

Cancels an open invitation, so its token no longer works. A seat needs [`TenancyPermissions.SeatsManage`](catalogue.md#tenancypermissions) at the invitation's unit, whoever issued it; an invitation into a unit where it does not manage seats is not found, as it is not among the ones it lists.

**Exceptions**

- `RefusalException`: `tenancy.invitation-not-found`, `tenancy.invitation-state` for one that was accepted or cancelled already.

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

The caller is the toolkit's own, read here and never handed in: a signed-in user with a verified identity, whose token role may hold a seat, and who did not sign in anonymously. It names no tenant and needs no seat: the token says which invitation, and so which tenant. Nothing is found by an address.

From there the work is the application's own, inside that tenant alone, for the seat that issued the invitation: that seat is who the placement and the grant keep as their giver, and the events name the system acting for it.

Accepting again as the same identity answers the same seat. Two acceptances at the same moment are kept apart by the invitation's own version and by the tenant's access revision: one is saved, and the other fails as any save that came second does, and is answered what the first made of the invitation when it is sent again.

**Parameters**

- `token`: The token, as [`IssueAsync`](#issueasyncstring-tunitid-troleid-datetimeoffset-timespan-cancellationtoken-tinvitationid) returned it.
- `verifiedAddress`: The address the application knows the calling identity to have, verified by its identity provider, or `null` when the application does not hold an invitation to its address. Given, it only narrows: an invitation for another address is refused. It never finds an invitation or a person.
- `cancellationToken`: Cancels the work.
- `seat`: The new seat's id, for imports and seeding; a new one otherwise.
- `configure`: Sets the fields the application added to its seat class, such as the name it is shown by, on the new seat once the invitation is found to hold, and the seat is placed and holds the role, and before the invitation is marked as accepted and the seat is handed to the store: so they are saved in the same transaction, and the class's own rules judge them there. When it throws, nothing is accepted: no seat is saved, by this call or by a later save in the same scope, and the invitation stays open. It is not run for an invitation the same identity accepted before, which answers the seat it made then.

**Returns**: The tenant, the slug it is selected by, and the seat the caller now has in it.

**Exceptions**

- `RefusalException`: `tenancy.identity-required` for a caller that is no signed-in identity that may hold a seat; `tenancy.invitation-not-found`, for a text that is no token too; `tenancy.invitation-cancelled`; `tenancy.invitation-used` when somebody else accepted it; `tenancy.invitation-lapsed` when its time ran out; `tenancy.address-mismatch`; `tenancy.tenant-inactive`; `tenancy.identity-has-seat`; `tenancy.unit-not-found` and `tenancy.unit-not-active`; `tenancy.role-not-found` and `tenancy.role-not-active`; `tenancy.invitation-unbacked` when its issuer may no longer give what it offers.
- `ConcurrencyConflictException`: Another change of access in the tenant, another acceptance included, was saved first.

### `TenancyUseCases.AcceptedInvitation`

```csharp
public sealed record AcceptedInvitation
```

What accepting an invitation gives the person who accepted.

| Member | Summary |
|---|---|
| [`AcceptedInvitation(TTenantId, string, TSeatId)`](#acceptedinvitationttenantid-string-tseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId Tenant { get; init; }` | The tenant they now have a seat in. |
| `string Slug { get; init; }` | The slug that tenant is selected by, for the next request. |
| `TSeatId Seat { get; init; }` | Their seat. |

#### `AcceptedInvitation(TTenantId, string, TSeatId)`

```csharp
public AcceptedInvitation(TTenantId Tenant, string Slug, TSeatId Seat)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Tenant`: The tenant they now have a seat in.
- `Slug`: The slug that tenant is selected by, for the next request.
- `Seat`: Their seat.

### `TenancyUseCases.InvitationOfToken<TInvitationId>`

```csharp
public sealed record InvitationOfToken<TInvitationId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
```

Which invitation a token is for, as the store answers it across tenants: ids, and nothing else of it.

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

### `TenancyUseCases.IssuedInvitation<TInvitationId>`

```csharp
public sealed record IssuedInvitation<TInvitationId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
```

An invitation as `IssueAsync` returns it: the one time its token is shown.

| Member | Summary |
|---|---|
| [`IssuedInvitation(TInvitationId, string, DateTimeOffset)`](#issuedinvitationtinvitationid-string-datetimeoffset) | The primary constructor: the type's summary says what it makes. |
| `TInvitationId Id { get; init; }` | The invitation. |
| `string Token { get; init; }` | The token whoever accepts needs. Send it to the person, in the fragment of a link or in a message's text, and keep it nowhere. |
| `DateTimeOffset ExpiresAt { get; init; }` | The first moment it can no longer be accepted. |
| [`ToString()`](#tostring) | Says which invitation and not its token: a record prints its members, and this one must not end up in a log. |

#### `IssuedInvitation(TInvitationId, string, DateTimeOffset)`

```csharp
public IssuedInvitation(TInvitationId Id, string Token, DateTimeOffset ExpiresAt)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Id`: The invitation.
- `Token`: The token whoever accepts needs. Send it to the person, in the fragment of a link or in a message's text, and keep it nowhere.
- `ExpiresAt`: The first moment it can no longer be accepted.

#### `ToString()`

```csharp
public override string ToString()
```

Says which invitation and not its token: a record prints its members, and this one must not end up in a log.

### `TenancyUseCases.OpenInvitation<TInvitationId>`

```csharp
public sealed record OpenInvitation<TInvitationId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
```

An invitation that can still be accepted, as the people who manage seats at its unit see it.

| Member | Summary |
|---|---|
| [`OpenInvitation(TInvitationId, string, TUnitId, TRoleId, DateTimeOffset?, DateTimeOffset, DateTimeOffset, TSeatId?, bool)`](#openinvitationtinvitationid-string-tunitid-troleid-datetimeoffset-datetimeoffset-datetimeoffset-tseatid-bool) | The primary constructor: the type's summary says what it makes. |
| `TInvitationId Id { get; init; }` | The invitation. |
| `string Address { get; init; }` | The address it is for. |
| `TUnitId UnitId { get; init; }` | The unit the seat would be placed in. |
| `TRoleId RoleId { get; init; }` | The role the seat would hold there. |
| `DateTimeOffset? GrantUntil { get; init; }` | When that grant would end, or `null` for no end. |
| `DateTimeOffset IssuedAt { get; init; }` | When it was issued. |
| `DateTimeOffset ExpiresAt { get; init; }` | The first moment it can no longer be accepted. |
| `TSeatId? IssuedBy { get; init; }` | The seat that issued it, or that system work issued it for; `null` for none. |
| `bool IssuedAsSystem { get; init; }` | Whether system work issued it. |

#### `OpenInvitation(TInvitationId, string, TUnitId, TRoleId, DateTimeOffset?, DateTimeOffset, DateTimeOffset, TSeatId?, bool)`

```csharp
public OpenInvitation(
    TInvitationId Id,
    string Address,
    TUnitId UnitId,
    TRoleId RoleId,
    DateTimeOffset? GrantUntil,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    TSeatId? IssuedBy,
    bool IssuedAsSystem)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Id`: The invitation.
- `Address`: The address it is for.
- `UnitId`: The unit the seat would be placed in.
- `RoleId`: The role the seat would hold there.
- `GrantUntil`: When that grant would end, or `null` for no end.
- `IssuedAt`: When it was issued.
- `ExpiresAt`: The first moment it can no longer be accepted.
- `IssuedBy`: The seat that issued it, or that system work issued it for; `null` for none.
- `IssuedAsSystem`: Whether system work issued it.

## Directory

Namespace `DDDToolkit.Supporting.Tenancy.UseCases`.

### `TenancyUseCases.TenancyDirectory`

```csharp
public sealed class TenancyDirectory
```

What the screens of an application show about the tenant: who the caller is and what they may do where, the same of any seat of the tenant, the tenant's seats, roles and units for pickers, and what the seats, roles and units with some ids are. It reads, and changes nothing.

This is where a role's name and a unit's name and path come from. The rows the access questions read ([`ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>`](questions.md#itenancyreadsourcettenantid-tseatid-tunitid-troleid)) carry no text that is shown to people, so a module that reads them next to its own tables answers ids, and whoever shows them asks here what they are: [`SeatsByIdAsync`](#seatsbyidasyncireadonlycollectiontseatid-cancellationtoken), [`RolesByIdAsync`](#rolesbyidasyncireadonlycollectiontroleid-cancellationtoken) and [`UnitsByIdAsync`](#unitsbyidasyncireadonlycollectiontunitid-cancellationtoken).

Seats and units are answered as the application's own classes, whole: the seat with its identity, its placements and their grants and every field the application added, and the unit with every field of its own, beside the path and depth that are not on it ([`TenancyUseCases.UnitInTree`](#tenancyusecasesunitintree)). A seat has no name in Tenancy, and a unit no kind: what a screen shows of them is the application's to select, with no statement more. Each is read for the answer and tracked by nobody, so nothing done to one is saved, by the question or by a save later in the same unit of work.

Whoever works in a tenant reads it: a seat of it, or system work in it. No permission is asked, for a list, for a question by id or for a seat's overview, and a question by id answers any seat, role or unit of the caller's tenant; an id of another tenant, or of nothing, is left out of the answer without a word, so the answer never says which. A question by id takes at most [`MostIds`](#tenancyusecasestenancydirectory) ids. Who may ask is the application's to say, on the request that asks: what another seat holds where, above all, is a choice of the application's.

The seats are those the caller reads: every seat of the tenant by default. On Postgres a read rule of the application's on its seat class may narrow that, and the lists and the questions by id of seats then answer the seats the rule lets the caller read, leaving the others out as they leave out an id of another tenant; an overview of a seat the rule leaves out is refused as one of another tenant is.

Paths are written from the root down, joined with `" / "`, and come from the closure of the tree, so a unit's path names the units above it even where the caller is not placed under those.

| Member | Summary |
|---|---|
| `MostIds = 200` | How many ids one question by id takes. |
| [`TenancyDirectory(IStore, TenancyCatalogue, TimeProvider)`](#tenancydirectoryistore-tenancycatalogue-timeprovider) | The primary constructor: the type's summary says what it makes. |
| `TenancyCatalogue Catalogue { get; }` | Every permission and role pack of the application. |
| [`WhoAmIAsync(CancellationToken)`](#whoamiasynccancellationtoken) | Who the calling seat is, and what it may do where: its own seat, whole, as the application's class, with the tenant, the paths of the units it is placed at, the roles its grants name, and every live permission it holds now with where it is granted and every unit it reaches ([`TenancyUseCases.SeatOverview`](#tenancyusecasesseatoverview)). |
| [`SeatOverviewAsync(TSeatId, CancellationToken)`](#seatoverviewasynctseatid-cancellationtoken) | Any seat of the caller's tenant and what it may do where, as [`WhoAmIAsync`](#whoamiasynccancellationtoken) answers the caller's own: the seat, whole, as the application's class, with the tenant, the paths of the units it is placed at, the roles its grants name, and every permission it holds now with where it is granted and every unit it reaches ([`TenancyUseCases.SeatOverview`](#tenancyusecasesseatoverview)). |
| [`ListSeatsAsync(CancellationToken)`](#listseatsasynccancellationtoken) | The tenant's seats, the application's own class, each whole and in the order of their ids: a seat has nothing of Tenancy's a person would order it by, so a screen orders what it shows by what it shows. |
| [`SeatsByIdAsync(IReadOnlyCollection<TSeatId>, CancellationToken)`](#seatsbyidasyncireadonlycollectiontseatid-cancellationtoken) | The seats among `ids` that are in the caller's tenant, as [`ListSeatsAsync`](#listseatsasynccancellationtoken) answers them: the application's own, whole, in the order of their ids. |
| [`ListRolesAsync(CancellationToken)`](#listrolesasynccancellationtoken) | The tenant's roles, the active ones first, each by name. |
| [`RolesByIdAsync(IReadOnlyCollection<TRoleId>, CancellationToken)`](#rolesbyidasyncireadonlycollectiontroleid-cancellationtoken) | The roles among `ids` that are the caller's tenant's, the active ones first, each by name, with whether it manages access. |
| [`ListUnitsAsync(CancellationToken)`](#listunitsasynccancellationtoken) | The units the caller reads, by path: for a seat, the units it is placed in and every unit below them; for system work in the tenant, every unit. |
| [`UnitsByIdAsync(IReadOnlyCollection<TUnitId>, CancellationToken)`](#unitsbyidasyncireadonlycollectiontunitid-cancellationtoken) | What the units among `ids` that are the caller's tenant's are called, by path, whichever of them the caller is placed under: a seat that works on something at a unit it is not placed under still reads what that unit is called, and so does one an application's restrictive rule keeps out of the organization. |

#### `TenancyDirectory(IStore, TenancyCatalogue, TimeProvider)`

```csharp
public TenancyDirectory(IStore store, TenancyCatalogue catalogue, TimeProvider clock)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `store`: Where it reads.
- `catalogue`: The catalogue, for the screens that assign roles.
- `clock`: What "now" is, for whether a grant applies.

#### `WhoAmIAsync(CancellationToken)`

```csharp
public Task<SeatOverview> WhoAmIAsync(CancellationToken cancellationToken)
```

Who the calling seat is, and what it may do where: its own seat, whole, as the application's class, with the tenant, the paths of the units it is placed at, the roles its grants name, and every live permission it holds now with where it is granted and every unit it reaches ([`TenancyUseCases.SeatOverview`](#tenancyusecasesseatoverview)). A seat asks this about itself; any seat of the tenant, its own included, is [`SeatOverviewAsync`](#seatoverviewasynctseatid-cancellationtoken).

**Exceptions**

- `RefusalException`: `tenancy.not-seated` for every caller that is not a seat: nobody, whatever it was refused for, and system work, which has no self to show.

**Remarks**

The seat is read as the lists read seats, untracked, and not loaded as a command loads one: nothing done to it is saved, by this question or by a save later in the same unit of work.

#### `SeatOverviewAsync(TSeatId, CancellationToken)`

```csharp
public Task<SeatOverview> SeatOverviewAsync(TSeatId seat, CancellationToken cancellationToken)
```

Any seat of the caller's tenant and what it may do where, as [`WhoAmIAsync`](#whoamiasynccancellationtoken) answers the caller's own: the seat, whole, as the application's class, with the tenant, the paths of the units it is placed at, the roles its grants name, and every permission it holds now with where it is granted and every unit it reaches ([`TenancyUseCases.SeatOverview`](#tenancyusecasesseatoverview)). Asked about the caller's own seat, it answers what [`WhoAmIAsync`](#whoamiasynccancellationtoken) answers.

**Parameters**

- `seat`: The seat asked about.
- `cancellationToken`: Cancels the read.

**Exceptions**

- `RefusalException`: The caller is nobody, with its own code; or `tenancy.seat-not-found` for a seat the caller does not read: one of another tenant, one that does not exist, or one a read rule of the application's leaves out, which the answer does not tell apart.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Remarks**

It asks nothing of the caller beyond what every question of the directory asks, a seat of the tenant or system work in it. Who may see another person's access is the application's choice, so the request that asks this states it (`IRequireAccess.RequiredAccess`), as a request states it for everything else: `TenancyAccess.AtTenant(TenancyPermissions.SeatsManage)` for a tenant's administration, say, or `TenancyAccess.InTenant()` where the database's read rules are the rule.

What comes back is what the caller reads, through the store, as the lists read seats. On Postgres Tenancy's policies give a seat another seat's grants only where it may read them: at the units where it manages grants, seats or units and below them, or all of them when it manages roles for the whole tenant; and a read rule of the application's on its seat class decides which seats it reads at all. The permissions are worked out from the grants that were read, by the rule the rights are written by, since a database that keeps the rights shows a seat its own alone: a permission is shown where the caller reads the grant that gives it, and a seat that is not active holds none. Where no policy applies, on another database or in a context without row level security, every seat comes with all its grants and permissions, and the request's requirement is the only rule: choose it as the rule you mean.

The seat is read untracked, as the lists read seats: nothing done to it is saved, by this question or by a save later in the same unit of work.

#### `ListSeatsAsync(CancellationToken)`

```csharp
public Task<IReadOnlyList<TSeat>> ListSeatsAsync(CancellationToken cancellationToken)
```

The tenant's seats, the application's own class, each whole and in the order of their ids: a seat has nothing of Tenancy's a person would order it by, so a screen orders what it shows by what it shows. The application selects what it shows, a name its seat class keeps, or a profile of its own found by the seat's identity, from the seats read here, with no statement more.

**Parameters**

- `cancellationToken`: Cancels the read.

**Exceptions**

- `RefusalException`: The caller is nobody.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Remarks**

One statement, which reads every seat with its identity, its placements and their grants, and every field the application added; tracked by nobody, so nothing done to a seat is saved. The identity is the application's to keep: what leaves is what it selects.

No permission is asked, and only a database's own read rules narrow what of another seat is read: on Postgres, Tenancy's policies answer another seat's grants only where the caller may read them, and elsewhere, or in a context without row level security, every seat comes with all its placements and grants. Where another seat's grants are shown, the request that asks says who may see them, as a request that answers from [`SeatOverviewAsync`](#seatoverviewasynctseatid-cancellationtoken) does; select a list's seats into what every member may see.

#### `SeatsByIdAsync(IReadOnlyCollection<TSeatId>, CancellationToken)`

```csharp
public Task<IReadOnlyList<TSeat>> SeatsByIdAsync(
    IReadOnlyCollection<TSeatId> ids,
    CancellationToken cancellationToken)
```

The seats among `ids` that are in the caller's tenant, as [`ListSeatsAsync`](#listseatsasynccancellationtoken) answers them: the application's own, whole, in the order of their ids. An id of another tenant, or of no seat, is left out without a word: the answer never says which.

**Parameters**

- `ids`: The seats asked about, at most [`MostIds`](#tenancyusecasestenancydirectory) different ones.
- `cancellationToken`: Cancels the read.

**Exceptions**

- `RefusalException`: The caller is nobody, with its own code; or `tenancy.too-many-ids`.
- `InvalidOperationException`: The caller is system work outside any tenant.

**Remarks**

Each seat comes with its placements and grants as [`ListSeatsAsync`](#listseatsasynccancellationtoken) reads them, narrowed by the database's own read rules alone: select what the caller is shown.

#### `ListRolesAsync(CancellationToken)`

```csharp
public Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken cancellationToken)
```

The tenant's roles, the active ones first, each by name.

**Exceptions**

- `RefusalException`: The caller is nobody.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `RolesByIdAsync(IReadOnlyCollection<TRoleId>, CancellationToken)`

```csharp
public Task<IReadOnlyList<RoleSummary>> RolesByIdAsync(
    IReadOnlyCollection<TRoleId> ids,
    CancellationToken cancellationToken)
```

The roles among `ids` that are the caller's tenant's, the active ones first, each by name, with whether it manages access. An id of another tenant, or of no role, is left out without a word.

**Parameters**

- `ids`: The roles asked about, at most [`MostIds`](#tenancyusecasestenancydirectory) different ones.
- `cancellationToken`: Cancels the read.

**Exceptions**

- `RefusalException`: The caller is nobody, with its own code; or `tenancy.too-many-ids`.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `ListUnitsAsync(CancellationToken)`

```csharp
public Task<IReadOnlyList<UnitInTree>> ListUnitsAsync(CancellationToken cancellationToken)
```

The units the caller reads, by path: for a seat, the units it is placed in and every unit below them; for system work in the tenant, every unit. Each is the application's own unit, whole, with its path and depth beside it ([`TenancyUseCases.UnitInTree`](#tenancyusecasesunitintree)), so a field the application added to its unit class, such as what kind of unit it is, is shown from the units read here, with no statement more.

**Parameters**

- `cancellationToken`: Cancels the read.

**Exceptions**

- `RefusalException`: The caller is nobody.
- `InvalidOperationException`: The caller is system work outside any tenant.

#### `UnitsByIdAsync(IReadOnlyCollection<TUnitId>, CancellationToken)`

```csharp
public Task<IReadOnlyList<NamedUnit<TUnitId>>> UnitsByIdAsync(
    IReadOnlyCollection<TUnitId> ids,
    CancellationToken cancellationToken)
```

What the units among `ids` that are the caller's tenant's are called, by path, whichever of them the caller is placed under: a seat that works on something at a unit it is not placed under still reads what that unit is called, and so does one an application's restrictive rule keeps out of the organization. So the answer is each unit's name, path and depth, and nothing more of it: on a database that keeps its own policies it comes from Tenancy's function that runs as its owner and answers the ids asked alone. An id of another tenant, or of no unit, is left out without a word.

**Parameters**

- `ids`: The units asked about, at most [`MostIds`](#tenancyusecasestenancydirectory) different ones.
- `cancellationToken`: Cancels the read.

**Exceptions**

- `RefusalException`: The caller is nobody, with its own code; or `tenancy.too-many-ids`.
- `InvalidOperationException`: The caller is system work outside any tenant.

### `TenancyUseCases.PermissionReach`

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

### `TenancyUseCases.RoleSummary`

```csharp
public sealed record RoleSummary
```

A role, and whether it manages access: whether the catalogue marks one of its live permissions as managing access, which an archived role never does. While containment is on ([`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions)), such a role is given only by a seat that holds those permissions, rather than by whoever holds `tenancy.grants.manage` where the seat is placed; once the application turns it off, it is given as any other role, and is still marked here.

| Member | Summary |
|---|---|
| [`RoleSummary(TRoleId, string, string?, RoleStatus, IReadOnlyList<string>, bool)`](#rolesummarytroleid-string-string-rolestatus-ireadonlyliststring-bool) | The primary constructor: the type's summary says what it makes. |
| `TRoleId Id { get; init; }` | The role. |
| `string Name { get; init; }` | Its name in the tenant. |
| `string? FromPack { get; init; }` | The permission of the catalogue's pack it was made from, or `null` for a role made by hand. |
| `RoleStatus Status { get; init; }` | Whether it is active, or archived and granting nothing. |
| `IReadOnlyList<string> Permissions { get; init; }` | The permissions it grants. |
| `bool ManagesAccess { get; init; }` | Whether the catalogue marks one of its live permissions as managing access; never for an archived role. |

#### `RoleSummary(TRoleId, string, string?, RoleStatus, IReadOnlyList<string>, bool)`

```csharp
public RoleSummary(
    TRoleId Id,
    string Name,
    string? FromPack,
    RoleStatus Status,
    IReadOnlyList<string> Permissions,
    bool ManagesAccess)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Id`: The role.
- `Name`: Its name in the tenant.
- `FromPack`: The permission of the catalogue's pack it was made from, or `null` for a role made by hand.
- `Status`: Whether it is active, or archived and granting nothing.
- `Permissions`: The permissions it grants.
- `ManagesAccess`: Whether the catalogue marks one of its live permissions as managing access; never for an archived role.

### `TenancyUseCases.SeatOverview`

```csharp
public sealed record SeatOverview
```

A seat and what it may do where: the application's own seat, whole, with only what is not on the seat itself beside it. The seat carries its id, its identity, its status, where it is placed with the roles granted there for which period, and every field the application added; Tenancy adds the tenant it is in, the path of each unit it is placed at, the roles its grants name, and every permission it holds at [`AsOf`](#tenancyusecasesseatoverview) with where that permission reaches. [`TenancyUseCases.TenancyDirectory.WhoAmIAsync`](#whoamiasynccancellationtoken) answers it for the calling seat, and [`TenancyUseCases.TenancyDirectory.SeatOverviewAsync`](#seatoverviewasynctseatid-cancellationtoken) for any seat of the tenant.

Nothing of it is the seat a second time, so an application shows the seat by what it chose, a name its seat class keeps or a profile found by the identity, from the seat itself, and puts its placements beside the paths and role names with [`UnitOf`](#unitoftunitid) and [`RoleOf`](#roleoftroleid). What leaves is what the application selects: the seat has its identity.

The seat was read for the overview and is tracked by nobody: nothing done to it is saved, by this question or by a save later in the same unit of work. Nothing in the record is the caller's own but the question that made it, so it shows any seat the same way. What it holds of a seat is what the caller reads: where a database's policies give the caller only some of another seat's grants, as Tenancy's do on Postgres, the seat comes with those, and the roles and permissions are those that they name and give.

| Member | Summary |
|---|---|
| [`SeatOverview(TenantSummary, TSeat, IReadOnlyList<UnitRef>, IReadOnlyList<RoleSummary>, IReadOnlyList<PermissionReach>, DateTimeOffset)`](#seatoverviewtenantsummary-tseat-ireadonlylistunitref-ireadonlylistrolesummary-ireadonlylistpermissionreach-datetimeoffset) | The primary constructor: the type's summary says what it makes. |
| `TenantSummary Tenant { get; init; }` | The tenant the seat is in. |
| `TSeat Seat { get; init; }` | The seat, the application's own class, with its placements and the grants the caller reads. |
| `IReadOnlyList<UnitRef> Units { get; init; }` | Each unit the seat is placed at, once, by its path from the root. |
| `IReadOnlyList<RoleSummary> Roles { get; init; }` | Each role the seat's grants name, once, by name, whether or not the grant applies now; a role the store does not answer, one a filter of the application's own hides, is left out rather than made up. |
| `IReadOnlyList<PermissionReach> Permissions { get; init; }` | Every permission the seat holds at `AsOf` from those grants, where it is granted and every unit it reaches: none while the seat is not active. |
| `DateTimeOffset AsOf { get; init; }` | The moment the overview holds for: the permissions are those held then, and a grant of the seat applies then when `grant.AppliesAt(AsOf)`. |
| [`UnitOf(TUnitId)`](#unitoftunitid) | A unit the seat is placed at, by its path: what [`Units`](#tenancyusecasesseatoverview) holds for it. |
| [`RoleOf(TRoleId)`](#roleoftroleid) | A role the seat's grants name: what [`Roles`](#tenancyusecasesseatoverview) holds for it. |

#### `SeatOverview(TenantSummary, TSeat, IReadOnlyList<UnitRef>, IReadOnlyList<RoleSummary>, IReadOnlyList<PermissionReach>, DateTimeOffset)`

```csharp
public SeatOverview(
    TenantSummary Tenant,
    TSeat Seat,
    IReadOnlyList<UnitRef> Units,
    IReadOnlyList<RoleSummary> Roles,
    IReadOnlyList<PermissionReach> Permissions,
    DateTimeOffset AsOf)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Tenant`: The tenant the seat is in.
- `Seat`: The seat, the application's own class, with its placements and the grants the caller reads.
- `Units`: Each unit the seat is placed at, once, by its path from the root.
- `Roles`: Each role the seat's grants name, once, by name, whether or not the grant applies now; a role the store does not answer, one a filter of the application's own hides, is left out rather than made up.
- `Permissions`: Every permission the seat holds at `AsOf` from those grants, where it is granted and every unit it reaches: none while the seat is not active.
- `AsOf`: The moment the overview holds for: the permissions are those held then, and a grant of the seat applies then when `grant.AppliesAt(AsOf)`.

#### `UnitOf(TUnitId)`

```csharp
public UnitRef UnitOf(TUnitId unit)
```

A unit the seat is placed at, by its path: what [`Units`](#tenancyusecasesseatoverview) holds for it.

**Parameters**

- `unit`: The unit of one of the seat's placements.

**Exceptions**

- `KeyNotFoundException`: The seat is placed at no such unit.

#### `RoleOf(TRoleId)`

```csharp
public RoleSummary? RoleOf(TRoleId role)
```

A role the seat's grants name: what [`Roles`](#tenancyusecasesseatoverview) holds for it.

**Parameters**

- `role`: The role of one of the seat's grants.

**Returns**: The role; or `null` when a grant of the seat names it and the store answered no such role, as a filter of the application's own on its role class does, a soft delete say. The grant is the seat's own all the same, so a screen shows it with what the application chooses for a role it cannot name.

**Exceptions**

- `KeyNotFoundException`: No grant of the seat names such a role.

### `TenancyUseCases.TenantSummary`

```csharp
public sealed record TenantSummary
```

A tenant.

| Member | Summary |
|---|---|
| [`TenantSummary(TTenantId, string, string, TenantShape, TenantStatus)`](#tenantsummaryttenantid-string-string-tenantshape-tenantstatus) | The primary constructor: the type's summary says what it makes. |
| `TTenantId Id { get; init; }` | The tenant. |
| `string Slug { get; init; }` | The slug it is known by. |
| `string Name { get; init; }` | Its name: its organization's. |
| `TenantShape Shape { get; init; }` | Its shape: its root alone, or a tree of units below it. |
| `TenantStatus Status { get; init; }` | Where it is in its life: being set up, active, suspended or closed. |

#### `TenantSummary(TTenantId, string, string, TenantShape, TenantStatus)`

```csharp
public TenantSummary(TTenantId Id, string Slug, string Name, TenantShape Shape, TenantStatus Status)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Id`: The tenant.
- `Slug`: The slug it is known by.
- `Name`: Its name: its organization's.
- `Shape`: Its shape: its root alone, or a tree of units below it.
- `Status`: Where it is in its life: being set up, active, suspended or closed.

### `TenancyUseCases.UnitInTree`

```csharp
public sealed record UnitInTree
```

A unit, the application's own and whole, with where it sits in the tree: its path from the root and how deep it is. The unit carries its id, its parent, its name, its status and every field the application added, such as what kind of unit it is; the path and the depth are not on it. They come from the closure and the units above it, which the answer does not carry, as a seat lists only the units under its placements, so the application would need a read more to work them out. So that is all this adds, and an application shows a unit by what it selects ([`TenancyUseCases.TenancyDirectory.ListUnitsAsync`](#listunitsasynccancellationtoken), [`TenancyUseCases.TenancyDirectory.UnitsByIdAsync`](#unitsbyidasyncireadonlycollectiontunitid-cancellationtoken)).

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

### `TenancyUseCases.UnitRef`

```csharp
public sealed record UnitRef
```

A unit, with its path from the root, such as `Harbor Works / North / North Coast`.

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

## Registration

What a host calls, from all three packages: the storage-agnostic core, Entity Framework's tables and services, the checks of requests, and Postgres.

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
| `ApplicationCatalogue? Catalogue { get; set; }` | What the application adds to the catalogue: role packs, permissions of its own and marks on permissions that manage access. Left unset, it adds none: the permissions are Tenancy's and what the modules contribute, and every tenant starts with the default administrators' role ([`TenancyPacks.DefaultAdministrators`](catalogue.md#defaultadministrators)). |
| `TenantSelectionOptions TenantSelection { get; }` | Which signed-in users may hold a seat, by the role their token carries: those with `authenticated` unless the application lists others in [`TenantSelectionOptions.SeatedTokenRoles`](callers.md#tenantselectionoptions). |
| [`OperatorTokenRoles`](#operatortokenroles) | The token roles whose users are operators of the application: staff who look across tenants, by the role their token carries (`Caller.Role`, as the host's token role map spells it). |
| [`IsOperator(Caller?)`](#isoperatorcaller) | Whether `caller` is one of the application's operators: a signed-in user whose token carries one of [`OperatorTokenRoles`](#operatortokenroles). |

#### `OperatorTokenRoles`

```csharp
public ISet<string> OperatorTokenRoles { get; }
```

The token roles whose users are operators of the application: staff who look across tenants, by the role their token carries (`Caller.Role`, as the host's token role map spells it). Empty by default: an application without operators has none.

```csharp
options.OperatorTokenRoles.Add("operator");
```

An operator holds no seat. Its token role is never one of [`TenantSelectionOptions.SeatedTokenRoles`](callers.md#tenantselectionoptions), so tenant selection answers nobody for it whatever tenant the request names, and registration refuses a role that is on both lists. An operator reads: the tenants, through `TenantDirectory`, and on Postgres whatever the policies of its own database role let it read. It changes nothing itself: what it asks for is carried out by system work that names it ([`TenancyWork.BeginOperator<TTenantId, TSeatId>`](callers.md#beginoperatorguid) and [`TenancyWork.BeginOperatorIn<TTenantId, TSeatId>`](callers.md#beginoperatorinttenantid-guid-string)).

#### `IsOperator(Caller?)`

```csharp
public bool IsOperator(Caller? caller)
```

Whether `caller` is one of the application's operators: a signed-in user whose token carries one of [`OperatorTokenRoles`](#operatortokenroles). The one place that says so: the tenants' directory and the check of [`TenancyRequirement.Operator`](callers.md#tenancyrequirementoperator) both ask here, so they mean the same caller by it. System work is never one, whoever it is recorded as, and neither is anyone without an identity.

**Parameters**

- `caller`: Who is calling, as the toolkit says: the caller of the request, not the Tenancy caller.

### `TenancyServiceCollectionExtensions`

```csharp
public static class TenancyServiceCollectionExtensions
```

Namespace `DDDToolkit.Supporting.Tenancy`.

Registration of Tenancy's storage-agnostic parts. The storage package registers the rest, the store and the seat directory, and calls [`AddTenancyCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`](#addtenancycoreiservicecollection-actiontenancyoptionsttenantid-tseatid-tunitid-troleid) itself; an application calls the storage package's registration, and adds its modules' permissions once, from the host: `services.AddTenancyPermissionsOfModules()`, which Tenancy's generator writes from the lists the modules mark with `TenancyPermissionsAttribute`. [`AddTenancyPermissions`](#addtenancypermissionsiservicecollection-ienumerablepermission) adds a list by hand.

| Member | Summary |
|---|---|
| [`AddTenancyCore(IServiceCollection, Action<TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>>?)`](#addtenancycoreiservicecollection-actiontenancyoptionsttenantid-tseatid-tunitid-troleid) | Registers the catalogue, the access answers, tenant selection and the use cases, everything except the store and the seat directory. |
| [`AddTenancyInvitationsCore(IServiceCollection, Action<TenancyInvitationOptions<TInvitationId>>?)`](#addtenancyinvitationscoreiservicecollection-actiontenancyinvitationoptionstinvitationid) | Registers the invitation use cases, closed over the application's invitation class, for an application that invites people into a tenant: everything of invitations except their store, which the storage package registers; that package calls this itself. |
| [`AddTenancyPermissions(IServiceCollection, IEnumerable<Permission>)`](#addtenancypermissionsiservicecollection-ienumerablepermission) | Adds permissions to the catalogue by hand. |
| [`SyncRolePacks(IServiceCollection)`](#syncrolepacksiservicecollection) | Brings every tenant's roles up to the packs they were made from, in the background, once the host has started: after its start-up checks, when it runs them, and after every hosted service has started. |

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

Registers the catalogue, the access answers, tenant selection and the use cases, everything except the store and the seat directory.

- The catalogue is built once, from [`TenancyOptions.Catalogue`](#tenancyoptionsttenantid-tseatid-tunitid-troleid), or from none when it is not set, and every [`PermissionContribution`](catalogue.md#permissioncontribution), the first time it is asked for; resolve it at start-up to have a catalogue that does not hold together stop the application there.
- The answers are one for the application; selection and the use cases are made per scope. Selection is registered without its id types as well, [`ITenantSelection`](callers.md#itenantselection), the same instance, unless a host registered its own before: that one stays.
- The toolkit's caller accessor a host registered before stays; otherwise the ambient caller is read.
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

Registers the invitation use cases, closed over the application's invitation class, for an application that invites people into a tenant: everything of invitations except their store, which the storage package registers; that package calls this itself. Call it after [`AddTenancyCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`](#addtenancycoreiservicecollection-actiontenancyoptionsttenantid-tseatid-tunitid-troleid), which registers what the use cases share: the catalogue, the options, the clock and who is calling.

**Parameters**

- `services`: The service collection.
- `configure`: Changes how long an invitation stays open, which has defaults; leave it out to keep them.

**Exceptions**

- `InvalidOperationException`: Tenancy itself is not registered yet; or the lifetimes are out of order.

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

#### `SyncRolePacks(IServiceCollection)`

```csharp
public static IServiceCollection SyncRolePacks(this IServiceCollection services)
```

Brings every tenant's roles up to the packs they were made from, in the background, once the host has started: after its start-up checks, when it runs them, and after every hosted service has started. A role made from a pack gets the permissions the pack gained since the role was made or last followed it, a module's new permissions in an administrators' pack that lists none among them, and loses the permissions the pack lost; what the tenant changed itself stays. Each role that changes raises [`RoleFollowedItsPack<TTenantId, TRoleId, TSeatId>`](aggregates.md#rolefolloweditspackttenantid-troleid-tseatid), for the host to tell the tenant's administrators; the package sends no message itself.

```csharp
builder.Services.RunStartupChecks();
builder.Services.SyncRolePacks();
```

It runs [`IRolePackSync`](catalogue.md#irolepacksync), which Tenancy's storage package registers with `AddTenancy`, once per start of the host, as Tenancy's system work in each tenant, one tenant after the other. A second run changes nothing, and so does a run on another instance of the host at the same time as this one: whichever commits second finds the roles following their packs already. A tenant it cannot sync is logged and left for the next start, and the host keeps serving meanwhile. A host that wants it elsewhere, in a deployment step or behind an operator's endpoint, leaves this call out and runs [`IRolePackSync`](catalogue.md#irolepacksync) itself.

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
| [`AddTenancy(IServiceCollection, Action<TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>>?)`](#addtenancyiservicecollection-actiontenancyoptionsttenantid-tseatid-tunitid-troleid) | Registers Tenancy over `TContext`, the context whose model calls `AddTenancy`: everything [`TenancyServiceCollectionExtensions.AddTenancyCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`](#addtenancycoreiservicecollection-actiontenancyoptionsttenantid-tseatid-tunitid-troleid) registers, the store the use cases load and save through, the seat directory tenant selection asks, the save interceptor, brought to every context `UseDDDToolkit` wires as Tenancy's part ([`TenancySaveInterceptor.PartName`](entity-framework.md#tenancysaveinterceptor)), the query executor, and what an event log asks: who acted, as Tenancy knows the caller ([`TenancyActedByAccessor`](entity-framework.md#tenancyactedbyaccessor), put around the accessor registered before this call, or around the toolkit's default), and the tenant of each row of the log `AddTenancyEventLogTable` maps. |
| [`AddTenancyInvitations(IServiceCollection, Action<TenancyInvitationOptions<TInvitationId>>?)`](#addtenancyinvitationsiservicecollection-actiontenancyinvitationoptionstinvitationid) | Registers invitations over `TContext`, the context whose model calls `AddTenancyInvitations`, for an application that invites people into a tenant: the invitation use cases (`InvitationCommands`) and the store they load and keep invitations through. |
| [`AddTenancyAccess(IServiceCollection)`](#addtenancyaccessiservicecollection) | Adds Tenancy's access check to the checks the requests of one module are held to (`AccessChecks<TRequests>`): what decides the cases of [`TenancyRequirement`](callers.md#tenancyrequirement) a request of that module declares, before its handler runs. |

#### `AddTenancy(IServiceCollection, Action<TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>>?)`

```csharp
public static IServiceCollection AddTenancy<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TContext>(
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
    where TContext : DbContext
```

Registers Tenancy over `TContext`, the context whose model calls `AddTenancy`: everything [`TenancyServiceCollectionExtensions.AddTenancyCore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`](#addtenancycoreiservicecollection-actiontenancyoptionsttenantid-tseatid-tunitid-troleid) registers, the store the use cases load and save through, the seat directory tenant selection asks, the save interceptor, brought to every context `UseDDDToolkit` wires as Tenancy's part ([`TenancySaveInterceptor.PartName`](entity-framework.md#tenancysaveinterceptor)), the query executor, and what an event log asks: who acted, as Tenancy knows the caller ([`TenancyActedByAccessor`](entity-framework.md#tenancyactedbyaccessor), put around the accessor registered before this call, or around the toolkit's default), and the tenant of each row of the log `AddTenancyEventLogTable` maps.

```csharp
services.AddTenancy<TenancyContext>(options =>
    options.Catalogue = ShopCatalogue.Application);   // optional: packs, permissions of its own, marks
services.AddDbContext<TenancyContext>((serviceProvider, options) => options
    .UseNpgsql(connectionString)
    .UseDDDToolkit(serviceProvider));
```

Every option has a default, so `services.AddTenancy<TenancyContext>()` is enough. A new id is made by the id itself, `TSeatId.Create()`, in code and before the save: the generator writes it for an id over a `Guid`, and an id over anything else declares its own. The store and the directory are scoped, like the context they use. The store offers a save that fails to every registered [`ITenancySaveFailures`](entity-framework.md#itenancysavefailures), which a package for one database adds, and leaves to the database what [`TenancyStoreOptions`](entity-framework.md#tenancystoreoptions) says, which such a package sets.

`AddTenancy<TenancyContext>` is generated into the project that declares the classes, closed over them (`TemplateRegistrationAttribute`), or into a project of the same `[assembly: Module]` that declares none, such as the module's infrastructure project next to its domain project. It calls this method with the classes and ids filled in; the context is still the caller's to name. This method can be called as well, with all ten.

It brings the start-up checks of [`TenancyChecks`](entity-framework.md#tenancychecks), which a host runs with `services.RunStartupChecks()`: the catalogue builds, every context that keeps rows to a tenant checks its saves, and a permission a role holds that the catalogue has lost is logged.

And it registers [`IRolePackSync`](catalogue.md#irolepacksync) over the context, which brings every tenant's roles up to the packs they were made from: `services.SyncRolePacks()` runs it once the host has started, and a host may run it itself.

**Parameters**

- `services`: The service collection.
- `configure`: Changes Tenancy's options, every one of which has a default; leave it out to keep them all.

**Exceptions**

- `ArgumentNullException`: `services` is null.
- `InvalidOperationException`: A token role is an operator's and seated as well.

#### `AddTenancyInvitations(IServiceCollection, Action<TenancyInvitationOptions<TInvitationId>>?)`

```csharp
public static IServiceCollection AddTenancyInvitations<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TInvitation, TInvitationId, TContext>(
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
    where TContext : DbContext
```

Registers invitations over `TContext`, the context whose model calls `AddTenancyInvitations`, for an application that invites people into a tenant: the invitation use cases (`InvitationCommands`) and the store they load and keep invitations through. Call it after `AddTenancy`, with the application's invitation class, its id and the context:

```csharp
services.AddTenancy<TenancyContext>();
services.AddTenancyInvitations<ShopInvitation, InvitationId, TenancyContext>();
```

That call is generated into the project that declares Tenancy's classes, closed over them, as `AddTenancy<TenancyContext>` is (`TemplateRegistrationAttribute`). The invitation class and its id are named in the call, since an application may have none. This method can be called as well, with all twelve. A new invitation's id is made by the id itself, `InvitationId.Create()`.

The store is scoped, and works in the unit of work of Tenancy's own store: an invitation is saved, and its acceptance with the seat it makes, by that store's one save.

**Parameters**

- `services`: The service collection.
- `configure`: Changes how long an invitation stays open, which has defaults; leave it out to keep them.

**Exceptions**

- `ArgumentNullException`: `services` is null.
- `InvalidOperationException`: Tenancy itself is not registered yet; or the lifetimes are out of order.

#### `AddTenancyAccess(IServiceCollection)`

```csharp
public static IServiceCollection AddTenancyAccess<TTenantId, TSeatId, TUnitId, TRoleId, TRequests, TContext>(this IServiceCollection services)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TRequests : class, IRequireAccess
    where TContext : DbContext
```

Adds Tenancy's access check to the checks the requests of one module are held to (`AccessChecks<TRequests>`): what decides the cases of [`TenancyRequirement`](callers.md#tenancyrequirement) a request of that module declares, before its handler runs.

```csharp
services.AddTenancyAccess<IBillingRequest, BillingContext>();
```

A module calls it for its own request interface and its own context, once, next to the checks for the cases only it has. The check asks a permission for the whole tenant over `TContext`, so that is Tenancy's own context or one that maps Tenancy's read model (`AddTenancyReadModel`, or `AddTenancyReadFunctions` where the database keeps the rights), and so is a permission at a unit ([`TenancyRequirement.AtUnit<TUnitId>`](callers.md#tenancyrequirementatunittunitid), closed over `TUnitId`); a module whose requests declare neither is never read for, whatever its context maps. The statement runs on a context of its own where the application registered a factory for the context (`IDbContextFactory<TContext>`), a pooled one say, and on the scope's own context otherwise.

The check is free of any dispatcher, as the package is: whatever calls `AccessChecks.RequireAsync` in front of the module's handlers holds the requests to it. In an application that uses the Mediator library that is the pipeline behavior the generator writes for an interface marked `[AccessRequests]`.

`AddTenancyAccess<IBillingRequest, BillingContext>` is generated into the project that declares Tenancy's classes, closed over their four ids (`TemplateRegistrationAttribute`), or into a project of the same `[assembly: Module]` that declares none. A module that only asks Tenancy declares none of the classes and calls this method itself, with the application's four ids written out, as it maps the read model. Tenancy itself is registered by `AddTenancy`, in the same container; adding the check for an interface more than once is harmless.

**Parameters**

- `services`: The service collection.

**Type parameters**

- `TTenantId`: The application's tenant id.
- `TSeatId`: The application's seat id.
- `TUnitId`: The application's unit id.
- `TRoleId`: The application's role id.
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

### `TenancyModelBuilderExtensions`

```csharp
public static class TenancyModelBuilderExtensions
```

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

Maps Tenancy into an Entity Framework model: its tables into the context the application keeps Tenancy in, its read model into every other context that asks Tenancy inside its own queries, as views over those tables or as Tenancy's read functions, and the tenant filter onto any entity of any module that belongs to a tenant.

| Member | Summary |
|---|---|
| [`AddTenancy(ModelBuilder, TenancyTableNames?, DatabaseFacade?)`](#addtenancymodelbuilder-tenancytablenames-databasefacade) | Maps Tenancy's tables, closed over the application's classes and ids, into the context's default schema: the tenants, organizations with their units, seats with their placements and grants, roles, the stored rights, the closure of each organization's tree and the tenants' access revisions, plus the read model's views over them. |
| [`AddTenancyInvitations(ModelBuilder, TenancyTableNames?)`](#addtenancyinvitationsmodelbuilder-tenancytablenames) | Maps invitations into the context that maps Tenancy's tables, for an application that invites people into a tenant: the invitations, closed over the application's invitation class, and next to them the digests of their tokens, in a table of its own. |
| [`AddTenancyReadModel(ModelBuilder, string?, TenancyTableNames?)`](#addtenancyreadmodelmodelbuilder-string-tenancytablenames) | Maps Tenancy's read model into a context that is not Tenancy's own, so its queries can ask Tenancy inside them: `answers.Over(thisContext).UnitsWhereIHold(permission)` becomes a subquery of the module's own query, one statement and one round trip. |
| [`AddTenancyReadFunctions(ModelBuilder, string?)`](#addtenancyreadfunctionsmodelbuilder-string) | Maps Tenancy's read model into a context that is not Tenancy's own as Tenancy's read functions, which a package for one database writes into it (`DDDToolkit.Supporting.Tenancy.Postgres` does): the six rows the access questions read, each from the function of its name ([`TenancyFunctionNames`](entity-framework.md#tenancyfunctionnames)) in `schema`, under the column names that function answers. |
| [`ScopeToTenant(EntityTypeBuilder<TEntity>, Expression<Func<TEntity, TTenantId>>)`](#scopetotenantentitytypebuildertentity-expressionfunctentity-ttenantid) | Keeps an entity of any module to the current Tenancy caller's tenant. |

#### `AddTenancy(ModelBuilder, TenancyTableNames?, DatabaseFacade?)`

```csharp
public static ModelBuilder AddTenancy<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>(
    this ModelBuilder modelBuilder,
    TenancyTableNames? tables = null,
    DatabaseFacade? database = null)
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Maps Tenancy's tables, closed over the application's classes and ids, into the context's default schema: the tenants, organizations with their units, seats with their placements and grants, roles, the stored rights, the closure of each organization's tree and the tenants' access revisions, plus the read model's views over them. Call it from `OnModelCreating` of the context Tenancy lives in:

```csharp
modelBuilder.HasDefaultSchema("tenancy");
modelBuilder.AddTenancy();
```

The call without type arguments is generated into the project that declares the classes, closed over them (`TemplateRegistrationAttribute`), or into a project of the same `[assembly: Module]` that declares none, such as the module's infrastructure project, where the context is, next to a domain project without Entity Framework. This is the method it calls, and it can be called as well: `modelBuilder.AddTenancy<ShopTenant, TenantId, ShopOrganization, ShopUnit, OrganizationUnitId, ShopSeat, SeatId, ShopRole, RoleId>()` is the same model.

The fields, entities and rules the application adds to its classes are mapped by the toolkit's conventions, as on any aggregate; the ids by the application's generated converters. Register those and `AddDDDToolkitConventions()` in `ConfigureConventions`.

Every table that has a tenant gets the tenant filter ([`TenancyQueryFilter`](entity-framework.md#tenancyqueryfilter)), and the save check, which `UseDDDToolkit` adds, reads the tenant of every row written to them.

The views of the read model carry access facts only, here as in a module's model: ids, permissions, periods, statuses, a unit's parent, and a role's pack and permissions. What a seat, a unit or a role is called is read from the tables themselves, by the directory (`TenancyDirectory`), which answers names by id.

Pass the context's `Database` as well, `modelBuilder.AddTenancy(database: Database)`, and two rules the aggregates keep are kept by the database too, with filtered unique indexes in the provider's SQL: a tenant's organization has a single root, and a seat a single primary placement. They catch a write that goes past the aggregates, such as SQL of your own. Postgres, SQLite and SQL Server get them; another provider gets neither. Adding them to an existing database is a migration of your own, like any change to the model. A host that keeps Tenancy on Postgres under row level security (`DDDToolkit.Supporting.Tenancy.Postgres`) needs the first: its start-up check refuses a database without the unique index on a tenant's root.

Every column, key and other index is named as the context names everything else, by Entity Framework or by a naming convention on its options. The condition of each filtered index reads its column by the name the model has for it when this is called, which a convention has given by then; a column of Tenancy's that the application names by hand, it names before this call.

**Parameters**

- `modelBuilder`: The model being built.
- `tables`: The names of the tables, and of the two filtered indexes; [`TenancyTableNames.Default`](#tenancytablenames) when not given.
- `database`: The context's `Database`, read for its provider's name only; without it, no filtered indexes.

**Exceptions**

- `ArgumentNullException`: `modelBuilder` is null.

#### `AddTenancyInvitations(ModelBuilder, TenancyTableNames?)`

```csharp
public static ModelBuilder AddTenancyInvitations<TInvitation, TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>(
    this ModelBuilder modelBuilder,
    TenancyTableNames? tables = null)
    where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Maps invitations into the context that maps Tenancy's tables, for an application that invites people into a tenant: the invitations, closed over the application's invitation class, and next to them the digests of their tokens, in a table of its own. Call it after `AddTenancy`, with the application's invitation class and its id:

```csharp
modelBuilder.AddTenancy(database: Database);
modelBuilder.AddTenancyInvitations<ShopInvitation, InvitationId>();
```

That call is the one generated into the project that declares Tenancy's classes, closed over the tenant, unit, role and seat ids (`TemplateRegistrationAttribute`), or into a project of the same `[assembly: Module]` that declares none. The invitation class and its id are named in the call, since an application may have none: one that never calls it has no invitations and no tables for them.

The digest of an invitation's token is kept apart from the invitation, so that listing invitations never reads it. On Postgres, `DDDToolkit.Supporting.Tenancy.Postgres` lets no caller read that table: a token is found through a function that answers which invitation a digest is for, and nothing else.

Both tables get the tenant filter and the save check, as Tenancy's others do. The fields the application adds to its invitation class are mapped by the toolkit's conventions, as on any aggregate. Adding the tables to an existing database is a migration of your own.

**Parameters**

- `modelBuilder`: The model being built.
- `tables`: The names of the tables, the same as `AddTenancy` was given; [`TenancyTableNames.Default`](#tenancytablenames) when not given.

**Type parameters**

- `TInvitation`: The application's invitation class, declared with `[InvitationAggregate<TInvitationId>]`.
- `TInvitationId`: The application's invitation id.
- `TTenantId`: The application's tenant id.
- `TUnitId`: The application's unit id.
- `TRoleId`: The application's role id.
- `TSeatId`: The application's seat id.

**Exceptions**

- `ArgumentNullException`: `modelBuilder` is null.

#### `AddTenancyReadModel(ModelBuilder, string?, TenancyTableNames?)`

```csharp
public static ModelBuilder AddTenancyReadModel<TTenantId, TSeatId, TUnitId, TRoleId>(
    this ModelBuilder modelBuilder,
    string? schema = "tenancy",
    TenancyTableNames? tables = null)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Maps Tenancy's read model into a context that is not Tenancy's own, so its queries can ask Tenancy inside them: `answers.Over(thisContext).UnitsWhereIHold(permission)` becomes a subquery of the module's own query, one statement and one round trip. The six rows the access questions read are keyless views over Tenancy's tables, their columns mapped exactly as [`AddTenancy<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`](#addtenancymodelbuilder-tenancytablenames-databasefacade) maps them. Entity Framework creates no views, so this context's migrations never touch Tenancy's tables.

```csharp
modelBuilder.AddTenancyReadModel<TenantId, SeatId, OrganizationUnitId, RoleId>();
modelBuilder.Entity<Project>().ScopeToTenant(project => project.TenantId);
```

The rows are access facts: ids, permissions, periods, statuses, a unit's parent, and a role's pack and permissions. None has a name, so a module that maps them cannot show what a seat, a unit or a role is called: it answers ids, and whoever shows them asks Tenancy's directory for the names, by id. [`TenancyModel.ReadsBeyondAccessFacts`](entity-framework.md#readsbeyondaccessfactsireadonlymodel-string-tenancytablenames) finds a module's model that maps more of Tenancy's than these rows.

It needs Tenancy's tables in the same database: one database with a schema per module, or one SQLite file. Where they are apart, ask Tenancy's own context first and pass the answer in as a list.

A module that sees only the application's ids, as above, writes them: nothing it sees says which of its ids are Tenancy's. Where the classes are visible, in the project that declares them or a project of the same module, `modelBuilder.AddTenancyReadModel()` is generated, closed over their four ids (`TemplateRegistrationAttribute`).

Where the database keeps the rights itself and shows a seat only its own ([`TenancyStoreOptions.DatabaseKeepsRights`](entity-framework.md#tenancystoreoptions), as on Postgres with `DDDToolkit.Supporting.Tenancy.Postgres`), a module reads no table of Tenancy's: map [`AddTenancyReadFunctions<TTenantId, TSeatId, TUnitId, TRoleId>`](#addtenancyreadfunctionsmodelbuilder-string) there instead. The questions refuse a context of a module that maps these views on such a database.

**Parameters**

- `modelBuilder`: The consumer's model being built.
- `schema`: The schema Tenancy's tables are in, or `null` for this model's default schema.
- `tables`: The tables' names, the same as Tenancy's context was given; [`TenancyTableNames.Default`](#tenancytablenames) when not given.

**Exceptions**

- `ArgumentNullException`: `modelBuilder` is null.

#### `AddTenancyReadFunctions(ModelBuilder, string?)`

```csharp
public static ModelBuilder AddTenancyReadFunctions<TTenantId, TSeatId, TUnitId, TRoleId>(
    this ModelBuilder modelBuilder,
    string? schema = "tenancy")
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Maps Tenancy's read model into a context that is not Tenancy's own as Tenancy's read functions, which a package for one database writes into it (`DDDToolkit.Supporting.Tenancy.Postgres` does): the six rows the access questions read, each from the function of its name ([`TenancyFunctionNames`](entity-framework.md#tenancyfunctionnames)) in `schema`, under the column names that function answers. The module's model then names no table of Tenancy's, only functions, which are what Tenancy offers other modules; and `answers.Over(thisContext)` works exactly as over the views of [`AddTenancyReadModel<TTenantId, TSeatId, TUnitId, TRoleId>`](#addtenancyreadmodelmodelbuilder-string-tenancytablenames): a question is a subquery of the module's own query, one statement and one round trip.

```csharp
if (Database.IsNpgsql())
{
    modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>("tenancy");
}
else
{
    modelBuilder.AddTenancyReadModel<TenantId, SeatId, OrganizationUnitId, RoleId>("tenancy");
}
```

The functions run as their caller, so the database decides their rows as it decides the tables': a seat is answered its own rights and the rest of its tenant, system work in a tenant all of that tenant. Every row keeps the tenant filter here as well. Entity Framework creates no functions, so this context's migrations never touch them, and Tenancy's own naming of its tables and columns, and how it stores a status, never reach this model: the functions answer under names and values of their own.

As with the views, the rows are access facts and none has a name: the functions answer no column a seat, a unit or a role is called by, and names are asked of Tenancy's directory, by id.

Use it where the database keeps the rights ([`TenancyStoreOptions.DatabaseKeepsRights`](entity-framework.md#tenancystoreoptions)); on a database without such functions, SQLite among them, map [`AddTenancyReadModel<TTenantId, TSeatId, TUnitId, TRoleId>`](#addtenancyreadmodelmodelbuilder-string-tenancytablenames). A context's model is built once for the database it runs on, so choosing between the two in `OnModelCreating` by the provider, as above, is safe for a context taken from a pool as well.

As for the views, a module that sees only the ids writes them, and where the classes are visible `modelBuilder.AddTenancyReadFunctions()` is generated, closed over their four ids.

**Parameters**

- `modelBuilder`: The consumer's model being built.
- `schema`: The schema Tenancy's functions are in, that of the context that maps Tenancy's tables; `null` for this model's default schema.

**Exceptions**

- `ArgumentNullException`: `modelBuilder` is null.

#### `ScopeToTenant(EntityTypeBuilder<TEntity>, Expression<Func<TEntity, TTenantId>>)`

```csharp
public static EntityTypeBuilder<TEntity> ScopeToTenant<TEntity, TTenantId>(
    this EntityTypeBuilder<TEntity> entity,
    Expression<Func<TEntity, TTenantId>> tenant)
    where TEntity : class
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

Keeps an entity of any module to the current Tenancy caller's tenant. It adds the tenant filter, a named query filter that sits next to the entity's other named filters rather than replacing them, and marks the entity for Tenancy's save check, which `UseDDDToolkit` adds: a row of another tenant is refused before anything is written, whoever loaded or made it.

```csharp
modelBuilder.Entity<Project>().ScopeToTenant(project => project.TenantId);
```

The tenant of a row is fixed once the row is there (`IsFixedAfterInsert`): a save that changed it is refused, by the save check first, and where privileges are written from the policies its column is left out of `UPDATE`.

Owned types are kept through their owner: scope the owner. The context must have the save check, which `UseDDDToolkit` adds (`UseTenancy` after `UseDDDToolkitCore`), and [`TenancyChecks.EnsureWired`](entity-framework.md#ensurewireddbcontext) checks it.

**Parameters**

- `entity`: The entity's builder.
- `tenant`: The property that holds the entity's tenant, as `entity => entity.TenantId`.

**Type parameters**

- `TEntity`: The entity.
- `TTenantId`: The application's tenant id.

**Exceptions**

- `ArgumentNullException`: `entity` or `tenant` is null.
- `ArgumentException`: `tenant` is not a property of the entity.

### `TenancyTableNames`

```csharp
public sealed record TenancyTableNames
```

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

The names of Tenancy's tables. The tables live in the default schema of the context that maps them.

A module that reads them through `AddTenancyReadModel` maps views over the same names, so an application that renames a table passes the same names there too; a view over a name nobody created fails on its first query rather than reading something else.

The tables are named here, and the two indexes `AddTenancy` names itself ([`RootIndex`](#tenancytablenames) and [`PrimaryIndex`](#tenancytablenames)). Every column, key and other index is named as the context names everything else, by Entity Framework or by a naming convention the context uses, and every SQL statement Tenancy writes reads those names from the model.

| Member | Summary |
|---|---|
| [`TenancyTableNames(string, string, string, string, string, string, string, string, string, string, string, string)`](#tenancytablenamesstring-string-string-string-string-string-string-string-string-string-string-string) | The primary constructor: the type's summary says what it makes. |
| `string Tenants { get; init; }` | The tenants. |
| `string Organizations { get; init; }` | Each tenant's organization. |
| `string Units { get; init; }` | The organizations' units. |
| `string UnitPaths { get; init; }` | The closure of each organization's tree. |
| `string Seats { get; init; }` | The seats. |
| `string Placements { get; init; }` | Where each seat is placed. |
| `string Grants { get; init; }` | The roles each placement grants. |
| `string Rights { get; init; }` | The permissions each seat holds where, written from the seats and the roles. |
| `string Roles { get; init; }` | The roles. |
| `string AccessRevisions { get; init; }` | One row per tenant that every change of rights takes first. |
| `string Invitations { get; init; }` | The invitations, in a context that maps them with `AddTenancyInvitations`. |
| `string InvitationDigests { get; init; }` | The digests of the invitations' tokens, kept apart from the invitations. |
| `static TenancyTableNames Default { get; }` | The names every call uses when it is given none. |
| `string? RootIndex { get; init; }` | The unique index that keeps a tenant's organization to a single root, which `AddTenancy` makes when it is given the context's database. Named after the units' table when not given: `IX_OrganizationUnits_TenantId_WhereRoot`. It is on the same column as the plain index on a unit's tenant, so it has a name of its own, which a naming convention leaves as it is. |
| `string? PrimaryIndex { get; init; }` | The unique index that keeps a seat to a single primary placement, made with [`RootIndex`](#tenancytablenames). Named after the placements' table when not given: `IX_SeatPlacements_SeatId_WherePrimary`. |
| [`SnakeCase`](#snakecase) | The same tables, and the two indexes, in snake\_case, for a context that names its columns that way with a naming convention, such as `UseSnakeCaseNamingConvention()`. |

#### `TenancyTableNames(string, string, string, string, string, string, string, string, string, string, string, string)`

```csharp
public TenancyTableNames(
    string Tenants = "Tenants",
    string Organizations = "Organizations",
    string Units = "OrganizationUnits",
    string UnitPaths = "OrganizationUnitPaths",
    string Seats = "Seats",
    string Placements = "SeatPlacements",
    string Grants = "SeatRoleGrants",
    string Rights = "SeatRights",
    string Roles = "Roles",
    string AccessRevisions = "TenancyAccessRevisions",
    string Invitations = "Invitations",
    string InvitationDigests = "InvitationDigests")
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Tenants`: The tenants.
- `Organizations`: Each tenant's organization.
- `Units`: The organizations' units.
- `UnitPaths`: The closure of each organization's tree.
- `Seats`: The seats.
- `Placements`: Where each seat is placed.
- `Grants`: The roles each placement grants.
- `Rights`: The permissions each seat holds where, written from the seats and the roles.
- `Roles`: The roles.
- `AccessRevisions`: One row per tenant that every change of rights takes first.
- `Invitations`: The invitations, in a context that maps them with `AddTenancyInvitations`.
- `InvitationDigests`: The digests of the invitations' tokens, kept apart from the invitations.

#### `SnakeCase`

```csharp
public static TenancyTableNames SnakeCase { get; }
```

The same tables, and the two indexes, in snake\_case, for a context that names its columns that way with a naming convention, such as `UseSnakeCaseNamingConvention()`. A convention renames only what was left to it, and `AddTenancy` gives these their names, so they are named here:

```csharp
modelBuilder.AddTenancy(TenancyTableNames.SnakeCase, Database);
```

### `TenancyPostgresServiceCollectionExtensions`

```csharp
public static class TenancyPostgresServiceCollectionExtensions
```

Namespace `DDDToolkit.Supporting.Tenancy.Postgres`.

Registers what Tenancy needs on Postgres with row level security.

| Member | Summary |
|---|---|
| [`AddTenancyPostgres(IServiceCollection)`](#addtenancypostgresiservicecollection) | Sets [`TenancyRowLevelSecurity.TenantSetting`](postgres.md#tenantsetting) to the current Tenancy caller's tenant whenever a context under row level security connects, next to the caller's role and claims and in the same statement; turns the last administrator the trigger keeps at commit, which no index states, into the refusal the use case gives for the same rule (a slug another tenant has, which the policies hide from the use case's own check, is refused by its unique index on any database, with no help from here); requires explicit callers (`CallerServiceCollectionExtensions.RequireExplicitCallers`), so work nobody said anything about fails rather than running as the login role, past every policy; and leaves the rights to the database ([`TenancyStoreOptions.DatabaseKeepsRights`](entity-framework.md#tenancystoreoptions)), whose trigger writes them and whose policies let a seat read only its own, so the save writes none, the store asks the database's functions about other seats, and Tenancy's few reads across tenants ask its functions as scoped system work in no tenant, rather than run as the application itself, past the policies. |

#### `AddTenancyPostgres(IServiceCollection)`

```csharp
public static IServiceCollection AddTenancyPostgres(this IServiceCollection services)
```

Sets [`TenancyRowLevelSecurity.TenantSetting`](postgres.md#tenantsetting) to the current Tenancy caller's tenant whenever a context under row level security connects, next to the caller's role and claims and in the same statement; turns the last administrator the trigger keeps at commit, which no index states, into the refusal the use case gives for the same rule (a slug another tenant has, which the policies hide from the use case's own check, is refused by its unique index on any database, with no help from here); requires explicit callers (`CallerServiceCollectionExtensions.RequireExplicitCallers`), so work nobody said anything about fails rather than running as the login role, past every policy; and leaves the rights to the database ([`TenancyStoreOptions.DatabaseKeepsRights`](entity-framework.md#tenancystoreoptions)), whose trigger writes them and whose policies let a seat read only its own, so the save writes none, the store asks the database's functions about other seats, and Tenancy's few reads across tenants ask its functions as scoped system work in no tenant, rather than run as the application itself, past the policies. Call it next to `AddSupabaseRowLevelSecurity` or `AddPostgresRowLevelSecurity`:

```csharp
builder.Services.AddSupabaseRowLevelSecurity();
builder.Services.AddTenancyPostgres();
```

The policies themselves come with the access files the export writes, which makes [`TenancyRowAccessContribution`](postgres.md#tenancyrowaccesscontribution) from the catalogue the application marks; [`TenancyPostgresChecks`](postgres.md#tenancypostgreschecks) checks at start-up that the database and the host are set up as they rely on. This registers those checks as start-up checks, which the host runs with `services.RunStartupChecks()`. Calling it more than once is harmless.

**Parameters**

- `services`: The application's services.

**Exceptions**

- `ArgumentNullException`: `services` is null.
