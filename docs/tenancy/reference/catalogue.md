---
title: "Tenancy API: Catalogue and role packs"
sidebar_label: Catalogue and role packs
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Tenancy API: Catalogue and role packs

The permissions the modules declare, the catalogue the host builds from them, the role packs a tenant's roles start from, and the sync that keeps those roles following their packs.

## Catalogue

Namespace `DDDToolkit.Supporting.Tenancy.Catalogue`.

### `ApplicationCatalogue`

```csharp
public sealed record ApplicationCatalogue
```

What the application adds to the catalogue, besides Tenancy's own permissions and what its modules contribute: role packs, permissions it owns itself, marks on permissions that manage access, and whether those permissions stay contained. It is data in code, built and checked once by [`TenancyCatalogue.Build`](#buildapplicationcatalogue-ienumerablepermission).

Every part is optional, and an application that needs none of them leaves the catalogue out: its modules contribute their permissions, every tenant starts with the role of [`TenancyPacks.DefaultAdministrators`](#defaultadministrators), given to its first seat, and the permissions that manage access stay contained. `new ApplicationCatalogue()` is that catalogue, and Tenancy builds it when `TenancyOptions.Catalogue` is not set. Tenancy asks for nothing more, because it decides nothing more with it: what kind of unit a unit is, a region or a site, is the application's to keep, as a field of its own unit class.

```csharp
public static ApplicationCatalogue Application { get; } = new(
    Packs: [new RolePack("viewer", "Viewer", "Looks at the orders", [ShopPermissions.OrdersView])],
    AccessManagingPermissions: [ShopPermissions.Refund]);
```

| Member | Summary |
|---|---|
| [`ApplicationCatalogue(IReadOnlyList<RolePack>?, IReadOnlyList<Permission>?, IReadOnlyList<string>?, bool)`](#applicationcatalogueireadonlylistrolepack-ireadonlylistpermission-ireadonlyliststring-bool) | The primary constructor: the type's summary says what it makes. |
| `IReadOnlyList<Permission>? Permissions { get; init; }` | The permissions the application owns itself, besides Tenancy's and the modules' contributions. A module's permissions are stated by the module, next to the code that asks for them, on a list it marks with `TenancyPermissionsAttribute`, and are not listed here. |
| `IReadOnlyList<string>? AccessManagingPermissions { get; init; }` | Permissions that manage access besides those marked where they are declared ([`Permission.ManagesAccess`](#managesaccess)): the application's own or any module's. Listing a permission that is already marked, Tenancy's included, changes nothing, so a module that starts marking its own permission breaks no application. A listed retired permission counts once it is live again. |
| [`ContainAccessManagingPermissions`](#containaccessmanagingpermissions) | Whether a seat hands on a permission that manages access only where it holds that permission itself: on unless the application turns it off. |
| `IReadOnlyList<RolePack> Packs { get; init; }` | The role packs, none when none are given: what is read is never `null`. |

#### `ApplicationCatalogue(IReadOnlyList<RolePack>?, IReadOnlyList<Permission>?, IReadOnlyList<string>?, bool)`

```csharp
public ApplicationCatalogue(
    IReadOnlyList<RolePack>? Packs = null,
    IReadOnlyList<Permission>? Permissions = null,
    IReadOnlyList<string>? AccessManagingPermissions = null,
    bool ContainAccessManagingPermissions = true)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Packs`: The role packs. Either none of them is an administrators' pack, and the catalogue adds [`TenancyPacks.DefaultAdministrators`](#defaultadministrators), which holds every live permission in a tenant of every shape; or there is one administrators' pack seeded for each shape of tenant ([`RolePack.SeededFor`](#rolepack)). Left out or empty: every tenant starts with the default administrators' role alone.
- `Permissions`: The permissions the application owns itself, besides Tenancy's and the modules' contributions. A module's permissions are stated by the module, next to the code that asks for them, on a list it marks with `TenancyPermissionsAttribute`, and are not listed here.
- `AccessManagingPermissions`: Permissions that manage access besides those marked where they are declared ([`Permission.ManagesAccess`](#managesaccess)): the application's own or any module's. Listing a permission that is already marked, Tenancy's included, changes nothing, so a module that starts marking its own permission breaks no application. A listed retired permission counts once it is live again.
- `ContainAccessManagingPermissions`: Whether a seat hands on a permission that manages access only where it holds that permission itself: on unless the application turns it off. While it is on:

  - a role that holds such a permission is given and taken away only by a seat that holds each of its permissions that manage access at that unit, for at least as long as the grant runs, and never by a seat to itself;
  - only an administrator puts such a permission into a role, takes one out, or archives a role that holds one;
  - a seat suspends, deactivates or reactivates another only where it could take away or give back each grant of a role that manages access that the change takes away or gives back;
  - a move of a unit gives or takes away such a permission from nobody where the mover does not hold it.

  On Postgres the database holds a seat, behind every query of its own, to the part a single statement shows: which roles that manage access it gives, changes and takes away at a unit, never to itself, the same for an invitation, and which seats holding one it stops. How long against its own hold, the administrator, and moves stay with the use cases, as they do for every rule of Tenancy's that a policy cannot see.

  Off, a role or a permission that manages access goes as one that manages none: a seat that holds [`TenancyPermissions.GrantsManage`](#tenancypermissions) at a unit gives every role there, to anyone placed there for as long as it says, and to itself for no longer than it holds that permission there; so in practice whoever holds a permission that manages access administers everything that permission reaches. Roles that manage no access go as they always do, and a move still gives the mover nothing. What keeps the access model sound stays either way: each use case asks its permission where it acts, a tenant keeps an administrator, and a seat keeps its identity and its tenant. Leave it on when the database can be reached without the application's handlers, through Supabase's Data API say.

  It is about a seat handing permissions on, not about the application: system work in a tenant holds every permission there and is never held to it. A handler that checked a condition of the application's own, a quiz passed say, gives the role inside `TenancyWork.BeginSystemIn`, with it on, for the calling seat and with a role the application chose, never one the request named. The export writes the setting into the access files, as it writes the marks, and on Postgres a start-up check refuses a database written with the other setting.

#### `ContainAccessManagingPermissions`

```csharp
public bool ContainAccessManagingPermissions { get; init; }
```

Whether a seat hands on a permission that manages access only where it holds that permission itself: on unless the application turns it off. While it is on:

- a role that holds such a permission is given and taken away only by a seat that holds each of its permissions that manage access at that unit, for at least as long as the grant runs, and never by a seat to itself;
- only an administrator puts such a permission into a role, takes one out, or archives a role that holds one;
- a seat suspends, deactivates or reactivates another only where it could take away or give back each grant of a role that manages access that the change takes away or gives back;
- a move of a unit gives or takes away such a permission from nobody where the mover does not hold it.

On Postgres the database holds a seat, behind every query of its own, to the part a single statement shows: which roles that manage access it gives, changes and takes away at a unit, never to itself, the same for an invitation, and which seats holding one it stops. How long against its own hold, the administrator, and moves stay with the use cases, as they do for every rule of Tenancy's that a policy cannot see.

Off, a role or a permission that manages access goes as one that manages none: a seat that holds [`TenancyPermissions.GrantsManage`](#tenancypermissions) at a unit gives every role there, to anyone placed there for as long as it says, and to itself for no longer than it holds that permission there; so in practice whoever holds a permission that manages access administers everything that permission reaches. Roles that manage no access go as they always do, and a move still gives the mover nothing. What keeps the access model sound stays either way: each use case asks its permission where it acts, a tenant keeps an administrator, and a seat keeps its identity and its tenant. Leave it on when the database can be reached without the application's handlers, through Supabase's Data API say.

It is about a seat handing permissions on, not about the application: system work in a tenant holds every permission there and is never held to it. A handler that checked a condition of the application's own, a quiz passed say, gives the role inside `TenancyWork.BeginSystemIn`, with it on, for the calling seat and with a role the application chose, never one the request named. The export writes the setting into the access files, as it writes the marks, and on Postgres a start-up check refuses a database written with the other setting.

### `Permission`

```csharp
public sealed record Permission
```

One permission of the catalogue: what a role grants, and what a use case asks for.

A permission is retired with `Retired` and never removed. A retired permission grants nothing and is not added to a role again, but a role that holds it keeps it, so retiring a permission breaks no role and no stored right.

| Member | Summary |
|---|---|
| `MaxNameLength = 128` | The longest permission: what a storage keeps a stored right's permission in. |
| [`Permission(string, string, string, IReadOnlyList<string>?, bool, int, bool)`](#permissionstring-string-string-ireadonlyliststring-bool-int-bool) | The primary constructor: the type's summary says what it makes. |
| `string Name { get; init; }` | The permission, such as `projects.edit`: lowercase, dotted, with at least one dot, and at most [`MaxNameLength`](#permission) characters. |
| `string Module { get; init; }` | The module that owns it, such as `Projects`. |
| `string Description { get; init; }` | What holding it lets a seat do, for the screens that assign roles. |
| `IReadOnlyList<string>? Implies { get; init; }` | Permissions that come with this one, such as the permission to view with the permission to edit. One hop only: a permission that another implies implies nothing itself. |
| `bool Retired { get; init; }` | Whether the permission is retired. |
| `int Order { get; init; }` | Where it is listed within its module; lower first. |
| [`ManagesAccess`](#managesaccess) | Whether holding the permission gives power over other people's access. |

#### `Permission(string, string, string, IReadOnlyList<string>?, bool, int, bool)`

```csharp
public Permission(
    string Name,
    string Module,
    string Description,
    IReadOnlyList<string>? Implies = null,
    bool Retired = false,
    int Order = 100,
    bool ManagesAccess = false)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Name`: The permission, such as `projects.edit`: lowercase, dotted, with at least one dot, and at most [`MaxNameLength`](#permission) characters.
- `Module`: The module that owns it, such as `Projects`.
- `Description`: What holding it lets a seat do, for the screens that assign roles.
- `Implies`: Permissions that come with this one, such as the permission to view with the permission to edit. One hop only: a permission that another implies implies nothing itself.
- `Retired`: Whether the permission is retired.
- `Order`: Where it is listed within its module; lower first.
- `ManagesAccess`: Whether holding the permission gives power over other people's access. A role holding such a permission is given and taken away only by a seat that holds the permission there itself, for at least as long, and never by a seat to itself, as long as the application keeps such permissions contained, which it does unless it turns [`ApplicationCatalogue.ContainAccessManagingPermissions`](#containaccessmanagingpermissions) off. Any other role is given by whoever holds [`TenancyPermissions.GrantsManage`](#tenancypermissions) where the seat is placed.

  Mark a permission when it should be given only by someone who holds it: power over roles, placements or the tree, or a decision the organization keeps for itself, such as naming a project's owner. A permission whose power reaches no further than the permissions of roles that manage no access may stay unmarked. Managing a project's crew is such a permission: a crew gives only permissions that act on its project.

  A permission nobody marks is given by every grants manager, to anyone placed where they hold [`TenancyPermissions.GrantsManage`](#tenancypermissions), themselves included. The application marks permissions it does not declare, a module's among them, with [`ApplicationCatalogue.AccessManagingPermissions`](#applicationcatalogue).

#### `ManagesAccess`

```csharp
public bool ManagesAccess { get; init; }
```

Whether holding the permission gives power over other people's access. A role holding such a permission is given and taken away only by a seat that holds the permission there itself, for at least as long, and never by a seat to itself, as long as the application keeps such permissions contained, which it does unless it turns [`ApplicationCatalogue.ContainAccessManagingPermissions`](#containaccessmanagingpermissions) off. Any other role is given by whoever holds [`TenancyPermissions.GrantsManage`](#tenancypermissions) where the seat is placed.

Mark a permission when it should be given only by someone who holds it: power over roles, placements or the tree, or a decision the organization keeps for itself, such as naming a project's owner. A permission whose power reaches no further than the permissions of roles that manage no access may stay unmarked. Managing a project's crew is such a permission: a crew gives only permissions that act on its project.

A permission nobody marks is given by every grants manager, to anyone placed where they hold [`TenancyPermissions.GrantsManage`](#tenancypermissions), themselves included. The application marks permissions it does not declare, a module's among them, with [`ApplicationCatalogue.AccessManagingPermissions`](#applicationcatalogue).

### `RolePack`

```csharp
public sealed record RolePack
```

A ready-made role the application offers. A tenant is given a copy of every pack seeded for its shape when it is provisioned, and the copy remembers the pack it came from; from then on the copy is the tenant's own to rename and to give other permissions. When the application changes the pack, the copy follows it once the host syncs the packs (`services.SyncRolePacks()`): what the pack gained is added, what it lost is taken out, and what the tenant changed itself stays.

| Member | Summary |
|---|---|
| `MaxKeyLength = 64` | The longest permission: what a storage keeps the pack a role was made from in. |
| [`RolePack(string, string, string, IReadOnlyList<string>, TenantShape?, bool, bool, int)`](#rolepackstring-string-string-ireadonlyliststring-tenantshape-bool-bool-int) | The primary constructor: the type's summary says what it makes. |
| `string Key { get; init; }` | The pack's key, unique in the catalogue, at most [`MaxKeyLength`](#rolepack) characters. |
| `string Name { get; init; }` | The name of the role made from it. |
| `string Description { get; init; }` | The description of the role made from it. |
| `IReadOnlyList<string> Permissions { get; init; }` | The permissions the role grants; the permissions they imply are added when the catalogue is built. An administrators' pack may leave the list empty, and then holds every live permission. |
| `TenantShape? SeededFor { get; init; }` | The one shape of tenant that is given a copy, or `null`, the default, for every shape. It says which tenants the pack is seeded into, not what the role is: a role has no shape. A tenant provisioned with that shape gets a copy, and so does one that changes to it and has none yet; a tenant of the other shape never gets one from the catalogue. A pack that `SeedOnProvision` leaves out is seeded into no tenant. |
| [`Administers`](#administers) | Whether this is the administrators' pack of the shapes it is seeded for: the first administrator is granted it. |
| `bool SeedOnProvision { get; init; }` | Whether a newly provisioned tenant gets a copy. |
| `int Order { get; init; }` | Where it is listed; lower first. |

#### `RolePack(string, string, string, IReadOnlyList<string>, TenantShape?, bool, bool, int)`

```csharp
public RolePack(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<string> Permissions,
    TenantShape? SeededFor = null,
    bool Administers = false,
    bool SeedOnProvision = true,
    int Order = 100)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Key`: The pack's key, unique in the catalogue, at most [`MaxKeyLength`](#rolepack) characters.
- `Name`: The name of the role made from it.
- `Description`: The description of the role made from it.
- `Permissions`: The permissions the role grants; the permissions they imply are added when the catalogue is built. An administrators' pack may leave the list empty, and then holds every live permission.
- `SeededFor`: The one shape of tenant that is given a copy, or `null`, the default, for every shape. It says which tenants the pack is seeded into, not what the role is: a role has no shape. A tenant provisioned with that shape gets a copy, and so does one that changes to it and has none yet; a tenant of the other shape never gets one from the catalogue. A pack that `SeedOnProvision` leaves out is seeded into no tenant.
- `Administers`: Whether this is the administrators' pack of the shapes it is seeded for: the first administrator is granted it. Either it lists no permissions, and then holds every live permission of the catalogue once built, a permission declared later included, which the roles made from it before get when the host syncs the packs; or it lists permissions, which must include every one of Tenancy's live permissions and every live permission that manages access, directly or through a permission that implies it. With those an administrator runs the tenant's access and gives every role, since a role that manages access is given only by a seat holding its permissions that do, while the role itself holds no permission to the application's work that the pack does not list. The catalogue refuses a listing pack that leaves one out, so marking another permission stops the application at start-up until the pack lists it.

  The list is what the administrators' role starts with, not a wall around the seat. An administrator still puts any permission into a role, and gives itself a role that manages no access, as every seat that manages grants may; the grant records the seat that gave it.

  An application that marks no pack as administering gets [`TenancyPacks.DefaultAdministrators`](#defaultadministrators), which lists no permissions, for every shape. One that marks a pack marks one for every shape: a single one with no `SeededFor`, or one for each shape, with that shape as its `SeededFor`.
- `SeedOnProvision`: Whether a newly provisioned tenant gets a copy.
- `Order`: Where it is listed; lower first.

#### `Administers`

```csharp
public bool Administers { get; init; }
```

Whether this is the administrators' pack of the shapes it is seeded for: the first administrator is granted it. Either it lists no permissions, and then holds every live permission of the catalogue once built, a permission declared later included, which the roles made from it before get when the host syncs the packs; or it lists permissions, which must include every one of Tenancy's live permissions and every live permission that manages access, directly or through a permission that implies it. With those an administrator runs the tenant's access and gives every role, since a role that manages access is given only by a seat holding its permissions that do, while the role itself holds no permission to the application's work that the pack does not list. The catalogue refuses a listing pack that leaves one out, so marking another permission stops the application at start-up until the pack lists it.

The list is what the administrators' role starts with, not a wall around the seat. An administrator still puts any permission into a role, and gives itself a role that manages no access, as every seat that manages grants may; the grant records the seat that gave it.

An application that marks no pack as administering gets [`TenancyPacks.DefaultAdministrators`](#defaultadministrators), which lists no permissions, for every shape. One that marks a pack marks one for every shape: a single one with no `SeededFor`, or one for each shape, with that shape as its `SeededFor`.

### `TenancyCatalogue`

```csharp
public sealed class TenancyCatalogue
```

Every permission and role pack the application has, checked once and then only read: Tenancy's permissions and the default administrators' pack, the application's own permissions and packs, and what its modules contribute.

It lives in code, not in a table. [`Build`](#buildapplicationcatalogue-ienumerablepermission) checks it as a whole and reports every problem at once, so a catalogue that does not hold together stops the application at start-up rather than half way through provisioning a tenant.

A permission is retired, never removed. A retired permission, and a stored permission the catalogue no longer knows because it was removed from code against that rule, both grant nothing: they are not live. A role may keep them; nothing adds them anew.

Some permissions manage access: they give power over other people's access, and a role holding one is given and taken away only by a seat that holds it there, while the catalogue keeps them contained ([`ContainAccessManagingPermissions`](#tenancycatalogue)). Tenancy's own permissions do, all but [`TenancyPermissions.HistoryView`](#tenancypermissions), which only reads; the application marks others where they are declared ([`Permission.ManagesAccess`](#managesaccess)) or in [`ApplicationCatalogue.AccessManagingPermissions`](#applicationcatalogue). A permission that is not live manages nothing.

| Member | Summary |
|---|---|
| `IReadOnlyList<Permission> Permissions { get; }` | Every permission, retired ones included: Tenancy's, the application's and the contributions', by module, order and permission. [`Permission.ManagesAccess`](#managesaccess) is set on every permission that is marked, where it is declared or by the application. |
| `IReadOnlyList<string> LivePermissions { get; }` | Every permission that is declared and not retired, in ordinal order. |
| `IReadOnlyList<string> AccessManagingPermissions { get; }` | Every live permission that manages access, in ordinal order: marked where it is declared, or by the application in [`ApplicationCatalogue.AccessManagingPermissions`](#applicationcatalogue). |
| `IReadOnlyList<RolePack> Packs { get; }` | The role packs as built, their permissions expanded. An administrators' pack that lists no permissions holds [`LivePermissions`](#tenancycatalogue); one that lists permissions holds those, as any other pack does. When the application declares no administrators' pack, [`TenancyPacks.DefaultAdministrators`](#defaultadministrators) comes first, holding [`LivePermissions`](#tenancycatalogue). |
| `bool HasDefaultAdministrators { get; }` | Whether the application declared no administrators' pack, so that [`Packs`](#tenancycatalogue) holds [`TenancyPacks.DefaultAdministrators`](#defaultadministrators), added by [`Build`](#buildapplicationcatalogue-ienumerablepermission). The pack of that permission is then the package's, named in the languages the package ships; otherwise every pack is the application's. |
| `bool ContainAccessManagingPermissions { get; }` | Whether a seat hands on the permissions of [`AccessManagingPermissions`](#tenancycatalogue) only where it holds them itself, as the application says in [`ApplicationCatalogue.ContainAccessManagingPermissions`](#containaccessmanagingpermissions): on unless it turns it off. The use cases ask it before every rule of containment, and the export writes it into the database's access files with the rest of the catalogue. It changes which permissions are marked in nothing: [`AccessManagingPermissions`](#tenancycatalogue) and [`ManagesAccess`](#managesaccessstring) answer the same either way. |
| [`Build(IEnumerable<Permission>)`](#buildienumerablepermission) | Builds the catalogue of an application that adds nothing to it, from the modules' contributions alone, as the registration does when `TenancyOptions.Catalogue` is not set: Tenancy's permissions, the modules' permissions, and [`TenancyPacks.DefaultAdministrators`](#defaultadministrators) as the one pack. |
| [`Build(ApplicationCatalogue, IEnumerable<Permission>)`](#buildapplicationcatalogue-ienumerablepermission) | Builds the catalogue from the application's part and the modules' contributions, and checks it as a whole. |
| [`Knows(string)`](#knowsstring) | Whether the catalogue declares `permission`, retired or not. |
| [`IsLive(string)`](#islivestring) | Whether `permission` is declared and not retired. |
| [`ManagesAccess(string)`](#managesaccessstring) | Whether `permission` manages access: it is declared, not retired, and marked, where it is declared or by the application. |
| [`AccessManagingPermissionsOf(RoleFacts)`](#accessmanagingpermissionsofrolefacts) | The permissions of a role that manage access, each once, in ordinal order: none for an archived role, which grants nothing, and none that are not live. |
| [`Expand(IEnumerable<string>)`](#expandienumerablestring) | The permissions a role made of `permissions` grants: trimmed, with the live permissions each one implies added, distinct, in ordinal order. |
| [`PacksFor(TenantShape)`](#packsfortenantshape) | The packs a newly provisioned tenant of `shape` gets a copy of, in their order: those seeded on provision whose [`RolePack.SeededFor`](#rolepack) is that shape or every shape. |
| [`AdministratorPackFor(TenantShape)`](#administratorpackfortenantshape) | The pack the first administrator of a tenant of `shape` is granted: the application's, or [`TenancyPacks.DefaultAdministrators`](#defaultadministrators) when it declares none. |
| [`RequireAskable(string)`](#requireaskablestring) | Checks a permission that code is about to ask about. |

#### `Build(IEnumerable<Permission>)`

```csharp
public static TenancyCatalogue Build(IEnumerable<Permission> contributed)
```

Builds the catalogue of an application that adds nothing to it, from the modules' contributions alone, as the registration does when `TenancyOptions.Catalogue` is not set: Tenancy's permissions, the modules' permissions, and [`TenancyPacks.DefaultAdministrators`](#defaultadministrators) as the one pack. What an export that runs without the registration builds the same catalogue with: `TenancyCatalogue.Build(TenancyPermissionsOfModules.All)`, the list Tenancy's generator writes into a project that composes the modules, from the lists they mark with `TenancyPermissionsAttribute`.

**Parameters**

- `contributed`: The permissions the modules contribute.

**Exceptions**

- [`TenancyCatalogueException`](#tenancycatalogueexception): Something does not hold together; every problem found is listed.

#### `Build(ApplicationCatalogue, IEnumerable<Permission>)`

```csharp
public static TenancyCatalogue Build(
    ApplicationCatalogue application,
    IEnumerable<Permission> contributed)
```

Builds the catalogue from the application's part and the modules' contributions, and checks it as a whole. An application that declares no administrators' pack gets [`TenancyPacks.DefaultAdministrators`](#defaultadministrators), for every shape.

**Parameters**

- `application`: The application's packs, permissions and marks. An application that adds none builds with [`Build`](#buildienumerablepermission), which passes `new ApplicationCatalogue()`.
- `contributed`: The permissions the modules contribute: in an export, `TenancyPermissionsOfModules.All`, which Tenancy's generator writes into a project that composes the modules, as the host registers them.

**Exceptions**

- [`TenancyCatalogueException`](#tenancycatalogueexception): Something does not hold together; every problem found is listed.

#### `Knows(string)`

```csharp
public bool Knows(string permission)
```

Whether the catalogue declares `permission`, retired or not.

#### `IsLive(string)`

```csharp
public bool IsLive(string permission)
```

Whether `permission` is declared and not retired. A permission the catalogue does not know is not live.

#### `ManagesAccess(string)`

```csharp
public bool ManagesAccess(string permission)
```

Whether `permission` manages access: it is declared, not retired, and marked, where it is declared or by the application. A permission the catalogue does not know manages nothing.

#### `AccessManagingPermissionsOf(RoleFacts)`

```csharp
public IReadOnlyList<string> AccessManagingPermissionsOf(RoleFacts role)
```

The permissions of a role that manage access, each once, in ordinal order: none for an archived role, which grants nothing, and none that are not live. A role manages access when this is not empty, and is then given and taken away only by a seat that holds these permissions there, while [`ContainAccessManagingPermissions`](#tenancycatalogue) is on.

**Parameters**

- `role`: What is known about the role.

#### `Expand(IEnumerable<string>)`

```csharp
public IReadOnlyList<string> Expand(IEnumerable<string> permissions)
```

The permissions a role made of `permissions` grants: trimmed, with the live permissions each one implies added, distinct, in ordinal order.

**Parameters**

- `permissions`: The permissions asked for.

**Exceptions**

- `RefusalException`: `tenancy.unknown-permission`, with the offending permissions in `Permissions`: a permission the catalogue does not know, or has retired.

#### `PacksFor(TenantShape)`

```csharp
public IEnumerable<RolePack> PacksFor(TenantShape shape)
```

The packs a newly provisioned tenant of `shape` gets a copy of, in their order: those seeded on provision whose [`RolePack.SeededFor`](#rolepack) is that shape or every shape.

#### `AdministratorPackFor(TenantShape)`

```csharp
public RolePack AdministratorPackFor(TenantShape shape)
```

The pack the first administrator of a tenant of `shape` is granted: the application's, or [`TenancyPacks.DefaultAdministrators`](#defaultadministrators) when it declares none.

#### `RequireAskable(string)`

```csharp
public void RequireAskable(string permission)
```

Checks a permission that code is about to ask about. A permission the catalogue does not know is a mistake in code, not something a caller did, so it is an `ArgumentException` rather than a refusal: a typo, or a module's permission that never reached the catalogue, which the message names the cure of. A retired permission may be asked about, and holds nowhere.

**Exceptions**

- `ArgumentException`: The catalogue does not know `permission`.

### `TenancyPermissions`

```csharp
public static class TenancyPermissions
```

Tenancy's own permissions. They are always in the catalogue, and no other module may declare a permission under `tenancy.`.

A permission held at the root holds for the whole tenant; a permission held at a unit holds for that unit and every unit below it. The permissions that manage the tenant itself (settings, adding seats, roles) are asked for at the root.

Every one of them but [`HistoryView`](#tenancypermissions) manages access: a role holding one is given only by a seat that holds it there. Reading what happened changes nobody's rights, so that permission is given like any other.

| Member | Summary |
|---|---|
| `SettingsManage = "tenancy.settings.manage"` | The tenant's own settings: its name, and its shape. Asked for tenant-wide. |
| `UnitsManage = "tenancy.units.manage"` | Change the tree below a unit: new units, names, parents and archiving. Asked for at a unit, and for a move at both parents. A move gives the mover no permission it lacks at the unit, and none for longer than it holds it there, and gives or takes away from anyone a permission that manages access only when the mover holds it at the unit for at least as long as that grant runs. |
| `SeatsManage = "tenancy.seats.manage"` | Add seats and suspend, reactivate or deactivate them, asked for tenant-wide; place a seat in a unit, withdraw it or make a placement primary, asked for at the unit. A seat that holds a role that manages access is suspended, reactivated or deactivated only by a seat that also holds that role's permissions that do, where and for as long as the seat holds them. |
| `GrantsManage = "tenancy.grants.manage"` | Give seats organization roles at a unit and take them away. Asked for at the unit. Holding it is enough to give roles: its holder gives any role that manages no access to a seat placed there or below without holding the role's permissions, and to itself for no longer than it holds this permission. A role that manages access it gives only while holding there each of its permissions that do, and never to itself. |
| `RolesManage = "tenancy.roles.manage"` | The tenant's roles: new ones, their names, their permissions and archiving. Asked for tenant-wide. |
| `HistoryView = "tenancy.history.view"` | Read the tenant's access history: which seat, role, grant or unit changed, when, and who changed it. Asked for tenant-wide. It manages no access, so a role that holds it is given like any other; an administrators' pack that lists its permissions holds it, as it holds every permission of Tenancy's. |
| `AdministratorPermission = "tenancy.roles.manage"` | The permission that makes a seat an administrator: whoever manages roles can give themselves any permission, so that is the permission the last-administrator rule counts. |
| `Module = "Tenancy"` | The module Tenancy's permissions are listed under. |
| `static IReadOnlyList<Permission> All { get; }` | Tenancy's permissions, as the catalogue lists them. |

### `TenancyPacks`

```csharp
public static class TenancyPacks
```

Tenancy's own role pack, which the catalogue adds when the application declares no administrators' pack.

A tenant cannot start without one: the first seat is granted the administrators' role, and it is that role that holds the permissions to manage access, so somebody can give the next role. Every application needs such a pack, and most need nothing more of it than every permission, so the application need not declare it. Once it declares an administrators' pack of its own, for any shape, it declares them for every shape, and this one is not added.

| Member | Summary |
|---|---|
| `DefaultAdministratorsKey = "administrator"` | The permission of [`DefaultAdministrators`](#defaultadministrators): the pack its roles remember they came from, and what `TenantToProvision.RoleIds` and `ProvisionedTenant.RolesByPack` name it by. |
| [`DefaultAdministrators`](#defaultadministrators) | The administrators' pack [`TenancyCatalogue.Build`](#buildapplicationcatalogue-ienumerablepermission) adds when the application declares none: named "Administrator", seeded for every shape on provision and listed first. |

#### `DefaultAdministrators`

```csharp
public static RolePack DefaultAdministrators { get; }
```

The administrators' pack [`TenancyCatalogue.Build`](#buildapplicationcatalogue-ienumerablepermission) adds when the application declares none: named "Administrator", seeded for every shape on provision and listed first. It lists no permissions, so the role made from it holds every live permission of the catalogue as built, as an administrators' pack of the application's own that lists none does. A permission a module declares later is the pack's too: a tenant provisioned after that gets it with the role, and a role made from the pack before gets it when the host syncs the packs (`services.SyncRolePacks()`).

Its name and description come in English and in Dutch, the languages the package ships: a tenant provisioned in Dutch gets a role called "Beheerder". An application's [`IRolePackTexts`](#irolepacktexts) is asked first, by this permission, so it names the role in any language, Dutch included.

### `IRolePackTexts`

```csharp
public interface IRolePackTexts
```

A role pack's name and description in one language, for the roles a tenant is given from the packs.

The catalogue declares each pack once, with one name and one description. A tenant that works in another language should not get its roles named in the catalogue's, so the use cases ask this when they make a role from a pack: when a tenant is provisioned with a `Language`, and when a change of shape copies the packs the tenant has no copy of yet. The texts are chosen once. From then on the role is the tenant's own, renamed like any other, and a later change of the tenant's language changes no role.

The application implements it over its own resources and registers it; without a language a role gets the catalogue's texts, and so it does without one registered, the default administrators' pack apart (below).

[`TenancyPacks.DefaultAdministrators`](#defaultadministrators), which the catalogue adds when the application declares no administrators' pack, is asked for like any pack, by [`TenancyPacks.DefaultAdministratorsKey`](#tenancypacks). Where this answers `null` for it, or none is registered, its role gets the package's own texts in the language: English and Dutch ship with the package, and any other language gets the English.

```csharp
public sealed class ShopPackTexts(IStringLocalizer<ShopPacks> texts) : IRolePackTexts
{
    public (string Name, string Description)? For(RolePack pack, CultureInfo culture)
    {
        using (CultureScope.Use(culture))
        {
            var name = texts[pack.Key + ".name"];
            return name.ResourceNotFound ? null : (name.Value, texts[pack.Key + ".description"].Value);
        }
    }
}

services.AddSingleton<IRolePackTexts, ShopPackTexts>();
```

| Member | Summary |
|---|---|
| [`For(RolePack, CultureInfo)`](#forrolepack-cultureinfo) | The texts of `pack` in `culture`, or `null` to keep the catalogue's own; for [`TenancyPacks.DefaultAdministrators`](#defaultadministrators), to keep the package's, in English or Dutch. |

#### `For(RolePack, CultureInfo)`

```csharp
(string Name, string Description)? For(RolePack pack, CultureInfo culture)
```

The texts of `pack` in `culture`, or `null` to keep the catalogue's own; for [`TenancyPacks.DefaultAdministrators`](#defaultadministrators), to keep the package's, in English or Dutch.

What comes back is checked like any role's name and description: a name that is blank or too long is refused with `tenancy.name-invalid`, and one another role of the tenant has already, ignoring case, with `tenancy.role-name-taken`. A tenant holds a copy of every pack seeded for its shape, and a flat tenant that turns hierarchical is given the packs seeded for a hierarchical one next to them, so every pack needs a name of its own in every language.

**Parameters**

- `pack`: The pack a role is being made from, as the catalogue built it.
- `culture`: The language the role is for: the tenant's.

### `PermissionContribution`

```csharp
public sealed class PermissionContribution
```

Permissions added to the catalogue besides the application's own, so a module that owns permissions declares them next to the code that asks for them rather than in the application's catalogue: the host's `AddTenancyPermissionsOfModules()` adds every module's list as one, and `AddTenancyPermissions` a list by hand. Contributions add up.

| Member | Summary |
|---|---|
| [`PermissionContribution(IEnumerable<Permission>)`](#permissioncontributionienumerablepermission) | The primary constructor: the type's summary says what it makes. |
| `IReadOnlyList<Permission> Permissions { get; }` | The permissions. |

#### `PermissionContribution(IEnumerable<Permission>)`

```csharp
public PermissionContribution(IEnumerable<Permission> permissions)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `permissions`: The permissions.

### `TenancyCatalogueAttribute`

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class TenancyCatalogueAttribute : Attribute
```

Marks the application's part of the catalogue, the one it hands Tenancy as `TenancyOptions.Catalogue`, so the program that exports the database's policies writes them from the same catalogue:

```csharp
public static class ShopCatalogue
{
    [TenancyCatalogue]
    public static ApplicationCatalogue Application { get; } = new(
        Packs: [new RolePack("viewer", "Viewer", "Looks at the orders", [ShopPermissions.OrdersView])]);
}
```

Tenancy on Postgres writes its functions, policies and triggers into every application that references it, from the catalogue the application runs with: its marks decide which roles manage access, and so which grants the policies contain, and its packs which roles a settings manager may add. The export runs before the application starts, so it builds that catalogue itself, as the host's registration does: this part, with the permissions of every module the exporting project references, every list marked `TenancyPermissionsAttribute`. An application that marks nothing gets what an application that leaves `TenancyOptions.Catalogue` unset gets: `new ApplicationCatalogue()`, Tenancy's permissions and the modules' and the default administrators' pack.

The member is a static property or field of type [`ApplicationCatalogue`](#applicationcatalogue), readable from the project that runs the export, which references the project that declares it: in a library it is public, in public types, which that library's own build checks (DDD00070), since the exporting project does not see it otherwise. One member is marked in all of them: the export cannot choose between two, and reports them, as it does one of another type (DDD00071).

### `TenancyCatalogueException`

```csharp
public sealed class TenancyCatalogueException : InvalidOperationException
```

Thrown by [`TenancyCatalogue.Build`](#buildapplicationcatalogue-ienumerablepermission) when the catalogue does not hold together. Every problem found is reported at once, so one start-up shows all of them.

| Member | Summary |
|---|---|
| [`TenancyCatalogueException(IReadOnlyList<string>)`](#tenancycatalogueexceptionireadonlyliststring) | Reports `problems`. |
| `IReadOnlyList<string> Problems { get; }` | Everything that is wrong, one sentence each. |

#### `TenancyCatalogueException(IReadOnlyList<string>)`

```csharp
public TenancyCatalogueException(IReadOnlyList<string> problems)
```

Reports `problems`.

**Parameters**

- `problems`: Everything that is wrong, one sentence each.

## Role packs

Namespace `DDDToolkit.Supporting.Tenancy`.

### `IRolePackSync`

```csharp
public interface IRolePackSync
```

Brings every tenant's roles up to the packs they were made from: the use case behind `services.SyncRolePacks()`, for a host that runs it itself, from a deployment step or an operator's endpoint.

```csharp
var report = await services.GetRequiredService<IRolePackSync>().SyncAsync(cancellationToken);
```

It visits the active and the suspended tenants, one after the other, and in each runs `RoleCommands.FollowPacksAsync` as Tenancy's system work in that tenant: it begins that work itself, so whoever calls it needs no caller of its own, and what it may do in a tenant is what system work there may. The events it raises name the system as who made the change. Each tenant is a unit of work of its own: a tenant whose sync fails is named in the report and left for the next run, and the others go on. A tenant another run changed at the same moment is tried again, and then finds nothing left to change.

Tenancy's storage package registers it with `AddTenancy`; a host on other storage registers its own.

| Member | Summary |
|---|---|
| [`SyncAsync(CancellationToken)`](#syncasynccancellationtoken) | Makes every role of every active or suspended tenant follow the pack it was made from. |

#### `SyncAsync(CancellationToken)`

```csharp
Task<RolePackSyncReport> SyncAsync(CancellationToken cancellationToken)
```

Makes every role of every active or suspended tenant follow the pack it was made from.

**Parameters**

- `cancellationToken`: Cancels the work, between two tenants or inside one.

**Returns**: What it did, counted over the tenants, with every tenant it could not sync.

### `RolePackSyncReport`

```csharp
public sealed record RolePackSyncReport
```

What a sync of the role packs did ([`IRolePackSync`](#irolepacksync)), counted over every tenant it visited. Which role gained or lost which permission is on the events it raised, one per role that changed.

| Member | Summary |
|---|---|
| [`RolePackSyncReport(int, int, int, int, IReadOnlyList<(string Tenant, Exception Error)>)`](#rolepacksyncreportint-int-int-int-ireadonlyliststring-tenant-exception-error) | The primary constructor: the type's summary says what it makes. |
| `int Tenants { get; init; }` | How many tenants it visited. |
| `int RolesChanged { get; init; }` | How many roles gained or lost a permission, each with its event. |
| `int RolesKeptForAnAdministrator { get; init; }` | How many roles it left as they were, because following their pack would leave a tenant without an administrator. |
| `int RolesWithoutTheirPack { get; init; }` | How many roles were made from a pack the catalogue no longer has, and kept their permissions. |
| `IReadOnlyList<(string Tenant, Exception Error)> Failed { get; init; }` | Each tenant it could not sync, by its id, with what went wrong; the next run tries it again. |
| `bool Succeeded { get; }` | Whether every tenant was synced. |

#### `RolePackSyncReport(int, int, int, int, IReadOnlyList<(string Tenant, Exception Error)>)`

```csharp
public RolePackSyncReport(
    int Tenants,
    int RolesChanged,
    int RolesKeptForAnAdministrator,
    int RolesWithoutTheirPack,
    IReadOnlyList<(string Tenant, Exception Error)> Failed)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Tenants`: How many tenants it visited.
- `RolesChanged`: How many roles gained or lost a permission, each with its event.
- `RolesKeptForAnAdministrator`: How many roles it left as they were, because following their pack would leave a tenant without an administrator.
- `RolesWithoutTheirPack`: How many roles were made from a pack the catalogue no longer has, and kept their permissions.
- `Failed`: Each tenant it could not sync, by its id, with what went wrong; the next run tries it again.
