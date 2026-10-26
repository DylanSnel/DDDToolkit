---
title: "Tenancy API: Aggregates and entities"
sidebar_label: Aggregates and entities
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Tenancy API: Aggregates and entities

The templates the application's own classes derive from, in the order of the tree: the tenant, its organization and units, the seat with its placements and grants, the role, the invitation. Each section opens with the name the application sees, the template in brackets, and the attribute that declares the application's class; its value objects, invariants and events follow it.

## Declaring the classes

Namespace `DDDToolkit.Supporting.Tenancy`.

### `GenerateTenancyClassesAttribute`

```csharp
[AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
public sealed class GenerateTenancyClassesAttribute : Attribute
```

Has the generator write Tenancy's classes this project does not declare, as the package ships them, and every id of theirs that no project declares. One line instead of five classes and four ids:

```csharp
[assembly: GenerateTenancyClasses]
```

The project then has a `Tenant`, an `Organization`, an `OrganizationUnit`, a `Role` and a `Seat`, each declared with its template, and a `TenantId`, an `OrganizationUnitId`, a `RoleId` and a `SeatId`, each an `[EntityId<Guid>]`: public, in the project's root namespace, with everything the toolkit's generators write for a class and an id declared by hand, `modelBuilder.AddTenancy()` and the class the use cases are named through, `TenancyUseCases`, among them, and Membership's member list over a written `SeatId`. An organization shares its tenant's id, so there is no `OrganizationId`.

**Seen from the next project up.** Other generators of this project, HotChocolate's for one, do not see what the switch wrote: a module of one project with an `[ObjectType<Role>]` declares that class itself. Code outside the root namespace imports the written types with a `global using` of the root namespace, since a file's using does not reach the parts the generators write.

**Your own class wins.** Declare one of them yourself, when it needs fields, rules or behaviour, and the generator writes the rest: `[SeatAggregate<SeatId>] public sealed partial class Seat { ... }` anywhere in the project, and no `Seat` is written. An id you declare, here or in a project of the module that this one references, is taken rather than written. Every generated class and id says in its documentation that it was generated, and how to replace it.

**In a module split by layer** the ids belong in the contracts project, which the other modules reference, and the classes in the domain project: put [`GenerateTenancyIdsAttribute`](#generatetenancyidsattribute) in the first and this in the second, which then takes the ids from the first. A contracts project that should not reference this package declares the four ids itself, one line each.

The invitation is not written: it is the one class an application may leave out, and declaring it is what turns invitations on. An application that wants them declares `[InvitationAggregate<InvitationId>]` and the id.

### `GenerateTenancyIdsAttribute`

```csharp
[AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
public sealed class GenerateTenancyIdsAttribute : Attribute
```

Has the generator write the ids of Tenancy's classes that this project does not declare, and no classes: for the contracts project of a module split by layer, which publishes the ids to the other modules and holds no aggregates.

```csharp
[assembly: GenerateTenancyIds]
```

The project then has a `TenantId`, an `OrganizationUnitId`, a `RoleId` and a `SeatId`, each an `[EntityId<Guid>]` published with `[ModuleContract]`, public, in its root namespace, with everything an id declared by hand gets. The module's domain project says [`GenerateTenancyClassesAttribute`](#generatetenancyclassesattribute), and takes these. An id this project declares itself wins: it is not written.

It is an attribute apart, rather than an option of [`GenerateTenancyClassesAttribute`](#generatetenancyclassesattribute), so each project says in its own words what it gets, and a contracts project cannot be handed aggregates by a word left out.

### `TenancyInstances`

```csharp
public static class TenancyInstances
```

Makes new instances of the application's own Tenancy classes, which the package cannot name, and puts them in their first state with their creation event raised. The use cases create through these, and so can tests, seeding and imports.

There are no factories to write. Each class is made through the parameterless constructor the generator writes on every class declared with a template, and the parent does what a constructor would. A field the application added starts at its default; set it in a method of the class after creation, or in a handler of the creation event.

Each takes who makes the instance, `by`, for its creation event: the use cases pass the actor of their caller, and seeding or an import that names nobody leaves it out.

```csharp
var seat = TenancyInstances.NewSeat<ShopSeat, SeatId, TenantId, OrganizationUnitId, RoleId>(
    SeatId.Create(), tenantId, identity);
seat.Rename("Ada"); // the application's own field: a seat has no name in Tenancy
```

| Member | Summary |
|---|---|
| [`NewTenant(TTenantId, ValidTenantSlug, TenantShape, TenancyActor<TSeatId>?)`](#newtenantttenantid-validtenantslug-tenantshape-tenancyactortseatid) | A new tenant, being provisioned, with [`TenantProvisioned<TTenantId, TSeatId>`](#tenantprovisionedttenantid-tseatid) raised. |
| [`NewOrganization(TTenantId, string, TUnitId, string, TenancyActor<TSeatId>?)`](#neworganizationttenantid-string-tunitid-string-tenancyactortseatid) | A new organization with its root, through the application's own unit class, and [`OrganizationUnitAdded<TTenantId, TUnitId, TSeatId>`](#organizationunitaddedttenantid-tunitid-tseatid) raised for the root. |
| [`NewSeat(TSeatId, TTenantId, Guid, TenancyActor<TSeatId>?)`](#newseattseatid-ttenantid-guid-tenancyactortseatid) | A new active seat, placed nowhere yet, with [`SeatAdded<TTenantId, TSeatId>`](#seataddedttenantid-tseatid) raised. |
| [`NewRole(TRoleId, TTenantId, RoleDraft, TenancyCatalogue, TenancyActor<TSeatId>?)`](#newroletroleid-ttenantid-roledraft-tenancycatalogue-tenancyactortseatid) | A new active role, its permissions expanded from the catalogue, with [`RoleCreated<TTenantId, TRoleId, TSeatId>`](#rolecreatedttenantid-troleid-tseatid) raised. |
| [`NewInvitation(TInvitationId, TTenantId, string, TUnitId, TRoleId, DateTimeOffset?, DateTimeOffset, DateTimeOffset, TSeatId?, bool, TenancyActor<TSeatId>?)`](#newinvitationtinvitationid-ttenantid-string-tunitid-troleid-datetimeoffset-datetimeoffset-datetimeoffset-tseatid-bool-tenancyactortseatid) | A new open invitation, with [`InvitationIssued<TTenantId, TInvitationId, TUnitId, TRoleId, TSeatId>`](#invitationissuedttenantid-tinvitationid-tunitid-troleid-tseatid) raised. |

#### `NewTenant(TTenantId, ValidTenantSlug, TenantShape, TenancyActor<TSeatId>?)`

```csharp
public static TTenant NewTenant<TTenant, TTenantId, TSeatId>(
    TTenantId id,
    ValidTenantSlug slug,
    TenantShape shape,
    TenancyActor<TSeatId>? by = null)
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A new tenant, being provisioned, with [`TenantProvisioned<TTenantId, TSeatId>`](#tenantprovisionedttenantid-tseatid) raised.

**Parameters**

- `id`: Its id.
- `slug`: The slug it is selected by.
- `shape`: Whether its organization is flat or a tree.
- `by`: Who makes it, for its creation event; `null` when nobody is named.

**Type parameters**

- `TTenant`: The application's tenant class.
- `TTenantId`: The application's tenant id.
- `TSeatId`: The application's seat id, which the actor on the creation event is closed over.

#### `NewOrganization(TTenantId, string, TUnitId, string, TenancyActor<TSeatId>?)`

```csharp
public static TOrganization NewOrganization<TOrganization, TTenantId, TUnit, TUnitId, TSeatId>(
    TTenantId id,
    string name,
    TUnitId rootId,
    string rootName,
    TenancyActor<TSeatId>? by = null)
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A new organization with its root, through the application's own unit class, and [`OrganizationUnitAdded<TTenantId, TUnitId, TSeatId>`](#organizationunitaddedttenantid-tunitid-tseatid) raised for the root. A field the application added to its unit class is set on `organization.Root` afterwards.

**Parameters**

- `id`: Its tenant's id.
- `name`: The tenant's name.
- `rootId`: The root's id.
- `rootName`: The root's name.
- `by`: Who makes it, for its creation event; `null` when nobody is named.

**Type parameters**

- `TOrganization`: The application's organization class.
- `TTenantId`: The application's tenant id, which the organization shares.
- `TUnit`: The application's unit class.
- `TUnitId`: The application's unit id.
- `TSeatId`: The application's seat id, which the actor on the creation event is closed over.

**Exceptions**

- `RefusalException`: `tenancy.name-invalid`, for the tenant's name or the root's.

#### `NewSeat(TSeatId, TTenantId, Guid, TenancyActor<TSeatId>?)`

```csharp
public static TSeat NewSeat<TSeat, TSeatId, TTenantId, TUnitId, TRoleId>(
    TSeatId id,
    TTenantId tenantId,
    Guid identity,
    TenancyActor<TSeatId>? by = null)
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

A new active seat, placed nowhere yet, with [`SeatAdded<TTenantId, TSeatId>`](#seataddedttenantid-tseatid) raised.

**Parameters**

- `id`: Its id.
- `tenantId`: The tenant it is in.
- `identity`: The verified identity it belongs to: the subject of the person's token.
- `by`: Who makes it, for its creation event; `null` when nobody is named.

**Type parameters**

- `TSeat`: The application's seat class.
- `TSeatId`: The application's seat id.
- `TTenantId`: The application's tenant id.
- `TUnitId`: The application's unit id.
- `TRoleId`: The application's role id.

**Exceptions**

- `RefusalException`: `tenancy.identity-required`.

#### `NewRole(TRoleId, TTenantId, RoleDraft, TenancyCatalogue, TenancyActor<TSeatId>?)`

```csharp
public static TRole NewRole<TRole, TRoleId, TTenantId, TSeatId>(
    TRoleId id,
    TTenantId tenantId,
    RoleDraft draft,
    TenancyCatalogue catalogue,
    TenancyActor<TSeatId>? by = null)
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A new active role, its permissions expanded from the catalogue, with [`RoleCreated<TTenantId, TRoleId, TSeatId>`](#rolecreatedttenantid-troleid-tseatid) raised. A role made from a pack remembers the pack's permissions as the catalogue builds it, whatever permissions the draft gives the role: an import's own permissions are the tenant's, and a later sync of the packs leaves them as they are.

**Parameters**

- `id`: Its id.
- `tenantId`: The tenant it belongs to.
- `draft`: Its name, description, permissions and the pack it comes from.
- `catalogue`: The catalogue its permissions come from.
- `by`: Who makes it, for its creation event; `null` when nobody is named.

**Type parameters**

- `TRole`: The application's role class.
- `TRoleId`: The application's role id.
- `TTenantId`: The application's tenant id.
- `TSeatId`: The application's seat id, which the actor on the creation event is closed over.

**Exceptions**

- `RefusalException`: `tenancy.name-invalid` or `tenancy.unknown-permission`.

#### `NewInvitation(TInvitationId, TTenantId, string, TUnitId, TRoleId, DateTimeOffset?, DateTimeOffset, DateTimeOffset, TSeatId?, bool, TenancyActor<TSeatId>?)`

```csharp
public static TInvitation NewInvitation<TInvitation, TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>(
    TInvitationId id,
    TTenantId tenantId,
    string address,
    TUnitId unitId,
    TRoleId roleId,
    DateTimeOffset? grantUntil,
    DateTimeOffset issuedAt,
    DateTimeOffset expiresAt,
    TSeatId? issuedBy = null,
    bool issuedAsSystem = false,
    TenancyActor<TSeatId>? by = null)
    where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A new open invitation, with [`InvitationIssued<TTenantId, TInvitationId, TUnitId, TRoleId, TSeatId>`](#invitationissuedttenantid-tinvitationid-tunitid-troleid-tseatid) raised. It has no token: the use case that issues an invitation makes one and hands its digest to the store with the invitation, so an invitation made here and saved by hand is one nobody can accept.

**Parameters**

- `id`: Its id.
- `tenantId`: The tenant it is into.
- `address`: The address it is for.
- `unitId`: The unit the seat is placed in.
- `roleId`: The role the seat is granted there.
- `grantUntil`: When that grant ends, later than `expiresAt`, or `null` for no end.
- `issuedAt`: When it is issued.
- `expiresAt`: The first moment it can no longer be accepted.
- `issuedBy`: The seat that issues it, or the seat system work issues it for; `null` for none.
- `issuedAsSystem`: Whether system work issues it, which is then not held to a seat's rights when it is accepted.
- `by`: Who makes it, for its creation event; `null` when nobody is named.

**Type parameters**

- `TInvitation`: The application's invitation class.
- `TInvitationId`: The application's invitation id.
- `TTenantId`: The application's tenant id.
- `TUnitId`: The application's unit id.
- `TRoleId`: The application's role id.
- `TSeatId`: The application's seat id.

**Exceptions**

- `RefusalException`: `tenancy.address-invalid` or `tenancy.invitation-grant-ends-first`.
- `ArgumentException`: `expiresAt` is not after `issuedAt`.

## Tenant

Namespace `DDDToolkit.Supporting.Tenancy`.

### Tenant (`TenantAggregate<TTenantId>`)

Declared in the application with `[TenantAggregate<TenantId>]`.

```csharp
public abstract class TenantAggregate<TTenantId> : AggregateRoot<TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

A tenant: the slug callers select it by, its status, and whether its organization is flat or a tree. The application declares its own class with [`TenantAggregateAttribute<TTenantId>`](#tenantaggregateattributettenantid), and the generator derives that class from this one, closed over the application's id.

Its status moves one way: provisioned, active, suspended and active again as often as needed, and closed once. A suspension and a closure each need a reason, and the last one is kept.

Nothing here is virtual. A class declared with the template adds fields, methods, entities and rules of its own, and reacts to this tenant's domain events; it does not change what the package decides.

Every method that changes the tenant takes who makes the change, `by`, and puts it on the event it raises. The use cases pass the actor of their caller. A tenant is declared without the seat class and does not know the seat id's type, so the methods take it: it follows from a caller's actor (`tenant.Suspend(reason, caller.Actor)`), and code that names nobody, an import or a test, names the type instead (`tenant.Activate<SeatId>()`).

**Type parameters**

- `TTenantId`: The application's tenant id.

| Member | Summary |
|---|---|
| `MaxReasonLength = 500` | The longest reason a suspension or closure may give. |
| [`TenantAggregate()`](#tenantaggregate) | Parameterless constructor for persistence frameworks and serializers. |
| `TenantSlug Slug { get; }` | The slug the tenant is selected by. Set when it is provisioned, and never changed. |
| `TenantStatus Status { get; }` | Where the tenant is in its life. |
| `TenantShape Shape { get; }` | Whether its organization is its root alone or a tree. |
| `string? StatusReason { get; }` | The reason given by the last suspension or closure, or `null` when there was none. |
| `bool IsActive { get; }` | Whether the tenant is in use. |
| [`Activate(TenancyActor<TSeatId>?)`](#activatetenancyactortseatid) | Puts a provisioned tenant in use. |
| [`Suspend(string, TenancyActor<TSeatId>?)`](#suspendstring-tenancyactortseatid) | Stops an active tenant for now. |
| [`Reactivate(TenancyActor<TSeatId>?)`](#reactivatetenancyactortseatid) | Puts a suspended tenant back in use. |
| [`Close(string, TenancyActor<TSeatId>?)`](#closestring-tenancyactortseatid) | Stops an active or suspended tenant for good. |
| [`ChangeShape(TenantShape, TenancyActor<TSeatId>?)`](#changeshapetenantshape-tenancyactortseatid) | Changes a flat tenant into a hierarchical one. |
| [`GetOwnInvariantViolations()`](#getowninvariantviolations) | Runs the rules and `CheckInvariants()` seam of this aggregate, and returns every rule that is broken, without throwing. |
| [`EnsureOwnInvariants()`](#ensureowninvariants) | Runs the rules and `CheckInvariants()` seam of this aggregate, and throws when it finds something broken. |
| [`GetInvariantViolations()`](#getinvariantviolations) | Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and returns every rule that is broken, without throwing. |
| [`EnsureInvariants()`](#ensureinvariants) | Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and throws when it finds something broken. |

#### `TenantAggregate()`

```csharp
protected TenantAggregate()
```

Parameterless constructor for persistence frameworks and serializers.

#### `Activate(TenancyActor<TSeatId>?)`

```csharp
public void Activate<TSeatId>(TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Puts a provisioned tenant in use.

**Parameters**

- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: a tenant does not know it by itself.

**Exceptions**

- `RefusalException`: `tenancy.tenant-state`: it is not being provisioned.

#### `Suspend(string, TenancyActor<TSeatId>?)`

```csharp
public void Suspend<TSeatId>(string reason, TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Stops an active tenant for now.

**Parameters**

- `reason`: Why, kept as [`StatusReason`](#tenant-tenantaggregatettenantid). Required.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: a tenant does not know it by itself.

**Exceptions**

- `RefusalException`: `tenancy.tenant-state` when it is not active, `tenancy.reason-required` without a reason, `tenancy.name-invalid` when the reason is longer than [`MaxReasonLength`](#tenant-tenantaggregatettenantid).

#### `Reactivate(TenancyActor<TSeatId>?)`

```csharp
public void Reactivate<TSeatId>(TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Puts a suspended tenant back in use. The reason it was suspended stays as the last one given.

**Parameters**

- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: a tenant does not know it by itself.

**Exceptions**

- `RefusalException`: `tenancy.tenant-state`: it is not suspended.

#### `Close(string, TenancyActor<TSeatId>?)`

```csharp
public void Close<TSeatId>(string reason, TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Stops an active or suspended tenant for good. A tenant still being provisioned is not closed; it was never open.

**Parameters**

- `reason`: Why, kept as [`StatusReason`](#tenant-tenantaggregatettenantid). Required.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: a tenant does not know it by itself.

**Exceptions**

- `RefusalException`: `tenancy.tenant-state` when it is being provisioned or already closed, `tenancy.reason-required` without a reason, `tenancy.name-invalid` when the reason is too long.

#### `ChangeShape(TenantShape, TenancyActor<TSeatId>?)`

```csharp
public void ChangeShape<TSeatId>(TenantShape to, TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Changes a flat tenant into a hierarchical one. The way back is refused: the units below the root would have nowhere to go.

**Parameters**

- `to`: The new shape.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: a tenant does not know it by itself.

**Exceptions**

- `RefusalException`: `tenancy.tenant-state` when the tenant is closed, `tenancy.shape-change` for anything but flat to hierarchical.

#### `GetOwnInvariantViolations()`

```csharp
public override IReadOnlyList<InvariantViolation> GetOwnInvariantViolations()
```

Runs the rules and `CheckInvariants()` seam of this aggregate, and returns every rule that is broken, without throwing. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `EnsureOwnInvariants()`

```csharp
public override void EnsureOwnInvariants()
```

Runs the rules and `CheckInvariants()` seam of this aggregate, and throws when it finds something broken. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `GetInvariantViolations()`

```csharp
public override IReadOnlyList<InvariantViolation> GetInvariantViolations()
```

Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and returns every rule that is broken, without throwing. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `EnsureInvariants()`

```csharp
public override void EnsureInvariants()
```

Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and throws when it finds something broken. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

### `TenantAggregateAttribute<TTenantId>`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TenantAggregateAttribute<TTenantId> : Attribute
```

Declares the application's tenant class. The generator derives it from [`TenantAggregate<TTenantId>`](#tenant-tenantaggregatettenantid), closed over the id given here:

```csharp
[TenantAggregate<TenantId>]
public sealed partial class ShopTenant;
```

**Type parameters**

- `TTenantId`: The application's tenant id, a `readonly partial record struct` marked `[EntityId<T>]`. Tenancy makes a new one with its `Create()`, which the generator writes for an id over a `Guid` and an id over anything else declares itself (DDD00067).

### `TenantSlug`

```csharp
public record TenantSlug : SingleValueObject<string>, IValidatable<ValidTenantSlug>, IValueObject, ISingleValue<TenantSlug, string>
```

The short name a tenant is selected by, such as `harbor`: 2 to 63 characters, each a lowercase letter, a digit or a dash, the first not a dash. It is set when the tenant is provisioned and never changes, because it is what callers send to say which tenant they mean.

[`Create`](#createstring) trims and lowercases what it is given, so `" Harbor "` and `harbor` are the same slug. The generator adds `ValidTenantSlug`, the twin that is valid by construction.

| Member | Summary |
|---|---|
| `MaxLength = 63` | The longest slug: 63 characters, the limit of one DNS label. |
| `Pattern = "^[a-z0-9][a-z0-9-]{1,62}$"` | What a slug looks like. A slug may end in a dash, which a DNS label may not: a host that puts slugs in host names checks that itself. |
| [`TenantSlug(string)`](#tenantslugstring) | *No documentation.* |
| [`TenantSlug()`](#tenantslug-1) | *No documentation.* |
| [`Create(string)`](#createstring) | A slug from what a person typed: trimmed and lowercased, and not yet checked. |
| [`Validate(ValidationErrorBuilder)`](#validatevalidationerrorbuilder) | The rules, as a list of reasons. |
| [`ToValid()`](#tovalid) | The always-valid twin. |

#### `TenantSlug(string)`

```csharp
protected TenantSlug(string value)
```

*No documentation.*

#### `TenantSlug()`

```csharp
protected TenantSlug()
```

*No documentation.*

#### `Create(string)`

```csharp
public static TenantSlug Create(string value)
```

A slug from what a person typed: trimmed and lowercased, and not yet checked.

**Parameters**

- `value`: The slug as given.

#### `Validate(ValidationErrorBuilder)`

```csharp
protected override void Validate(ValidationErrorBuilder errors)
```

The rules, as a list of reasons. Override this instead of `ValueObject.Validate` when a caller needs to know *why* a value was refused. A run that adds nothing to `errors` is a pass.

Both overloads run, and both must be happy: the value is valid when `ValueObject.Validate` returned `true` and no failure was added here. Overriding only one is the normal case. With DDDToolkit.FluentValidation referenced this overload is generated for you, so override neither and write rules instead.

**Parameters**

- `errors`: Collects this run's failures.

#### `ToValid()`

```csharp
public ValidTenantSlug ToValid()
```

The always-valid twin. Throws when the value is invalid; call TryToValid() to be handed the failures instead.

### `ValidTenantSlug`

```csharp
public record ValidTenantSlug : TenantSlug, IAlwaysValid, ISingleValue<ValidTenantSlug, string>
```

*No documentation.*

| Member | Summary |
|---|---|
| [`ValidTenantSlug(TenantSlug)`](#validtenantslugtenantslug) | *No documentation.* |
| [`ValidTenantSlug(string)`](#validtenantslugstring) | *No documentation.* |

#### `ValidTenantSlug(TenantSlug)`

```csharp
public ValidTenantSlug(TenantSlug value)
```

*No documentation.*

#### `ValidTenantSlug(string)`

```csharp
public ValidTenantSlug(string value)
```

*No documentation.*

### `TenantShape`

```csharp
public enum TenantShape
```

Whether a tenant's organization is a tree or only its root. A tenant starts flat or hierarchical, and a flat one can become hierarchical; the way back is closed, because units below the root would have to go.

| Member | Summary |
|---|---|
| `Flat = 0` | The organization is its root alone, and every seat is placed there. |
| `Hierarchical = 1` | The organization is a tree of units below the root. |

### `TenantStatus`

```csharp
public enum TenantStatus
```

Where a tenant is in its life. It moves one way: provisioned, then active, suspended and active again as often as needed, and closed once, for good.

| Member | Summary |
|---|---|
| `Provisioning = 0` | Being set up; nobody works in it yet. |
| `Active = 1` | In use. |
| `Suspended = 2` | Stopped for now, with a reason, and able to be reactivated. |
| `Closed = 3` | Stopped for good, with a reason. Nothing changes a closed tenant. |

### `TenantAggregate.ClosedAndSuspendedHaveAReason`

```csharp
public sealed class ClosedAndSuspendedHaveAReason : IInvariant<TenantAggregate<TTenantId>>
```

A suspended or closed tenant says why.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(TenantAggregate<TTenantId>)`](#checktenantaggregatettenantid) | Runs the rule. |

#### `Check(TenantAggregate<TTenantId>)`

```csharp
public InvariantFailure? Check(TenantAggregate<TTenantId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `TenantAggregate.SlugIsValid`

```csharp
public sealed class SlugIsValid : IInvariant<TenantAggregate<TTenantId>>
```

The slug follows the pattern. A slug is only ever set from a valid one, so this is the net under that.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(TenantAggregate<TTenantId>)`](#checktenantaggregatettenantid-1) | Runs the rule. |

#### `Check(TenantAggregate<TTenantId>)`

```csharp
public InvariantFailure? Check(TenantAggregate<TTenantId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `TenantActivated<TTenantId, TSeatId>`

```csharp
public sealed record TenantActivated<TTenantId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A tenant became active.

| Member | Summary |
|---|---|
| [`TenantActivated(TTenantId, TenancyActor<TSeatId>?)`](#tenantactivatedttenantid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant activated. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `TenantActivated(TTenantId, TenancyActor<TSeatId>?)`

```csharp
public TenantActivated(TTenantId TenantId, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant activated.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `TenantClosed<TTenantId, TSeatId>`

```csharp
public sealed record TenantClosed<TTenantId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A tenant was closed, for good.

| Member | Summary |
|---|---|
| [`TenantClosed(TTenantId, string, TenancyActor<TSeatId>?)`](#tenantclosedttenantid-string-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant closed. |
| `string Reason { get; init; }` | Why it was closed, as the command gave it. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `TenantClosed(TTenantId, string, TenancyActor<TSeatId>?)`

```csharp
public TenantClosed(TTenantId TenantId, string Reason, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant closed.
- `Reason`: Why it was closed, as the command gave it.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `TenantProvisioned<TTenantId, TSeatId>`

```csharp
public sealed record TenantProvisioned<TTenantId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A tenant was provisioned: it exists, and is not active yet.

| Member | Summary |
|---|---|
| [`TenantProvisioned(TTenantId, string, TenantShape, TenancyActor<TSeatId>?)`](#tenantprovisionedttenantid-string-tenantshape-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant provisioned. |
| `string Slug { get; init; }` | The slug it is known by. |
| `TenantShape Shape { get; init; }` | Its shape: [`TenantShape.Flat`](#tenantshape), the root alone, or [`TenantShape.Hierarchical`](#tenantshape), a tree of units below it. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `TenantProvisioned(TTenantId, string, TenantShape, TenancyActor<TSeatId>?)`

```csharp
public TenantProvisioned(
    TTenantId TenantId,
    string Slug,
    TenantShape Shape,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant provisioned.
- `Slug`: The slug it is known by.
- `Shape`: Its shape: [`TenantShape.Flat`](#tenantshape), the root alone, or [`TenantShape.Hierarchical`](#tenantshape), a tree of units below it.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `TenantReactivated<TTenantId, TSeatId>`

```csharp
public sealed record TenantReactivated<TTenantId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A suspended tenant became active again.

| Member | Summary |
|---|---|
| [`TenantReactivated(TTenantId, TenancyActor<TSeatId>?)`](#tenantreactivatedttenantid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant reactivated. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `TenantReactivated(TTenantId, TenancyActor<TSeatId>?)`

```csharp
public TenantReactivated(TTenantId TenantId, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant reactivated.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `TenantShapeChanged<TTenantId, TSeatId>`

```csharp
public sealed record TenantShapeChanged<TTenantId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A tenant's shape changed, which only ever goes from flat to hierarchical.

| Member | Summary |
|---|---|
| [`TenantShapeChanged(TTenantId, TenantShape, TenantShape, TenancyActor<TSeatId>?)`](#tenantshapechangedttenantid-tenantshape-tenantshape-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TenantShape From { get; init; }` | The shape it had. |
| `TenantShape To { get; init; }` | The shape it has now. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `TenantShapeChanged(TTenantId, TenantShape, TenantShape, TenancyActor<TSeatId>?)`

```csharp
public TenantShapeChanged(
    TTenantId TenantId,
    TenantShape From,
    TenantShape To,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `From`: The shape it had.
- `To`: The shape it has now.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `TenantSuspended<TTenantId, TSeatId>`

```csharp
public sealed record TenantSuspended<TTenantId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A tenant was suspended.

| Member | Summary |
|---|---|
| [`TenantSuspended(TTenantId, string, TenancyActor<TSeatId>?)`](#tenantsuspendedttenantid-string-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant suspended. |
| `string Reason { get; init; }` | Why it was suspended, as the command gave it. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `TenantSuspended(TTenantId, string, TenancyActor<TSeatId>?)`

```csharp
public TenantSuspended(TTenantId TenantId, string Reason, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant suspended.
- `Reason`: Why it was suspended, as the command gave it.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

## Organization

Namespace `DDDToolkit.Supporting.Tenancy`.

### Organization (`OrganizationAggregate<TTenantId, TUnit, TUnitId>`)

Declared in the application with `[OrganizationAggregate<TenantId>]`.

```csharp
public abstract class OrganizationAggregate<TTenantId, TUnit, TUnitId> : AggregateRoot<TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

A tenant's organization: its name and the tree of its units, one root and everything below it. It shares its tenant's id. The application declares its own class with [`OrganizationAggregateAttribute<TTenantId>`](#organizationaggregateattributettenantid), and the generator closes this one over the application's tenant id, its unit class and the unit's id.

The tree is one aggregate so that its rules hold for the whole of it at once: exactly one root, no unit under itself, at most [`MaxDepth`](#organization-organizationaggregatettenantid-tunit-tunitid) levels. Every change goes through a method here, which refuses what would break them before anything changes; the nested invariants are the net under that. A unit is archived, never deleted, so what points at it keeps something to point at.

The walks over the tree keep a visited set, and the walks up and down by level stop one step past [`MaxDepth`](#organization-organizationaggregatettenantid-tunit-tunitid), so a tree that is broken all the same (written by hand into the database, say) never makes them loop; the invariants report what is wrong with it.

Every method that changes the organization takes who makes the change, `by`, and puts it on the event it raises. The use cases pass the actor of their caller. An organization is declared without the seat class and does not know the seat id's type, so the methods take it: it follows from a caller's actor (`organization.ArchiveUnit(unit, caller.Actor)`), and code that names nobody, an import or a test, names the type instead (`organization.ArchiveUnit<SeatId>(unit)`).

**Type parameters**

- `TTenantId`: The application's tenant id, which is also this organization's id.
- `TUnit`: The application's unit class.
- `TUnitId`: The application's unit id.

| Member | Summary |
|---|---|
| `MaxDepth = 32` | The most levels an organization has, the root being the first. |
| `MaxNameLength = 200` | The longest name an organization, which is the tenant's name, may have. |
| [`OrganizationAggregate()`](#organizationaggregate) | Parameterless constructor for persistence frameworks and serializers. |
| `string Name { get; }` | The tenant's name. |
| `IReadOnlyList<TUnit> Units { get; }` | Read-only view over ``DDDToolkit.Supporting.Tenancy.OrganizationAggregate`3._units``. Mutate the collection through the field. |
| [`Root`](#root) | The one unit with no parent. |
| [`Rename(string, TenancyActor<TSeatId>?)`](#renamestring-tenancyactortseatid) | Renames the organization. |
| [`AddUnit(TUnitId, TUnitId, string, TenantShape, TenancyActor<TSeatId>?, Action<TUnit>?)`](#addunittunitid-tunitid-string-tenantshape-tenancyactortseatid-actiontunit) | Adds a unit below an active one, in a hierarchical tenant. |
| [`RenameUnit(TUnitId, string, TenancyActor<TSeatId>?)`](#renameunittunitid-string-tenancyactortseatid) | Renames an active unit. |
| [`MoveUnit(TUnitId, TUnitId, TenancyActor<TSeatId>?)`](#moveunittunitid-tunitid-tenancyactortseatid) | Moves a unit, and everything below it, under another active unit. |
| [`ArchiveUnit(TUnitId, TenancyActor<TSeatId>?)`](#archiveunittunitid-tenancyactortseatid) | Archives a unit that has no active unit below it. |
| [`FindUnit(TUnitId)`](#findunittunitid) | The unit with this id, archived or not, or `null` when the organization has none. |
| [`IsActiveUnit(TUnitId)`](#isactiveunittunitid) | Whether the organization has an active unit with this id. |
| [`DepthOf(TUnitId)`](#depthoftunitid) | How deep a unit is: 1 for the root, 2 for a unit right below it. |
| [`AncestorsOf(TUnitId)`](#ancestorsoftunitid) | A unit and the units above it, the unit itself first and the root last. |
| [`SubtreeOf(TUnitId)`](#subtreeoftunitid) | A unit and every unit below it, the unit itself first, archived ones included. |
| [`HeightOf(TUnitId)`](#heightoftunitid) | How many levels a unit and the units below it span: 1 for a unit with nothing below it. |
| [`GetOwnInvariantViolations()`](#getowninvariantviolations-1) | Runs the rules and `CheckInvariants()` seam of this aggregate, and returns every rule that is broken, without throwing. |
| [`EnsureOwnInvariants()`](#ensureowninvariants-1) | Runs the rules and `CheckInvariants()` seam of this aggregate, and throws when it finds something broken. |
| [`GetInvariantViolations()`](#getinvariantviolations-1) | Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and returns every rule that is broken, without throwing. |
| [`EnsureInvariants()`](#ensureinvariants-1) | Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and throws when it finds something broken. |

#### `OrganizationAggregate()`

```csharp
protected OrganizationAggregate()
```

Parameterless constructor for persistence frameworks and serializers.

#### `Root`

```csharp
public TUnit Root { get; }
```

The one unit with no parent.

**Exceptions**

- `InvalidOperationException`: The organization has no root, which its invariants never let it save.

#### `Rename(string, TenancyActor<TSeatId>?)`

```csharp
public void Rename<TSeatId>(string name, TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Renames the organization. The same name again changes nothing and raises nothing.

**Parameters**

- `name`: The new name, 1 to [`MaxNameLength`](#organization-organizationaggregatettenantid-tunit-tunitid) characters.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: an organization does not know it by itself.

**Exceptions**

- `RefusalException`: `tenancy.name-invalid`: blank or too long.

#### `AddUnit(TUnitId, TUnitId, string, TenantShape, TenancyActor<TSeatId>?, Action<TUnit>?)`

```csharp
public TUnit AddUnit<TSeatId>(
    TUnitId id,
    TUnitId parentId,
    string name,
    TenantShape shape,
    TenancyActor<TSeatId>? by = null,
    Action<TUnit>? configure = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Adds a unit below an active one, in a hierarchical tenant. A field the application added to its unit class starts at its default unless `configure` sets it.

**Parameters**

- `id`: The new unit's id.
- `parentId`: The unit it hangs under.
- `name`: Its name, 1 to [`OrganizationUnitEntity.MaxNameLength`](#organizationunit-organizationunitentitytunitid) characters.
- `shape`: The tenant's shape: a flat tenant has only its root.
- `by`: Who makes the change, for the event; `null` when nobody is named.
- `configure`: Sets the application's own fields on the new unit, after every check here and before the organization takes the unit in. When it throws, the organization is as it was: no unit and no event, so a later save in the same unit of work has nothing of it to write.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: an organization does not know it by itself.

**Returns**: The new unit, an instance of the application's own class.

**Exceptions**

- `RefusalException`: In this order: `tenancy.flat-tenant`, `tenancy.unit-not-found` for the parent, `tenancy.unit-not-active` when the parent is archived, `tenancy.depth-exceeded`, `tenancy.name-invalid`.
- `ArgumentException`: A unit with `id` exists. Ids are made to be new, and one is given only by an import or seeding, so a clash is a mistake in that code rather than something a caller did.

#### `RenameUnit(TUnitId, string, TenancyActor<TSeatId>?)`

```csharp
public void RenameUnit<TSeatId>(TUnitId id, string name, TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Renames an active unit. The same name again changes nothing and raises nothing.

**Parameters**

- `id`: The unit.
- `name`: Its new name.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: an organization does not know it by itself.

**Exceptions**

- `RefusalException`: `tenancy.unit-not-found`, `tenancy.unit-archived`, `tenancy.name-invalid`.

#### `MoveUnit(TUnitId, TUnitId, TenancyActor<TSeatId>?)`

```csharp
public void MoveUnit<TSeatId>(TUnitId id, TUnitId newParentId, TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Moves a unit, and everything below it, under another active unit. What a permission held above the unit reaches changes with it, which is why the use case also guards the move against concurrent grants.

**Parameters**

- `id`: The unit that moves.
- `newParentId`: The unit it hangs under from now on.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: an organization does not know it by itself.

**Exceptions**

- `RefusalException`: In this order: `tenancy.unit-not-found`, `tenancy.root-immovable`, `tenancy.unit-archived`, `tenancy.unit-not-found` for the new parent, `tenancy.unit-not-active` when it is archived, `tenancy.cycle` when it is the unit or below it, `tenancy.same-parent`, `tenancy.depth-exceeded`.

#### `ArchiveUnit(TUnitId, TenancyActor<TSeatId>?)`

```csharp
public void ArchiveUnit<TSeatId>(TUnitId id, TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Archives a unit that has no active unit below it. Seats placed there and rights granted there keep working. Nothing new goes to it: no unit is added below it or moved under it here, and the use cases place no seat and grant no role there.

**Parameters**

- `id`: The unit.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: an organization does not know it by itself.

**Exceptions**

- `RefusalException`: `tenancy.unit-not-found`, `tenancy.root-not-archivable`, `tenancy.unit-archived`, `tenancy.unit-has-active-children`.

#### `FindUnit(TUnitId)`

```csharp
public TUnit? FindUnit(TUnitId id)
```

The unit with this id, archived or not, or `null` when the organization has none.

#### `IsActiveUnit(TUnitId)`

```csharp
public bool IsActiveUnit(TUnitId id)
```

Whether the organization has an active unit with this id.

#### `DepthOf(TUnitId)`

```csharp
public int DepthOf(TUnitId id)
```

How deep a unit is: 1 for the root, 2 for a unit right below it.

**Exceptions**

- `RefusalException`: `tenancy.unit-not-found`.

#### `AncestorsOf(TUnitId)`

```csharp
public IReadOnlyList<TUnitId> AncestorsOf(TUnitId id)
```

A unit and the units above it, the unit itself first and the root last.

**Exceptions**

- `RefusalException`: `tenancy.unit-not-found`.

#### `SubtreeOf(TUnitId)`

```csharp
public IReadOnlyList<TUnitId> SubtreeOf(TUnitId id)
```

A unit and every unit below it, the unit itself first, archived ones included.

**Exceptions**

- `RefusalException`: `tenancy.unit-not-found`.

#### `HeightOf(TUnitId)`

```csharp
public int HeightOf(TUnitId id)
```

How many levels a unit and the units below it span: 1 for a unit with nothing below it.

**Exceptions**

- `RefusalException`: `tenancy.unit-not-found`.

#### `GetOwnInvariantViolations()`

```csharp
public override IReadOnlyList<InvariantViolation> GetOwnInvariantViolations()
```

Runs the rules and `CheckInvariants()` seam of this aggregate, and returns every rule that is broken, without throwing. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `EnsureOwnInvariants()`

```csharp
public override void EnsureOwnInvariants()
```

Runs the rules and `CheckInvariants()` seam of this aggregate, and throws when it finds something broken. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `GetInvariantViolations()`

```csharp
public override IReadOnlyList<InvariantViolation> GetInvariantViolations()
```

Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and returns every rule that is broken, without throwing. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `EnsureInvariants()`

```csharp
public override void EnsureInvariants()
```

Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and throws when it finds something broken. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

### `OrganizationAggregateAttribute<TTenantId>`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OrganizationAggregateAttribute<TTenantId> : Attribute
```

Declares the application's organization class. The generator derives it from [`OrganizationAggregate<TTenantId, TUnit, TUnitId>`](#organization-organizationaggregatettenantid-tunit-tunitid), closed over the tenant id given here and the application's unit class and unit id, which it takes from the class declared with [`OrganizationUnitAttribute<TUnitId>`](#organizationunitattributetunitid):

```csharp
[OrganizationAggregate<TenantId>]
public sealed partial class ShopOrganization;
```

**Type parameters**

- `TTenantId`: The application's tenant id: an organization shares its tenant's id.

### OrganizationUnit (`OrganizationUnitEntity<TUnitId>`)

Declared in the application with `[OrganizationUnit<UnitId>]`.

```csharp
public abstract class OrganizationUnitEntity<TUnitId> : Entity<TUnitId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

One unit of a tenant's organization: a company, a region, a site, whatever the application's organization is made of. The application declares its own class with [`OrganizationUnitAttribute<TUnitId>`](#organizationunitattributetunitid).

A unit belongs to its organization, and nothing else changes it: every mutator here is internal, and the organization checks the tree before it calls one.

It has no kind. Nothing about access reads what kind of unit a unit is, so Tenancy keeps none: an application that tells regions from sites adds a field of its own to its class, an enum say, and sets it in the callback the use cases take when they make a unit (`TenantToProvision.ConfigureRoot` and the `configure` of `OrganizationCommands.AddUnitAsync`).

**Type parameters**

- `TUnitId`: The application's unit id.

| Member | Summary |
|---|---|
| `MaxNameLength = 200` | The longest name a unit may have. |
| [`OrganizationUnitEntity()`](#organizationunitentity) | Parameterless constructor for persistence frameworks and serializers. |
| `TUnitId? ParentId { get; }` | The unit this one hangs under, or `null` for the root. |
| `string Name { get; }` | The unit's name. |
| `UnitStatus Status { get; }` | Whether the unit is in use. |
| `bool IsRoot { get; }` | Whether this is the organization's root. |
| [`GetOwnInvariantViolations()`](#getowninvariantviolations-2) | Runs the rules and `CheckInvariants()` seam of this entity, and returns every rule that is broken, without throwing. |
| [`EnsureOwnInvariants()`](#ensureowninvariants-2) | Runs the rules and `CheckInvariants()` seam of this entity, and throws when it finds something broken. |
| [`GetInvariantViolations()`](#getinvariantviolations-2) | Runs the rules and `CheckInvariants()` seam of this entity and of every child entity it holds, and returns every rule that is broken, without throwing. |
| [`EnsureInvariants()`](#ensureinvariants-2) | Runs the rules and `CheckInvariants()` seam of this entity and of every child entity it holds, and throws when it finds something broken. |

#### `OrganizationUnitEntity()`

```csharp
protected OrganizationUnitEntity()
```

Parameterless constructor for persistence frameworks and serializers.

#### `GetOwnInvariantViolations()`

```csharp
public override IReadOnlyList<InvariantViolation> GetOwnInvariantViolations()
```

Runs the rules and `CheckInvariants()` seam of this entity, and returns every rule that is broken, without throwing. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `EnsureOwnInvariants()`

```csharp
public override void EnsureOwnInvariants()
```

Runs the rules and `CheckInvariants()` seam of this entity, and throws when it finds something broken. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `GetInvariantViolations()`

```csharp
public override IReadOnlyList<InvariantViolation> GetInvariantViolations()
```

Runs the rules and `CheckInvariants()` seam of this entity and of every child entity it holds, and returns every rule that is broken, without throwing. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `EnsureInvariants()`

```csharp
public override void EnsureInvariants()
```

Runs the rules and `CheckInvariants()` seam of this entity and of every child entity it holds, and throws when it finds something broken. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

### `OrganizationUnitAttribute<TUnitId>`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OrganizationUnitAttribute<TUnitId> : Attribute
```

Declares the application's organization unit class. The generator derives it from [`OrganizationUnitEntity<TUnitId>`](#organizationunit-organizationunitentitytunitid), closed over the id given here:

```csharp
[OrganizationUnit<OrganizationUnitId>]
public sealed partial class ShopUnit;
```

**Type parameters**

- `TUnitId`: The application's unit id, a `readonly partial record struct` marked `[EntityId<T>]`. Tenancy makes a new one with its `Create()`, which the generator writes for an id over a `Guid` and an id over anything else declares itself (DDD00067).

### `UnitStatus`

```csharp
public enum UnitStatus
```

Whether a unit is in use. A unit is archived, never deleted, so what still points at it keeps a unit to point at.

| Member | Summary |
|---|---|
| `Active = 0` | In use: units can be added below it and seats placed in it. |
| `Archived = 1` | No longer in use. What already hangs at it keeps working; nothing new is added to it. |

### `OrganizationAggregate.DepthAtMostMax`

```csharp
public sealed class DepthAtMostMax : IInvariant<OrganizationAggregate<TTenantId, TUnit, TUnitId>>
```

No unit is deeper than [`OrganizationAggregate.MaxDepth`](#organization-organizationaggregatettenantid-tunit-tunitid).

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(OrganizationAggregate<TTenantId, TUnit, TUnitId>)`](#checkorganizationaggregatettenantid-tunit-tunitid) | Runs the rule. |

#### `Check(OrganizationAggregate<TTenantId, TUnit, TUnitId>)`

```csharp
public InvariantFailure? Check(OrganizationAggregate<TTenantId, TUnit, TUnitId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `OrganizationAggregate.ExactlyOneRoot`

```csharp
public sealed class ExactlyOneRoot : IInvariant<OrganizationAggregate<TTenantId, TUnit, TUnitId>>
```

An organization has exactly one root.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(OrganizationAggregate<TTenantId, TUnit, TUnitId>)`](#checkorganizationaggregatettenantid-tunit-tunitid-1) | Runs the rule. |

#### `Check(OrganizationAggregate<TTenantId, TUnit, TUnitId>)`

```csharp
public InvariantFailure? Check(OrganizationAggregate<TTenantId, TUnit, TUnitId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `OrganizationAggregate.NameIsValid`

```csharp
public sealed class NameIsValid : IInvariant<OrganizationAggregate<TTenantId, TUnit, TUnitId>>
```

The organization has a name of 1 to [`OrganizationAggregate.MaxNameLength`](#organization-organizationaggregatettenantid-tunit-tunitid) characters.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(OrganizationAggregate<TTenantId, TUnit, TUnitId>)`](#checkorganizationaggregatettenantid-tunit-tunitid-2) | Runs the rule. |

#### `Check(OrganizationAggregate<TTenantId, TUnit, TUnitId>)`

```csharp
public InvariantFailure? Check(OrganizationAggregate<TTenantId, TUnit, TUnitId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `OrganizationAggregate.NoCycles`

```csharp
public sealed class NoCycles : IInvariant<OrganizationAggregate<TTenantId, TUnit, TUnitId>>
```

No unit is above itself.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(OrganizationAggregate<TTenantId, TUnit, TUnitId>)`](#checkorganizationaggregatettenantid-tunit-tunitid-3) | Runs the rule. |

#### `Check(OrganizationAggregate<TTenantId, TUnit, TUnitId>)`

```csharp
public InvariantFailure? Check(OrganizationAggregate<TTenantId, TUnit, TUnitId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `OrganizationAggregate.ParentsAreKnown`

```csharp
public sealed class ParentsAreKnown : IInvariant<OrganizationAggregate<TTenantId, TUnit, TUnitId>>
```

Every unit's parent is a unit of the organization.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(OrganizationAggregate<TTenantId, TUnit, TUnitId>)`](#checkorganizationaggregatettenantid-tunit-tunitid-4) | Runs the rule. |

#### `Check(OrganizationAggregate<TTenantId, TUnit, TUnitId>)`

```csharp
public InvariantFailure? Check(OrganizationAggregate<TTenantId, TUnit, TUnitId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `OrganizationUnitEntity.NameIsValid`

```csharp
public sealed class NameIsValid : IInvariant<OrganizationUnitEntity<TUnitId>>
```

A unit has a name of 1 to [`OrganizationUnitEntity.MaxNameLength`](#organizationunit-organizationunitentitytunitid) characters.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(OrganizationUnitEntity<TUnitId>)`](#checkorganizationunitentitytunitid) | Runs the rule. |

#### `Check(OrganizationUnitEntity<TUnitId>)`

```csharp
public InvariantFailure? Check(OrganizationUnitEntity<TUnitId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `OrganizationRenamed<TTenantId, TSeatId>`

```csharp
public sealed record OrganizationRenamed<TTenantId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

The organization's name changed.

| Member | Summary |
|---|---|
| [`OrganizationRenamed(TTenantId, TenancyActor<TSeatId>?)`](#organizationrenamedttenantid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant whose organization it is. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `OrganizationRenamed(TTenantId, TenancyActor<TSeatId>?)`

```csharp
public OrganizationRenamed(TTenantId TenantId, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant whose organization it is.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `OrganizationUnitAdded<TTenantId, TUnitId, TSeatId>`

```csharp
public sealed record OrganizationUnitAdded<TTenantId, TUnitId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A unit was added: the root when the organization was made, or a unit below an existing one.

| Member | Summary |
|---|---|
| [`OrganizationUnitAdded(TTenantId, TUnitId, TUnitId?, TenancyActor<TSeatId>?)`](#organizationunitaddedttenantid-tunitid-tunitid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TUnitId UnitId { get; init; }` | The unit added. |
| `TUnitId? ParentId { get; init; }` | The unit it was added below, or `null` for the root. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `OrganizationUnitAdded(TTenantId, TUnitId, TUnitId?, TenancyActor<TSeatId>?)`

```csharp
public OrganizationUnitAdded(
    TTenantId TenantId,
    TUnitId UnitId,
    TUnitId? ParentId,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `UnitId`: The unit added.
- `ParentId`: The unit it was added below, or `null` for the root.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `OrganizationUnitArchived<TTenantId, TUnitId, TSeatId>`

```csharp
public sealed record OrganizationUnitArchived<TTenantId, TUnitId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A unit was archived.

| Member | Summary |
|---|---|
| [`OrganizationUnitArchived(TTenantId, TUnitId, TenancyActor<TSeatId>?)`](#organizationunitarchivedttenantid-tunitid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TUnitId UnitId { get; init; }` | The unit archived. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `OrganizationUnitArchived(TTenantId, TUnitId, TenancyActor<TSeatId>?)`

```csharp
public OrganizationUnitArchived(TTenantId TenantId, TUnitId UnitId, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `UnitId`: The unit archived.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `OrganizationUnitMoved<TTenantId, TUnitId, TSeatId>`

```csharp
public sealed record OrganizationUnitMoved<TTenantId, TUnitId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A unit, and everything below it, moved under another parent.

| Member | Summary |
|---|---|
| [`OrganizationUnitMoved(TTenantId, TUnitId, TUnitId, TUnitId, TenancyActor<TSeatId>?)`](#organizationunitmovedttenantid-tunitid-tunitid-tunitid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TUnitId UnitId { get; init; }` | The unit that moved, with everything below it. |
| `TUnitId FromParentId { get; init; }` | The parent it hung under before. |
| `TUnitId ToParentId { get; init; }` | The parent it hangs under now. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `OrganizationUnitMoved(TTenantId, TUnitId, TUnitId, TUnitId, TenancyActor<TSeatId>?)`

```csharp
public OrganizationUnitMoved(
    TTenantId TenantId,
    TUnitId UnitId,
    TUnitId FromParentId,
    TUnitId ToParentId,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `UnitId`: The unit that moved, with everything below it.
- `FromParentId`: The parent it hung under before.
- `ToParentId`: The parent it hangs under now.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `OrganizationUnitRenamed<TTenantId, TUnitId, TSeatId>`

```csharp
public sealed record OrganizationUnitRenamed<TTenantId, TUnitId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A unit's name changed.

| Member | Summary |
|---|---|
| [`OrganizationUnitRenamed(TTenantId, TUnitId, TenancyActor<TSeatId>?)`](#organizationunitrenamedttenantid-tunitid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TUnitId UnitId { get; init; }` | The unit renamed. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `OrganizationUnitRenamed(TTenantId, TUnitId, TenancyActor<TSeatId>?)`

```csharp
public OrganizationUnitRenamed(TTenantId TenantId, TUnitId UnitId, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `UnitId`: The unit renamed.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

## Seat

Namespace `DDDToolkit.Supporting.Tenancy`.

### Seat (`SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>`)

Declared in the application with `[SeatAggregate<SeatId>]`.

```csharp
public abstract class SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> : AggregateRoot<TSeatId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

A person's seat in one tenant: the verified identity it belongs to, the units it is placed in, and the roles it holds at each. The application declares its own class with [`SeatAggregateAttribute<TSeatId>`](#seataggregateattributetseatid).

The identity is the subject of a verified token, set when the seat is made and never changed. A seat is never linked to a person by an e-mail address, and none is kept here; an application that wants contact details adds them to its own class.

Nor does a seat have a name. No rule of Tenancy reads one, so what a person is shown by is the application's to say: a name per tenant on its own seat class, set in the callback of the use case that makes the seat; the person's own name, from the identity provider; or a profile of its own, found by [`Identity`](#seat-seataggregatetseatid-ttenantid-tunitid-troleid). The directory answers the application's own seat, whole, and the application selects what a screen shows of it.

Placements and grants are the seat's own: it checks every change to them, and hands them out as read-only views. What a role grants is not the seat's to know; the use case reads the role and passes its [`RoleFacts`](#rolefacts) in.

Every method that changes the seat takes who makes the change, `by`, and puts it on the event it raises; the use cases pass the actor of their caller. It is a record of the change and decides nothing: the rules about a seat acting on itself read the seat a placement or a grant keeps, `placedBy` and `grantedBy`.

**Type parameters**

- `TSeatId`: The application's seat id.
- `TTenantId`: The application's tenant id.
- `TUnitId`: The application's unit id.
- `TRoleId`: The application's role id.

| Member | Summary |
|---|---|
| `MaxReasonLength = 500` | The longest reason a grant may give. |
| [`SeatAggregate()`](#seataggregate) | Parameterless constructor for persistence frameworks and serializers. |
| `TTenantId TenantId { get; }` | The tenant the seat is in. |
| `Guid Identity { get; }` | The verified identity the seat belongs to: the subject of its token. Never changes. |
| `SeatStatus Status { get; }` | Whether the seat's grants count. |
| `IReadOnlyList<Placement<TSeatId, TUnitId, TRoleId>> Placements { get; }` | The units the seat is placed in, with the roles it holds at each. A read-only view. |
| [`Suspend(TenancyActor<TSeatId>?)`](#suspendtenancyactortseatid) | Stops an active seat for now. |
| [`Reactivate(TenancyActor<TSeatId>?)`](#reactivatetenancyactortseatid-1) | Makes a suspended seat active again. |
| [`Deactivate(TenancyActor<TSeatId>?)`](#deactivatetenancyactortseatid) | Stops an active or suspended seat for good. |
| [`Place(TUnitId, bool, DateTimeOffset, TSeatId?, TenancyActor<TSeatId>?)`](#placetunitid-bool-datetimeoffset-tseatid-tenancyactortseatid) | Places the seat in a unit. |
| [`Withdraw(TUnitId, TenancyActor<TSeatId>?)`](#withdrawtunitid-tenancyactortseatid) | Withdraws the seat from a unit: every role it holds there is revoked first, each with its own event, and then the placement goes. |
| [`MakePrimary(TUnitId, TenancyActor<TSeatId>?)`](#makeprimarytunitid-tenancyactortseatid) | Makes the placement in `unit` the seat's primary one. |
| [`Grant(TUnitId, TRoleId, RoleFacts, GrantPeriod, TSeatId?, string?, TenancyActor<TSeatId>?)`](#granttunitid-troleid-rolefacts-grantperiod-tseatid-string-tenancyactortseatid) | Grants the seat a role at a unit where it is placed. |
| [`Revoke(TUnitId, TRoleId, TenancyActor<TSeatId>?)`](#revoketunitid-troleid-tenancyactortseatid) | Revokes a role the seat holds at a unit. |
| [`IsAdministratorAt(TUnitId, Func<TRoleId, RoleFacts?>, DateTimeOffset)`](#isadministratorattunitid-functroleid-rolefacts-datetimeoffset) | Whether this seat, as it is in memory, is an administrator of its tenant: it is active, placed at the root, and holds there, with no end date and applying now, an active role that grants [`TenancyPermissions.AdministratorPermission`](catalogue.md#tenancypermissions). |
| [`GetOwnInvariantViolations()`](#getowninvariantviolations-3) | Runs the rules and `CheckInvariants()` seam of this aggregate, and returns every rule that is broken, without throwing. |
| [`EnsureOwnInvariants()`](#ensureowninvariants-3) | Runs the rules and `CheckInvariants()` seam of this aggregate, and throws when it finds something broken. |
| [`GetInvariantViolations()`](#getinvariantviolations-3) | Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and returns every rule that is broken, without throwing. |
| [`EnsureInvariants()`](#ensureinvariants-3) | Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and throws when it finds something broken. |

#### `SeatAggregate()`

```csharp
protected SeatAggregate()
```

Parameterless constructor for persistence frameworks and serializers.

#### `Suspend(TenancyActor<TSeatId>?)`

```csharp
public void Suspend(TenancyActor<TSeatId>? by = null)
```

Stops an active seat for now. Its placements and grants stay, and give it nothing while it is suspended.

**Parameters**

- `by`: Who makes the change, for the event; `null` when nobody is named.

**Exceptions**

- `RefusalException`: `tenancy.seat-state`: it is not active.

#### `Reactivate(TenancyActor<TSeatId>?)`

```csharp
public void Reactivate(TenancyActor<TSeatId>? by = null)
```

Makes a suspended seat active again.

**Parameters**

- `by`: Who makes the change, for the event; `null` when nobody is named.

**Exceptions**

- `RefusalException`: `tenancy.seat-state`: it is not suspended.

#### `Deactivate(TenancyActor<TSeatId>?)`

```csharp
public void Deactivate(TenancyActor<TSeatId>? by = null)
```

Stops an active or suspended seat for good.

**Parameters**

- `by`: Who makes the change, for the event; `null` when nobody is named.

**Exceptions**

- `RefusalException`: `tenancy.seat-state`: it is already deactivated.

#### `Place(TUnitId, bool, DateTimeOffset, TSeatId?, TenancyActor<TSeatId>?)`

```csharp
public Placement<TSeatId, TUnitId, TRoleId> Place(
    TUnitId unit,
    bool primary,
    DateTimeOffset at,
    TSeatId? placedBy,
    TenancyActor<TSeatId>? by = null)
```

Places the seat in a unit. Whether the unit is an active unit of the organization is the use case's to check: the organization is another aggregate.

**Parameters**

- `unit`: The unit.
- `primary`: Whether this becomes the seat's primary placement.
- `at`: When.
- `placedBy`: The seat placing it, or `null` for system work with no seat acting.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Exceptions**

- `RefusalException`: `tenancy.seat-state` when the seat is not active, `tenancy.self-assignment` when it would place itself, `tenancy.duplicate-placement`, `tenancy.second-primary`.

#### `Withdraw(TUnitId, TenancyActor<TSeatId>?)`

```csharp
public void Withdraw(TUnitId unit, TenancyActor<TSeatId>? by = null)
```

Withdraws the seat from a unit: every role it holds there is revoked first, each with its own event, and then the placement goes.

**Parameters**

- `unit`: The unit of the placement.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Exceptions**

- `RefusalException`: `tenancy.placement-not-found`.

#### `MakePrimary(TUnitId, TenancyActor<TSeatId>?)`

```csharp
public void MakePrimary(TUnitId unit, TenancyActor<TSeatId>? by = null)
```

Makes the placement in `unit` the seat's primary one. Already primary changes nothing and raises nothing.

**Parameters**

- `unit`: The unit of the placement.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Exceptions**

- `RefusalException`: `tenancy.placement-not-found`.

#### `Grant(TUnitId, TRoleId, RoleFacts, GrantPeriod, TSeatId?, string?, TenancyActor<TSeatId>?)`

```csharp
public void Grant(
    TUnitId unit,
    TRoleId role,
    RoleFacts facts,
    GrantPeriod period,
    TSeatId? grantedBy,
    string? reason,
    TenancyActor<TSeatId>? by = null)
```

Grants the seat a role at a unit where it is placed. Whether the granting seat may give the role (its permissions that manage access, held there long enough, and never a role that manages access to itself) is the use case's to check, because it needs the catalogue and another seat's rights. A host that calls [`Grant`](#granttunitid-troleid-rolefacts-grantperiod-tseatid-string-tenancyactortseatid) directly gets neither. A seat may be recorded as its own granter.

**Parameters**

- `unit`: The unit of one of the seat's placements.
- `role`: The role.
- `facts`: What the use case read about the role.
- `period`: When the grant applies.
- `grantedBy`: The seat granting it, or `null` for system work with no seat acting.
- `reason`: Why, at most [`MaxReasonLength`](#seat-seataggregatetseatid-ttenantid-tunitid-troleid) characters, or `null`.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Exceptions**

- `RefusalException`: `tenancy.seat-state` when the seat is not active, `tenancy.placement-not-found`, `tenancy.role-not-active`, `tenancy.duplicate-grant`, `tenancy.name-invalid` for a reason that is too long.

#### `Revoke(TUnitId, TRoleId, TenancyActor<TSeatId>?)`

```csharp
public void Revoke(TUnitId unit, TRoleId role, TenancyActor<TSeatId>? by = null)
```

Revokes a role the seat holds at a unit.

**Parameters**

- `unit`: The unit of the placement.
- `role`: The role.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Exceptions**

- `RefusalException`: `tenancy.grant-not-found`: the seat does not hold the role there.

#### `IsAdministratorAt(TUnitId, Func<TRoleId, RoleFacts?>, DateTimeOffset)`

```csharp
public bool IsAdministratorAt(TUnitId root, Func<TRoleId, RoleFacts?> roles, DateTimeOffset now)
```

Whether this seat, as it is in memory, is an administrator of its tenant: it is active, placed at the root, and holds there, with no end date and applying now, an active role that grants [`TenancyPermissions.AdministratorPermission`](catalogue.md#tenancypermissions). A grant with an end date does not count: counting it would let the tenant lose its last administrator on a date nobody picked for that.

**Parameters**

- `root`: The organization's root.
- `roles`: What is known about each role the seat holds, or `null` for a role that is not found.
- `now`: The moment to ask about.

#### `GetOwnInvariantViolations()`

```csharp
public override IReadOnlyList<InvariantViolation> GetOwnInvariantViolations()
```

Runs the rules and `CheckInvariants()` seam of this aggregate, and returns every rule that is broken, without throwing. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `EnsureOwnInvariants()`

```csharp
public override void EnsureOwnInvariants()
```

Runs the rules and `CheckInvariants()` seam of this aggregate, and throws when it finds something broken. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `GetInvariantViolations()`

```csharp
public override IReadOnlyList<InvariantViolation> GetInvariantViolations()
```

Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and returns every rule that is broken, without throwing. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `EnsureInvariants()`

```csharp
public override void EnsureInvariants()
```

Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and throws when it finds something broken. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

### `SeatAggregateAttribute<TSeatId>`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SeatAggregateAttribute<TSeatId> : Attribute
```

Declares the application's seat class. The generator derives it from [`SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>`](#seat-seataggregatetseatid-ttenantid-tunitid-troleid), closed over the seat id given here and the tenant, unit and role ids of the classes declared with the other templates:

```csharp
[SeatAggregate<SeatId>]
public sealed partial class ShopSeat;
```

**Type parameters**

- `TSeatId`: The application's seat id, a `readonly partial record struct` marked `[EntityId<T>]`. Tenancy makes a new one with its `Create()`, which the generator writes for an id over a `Guid` and an id over anything else declares itself (DDD00067).

### `Placement<TSeatId, TUnitId, TRoleId>`

```csharp
public sealed class Placement<TSeatId, TUnitId, TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Where a seat belongs: one unit, with the roles granted to the seat at that unit. Within its seat it is known by that unit, because a seat has at most one placement per unit, and at most one of them is primary.

A placement says where a seat belongs; what it may do there comes from the roles granted with it. A plain class rather than a toolkit entity, and not extended by the application: only the seat changes it.

**Type parameters**

- `TSeatId`: The application's seat id.
- `TUnitId`: The application's unit id.
- `TRoleId`: The application's role id.

| Member | Summary |
|---|---|
| `TUnitId UnitId { get; }` | The unit the seat is placed in. |
| `bool IsPrimary { get; }` | Whether this is the seat's primary placement. |
| `DateTimeOffset PlacedAt { get; }` | When the seat was placed. |
| `TSeatId? PlacedBy { get; }` | The seat that placed it, or `null` when system work did with no seat acting. |
| `IReadOnlyList<RoleGrant<TSeatId, TRoleId>> Grants { get; }` | The roles the seat holds at this unit. A read-only view: only the seat changes them. |

### `RoleGrant<TSeatId, TRoleId>`

```csharp
public sealed class RoleGrant<TSeatId, TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

A role a seat holds at the unit of one of its placements, for a period. It is known by its role within its placement: a placement holds a role at most once.

A plain class rather than a toolkit entity, and not extended by the application: a grant is a fact about a seat, and only the seat changes it.

**Type parameters**

- `TSeatId`: The application's seat id.
- `TRoleId`: The application's role id.

| Member | Summary |
|---|---|
| `TRoleId RoleId { get; }` | The role granted. |
| `DateTimeOffset StartsAt { get; }` | The first moment the grant applies. |
| `DateTimeOffset? EndsAt { get; }` | The first moment it no longer applies, or `null` when it has no end. |
| `TSeatId? GrantedBy { get; }` | The seat that made the grant, or `null` when system work made it with no seat acting. |
| `string? Reason { get; }` | Why it was granted, at most 500 characters, or `null`. |
| [`AppliesAt(DateTimeOffset)`](#appliesatdatetimeoffset) | Whether the grant applies at `moment`: it has started, and has not ended. |

#### `AppliesAt(DateTimeOffset)`

```csharp
public bool AppliesAt(DateTimeOffset moment)
```

Whether the grant applies at `moment`: it has started, and has not ended.

**Parameters**

- `moment`: The moment to ask about, usually now.

### `SeatStatus`

```csharp
public enum SeatStatus
```

Whether a seat's grants count. Only an active seat holds rights; a suspended one can be reactivated, and a deactivated one is final.

| Member | Summary |
|---|---|
| `Active = 0` | In use: its grants give it rights. |
| `Suspended = 1` | Stopped for now. Its placements and grants stay, and give it nothing until it is reactivated. |
| `Deactivated = 2` | Stopped for good. |

### `SeatAggregate.AtMostOnePrimary`

```csharp
public sealed class AtMostOnePrimary : IInvariant<SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>>
```

A seat has at most one primary placement.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>)`](#checkseataggregatetseatid-ttenantid-tunitid-troleid) | Runs the rule. |

#### `Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>)`

```csharp
public InvariantFailure? Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `SeatAggregate.GrantPeriodsInOrder`

```csharp
public sealed class GrantPeriodsInOrder : IInvariant<SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>>
```

Every grant with an end has it later than its start.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>)`](#checkseataggregatetseatid-ttenantid-tunitid-troleid-1) | Runs the rule. |

#### `Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>)`

```csharp
public InvariantFailure? Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `SeatAggregate.IdentityIsSet`

```csharp
public sealed class IdentityIsSet : IInvariant<SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>>
```

A seat belongs to a verified identity.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>)`](#checkseataggregatetseatid-ttenantid-tunitid-troleid-2) | Runs the rule. |

#### `Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>)`

```csharp
public InvariantFailure? Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `SeatAggregate.OneGrantPerRolePerPlacement`

```csharp
public sealed class OneGrantPerRolePerPlacement : IInvariant<SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>>
```

A placement holds a role at most once.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>)`](#checkseataggregatetseatid-ttenantid-tunitid-troleid-3) | Runs the rule. |

#### `Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>)`

```csharp
public InvariantFailure? Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `SeatAggregate.OnePlacementPerUnit`

```csharp
public sealed class OnePlacementPerUnit : IInvariant<SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>>
```

A seat is placed in a unit at most once.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>)`](#checkseataggregatetseatid-ttenantid-tunitid-troleid-4) | Runs the rule. |

#### `Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>)`

```csharp
public InvariantFailure? Check(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `OrganizationRoleGranted<TTenantId, TSeatId, TUnitId, TRoleId>`

```csharp
public sealed record OrganizationRoleGranted<TTenantId, TSeatId, TUnitId, TRoleId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

A seat was granted a role at a unit where it is placed, for a period.

| Member | Summary |
|---|---|
| [`OrganizationRoleGranted(TTenantId, TSeatId, TUnitId, TRoleId, DateTimeOffset, DateTimeOffset?, TSeatId?, TenancyActor<TSeatId>?)`](#organizationrolegrantedttenantid-tseatid-tunitid-troleid-datetimeoffset-datetimeoffset-tseatid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TSeatId SeatId { get; init; }` | The seat that holds the role from now on. |
| `TUnitId UnitId { get; init; }` | The unit it holds the role at. |
| `TRoleId RoleId { get; init; }` | The role. |
| `DateTimeOffset StartsAt { get; init; }` | The first moment the grant applies. |
| `DateTimeOffset? EndsAt { get; init; }` | The first moment it no longer applies, or `null` when it has no end. |
| `TSeatId? GrantedBy { get; init; }` | The seat the grant keeps as its granter, or `null` when system work made it with no seat acting. `By` says who acted in full: for system work done for a seat this is that seat, and `By` is the system. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change. |

#### `OrganizationRoleGranted(TTenantId, TSeatId, TUnitId, TRoleId, DateTimeOffset, DateTimeOffset?, TSeatId?, TenancyActor<TSeatId>?)`

```csharp
public OrganizationRoleGranted(
    TTenantId TenantId,
    TSeatId SeatId,
    TUnitId UnitId,
    TRoleId RoleId,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    TSeatId? GrantedBy,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `SeatId`: The seat that holds the role from now on.
- `UnitId`: The unit it holds the role at.
- `RoleId`: The role.
- `StartsAt`: The first moment the grant applies.
- `EndsAt`: The first moment it no longer applies, or `null` when it has no end.
- `GrantedBy`: The seat the grant keeps as its granter, or `null` when system work made it with no seat acting. `By` says who acted in full: for system work done for a seat this is that seat, and `By` is the system.
- `By`: Who made the change.

### `OrganizationRoleRevoked<TTenantId, TSeatId, TUnitId, TRoleId>`

```csharp
public sealed record OrganizationRoleRevoked<TTenantId, TSeatId, TUnitId, TRoleId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

A role a seat held at a unit was revoked, on its own or because the placement was withdrawn.

| Member | Summary |
|---|---|
| [`OrganizationRoleRevoked(TTenantId, TSeatId, TUnitId, TRoleId, TenancyActor<TSeatId>?)`](#organizationrolerevokedttenantid-tseatid-tunitid-troleid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TSeatId SeatId { get; init; }` | The seat that held the role. |
| `TUnitId UnitId { get; init; }` | The unit it held the role at. |
| `TRoleId RoleId { get; init; }` | The role revoked. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `OrganizationRoleRevoked(TTenantId, TSeatId, TUnitId, TRoleId, TenancyActor<TSeatId>?)`

```csharp
public OrganizationRoleRevoked(
    TTenantId TenantId,
    TSeatId SeatId,
    TUnitId UnitId,
    TRoleId RoleId,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `SeatId`: The seat that held the role.
- `UnitId`: The unit it held the role at.
- `RoleId`: The role revoked.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `PrimaryPlacementChanged<TTenantId, TSeatId, TUnitId>`

```csharp
public sealed record PrimaryPlacementChanged<TTenantId, TSeatId, TUnitId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

A seat's primary placement is now the one in this unit.

| Member | Summary |
|---|---|
| [`PrimaryPlacementChanged(TTenantId, TSeatId, TUnitId, TenancyActor<TSeatId>?)`](#primaryplacementchangedttenantid-tseatid-tunitid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TSeatId SeatId { get; init; }` | The seat. |
| `TUnitId UnitId { get; init; }` | The unit of its primary placement from now on. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `PrimaryPlacementChanged(TTenantId, TSeatId, TUnitId, TenancyActor<TSeatId>?)`

```csharp
public PrimaryPlacementChanged(
    TTenantId TenantId,
    TSeatId SeatId,
    TUnitId UnitId,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `SeatId`: The seat.
- `UnitId`: The unit of its primary placement from now on.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `SeatAdded<TTenantId, TSeatId>`

```csharp
public sealed record SeatAdded<TTenantId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A seat was added to a tenant.

| Member | Summary |
|---|---|
| [`SeatAdded(TTenantId, TSeatId, TenancyActor<TSeatId>?)`](#seataddedttenantid-tseatid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant the seat was added to. |
| `TSeatId SeatId { get; init; }` | The seat added. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)); for a seat an invitation made, the system, for the seat that issued the invitation, as on `InvitationAccepted`. `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `SeatAdded(TTenantId, TSeatId, TenancyActor<TSeatId>?)`

```csharp
public SeatAdded(TTenantId TenantId, TSeatId SeatId, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant the seat was added to.
- `SeatId`: The seat added.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)); for a seat an invitation made, the system, for the seat that issued the invitation, as on `InvitationAccepted`. `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `SeatDeactivated<TTenantId, TSeatId>`

```csharp
public sealed record SeatDeactivated<TTenantId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A seat was deactivated, for good.

| Member | Summary |
|---|---|
| [`SeatDeactivated(TTenantId, TSeatId, TenancyActor<TSeatId>?)`](#seatdeactivatedttenantid-tseatid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TSeatId SeatId { get; init; }` | The seat deactivated. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `SeatDeactivated(TTenantId, TSeatId, TenancyActor<TSeatId>?)`

```csharp
public SeatDeactivated(TTenantId TenantId, TSeatId SeatId, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `SeatId`: The seat deactivated.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `SeatPlaced<TTenantId, TSeatId, TUnitId>`

```csharp
public sealed record SeatPlaced<TTenantId, TSeatId, TUnitId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

A seat was placed in a unit.

| Member | Summary |
|---|---|
| [`SeatPlaced(TTenantId, TSeatId, TUnitId, bool, TenancyActor<TSeatId>?)`](#seatplacedttenantid-tseatid-tunitid-bool-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TSeatId SeatId { get; init; }` | The seat placed. |
| `TUnitId UnitId { get; init; }` | The unit it was placed in. |
| `bool IsPrimary { get; init; }` | Whether the placement is the seat's primary one from now on. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)); for a seat an invitation made, the system, for the seat that issued the invitation, as on `InvitationAccepted`. `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `SeatPlaced(TTenantId, TSeatId, TUnitId, bool, TenancyActor<TSeatId>?)`

```csharp
public SeatPlaced(
    TTenantId TenantId,
    TSeatId SeatId,
    TUnitId UnitId,
    bool IsPrimary,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `SeatId`: The seat placed.
- `UnitId`: The unit it was placed in.
- `IsPrimary`: Whether the placement is the seat's primary one from now on.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)); for a seat an invitation made, the system, for the seat that issued the invitation, as on `InvitationAccepted`. `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `SeatReactivated<TTenantId, TSeatId>`

```csharp
public sealed record SeatReactivated<TTenantId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A suspended seat became active again.

| Member | Summary |
|---|---|
| [`SeatReactivated(TTenantId, TSeatId, TenancyActor<TSeatId>?)`](#seatreactivatedttenantid-tseatid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TSeatId SeatId { get; init; }` | The seat reactivated. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `SeatReactivated(TTenantId, TSeatId, TenancyActor<TSeatId>?)`

```csharp
public SeatReactivated(TTenantId TenantId, TSeatId SeatId, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `SeatId`: The seat reactivated.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `SeatSuspended<TTenantId, TSeatId>`

```csharp
public sealed record SeatSuspended<TTenantId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A seat was suspended.

| Member | Summary |
|---|---|
| [`SeatSuspended(TTenantId, TSeatId, TenancyActor<TSeatId>?)`](#seatsuspendedttenantid-tseatid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TSeatId SeatId { get; init; }` | The seat suspended. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `SeatSuspended(TTenantId, TSeatId, TenancyActor<TSeatId>?)`

```csharp
public SeatSuspended(TTenantId TenantId, TSeatId SeatId, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `SeatId`: The seat suspended.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `SeatWithdrawn<TTenantId, TSeatId, TUnitId>`

```csharp
public sealed record SeatWithdrawn<TTenantId, TSeatId, TUnitId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

A seat's placement in a unit was withdrawn, after every role it held there was revoked.

| Member | Summary |
|---|---|
| [`SeatWithdrawn(TTenantId, TSeatId, TUnitId, TenancyActor<TSeatId>?)`](#seatwithdrawnttenantid-tseatid-tunitid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TSeatId SeatId { get; init; }` | The seat. |
| `TUnitId UnitId { get; init; }` | The unit its placement was withdrawn from. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `SeatWithdrawn(TTenantId, TSeatId, TUnitId, TenancyActor<TSeatId>?)`

```csharp
public SeatWithdrawn(TTenantId TenantId, TSeatId SeatId, TUnitId UnitId, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `SeatId`: The seat.
- `UnitId`: The unit its placement was withdrawn from.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

## Role

Namespace `DDDToolkit.Supporting.Tenancy`.

### Role (`RoleAggregate<TRoleId, TTenantId>`)

Declared in the application with `[RoleAggregate<RoleId>]`.

```csharp
public abstract class RoleAggregate<TRoleId, TTenantId> : AggregateRoot<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

A tenant's role: a name, and the permissions it grants wherever it is granted. The application declares its own class with [`RoleAggregateAttribute<TRoleId>`](#roleaggregateattributetroleid).

Its permissions come from the catalogue only, with the permissions they imply added, and are kept distinct and sorted. A permission the catalogue later retires, or no longer knows at all, may stay on the role; it grants nothing, and is only refused when someone adds it anew. A role made from a pack remembers which one in [`FromPack`](#role-roleaggregatetroleid-ttenantid), and only when it is made.

A role made from a pack follows it ([`FollowPack<TSeatId>`](#followpacktenancycatalogue-tenancyactortseatid)): it remembers what the pack gave it ([`PermissionsFromPack`](#role-roleaggregatetroleid-ttenantid)), so a permission the pack gains later is added to it, a permission the pack loses is taken out, and what the tenant changed itself stays as the tenant left it. The application runs that for every tenant with `services.SyncRolePacks()`; nothing else changes a role behind the tenant's back.

Whoever holds [`TenancyPermissions.RolesManage`](catalogue.md#tenancypermissions) may put any permission into any role. That is on purpose: that permission is the administrator's permission, and an administrator can give themselves anything already. A permission added to a role reaches every seat that holds the role, seats that gave themselves the role included, so the use case lets an administrator alone add or take out a permission that manages access, or archive a role that holds one.

Every method that changes the role takes who makes the change, `by`, and puts it on the event it raises. The use cases pass the actor of their caller. A role is declared without the seat class and does not know the seat id's type, so the methods take it: it follows from a caller's actor (`role.Archive(caller.Actor)`), and code that names nobody, an import or a test, names the type instead (`role.Archive<SeatId>()`).

**Type parameters**

- `TRoleId`: The application's role id.
- `TTenantId`: The application's tenant id.

| Member | Summary |
|---|---|
| `MaxNameLength = 120` | The longest name a role may have. |
| `MaxDescriptionLength = 1000` | The longest description a role may have. |
| [`RoleAggregate()`](#roleaggregate) | Parameterless constructor for persistence frameworks and serializers. |
| `TTenantId TenantId { get; }` | The tenant the role belongs to. |
| `string Name { get; }` | The role's name, unique in its tenant. |
| `string Description { get; }` | What the role is for. |
| `string? FromPack { get; }` | The key of the pack the role was copied from, or `null` for a role made by hand. Set when it is made. |
| `RoleStatus Status { get; }` | Whether the role is in use. An archived role grants nothing. |
| `IReadOnlyList<string> Permissions { get; }` | The permissions the role grants: expanded, distinct and in ordinal order. |
| `IReadOnlyList<string>? PermissionsFromPack { get; }` | The permissions its pack gave the role, as the pack held them when the role was made from it or last followed it: what the next [`FollowPack<TSeatId>`](#followpacktenancycatalogue-tenancyactortseatid) compares the pack with, to tell a permission the pack gained or lost from one the tenant added or took out itself. Expanded, distinct and in ordinal order: the pack's permissions, whatever permissions the role was made with. `null` for a role made by hand, and for one made before roles remembered it or from a pack the catalogue did not have then, which follows its pack as if the pack had given it nothing yet. |
| `RoleFacts Facts { get; }` | This role's status and permissions as a snapshot, for a seat's rules. |
| [`Rename(string, string, TenancyActor<TSeatId>?)`](#renamestring-string-tenancyactortseatid) | Renames the role and describes it again. |
| [`SetPermissions(IReadOnlyCollection<string>, TenancyCatalogue, TenancyActor<TSeatId>?)`](#setpermissionsireadonlycollectionstring-tenancycatalogue-tenancyactortseatid) | Sets the permissions the role grants. |
| [`FollowPack(TenancyCatalogue, TenancyActor<TSeatId>?)`](#followpacktenancycatalogue-tenancyactortseatid) | Follows the pack the role was made from, as `catalogue` builds it now. |
| [`Archive(TenancyActor<TSeatId>?)`](#archivetenancyactortseatid) | Archives the role. |
| [`Holds(string)`](#holdsstring) | Whether the role holds `permission`, live or not. |
| [`GetOwnInvariantViolations()`](#getowninvariantviolations-4) | Runs the rules and `CheckInvariants()` seam of this aggregate, and returns every rule that is broken, without throwing. |
| [`EnsureOwnInvariants()`](#ensureowninvariants-4) | Runs the rules and `CheckInvariants()` seam of this aggregate, and throws when it finds something broken. |
| [`GetInvariantViolations()`](#getinvariantviolations-4) | Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and returns every rule that is broken, without throwing. |
| [`EnsureInvariants()`](#ensureinvariants-4) | Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and throws when it finds something broken. |

#### `RoleAggregate()`

```csharp
protected RoleAggregate()
```

Parameterless constructor for persistence frameworks and serializers.

#### `Rename(string, string, TenancyActor<TSeatId>?)`

```csharp
public void Rename<TSeatId>(string name, string description, TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Renames the role and describes it again. The same name and description change nothing and raise nothing.

**Parameters**

- `name`: Its new name.
- `description`: What it is for.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: a role does not know it by itself.

**Exceptions**

- `RefusalException`: `tenancy.role-archived`, `tenancy.name-invalid`.

#### `SetPermissions(IReadOnlyCollection<string>, TenancyCatalogue, TenancyActor<TSeatId>?)`

```csharp
public void SetPermissions<TSeatId>(
    IReadOnlyCollection<string> permissions,
    TenancyCatalogue catalogue,
    TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Sets the permissions the role grants. A permission the role already holds that the catalogue has since retired, or no longer knows, may stay as it is; every other permission must be a live permission of the catalogue, and the permissions it implies are added. The same set again changes nothing and raises nothing. A change raises [`RolePermissionsChanged<TTenantId, TRoleId, TSeatId>`](#rolepermissionschangedttenantid-troleid-tseatid) with the permissions that came in and the permissions that went out, the implied ones included.

**Parameters**

- `permissions`: Every permission the role grants from now on.
- `catalogue`: The catalogue the permissions come from.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: a role does not know it by itself.

**Exceptions**

- `RefusalException`: `tenancy.role-archived`, `tenancy.unknown-permission` for a permission that is new to the role and not live.

#### `FollowPack(TenancyCatalogue, TenancyActor<TSeatId>?)`

```csharp
public bool FollowPack<TSeatId>(TenancyCatalogue catalogue, TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Follows the pack the role was made from, as `catalogue` builds it now. What the pack gave the role the last time ([`PermissionsFromPack`](#role-roleaggregatetroleid-ttenantid)) is compared with what the pack holds now: a permission the pack gained is added to the role, and a permission it lost is taken out. What the tenant changed itself stays: a permission it took out of the role stays out while the pack still holds it, and a permission it added stays while the pack never held it. The pack's permissions are remembered for the next time.

Four things the comparison cannot tell apart are settled this way. A permission the tenant added by hand that the pack gains later is the pack's from then on, and goes when the pack loses it. A permission the tenant took out that the pack loses, and gains again later, comes back as a permission the pack gained: once the pack lost it, the record no longer holds it, and the event names it, among the permissions that manage access when it manages access. A permission that is not live, retired or no longer known, is never taken out: it grants nothing, and retiring a permission changes no role. And a role that has no record yet, made before roles remembered their pack's permissions, follows as if the pack had given it nothing: it gets every permission the pack holds that it lacks, and loses none. A live permission the role keeps brings the permissions it implies, as everywhere a role's permissions are set.

A permission that manages access follows the same rule, whoever runs this: only the application's code puts a permission in a pack. The event says which of the permissions that came in or went out manage access, so whoever is told about the change, the tenant's administrators say, can see it.

**Parameters**

- `catalogue`: The catalogue the pack is read from, as the application runs with it now.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: a role does not know it by itself.

**Returns**: Whether the role changed: its permissions, which raises [`RoleFollowedItsPack<TTenantId, TRoleId, TSeatId>`](#rolefolloweditspackttenantid-troleid-tseatid), or only what it remembers of its pack, or the order its permissions were stored in, which raises nothing. `false` for a role made by hand, one whose pack the catalogue no longer has, and one that follows its pack already.

**Exceptions**

- `RefusalException`: `tenancy.role-archived`: an archived role follows nothing.

#### `Archive(TenancyActor<TSeatId>?)`

```csharp
public void Archive<TSeatId>(TenancyActor<TSeatId>? by = null)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Archives the role. It stays, granted wherever it was, and grants nothing from now on.

**Parameters**

- `by`: Who makes the change, for the event; `null` when nobody is named.

**Type parameters**

- `TSeatId`: The application's seat id, which the actor is closed over: a role does not know it by itself.

**Exceptions**

- `RefusalException`: `tenancy.role-archived`: it already is.

#### `Holds(string)`

```csharp
public bool Holds(string permission)
```

Whether the role holds `permission`, live or not. Whether it grants it is another question: see [`Status`](#role-roleaggregatetroleid-ttenantid) and the catalogue.

#### `GetOwnInvariantViolations()`

```csharp
public override IReadOnlyList<InvariantViolation> GetOwnInvariantViolations()
```

Runs the rules and `CheckInvariants()` seam of this aggregate, and returns every rule that is broken, without throwing. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `EnsureOwnInvariants()`

```csharp
public override void EnsureOwnInvariants()
```

Runs the rules and `CheckInvariants()` seam of this aggregate, and throws when it finds something broken. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `GetInvariantViolations()`

```csharp
public override IReadOnlyList<InvariantViolation> GetInvariantViolations()
```

Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and returns every rule that is broken, without throwing. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `EnsureInvariants()`

```csharp
public override void EnsureInvariants()
```

Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and throws when it finds something broken. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

### `RoleAggregateAttribute<TRoleId>`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RoleAggregateAttribute<TRoleId> : Attribute
```

Declares the application's role class. The generator derives it from [`RoleAggregate<TRoleId, TTenantId>`](#role-roleaggregatetroleid-ttenantid), closed over the role id given here and the tenant id of the class declared with [`TenantAggregateAttribute<TTenantId>`](#tenantaggregateattributettenantid):

```csharp
[RoleAggregate<RoleId>]
public sealed partial class ShopRole;
```

**Type parameters**

- `TRoleId`: The application's role id, a `readonly partial record struct` marked `[EntityId<T>]`. Tenancy makes a new one with its `Create()`, which the generator writes for an id over a `Guid` and an id over anything else declares itself (DDD00067).

### `RoleDraft`

```csharp
public sealed record RoleDraft
```

What a new role is made from.

| Member | Summary |
|---|---|
| [`RoleDraft(string, string, IReadOnlyCollection<string>, string?)`](#roledraftstring-string-ireadonlycollectionstring-string) | The primary constructor: the type's summary says what it makes. |
| `string Name { get; init; }` | The role's name, 1 to 120 characters, unique in its tenant. |
| `string Description { get; init; }` | What the role is for, at most 1000 characters. |
| `IReadOnlyCollection<string> Permissions { get; init; }` | The permissions it grants; the permissions they imply are added. |
| `string? FromPack { get; init; }` | The key of the pack it was copied from, or `null` for a role made by hand. |

#### `RoleDraft(string, string, IReadOnlyCollection<string>, string?)`

```csharp
public RoleDraft(
    string Name,
    string Description,
    IReadOnlyCollection<string> Permissions,
    string? FromPack = null)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Name`: The role's name, 1 to 120 characters, unique in its tenant.
- `Description`: What the role is for, at most 1000 characters.
- `Permissions`: The permissions it grants; the permissions they imply are added.
- `FromPack`: The key of the pack it was copied from, or `null` for a role made by hand.

### `RoleFacts`

```csharp
public sealed record RoleFacts
```

The two things about a role that the seat's rules depend on: whether the role is active, and its permissions. Seats and roles are separate aggregates; the use case reads the role and passes this snapshot to the seat.

| Member | Summary |
|---|---|
| [`RoleFacts(bool, IReadOnlyCollection<string>)`](#rolefactsbool-ireadonlycollectionstring) | The primary constructor: the type's summary says what it makes. |
| `bool IsActive { get; init; }` | Whether the role is active; an archived role grants nothing. |
| `IReadOnlyCollection<string> Permissions { get; init; }` | The role's permissions, expanded. |

#### `RoleFacts(bool, IReadOnlyCollection<string>)`

```csharp
public RoleFacts(bool IsActive, IReadOnlyCollection<string> Permissions)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `IsActive`: Whether the role is active; an archived role grants nothing.
- `Permissions`: The role's permissions, expanded.

### `RoleStatus`

```csharp
public enum RoleStatus
```

Whether a role is in use. An archived role grants nothing, wherever it is still granted, and is not granted again.

| Member | Summary |
|---|---|
| `Active = 0` | In use. |
| `Archived = 1` | No longer in use. It stays, so the grants that name it still name something. |

### `RoleAggregate.NameIsValid`

```csharp
public sealed class NameIsValid : IInvariant<RoleAggregate<TRoleId, TTenantId>>
```

A role has a name of 1 to [`RoleAggregate.MaxNameLength`](#role-roleaggregatetroleid-ttenantid) characters and a description of at most [`RoleAggregate.MaxDescriptionLength`](#role-roleaggregatetroleid-ttenantid).

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(RoleAggregate<TRoleId, TTenantId>)`](#checkroleaggregatetroleid-ttenantid) | Runs the rule. |

#### `Check(RoleAggregate<TRoleId, TTenantId>)`

```csharp
public InvariantFailure? Check(RoleAggregate<TRoleId, TTenantId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `RoleAggregate.PermissionsNormalized`

```csharp
public sealed class PermissionsNormalized : IInvariant<RoleAggregate<TRoleId, TTenantId>>
```

A role's permissions are distinct, not blank, and in ordinal order.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(RoleAggregate<TRoleId, TTenantId>)`](#checkroleaggregatetroleid-ttenantid-1) | Runs the rule. |

#### `Check(RoleAggregate<TRoleId, TTenantId>)`

```csharp
public InvariantFailure? Check(RoleAggregate<TRoleId, TTenantId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `RoleArchived<TTenantId, TRoleId, TSeatId>`

```csharp
public sealed record RoleArchived<TTenantId, TRoleId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A role was archived, and grants nothing from now on.

| Member | Summary |
|---|---|
| [`RoleArchived(TTenantId, TRoleId, TenancyActor<TSeatId>?)`](#rolearchivedttenantid-troleid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant the role belongs to. |
| `TRoleId RoleId { get; init; }` | The role archived. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `RoleArchived(TTenantId, TRoleId, TenancyActor<TSeatId>?)`

```csharp
public RoleArchived(TTenantId TenantId, TRoleId RoleId, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant the role belongs to.
- `RoleId`: The role archived.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `RoleCreated<TTenantId, TRoleId, TSeatId>`

```csharp
public sealed record RoleCreated<TTenantId, TRoleId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A role was created, by hand or copied from a pack.

| Member | Summary |
|---|---|
| [`RoleCreated(TTenantId, TRoleId, string?, TenancyActor<TSeatId>?)`](#rolecreatedttenantid-troleid-string-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant the role belongs to. |
| `TRoleId RoleId { get; init; }` | The role created. |
| `string? FromPack { get; init; }` | The permission of the catalogue's pack it was copied from, or `null` for a role made by hand. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `RoleCreated(TTenantId, TRoleId, string?, TenancyActor<TSeatId>?)`

```csharp
public RoleCreated(TTenantId TenantId, TRoleId RoleId, string? FromPack, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant the role belongs to.
- `RoleId`: The role created.
- `FromPack`: The permission of the catalogue's pack it was copied from, or `null` for a role made by hand.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `RoleFollowedItsPack<TTenantId, TRoleId, TSeatId>`

```csharp
public sealed record RoleFollowedItsPack<TTenantId, TRoleId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A role made from a pack followed it, because the pack changed since the role was made or last followed it: every seat that holds the role gained `Added` and lost `Removed`, wherever it holds the role. The application's code changed the pack, so the tenant did not choose this: it is what a host tells the tenant's administrators about. The package sends no message itself.

| Member | Summary |
|---|---|
| [`RoleFollowedItsPack(TTenantId, TRoleId, string, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, TenancyActor<TSeatId>?)`](#rolefolloweditspackttenantid-troleid-string-ireadonlyliststring-ireadonlyliststring-ireadonlyliststring-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant the role belongs to. |
| `TRoleId RoleId { get; init; }` | The role. |
| `string Pack { get; init; }` | The key of the pack the role was made from, and followed. |
| `IReadOnlyList<string> Added { get; init; }` | The permissions the role grants from now on and did not before, the permissions they imply included, in ordinal order. |
| `IReadOnlyList<string> Removed { get; init; }` | The permissions the role granted before and no longer does, in ordinal order. |
| `IReadOnlyList<string> ManagingAccess { get; init; }` | The permissions of `Added` and `Removed` that manage access, as the catalogue marked them at the time, in ordinal order: empty when the change gives and takes no power over other people's access. A seat's own change of such a permission is an administrator's alone; this one the application made. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: system work, the application's own, unless it was begun for an operator. |

#### `RoleFollowedItsPack(TTenantId, TRoleId, string, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, TenancyActor<TSeatId>?)`

```csharp
public RoleFollowedItsPack(
    TTenantId TenantId,
    TRoleId RoleId,
    string Pack,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> ManagingAccess,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant the role belongs to.
- `RoleId`: The role.
- `Pack`: The key of the pack the role was made from, and followed.
- `Added`: The permissions the role grants from now on and did not before, the permissions they imply included, in ordinal order.
- `Removed`: The permissions the role granted before and no longer does, in ordinal order.
- `ManagingAccess`: The permissions of `Added` and `Removed` that manage access, as the catalogue marked them at the time, in ordinal order: empty when the change gives and takes no power over other people's access. A seat's own change of such a permission is an administrator's alone; this one the application made.
- `By`: Who made the change: system work, the application's own, unless it was begun for an operator.

### `RolePermissionsChanged<TTenantId, TRoleId, TSeatId>`

```csharp
public sealed record RolePermissionsChanged<TTenantId, TRoleId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

The permissions a role grants changed: every seat that holds the role gained `Added` and lost `Removed`, wherever it holds the role.

| Member | Summary |
|---|---|
| [`RolePermissionsChanged(TTenantId, TRoleId, IReadOnlyList<string>, IReadOnlyList<string>, TenancyActor<TSeatId>?)`](#rolepermissionschangedttenantid-troleid-ireadonlyliststring-ireadonlyliststring-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant the role belongs to. |
| `TRoleId RoleId { get; init; }` | The role. |
| `IReadOnlyList<string> Added { get; init; }` | The permissions the role grants from now on and did not before, the permissions they imply included, in ordinal order. |
| `IReadOnlyList<string> Removed { get; init; }` | The permissions the role granted before and no longer does, in ordinal order. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change. |

#### `RolePermissionsChanged(TTenantId, TRoleId, IReadOnlyList<string>, IReadOnlyList<string>, TenancyActor<TSeatId>?)`

```csharp
public RolePermissionsChanged(
    TTenantId TenantId,
    TRoleId RoleId,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant the role belongs to.
- `RoleId`: The role.
- `Added`: The permissions the role grants from now on and did not before, the permissions they imply included, in ordinal order.
- `Removed`: The permissions the role granted before and no longer does, in ordinal order.
- `By`: Who made the change.

### `RoleRenamed<TTenantId, TRoleId, TSeatId>`

```csharp
public sealed record RoleRenamed<TTenantId, TRoleId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A role's name or description changed.

| Member | Summary |
|---|---|
| [`RoleRenamed(TTenantId, TRoleId, TenancyActor<TSeatId>?)`](#rolerenamedttenantid-troleid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant the role belongs to. |
| `TRoleId RoleId { get; init; }` | The role renamed or described anew. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `RoleRenamed(TTenantId, TRoleId, TenancyActor<TSeatId>?)`

```csharp
public RoleRenamed(TTenantId TenantId, TRoleId RoleId, TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant the role belongs to.
- `RoleId`: The role renamed or described anew.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

## Invitation

Namespace `DDDToolkit.Supporting.Tenancy`.

### Invitation (`InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>`)

Declared in the application with `[InvitationAggregate<InvitationId>]`.

```csharp
public abstract class InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId> : AggregateRoot<TInvitationId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

An invitation into a tenant: an offer, to whoever holds its token, of a seat placed in one unit with one role there. The application declares its own class with [`InvitationAggregateAttribute<TInvitationId>`](#invitationaggregateattributetinvitationid).

No seat exists until the invitation is accepted. Accepting it makes the seat for the verified identity of the person who accepts, places it in [`UnitId`](#invitation-invitationaggregatetinvitationid-ttenantid-tunitid-troleid-tseatid) and grants it [`RoleId`](#invitation-invitationaggregatetinvitationid-ttenantid-tunitid-troleid-tseatid) there until [`GrantUntil`](#invitation-invitationaggregatetinvitationid-ttenantid-tunitid-troleid-tseatid), all in one save. Until then the invitation is the only thing there is, and cancelling it leaves nothing behind.

It is for an address, the one it is sent to, and that is all the address is: where to send it, and what the people who manage seats recognize it by. It never finds a person. What accepts an invitation is its token and a verified identity; an application that knows the verified address of that identity may pass it along, and an invitation sent elsewhere is then refused. The address is forgotten once the invitation is accepted or cancelled.

It suggests no name for the seat: a seat has none in Tenancy. What the seat is shown by is the application's, set in the callback of the use case that accepts.

The token is not here, and neither is its digest: the store keeps the digest apart from the invitation, so reading invitations never reads it.

What it offers does not change after it was issued: the tenant, the unit, the role, the end of the grant, when it ends itself and who issued it. An issuer's rights are asked again when it is accepted, by the use case.

**Type parameters**

- `TInvitationId`: The application's invitation id.
- `TTenantId`: The application's tenant id.
- `TUnitId`: The application's unit id.
- `TRoleId`: The application's role id.
- `TSeatId`: The application's seat id.

| Member | Summary |
|---|---|
| `MaxAddressLength = 254` | The longest address an invitation may be for: the longest an e-mail address may be. |
| [`InvitationAggregate()`](#invitationaggregate) | Parameterless constructor for persistence frameworks and serializers. |
| `TTenantId TenantId { get; }` | The tenant the invitation is into. |
| `string? Address { get; }` | The address the invitation is for, as it was given, while the invitation is open; `null` once it was accepted or cancelled. |
| `TUnitId UnitId { get; }` | The unit the seat is placed in, as its primary placement. |
| `TRoleId RoleId { get; }` | The role the seat is granted at that unit. |
| `DateTimeOffset? GrantUntil { get; }` | When the grant ends, or `null` for no end. Later than [`ExpiresAt`](#invitation-invitationaggregatetinvitationid-ttenantid-tunitid-troleid-tseatid). |
| `InvitationState State { get; }` | Whether it is open, accepted or cancelled. |
| `DateTimeOffset IssuedAt { get; }` | When it was issued. |
| `DateTimeOffset ExpiresAt { get; }` | The first moment it can no longer be accepted. |
| `TSeatId? IssuedBy { get; }` | The seat that issued it, or the seat system work issued it for; `null` when system work issued it with no seat acting. The seat a placement and a grant made from it keep as who made them. |
| `bool IssuedAsSystem { get; }` | Whether system work issued it. An invitation a seat issued gives no more than that seat may still give when it is accepted; one system work issued is not held to a seat. |
| `DateTimeOffset? AcceptedAt { get; }` | When it was accepted, or `null`. |
| `TSeatId? AcceptedAs { get; }` | The seat it made, or `null` while nobody accepted it. |
| `DateTimeOffset? ClosedAt { get; }` | When it was cancelled, or `null`. |
| [`IsOpenAt(DateTimeOffset)`](#isopenatdatetimeoffset) | Whether the invitation can be accepted at `moment`: it is open and its time has not run out. |
| [`IsFor(string?)`](#isforstring) | Whether the invitation is for `address`: the same address, whatever the case of its letters and the space around it. |
| [`Cancel(DateTimeOffset, TenancyActor<TSeatId>?)`](#canceldatetimeoffset-tenancyactortseatid) | Cancels an open invitation, one whose time ran out included: it can no longer be accepted, and forgets the address it was for. |
| [`GetOwnInvariantViolations()`](#getowninvariantviolations-5) | Runs the rules and `CheckInvariants()` seam of this aggregate, and returns every rule that is broken, without throwing. |
| [`EnsureOwnInvariants()`](#ensureowninvariants-5) | Runs the rules and `CheckInvariants()` seam of this aggregate, and throws when it finds something broken. |
| [`GetInvariantViolations()`](#getinvariantviolations-5) | Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and returns every rule that is broken, without throwing. |
| [`EnsureInvariants()`](#ensureinvariants-5) | Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and throws when it finds something broken. |

#### `InvitationAggregate()`

```csharp
protected InvitationAggregate()
```

Parameterless constructor for persistence frameworks and serializers.

#### `IsOpenAt(DateTimeOffset)`

```csharp
public bool IsOpenAt(DateTimeOffset moment)
```

Whether the invitation can be accepted at `moment`: it is open and its time has not run out. Decided from the clock every time it is asked, so nothing has to mark an invitation as run out for it to be so.

**Parameters**

- `moment`: The moment to ask about, usually now.

#### `IsFor(string?)`

```csharp
public bool IsFor(string? address)
```

Whether the invitation is for `address`: the same address, whatever the case of its letters and the space around it. False once the address is forgotten.

**Parameters**

- `address`: An address, such as the verified address of the identity that accepts.

#### `Cancel(DateTimeOffset, TenancyActor<TSeatId>?)`

```csharp
public void Cancel(DateTimeOffset at, TenancyActor<TSeatId>? by = null)
```

Cancels an open invitation, one whose time ran out included: it can no longer be accepted, and forgets the address it was for.

**Parameters**

- `at`: When.
- `by`: Who makes the change, for the event; `null` when nobody is named.

**Exceptions**

- `RefusalException`: `tenancy.invitation-state`: it was accepted or cancelled already.

#### `GetOwnInvariantViolations()`

```csharp
public override IReadOnlyList<InvariantViolation> GetOwnInvariantViolations()
```

Runs the rules and `CheckInvariants()` seam of this aggregate, and returns every rule that is broken, without throwing. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `EnsureOwnInvariants()`

```csharp
public override void EnsureOwnInvariants()
```

Runs the rules and `CheckInvariants()` seam of this aggregate, and throws when it finds something broken. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `GetInvariantViolations()`

```csharp
public override IReadOnlyList<InvariantViolation> GetInvariantViolations()
```

Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and returns every rule that is broken, without throwing. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

#### `EnsureInvariants()`

```csharp
public override void EnsureInvariants()
```

Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and throws when it finds something broken. A class declared with this parent's template replaces this with its own, which runs these rules and then its own; this one is for a class derived by hand.

### `InvitationAggregateAttribute<TInvitationId>`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class InvitationAggregateAttribute<TInvitationId> : Attribute
```

Declares the application's invitation class, for an application that invites people into a tenant. The generator derives it from [`InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>`](#invitation-invitationaggregatetinvitationid-ttenantid-tunitid-troleid-tseatid), closed over the invitation id given here and the tenant, unit, role and seat ids of the classes declared with the other templates:

```csharp
[InvitationAggregate<InvitationId>]
public sealed partial class ShopInvitation;
```

It is the one class of Tenancy's an application may leave out: without it there are no invitations, and nothing else changes.

**Type parameters**

- `TInvitationId`: The application's invitation id, a `readonly partial record struct` marked `[EntityId<T>]`. Tenancy makes a new one with its `Create()`, which the generator writes for an id over a `Guid` and an id over anything else declares itself (DDD00067).

### `InvitationState`

```csharp
public enum InvitationState
```

Where an invitation is in its life. It starts open and ends once: accepted, by the person who then has the seat, or cancelled. An open invitation whose time ran out stays open in this column; whether it can still be accepted is decided when somebody tries, from its end and the clock.

| Member | Summary |
|---|---|
| `Open = 0` | Issued, and not accepted or cancelled since. |
| `Accepted = 1` | Accepted: it made a seat, and makes no other. |
| `Cancelled = 2` | Cancelled before anybody accepted it. |

### `InvitationAggregate.AddressIsValid`

```csharp
public sealed class AddressIsValid : IInvariant<InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>>
```

An open invitation is for one valid address; one that is over has forgotten it.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>)`](#checkinvitationaggregatetinvitationid-ttenantid-tunitid-troleid-tseatid) | Runs the rule. |

#### `Check(InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>)`

```csharp
public InvariantFailure? Check(InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `InvitationAggregate.GrantOutlastsTheInvitation`

```csharp
public sealed class GrantOutlastsTheInvitation : IInvariant<InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>>
```

The grant an invitation offers does not end before the invitation does.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>)`](#checkinvitationaggregatetinvitationid-ttenantid-tunitid-troleid-tseatid-1) | Runs the rule. |

#### `Check(InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>)`

```csharp
public InvariantFailure? Check(InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `InvitationAccepted<TTenantId, TInvitationId, TSeatId>`

```csharp
public sealed record InvitationAccepted<TTenantId, TInvitationId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

An invitation was accepted, and made a seat. The seat's own events, added, placed and granted its role, are raised by the same save.

| Member | Summary |
|---|---|
| [`InvitationAccepted(TTenantId, TInvitationId, TSeatId, TenancyActor<TSeatId>?)`](#invitationacceptedttenantid-tinvitationid-tseatid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant. |
| `TInvitationId InvitationId { get; init; }` | The invitation. |
| `TSeatId SeatId { get; init; }` | The seat it made, which is the person who accepted. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the system, for the seat that issued the invitation. The person who accepts has no seat until the save is done, so acceptance is the application's work on what the issuer offered. |

#### `InvitationAccepted(TTenantId, TInvitationId, TSeatId, TenancyActor<TSeatId>?)`

```csharp
public InvitationAccepted(
    TTenantId TenantId,
    TInvitationId InvitationId,
    TSeatId SeatId,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant.
- `InvitationId`: The invitation.
- `SeatId`: The seat it made, which is the person who accepted.
- `By`: Who made the change: the system, for the seat that issued the invitation. The person who accepts has no seat until the save is done, so acceptance is the application's work on what the issuer offered.

### `InvitationCancelled<TTenantId, TInvitationId, TSeatId>`

```csharp
public sealed record InvitationCancelled<TTenantId, TInvitationId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

An open invitation was cancelled, and can no longer be accepted.

| Member | Summary |
|---|---|
| [`InvitationCancelled(TTenantId, TInvitationId, TenancyActor<TSeatId>?)`](#invitationcancelledttenantid-tinvitationid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant the invitation was into. |
| `TInvitationId InvitationId { get; init; }` | The invitation. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it. |

#### `InvitationCancelled(TTenantId, TInvitationId, TenancyActor<TSeatId>?)`

```csharp
public InvitationCancelled(
    TTenantId TenantId,
    TInvitationId InvitationId,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant the invitation was into.
- `InvitationId`: The invitation.
- `By`: Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an operator or a token the work was begun for ([`TenancyActor<TSeatId>`](callers.md#tenancyactortseatid)). `null` only where the aggregate was changed outside the package's use cases, with no actor handed to it.

### `InvitationIssued<TTenantId, TInvitationId, TUnitId, TRoleId, TSeatId>`

```csharp
public sealed record InvitationIssued<TTenantId, TInvitationId, TUnitId, TRoleId, TSeatId> : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

An invitation was issued: an offer of a seat, placed in a unit, with a role there.

| Member | Summary |
|---|---|
| [`InvitationIssued(TTenantId, TInvitationId, TUnitId, TRoleId, DateTimeOffset, TSeatId?, TenancyActor<TSeatId>?)`](#invitationissuedttenantid-tinvitationid-tunitid-troleid-datetimeoffset-tseatid-tenancyactortseatid) | The primary constructor: the type's summary says what it makes. |
| `TTenantId TenantId { get; init; }` | The tenant the invitation is into. |
| `TInvitationId InvitationId { get; init; }` | The invitation. |
| `TUnitId UnitId { get; init; }` | The unit the seat would be placed in. |
| `TRoleId RoleId { get; init; }` | The role the seat would hold there. |
| `DateTimeOffset ExpiresAt { get; init; }` | The first moment it can no longer be accepted. |
| `TSeatId? IssuedBy { get; init; }` | The seat the invitation keeps as its issuer, whose rights are asked again when it is accepted, or `null` when system work issued it with no seat acting. |
| `TenancyActor<TSeatId>? By { get; init; }` | Who made the change. |

#### `InvitationIssued(TTenantId, TInvitationId, TUnitId, TRoleId, DateTimeOffset, TSeatId?, TenancyActor<TSeatId>?)`

```csharp
public InvitationIssued(
    TTenantId TenantId,
    TInvitationId InvitationId,
    TUnitId UnitId,
    TRoleId RoleId,
    DateTimeOffset ExpiresAt,
    TSeatId? IssuedBy,
    TenancyActor<TSeatId>? By)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `TenantId`: The tenant the invitation is into.
- `InvitationId`: The invitation.
- `UnitId`: The unit the seat would be placed in.
- `RoleId`: The role the seat would hold there.
- `ExpiresAt`: The first moment it can no longer be accepted.
- `IssuedBy`: The seat the invitation keeps as its issuer, whose rights are asked again when it is accepted, or `null` when system work issued it with no seat acting.
- `By`: Who made the change.

## Shared value objects

Namespace `DDDToolkit.Supporting.Tenancy`.

### `GrantPeriod`

```csharp
public readonly record struct GrantPeriod
```

When a grant applies: from [`Starts`](#grantperiod), and until [`Ends`](#grantperiod) when it has an end.

Not a toolkit value object, because it is stored as the two columns of the row that holds it rather than as a value of its own; it only makes sure the two agree when a period is made.

| Member | Summary |
|---|---|
| `DateTimeOffset Starts { get; }` | The first moment the grant applies. |
| `DateTimeOffset? Ends { get; }` | The first moment it no longer applies, or `null` when it has no end. |
| [`Open(DateTimeOffset)`](#opendatetimeoffset) | A period from `starts` with no end. |
| [`Between(DateTimeOffset, DateTimeOffset?)`](#betweendatetimeoffset-datetimeoffset) | A period from `starts` to `ends`, or with no end when that is `null`. |
| [`AppliesAt(DateTimeOffset)`](#appliesatdatetimeoffset-1) | Whether the period covers `moment`: it has started, and has not ended. |

#### `Open(DateTimeOffset)`

```csharp
public static GrantPeriod Open(DateTimeOffset starts)
```

A period from `starts` with no end.

**Parameters**

- `starts`: The first moment the grant applies.

#### `Between(DateTimeOffset, DateTimeOffset?)`

```csharp
public static GrantPeriod Between(DateTimeOffset starts, DateTimeOffset? ends)
```

A period from `starts` to `ends`, or with no end when that is `null`.

**Parameters**

- `starts`: The first moment the grant applies.
- `ends`: The first moment it no longer applies.

**Exceptions**

- `RefusalException`: `tenancy.invalid-period`: it ends at or before it starts.

#### `AppliesAt(DateTimeOffset)`

```csharp
public bool AppliesAt(DateTimeOffset moment)
```

Whether the period covers `moment`: it has started, and has not ended.

**Parameters**

- `moment`: The moment to ask about, usually now.
