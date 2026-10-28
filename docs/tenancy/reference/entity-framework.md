---
title: "Tenancy API: Entity Framework"
sidebar_label: Entity Framework
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Tenancy API: Entity Framework

The public API of `DDDToolkit.Supporting.Tenancy.EntityFramework`, by folder, with the marker `[OfTenant]` from `DDDToolkit.Supporting.Tenancy.Abstractions`. `db.Tenancy()` is with the queries, under [Queries](queries.md#queries). The services a host registers, `AddTenancy`, `AddTenancyInvitations` and `AddTenancyAccess`, are with the managers, under [Registration](managers.md#registration).

## Mapping

How a context maps Tenancy. A class marks the property that holds its tenant with `[OfTenant]`. The marker names its convention with `[ModelConvention]`, and every context that calls `AddDDDToolkitConventions()` runs that convention. The convention gives the class the tenant filter and Tenancy's save check. A context that keeps the tenant in another property, or in a column of its own, calls `ScopeToTenant` on the class instead, and that call wins. `AddTenancy`, `AddTenancyReadModel` and `AddTenancyReadFunctions` on the model builder map Tenancy's own tables, its read model, or its read functions.

### `OfTenantAttribute`

```csharp
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class OfTenantAttribute : Attribute
```

Namespace `DDDToolkit.Supporting.Tenancy`.

Marks the property of an aggregate root that says which tenant it belongs to. It is a fact about what the aggregate is, said in the domain beside its `[Owner]` and its `[At]`. It is no rule about who may do what with it:

```csharp
[AggregateRoot<ProjectId>]
public sealed partial class Project
{
    [OfTenant] public TenantId TenantId { get; private set; }
}
```

Tenancy's Entity Framework package maps it through `AddDDDToolkitConventions()`. Every context that maps the class keeps it to the current caller's tenant, with the tenant filter on its reads and Tenancy's save check on its writes, as `ScopeToTenant(row => row.TenantId)` does. The context writes no line for it.

**Remarks**

The property is of the application's tenant id, never nullable, since a row of no tenant is kept to none. A class has one, on the root of a hierarchy the context stores. On a type the context owns it marks nothing, since an owned row is kept to its tenant through its owner. A model that breaks one of these is refused while it is built.

A context that calls `ScopeToTenant` on the class keeps that mapping instead, for a tenant held in another property or a column of its own. Without Tenancy's Entity Framework package in the application, a model with the marker is refused while it is built.

The marker is in a package of its own, with no Entity Framework and nothing else of Tenancy's. A domain project that sees only Tenancy's ids references this package. Tenancy's own package references it, so a project with Tenancy has the marker as well.

### `OfTenantConvention`

```csharp
public sealed class OfTenantConvention : IModelFinalizingConvention, IConvention
```

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

Keeps every class whose tenant is marked [`OfTenantAttribute`](#oftenantattribute) to the current Tenancy user's tenant, as [`TenancyModelBuilderExtensions.ScopeToTenant<TEntity, TTenantId>`](#scopetotenantentitytypebuildertentity-expressionfunctentity-ttenantid) keeps a class it is called on: the tenant filter on its reads, and Tenancy's save check on its writes. A context needs no line for it:

```csharp
[AggregateRoot<ProjectId>]
public sealed partial class Project
{
    [OfTenant] public TenantId TenantId { get; private set; }
}

// In the context: nothing for the tenant.
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    => configurationBuilder.AddDDDToolkitConventions();
```

The marker names this convention, and `AddDDDToolkitConventions()` runs it in every model that maps a class with the marker, when the model is finalized. It gives the class what `ScopeToTenant(row => row.TenantId)` gives: the named filter [`TenancyQueryFilter.Name`](#tenancyqueryfilter) next to the class's other named filters, the property for the save check, which `UseDDDToolkit` adds, and the property fixed once the row is there. A class the context keeps to a tenant itself, with `ScopeToTenant`, keeps that mapping: the context's call wins.

The marked property is of the application's tenant id, never nullable, and a class has one. It sits on the root of a hierarchy the context stores, since a filter is the root's. A type the context owns is kept through its owner, so a marker on it maps nothing. A model that breaks one of these is refused while it is built, naming the class.

| Member | Summary |
|---|---|
| [`ProcessModelFinalizing(IConventionModelBuilder, IConventionContext<IConventionModelBuilder>)`](#processmodelfinalizingiconventionmodelbuilder-iconventioncontexticonventionmodelbuilder) | Called when a model is being finalized. |

#### `ProcessModelFinalizing(IConventionModelBuilder, IConventionContext<IConventionModelBuilder>)`

```csharp
public void ProcessModelFinalizing(
    IConventionModelBuilder modelBuilder,
    IConventionContext<IConventionModelBuilder> context)
```

Called when a model is being finalized.

**Parameters**

- `modelBuilder`: The builder for the model.
- `context`: Additional information associated with convention execution.

### `ModelConventionAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModelConventionAttribute : Attribute
```

Namespace `DDDToolkit.Abstractions.Attributes`.

Names the Entity Framework convention that maps what the attribute it marks says. A package declares a marker for domain classes, which reference no Entity Framework, and the convention that maps it in its package for Entity Framework. This attribute joins the two, so a context maps the marked classes without a line per class:

```csharp
[AttributeUsage(AttributeTargets.Property)]
[ModelConvention("Billing.EntityFramework.BilledToConvention, Billing.EntityFramework")]
public sealed class BilledToAttribute : Attribute;
```

`AddDDDToolkitConventions()` runs the convention in every model that maps a class with the marker, on the class or on one of its properties. It runs once per model, when the model is finalized, and finds the marked classes itself. What a context configures by hand in `OnModelCreating` is there by then, so the convention can leave a class the context mapped itself as it is.

A marker may be generic, as a template attribute is: `[ModelConvention]` on its definition counts for every class declared with it. The conventions are found from the classes a model maps and from nothing registered, so `dotnet ef` builds the model the application runs with.

**Remarks**

The convention is named by its assembly-qualified name, because the marker's assembly does not reference the convention's. It has a public constructor that takes nothing, and it implements Entity Framework's `IModelFinalizingConvention`. The class itself may be internal to its package, since the toolkit makes it by its name. A model with a marked class is refused while it is built when the convention's assembly is not in the application, with a message that names the assembly to reference.

| Member | Summary |
|---|---|
| [`ModelConventionAttribute(string)`](#modelconventionattributestring) | The primary constructor: the type's summary says what it makes. |
| `string Convention { get; }` | The convention's assembly-qualified name. |

#### `ModelConventionAttribute(string)`

```csharp
public ModelConventionAttribute(string convention)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `convention`: The convention's assembly-qualified name, as `"Namespace.Convention, Assembly"`.

**Remarks**

The convention is named by its assembly-qualified name, because the marker's assembly does not reference the convention's. It has a public constructor that takes nothing, and it implements Entity Framework's `IModelFinalizingConvention`. The class itself may be internal to its package, since the toolkit makes it by its name. A model with a marked class is refused while it is built when the convention's assembly is not in the application, with a message that names the assembly to reference.

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
| [`AddTenancyReadModel(ModelBuilder, string?, TenancyTableNames?)`](#addtenancyreadmodelmodelbuilder-string-tenancytablenames) | Maps Tenancy's read model into a context that is not Tenancy's own, so its queries can ask Tenancy inside them: `db.Tenancy().UnitsWhereIHold(permission)` becomes a subquery of the module's own query, one statement and one round trip. |
| [`AddTenancyReadFunctions(ModelBuilder, string?)`](#addtenancyreadfunctionsmodelbuilder-string) | Maps Tenancy's read model into a context that is not Tenancy's own as Tenancy's read functions, which a package for one database writes into it (`DDDToolkit.Supporting.Tenancy.Postgres` does): the six rows the access questions read, each from the function of its name ([`TenancyFunctionNames`](#tenancyfunctionnames)) in `schema`, under the column names that function answers. |
| [`ScopeToTenant(EntityTypeBuilder<TEntity>, Expression<Func<TEntity, TTenantId>>)`](#scopetotenantentitytypebuildertentity-expressionfunctentity-ttenantid) | Keeps an entity of any module to the current Tenancy user's tenant. |

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

Every table that has a tenant gets the tenant filter ([`TenancyQueryFilter`](#tenancyqueryfilter)), and the save check, which `UseDDDToolkit` adds, reads the tenant of every row written to them.

The views of the read model carry access facts only, here as in a module's model: ids, permissions, periods, statuses, a unit's parent, and a role's starter role and permissions. What a seat, a unit or a role is called is read from the tables themselves, by the managers' reads, which answer names by id.

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

Maps Tenancy's read model into a context that is not Tenancy's own, so its queries can ask Tenancy inside them: `db.Tenancy().UnitsWhereIHold(permission)` becomes a subquery of the module's own query, one statement and one round trip. The six rows the access questions read are keyless views over Tenancy's tables, their columns mapped exactly as [`AddTenancy<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>`](#addtenancymodelbuilder-tenancytablenames-databasefacade) maps them. Entity Framework creates no views, so this context's migrations never touch Tenancy's tables.

```csharp
modelBuilder.AddTenancyReadModel<TenantId, SeatId, OrganizationUnitId, RoleId>();
modelBuilder.Entity<Project>().ScopeToTenant(project => project.TenantId);
```

The rows are access facts: ids, permissions, periods, statuses, a unit's parent, and a role's starter role and permissions. None has a name, so a module that maps them cannot show what a seat, a unit or a role is called: it answers ids, and whoever shows them asks Tenancy's managers for the names, by id. [`TenancyModel.ReadsBeyondAccessFacts`](#readsbeyondaccessfactsireadonlymodel-string-tenancytablenames) finds a module's model that maps more of Tenancy's than these rows.

It needs Tenancy's tables in the same database: one database with a schema per module, or one SQLite file. Where they are apart, ask Tenancy's own context first and pass the answer in as a list.

A module that sees only the application's ids, as above, writes them: nothing it sees says which of its ids are Tenancy's. Where the classes are visible, in the project that declares them or a project of the same module, `modelBuilder.AddTenancyReadModel()` is generated, closed over their four ids (`TemplateRegistrationAttribute`).

Where the database keeps the rights itself and shows a seat only its own ([`TenancyStoreOptions.DatabaseKeepsRights`](#tenancystoreoptions), as on Postgres with `DDDToolkit.Supporting.Tenancy.Postgres`), a module reads no table of Tenancy's: map [`AddTenancyReadFunctions<TTenantId, TSeatId, TUnitId, TRoleId>`](#addtenancyreadfunctionsmodelbuilder-string) there instead. The questions refuse a context of a module that maps these views on such a database.

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

Maps Tenancy's read model into a context that is not Tenancy's own as Tenancy's read functions, which a package for one database writes into it (`DDDToolkit.Supporting.Tenancy.Postgres` does): the six rows the access questions read, each from the function of its name ([`TenancyFunctionNames`](#tenancyfunctionnames)) in `schema`, under the column names that function answers. The module's model then names no table of Tenancy's, only functions, which are what Tenancy offers other modules; and `db.Tenancy()` works exactly as over the views of [`AddTenancyReadModel<TTenantId, TSeatId, TUnitId, TRoleId>`](#addtenancyreadmodelmodelbuilder-string-tenancytablenames): a question is a subquery of the module's own query, one statement and one round trip.

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

As with the views, the rows are access facts and none has a name: the functions answer no column a seat, a unit or a role is called by, and names are asked of Tenancy's managers, by id.

Use it where the database keeps the rights ([`TenancyStoreOptions.DatabaseKeepsRights`](#tenancystoreoptions)); on a database without such functions, SQLite among them, map [`AddTenancyReadModel<TTenantId, TSeatId, TUnitId, TRoleId>`](#addtenancyreadmodelmodelbuilder-string-tenancytablenames). A context's model is built once for the database it runs on, so choosing between the two in `OnModelCreating` by the provider, as above, is safe for a context taken from a pool as well.

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

Keeps an entity of any module to the current Tenancy user's tenant. It adds the tenant filter, a named query filter that sits next to the entity's other named filters rather than replacing them, and marks the entity for Tenancy's save check, which `UseDDDToolkit` adds: a row of another tenant is refused before anything is written, whoever loaded or made it.

The tenant of a row is fixed once the row is there (`IsFixedAfterInsert`): a save that changed it is refused, by the save check first, and where privileges are written from the policies its column is left out of `UPDATE`.

Owned types are kept through their owner: scope the owner. The context must have the save check, which `UseDDDToolkit` adds (`UseTenancy` after `UseDDDToolkitCore`), and [`TenancyChecks.EnsureWired`](#ensurewireddbcontext) checks it.

A class whose tenant is marked `[OfTenant]` in the domain needs no call: [`OfTenantConvention`](#oftenantconvention), which `AddDDDToolkitConventions()` runs, gives it the same. Call this for a class without the marker, or for a tenant the context holds elsewhere than in the marked property: the call wins over the marker.

**Parameters**

- `entity`: The entity's builder.
- `tenant`: The property that holds the entity's tenant, as `entity => entity.TenantId`.

**Type parameters**

- `TEntity`: The entity.
- `TTenantId`: The application's tenant id.

**Returns**: The entity's builder, for further calls.

**Exceptions**

- `ArgumentNullException`: `entity` or `tenant` is null.
- `ArgumentException`: `tenant` is not a property of the entity.

**Example**

A table the module maps by hand, whose class has no `[OfTenant]` and keeps its tenant in `CompanyId`, a property of the application's tenant id:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<ImportedTimesheet>().ScopeToTenant(sheet => sheet.CompanyId);
}
```

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

### `TenancyAccessRevision<TTenantId>`

```csharp
public sealed class TenancyAccessRevision<TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

One row per tenant that every change of rights in it reads and bumps, before it checks anything. Its [`Revision`](#tenancyaccessrevisionttenantid) is a concurrency token, so of two changes that each decided on what the other was about to change, the second to save fails instead of both being written. The seats, roles and units such a change touches are different rows, and their own versions could not see the conflict.

**Type parameters**

- `TTenantId`: The application's tenant id.

| Member | Summary |
|---|---|
| `TTenantId TenantId { get; set; }` | The tenant. |
| `long Revision { get; set; }` | How many changes of rights the tenant has saved. |

### `TenancyModel`

```csharp
public static class TenancyModel
```

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

What a model built with `AddTenancy`, `AddTenancyReadModel`, `AddTenancyReadFunctions`, `ScopeToTenant` and `[OfTenant]` says about tenancy, for a package that works from the model, such as one that writes database policies: which entity types are Tenancy's own tables, which property holds the tenant of an entity type kept to one, whether the model reads Tenancy through its functions, whether a module's model reads more of Tenancy's than the access facts of its read model, which entity types keep who changed their rows, and where Tenancy's access history is.

| Member | Summary |
|---|---|
| `ReadFunctionsAnnotation = "DDDToolkit:Tenancy:ReadFunctions"` | The annotation `AddTenancyReadFunctions` puts on a model: its read model is read from Tenancy's functions, and names no table of Tenancy's. [`ReadsThroughFunctions`](#readsthroughfunctionsireadonlymodel) answers it. |
| [`ReadsThroughFunctions(IReadOnlyModel)`](#readsthroughfunctionsireadonlymodel) | Whether `model` reads Tenancy through its functions: its read model was mapped with `AddTenancyReadFunctions`, so each row the access questions read comes from the function of [`TenancyFunctionNames`](#tenancyfunctionnames) that answers it, and the model maps no table or view of Tenancy's. |
| [`ReadsBeyondAccessFacts(IReadOnlyModel, string?, TenancyTableNames?)`](#readsbeyondaccessfactsireadonlymodel-string-tenancytablenames) | What a module's model reads of Tenancy's beyond the access facts, as one sentence per finding; empty when it reads nothing more. |
| [`TableOf(IEntityType)`](#tableofientitytype) | Which of Tenancy's tables `entityType` is: one of the package's classes, or the application's class declared with its template, mapped to a table. |
| [`RecordsWhoChanged(IReadOnlyEntityType)`](#recordswhochangedireadonlyentitytype) | Whether `entityType` keeps who wrote its row first and who changed it last, in the six columns of [`TenancyAttribution`](#tenancyattribution): `RecordsWhoChanged` was called on it, or on a type it derives from. |
| [`EventLogOf(IModel)`](#eventlogofimodel) | Tenancy's access history in `model`, the log `AddTenancyEventLogTable` maps, with whether its table lets a row go once it is old enough; `null` for a model without one. |
| [`TenantPropertyOf(IEntityType)`](#tenantpropertyofientitytype) | The property that holds the tenant of `entityType`, as `ScopeToTenant`, `[OfTenant]` or `AddTenancy` marked it: the property the tenant filter reads and the save check checks. |

#### `ReadsThroughFunctions(IReadOnlyModel)`

```csharp
public static bool ReadsThroughFunctions(IReadOnlyModel model)
```

Whether `model` reads Tenancy through its functions: its read model was mapped with `AddTenancyReadFunctions`, so each row the access questions read comes from the function of [`TenancyFunctionNames`](#tenancyfunctionnames) that answers it, and the model maps no table or view of Tenancy's.

**Exceptions**

- `ArgumentNullException`: `model` is null.

#### `ReadsBeyondAccessFacts(IReadOnlyModel, string?, TenancyTableNames?)`

```csharp
public static IReadOnlyList<string> ReadsBeyondAccessFacts(
    IReadOnlyModel model,
    string? schema = "tenancy",
    TenancyTableNames? tables = null)
```

What a module's model reads of Tenancy's beyond the access facts, as one sentence per finding; empty when it reads nothing more. Two things are found: an entity type that is not one of this package's rows and is mapped onto a table or view of Tenancy's (`tables` in `schema`) or onto one of its functions ([`TenancyFunctionNames`](#tenancyfunctionnames)); and a property on one of this package's rows that the package did not give it, a shadow property included. A model that maps Tenancy's own aggregates is Tenancy's and is not judged. SQL written by hand is not a mapping and is not seen.

The rows of the read model carry what an access rule reads and no text that is shown to people, so a module whose model passes cannot show what a seat, a unit or a role is called from its own queries: it answers ids, and the names are for Tenancy's managers to answer. Assert it in a test over every module's model, built for each database the application runs on:

```csharp
TenancyModel.ReadsBeyondAccessFacts(projectsContext.Model).Should().BeEmpty();
```

It judges the shape of a mapping, and is no lock on the database: what a caller may read there is the policies' to decide. Names are compared ignoring case, as some databases do. A database without schemas, SQLite among them, finds a table by its name alone, so a type mapped there under one of Tenancy's names reads Tenancy's table whichever schema its mapping names, and a check that is given Tenancy's schema does not find it. For the model built for such a database, ask once more for each schema the model names; `null` asks under the model's default schema, which is where a type mapped with no schema is.

**Parameters**

- `model`: The module's model.
- `schema`: The schema Tenancy's tables and functions are in, or `null` for this model's default schema.
- `tables`: The tables' names, the same as Tenancy's context was given; [`TenancyTableNames.Default`](#tenancytablenames) when not given.

**Exceptions**

- `ArgumentNullException`: `model` is null.

#### `TableOf(IEntityType)`

```csharp
public static TenancyTable? TableOf(IEntityType entityType)
```

Which of Tenancy's tables `entityType` is: one of the package's classes, or the application's class declared with its template, mapped to a table. `null` for any other entity type, an entity of the application's own on a Tenancy class included, and for the rows a consumer's read model maps as views over Tenancy's tables or reads from its functions.

**Exceptions**

- `ArgumentNullException`: `entityType` is null.

#### `RecordsWhoChanged(IReadOnlyEntityType)`

```csharp
public static bool RecordsWhoChanged(IReadOnlyEntityType entityType)
```

Whether `entityType` keeps who wrote its row first and who changed it last, in the six columns of [`TenancyAttribution`](#tenancyattribution): `RecordsWhoChanged` was called on it, or on a type it derives from.

**Exceptions**

- `ArgumentNullException`: `entityType` is null.

#### `EventLogOf(IModel)`

```csharp
public static (IEntityType Log, bool LetsRowsGo)? EventLogOf(IModel model)
```

Tenancy's access history in `model`, the log `AddTenancyEventLogTable` maps, with whether its table lets a row go once it is old enough; `null` for a model without one. The row's tenant is its property [`TenancyEventLogTable.TenantId`](#tenancyeventlogtable).

**Exceptions**

- `ArgumentNullException`: `model` is null.

#### `TenantPropertyOf(IEntityType)`

```csharp
public static IProperty? TenantPropertyOf(IEntityType entityType)
```

The property that holds the tenant of `entityType`, as `ScopeToTenant`, `[OfTenant]` or `AddTenancy` marked it: the property the tenant filter reads and the save check checks. For a type derived in a hierarchy, its root's. `null` for an entity type not kept to a tenant, such as an owned type, which is kept through its owner.

**Exceptions**

- `ArgumentNullException`: `entityType` is null.

### `TenancyQueryFilter`

```csharp
public static class TenancyQueryFilter
```

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

The query filter that keeps every read of a tenant's rows to the current Tenancy user's tenant. It matches the rows of the tenant a seat or system work in a tenant acts in, and nothing for nobody and for system work outside any tenant.

It is a named filter, so an application's own filter on the same entity, a soft delete say, is another named filter and both apply. A read that must look across tenants for the application, as finding a person's seats does, skips it by name with `IgnoreQueryFilters([TenancyQueryFilter.Name])`, which leaves every other filter in place. Tenancy's own bookkeeping, the rows a save writes and the checks that stand in for a unique index, skips every filter: it must see each row the database holds.

| Member | Summary |
|---|---|
| `Name = "tenancy.current-tenant"` | The filter's name, for `IgnoreQueryFilters`. |

### `TenancyTable`

```csharp
public enum TenancyTable
```

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

Tenancy's own tables, as [TenancyModel.TableOf](#tableofientitytype) names them.

| Member | Summary |
|---|---|
| `Tenant = 0` | The tenants. |
| `Organization = 1` | Each tenant's organization. |
| `Unit = 2` | The organizations' units. |
| `UnitPath = 3` | The closure of each organization's tree. |
| `Seat = 4` | The seats. |
| `Placement = 5` | Where each seat is placed. |
| `Grant = 6` | The roles each placement grants. |
| `Right = 7` | The permissions each seat holds where, written from the seats and the roles. |
| `Role = 8` | The roles. |
| `AccessRevision = 9` | One row per tenant that every change of rights takes first. |
| `Invitation = 10` | The invitations into a tenant, in a model that maps them with `AddTenancyInvitations`. |
| `InvitationDigest = 11` | The digests of the invitations' tokens, kept apart from the invitations. |

## Store

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

### `ITenancySaveFailures`

```csharp
public interface ITenancySaveFailures
```

Turns a failed save of Tenancy's store into the refusal it stands for. Some rules are kept by the database where it sees more than the manager does; when such a rule fails the save, the caller should get the same coded refusal the manager gives, not the database's error.

The unique indexes need none: each says in the mapping which refusal a save that breaks it gets, and the toolkit's own interceptor makes it, on any database, before a translator is asked. A translator is for a rule no index states, such as one a trigger of one database checks when the transaction commits.

A package for one database registers one, as a singleton or scoped; the store asks every registered one in the order they were registered, and the first refusal wins. When none knows the failure, the exception is thrown on as it was. A save that was refused already is not offered.

| Member | Summary |
|---|---|
| [`Translate(Exception, DbContext)`](#translateexception-dbcontext) | A refusal for `failure`, the exception the save of `context` failed with (a constraint a trigger checks at commit), or `null` when it is not one this knows. |

#### `Translate(Exception, DbContext)`

```csharp
RefusalException? Translate(Exception failure, DbContext context)
```

A refusal for `failure`, the exception the save of `context` failed with (a constraint a trigger checks at commit), or `null` when it is not one this knows.

**Parameters**

- `failure`: What the save threw.
- `context`: The context that saved: Tenancy's.

### `TenancyStoreOptions`

```csharp
public sealed class TenancyStoreOptions
```

What Tenancy's store leaves to the database. Nothing by default.

| Member | Summary |
|---|---|
| `bool DatabaseKeepsRights { get; set; }` | The database writes each seat's rights itself, from its grants, its seat and its roles, and lets a seat read only its own. The save then writes no rights rows, and Tenancy's store asks the database's functions for what reaches past the calling seat's own rows: the tenant's administrators, the rights a move of a unit changes, and who holds a permission at a unit. Tenancy's reads across tenants, the tenants a sweep visits and the invitation a token is for, ask functions too, as scoped system work in no tenant, and nothing of Tenancy's then runs as the application itself. The functions are named by [`TenancyFunctionNames`](#tenancyfunctionnames), and a package for one database writes them, together with what keeps the rights: `DDDToolkit.Supporting.Tenancy.Postgres` does, and its `AddTenancyPostgres()` turns this on. Nothing else should: with it on and no such functions in the database, the rights are never written. |

## Saving

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

### `TenancySaveInterceptor`

```csharp
public abstract class TenancySaveInterceptor : SaveChangesInterceptor
```

Tenancy's part of every save, in Tenancy's own context and in every context with an entity kept to a tenant: the save check. Every row the save adds, changes or deletes that belongs to a tenant must belong to the current Tenancy user's, so a handler that loaded or made the wrong row cannot write it.

| Tenancy user | What the check does |
|---|---|
| A seat, or system work in a tenant | Refuses a row of another tenant with `tenancy.other-tenant`. |
| Nobody | Refuses every such row with `tenancy.not-seated`. |
| System work outside any tenant | Refuses every such row with `tenancy.other-tenant`: it acts in no tenant, and provisioning saves as system work in the tenant it makes. |

Rows of owned types (units, placements, grants) are checked through the aggregate that owns them, and rows of a derived type as rows of the type at the root of its hierarchy, which is the one kept to a tenant. The check also refuses, on the first save, a model in which an entity type kept to a tenant has lost the tenant filter.

Once the check has let the save through, it fills in who changed each row of an entity type that keeps it ([`TenancyAttribution.RecordsWhoChanged<T, TSeatId>`](#recordswhochangedentitytypebuildert)): the actor of the Tenancy user the save runs as, on a new row as who wrote it first and who changed it last, on a changed row as who changed it last.

In Tenancy's own context it then writes, in the same save, what the access questions read: the closure of every organization whose tree changed and the rights of every seat the save reaches, a role's holders included. The rights the managers check are therefore never behind the aggregates they were saved with. Where the database keeps the rights itself ([`TenancyStoreOptions.DatabaseKeepsRights`](#tenancystoreoptions)), it writes them, in the same transaction, and the save writes the closure alone.

`AddTenancy` on the service collection registers the one instance; `UseDDDToolkit` adds it to every context it wires, and `UseTenancy` to a context configured with `UseDDDToolkitCore`. It must come after the toolkit's own interceptors, so it sees what the domain event handlers changed and only aggregates that passed their invariants; [`TenancyChecks.EnsureWired`](#ensurewireddbcontext) checks both.

| Member | Summary |
|---|---|
| `PartName = "tenancy.save-check"` | The name of the part Tenancy brings to a context's options (`ContextPart`): `AddTenancy` registers it, and `UseDDDToolkit` adds this interceptor to every context it wires. The information line of `UseDDDToolkit` names it, and so does the model of a context that keeps rows to a tenant, which cannot do without it. |
| `PartPosition = 2147483647` | Where the part goes among the parts of a context's options: last, the highest position there is. After the toolkit's own interceptors, which come before every part, so the save check sees what the domain event handlers changed and only aggregates that passed their invariants; and after every other part, whatever position a host gives one of its own, so a row such a part adds or changes is checked like any other. |
| [`SavingChanges(DbContextEventData, InterceptionResult<int>)`](#savingchangesdbcontexteventdata-interceptionresultint) | Called at the start of `DbContext.SaveChanges`. |
| [`SavingChangesAsync(DbContextEventData, InterceptionResult<int>, CancellationToken)`](#savingchangesasyncdbcontexteventdata-interceptionresultint-cancellationtoken) | Called at the start of `DbContext.SaveChangesAsync`. |

#### `SavingChanges(DbContextEventData, InterceptionResult<int>)`

```csharp
public override InterceptionResult<int> SavingChanges(
    DbContextEventData eventData,
    InterceptionResult<int> result)
```

Called at the start of `DbContext.SaveChanges`.

**Parameters**

- `eventData`: Contextual information about the `DbContext` being used.
- `result`: Represents the current result if one exists. This value will have `InterceptionResult.HasResult` set to `true` if some previous interceptor suppressed execution by calling `InterceptionResult.SuppressWithResult`. This value is typically used as the return value for the implementation of this method.

**Returns**: If `InterceptionResult.HasResult` is false, the EF will continue as normal. If `InterceptionResult.HasResult` is true, then EF will suppress the operation it was about to perform and use `InterceptionResult.Result` instead. An implementation of this method for any interceptor that is not attempting to change the result is to return the `result` value passed in.

#### `SavingChangesAsync(DbContextEventData, InterceptionResult<int>, CancellationToken)`

```csharp
public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
    DbContextEventData eventData,
    InterceptionResult<int> result,
    CancellationToken cancellationToken = default)
```

Called at the start of `DbContext.SaveChangesAsync`.

**Parameters**

- `eventData`: Contextual information about the `DbContext` being used.
- `result`: Represents the current result if one exists. This value will have `InterceptionResult.HasResult` set to `true` if some previous interceptor suppressed execution by calling `InterceptionResult.SuppressWithResult`. This value is typically used as the return value for the implementation of this method.
- `cancellationToken`: A `CancellationToken` to observe while waiting for the task to complete.

**Returns**: If `InterceptionResult.HasResult` is false, the EF will continue as normal. If `InterceptionResult.HasResult` is true, then EF will suppress the operation it was about to perform and use `InterceptionResult.Result` instead. An implementation of this method for any interceptor that is not attempting to change the result is to return the `result` value passed in.

**Exceptions**

- `OperationCanceledException`: If the `CancellationToken` is canceled.

## Read model

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

### `EfQueryExecutor`

```csharp
public sealed class EfQueryExecutor : IQueryExecutor
```

Runs the access questions' queries with Entity Framework's asynchronous operators, and a query that is not Entity Framework's, such as an answer that is a list in memory, with the synchronous ones.

| Member | Summary |
|---|---|
| `static EfQueryExecutor Instance { get; }` | The one executor; it keeps no state. |
| [`AnyAsync(IQueryable<T>, CancellationToken)`](#anyasynciqueryablet-cancellationtoken) | Whether the query has any row. |
| [`ListAsync(IQueryable<T>, CancellationToken)`](#listasynciqueryablet-cancellationtoken) | Every row of the query. |
| [`FirstOrDefaultAsync(IQueryable<T>, CancellationToken)`](#firstordefaultasynciqueryablet-cancellationtoken) | The first row of the query, or the default when it has none. |

#### `AnyAsync(IQueryable<T>, CancellationToken)`

```csharp
public Task<bool> AnyAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
```

Whether the query has any row.

**Parameters**

- `query`: The query, of the read source's storage.
- `cancellationToken`: Cancels the query.

**Type parameters**

- `T`: The type of the query's rows.

#### `ListAsync(IQueryable<T>, CancellationToken)`

```csharp
public Task<IReadOnlyList<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
```

Every row of the query.

**Parameters**

- `query`: The query, of the read source's storage.
- `cancellationToken`: Cancels the query.

**Type parameters**

- `T`: The type of the query's rows.

#### `FirstOrDefaultAsync(IQueryable<T>, CancellationToken)`

```csharp
public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
```

The first row of the query, or the default when it has none.

**Parameters**

- `query`: The query, of the read source's storage.
- `cancellationToken`: Cancels the query.

**Type parameters**

- `T`: The type of the query's rows.

### `EfTenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>`

```csharp
public sealed class EfTenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> : ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

The rows the access questions read, from one context: Tenancy's own, or a module's that maps the read model with `AddTenancyReadModel` or `AddTenancyReadFunctions`. The questions built over it are queries of that context, so a module that composes one into its own query gets one statement. Every set carries the tenant filter, and none is tracked.

Where the database keeps the rights and shows a seat only its own ([`TenancyStoreOptions.DatabaseKeepsRights`](#tenancystoreoptions)), who holds a permission at a unit is asked of the database's function of that name ([`TenancyFunctionNames.SeatsHoldingPermissionAt`](#tenancyfunctionnames)) rather than worked out over the rows, which would find nobody but the calling seat. That, too, is a query of the context.

| Member | Summary |
|---|---|
| [`EfTenancyReadSource(DbContext)`](#eftenancyreadsourcedbcontext) | The rows of `context`. |
| `IQueryable<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>> SeatRights { get; }` | The permissions seats hold at units. |
| `IQueryable<OrganizationUnitPath<TTenantId, TUnitId>> UnitPaths { get; }` | The closure of every organization's tree. |
| `IQueryable<OrganizationUnitRow<TTenantId, TUnitId>> Units { get; }` | The units. |
| `IQueryable<RoleRow<TTenantId, TRoleId>> Roles { get; }` | The roles. |
| `IQueryable<PlacementRow<TSeatId, TUnitId>> Placements { get; }` | Where seats are placed. |
| `IQueryable<SeatRow<TTenantId, TSeatId>> Seats { get; }` | The seats. |
| [`SeatsHoldingPermissionAt(string, TUnitId)`](#seatsholdingpermissionatstring-tunitid) | The seats that hold `permission` at `unit` now, as the storage itself answers the calling seat, or `null` when it has no answer of its own and the question is worked out over the rows above. |

#### `EfTenancyReadSource(DbContext)`

```csharp
public EfTenancyReadSource(DbContext context)
```

The rows of `context`.

**Parameters**

- `context`: The context to read from. Whether the database keeps the rights is read from the services the context was made by, the application's [`TenancyStoreOptions`](#tenancystoreoptions); a context made by hand has none, and is read as one whose database does not.

**Exceptions**

- `ArgumentNullException`: `context` is null.
- `InvalidOperationException`: The database keeps the rights, and `context` is a module's that maps the read model as views over Tenancy's tables: there a module asks Tenancy's functions, mapped with `AddTenancyReadFunctions`.

#### `SeatsHoldingPermissionAt(string, TUnitId)`

```csharp
public IQueryable<TSeatId>? SeatsHoldingPermissionAt(string permission, TUnitId unit)
```

The seats that hold `permission` at `unit` now, as the storage itself answers the calling seat, or `null` when it has no answer of its own and the question is worked out over the rows above. A storage that shows a seat only its own [`ITenancyReadSource.SeatRights`](queries.md#itenancyreadsourcettenantid-tseatid-tunitid-troleid) has one: over those rows the question would find nobody but the seat itself, whoever asks. Its answer keeps to what [`ITenancyQueries.SeatsHoldingPermissionAt`](queries.md#seatsholdingpermissionatstring-tunitid) says a seat learns, and is a query of the same context, so it still composes into one statement.

**Parameters**

- `permission`: A live permission of the catalogue.
- `unit`: The unit.

**Remarks**

Answered by the database's function where it keeps the rights and the context's model maps the function's row, as `AddTenancy`, `AddTenancyReadModel` and `AddTenancyReadFunctions` do; `null` anywhere else.

## Read functions

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

### `TenancyFunctionNames`

```csharp
public static class TenancyFunctionNames
```

The names of Tenancy's database functions: the contract between a package that writes them for one database, the mapping of a module that reads Tenancy through them (`AddTenancyReadFunctions`), and the store that asks them when [`TenancyStoreOptions.DatabaseKeepsRights`](#tenancystoreoptions) is on. Each lives in the schema of the context that maps Tenancy's tables, and answers under the column names given here, whatever the tables' own columns are called and however their values are stored.

The first six answer the rows of the read model, one each, with the properties' names as the columns' and no parameters. They run as their caller, so what a caller may read of Tenancy's tables decides their rows: a seat its own rights and the rest of its tenant, system work in a tenant all of that tenant. A status is answered as the name of its enum member. They answer access facts only, exactly the columns listed here: none answers what a seat, a unit or a role is called, which Tenancy's managers answer by id.

The next three answer a seat what it may learn of other seats' rights, the one after them what the units it asks about are called, the one after that what the tenants a person has a seat in are called, and the three after that read across tenants, for system work that began in no tenant. These run as their owner, and answer ids, permissions, dates and the names of the units and the tenants asked about only. So does the last, which a database has where the context maps invitations: it reads across tenants too, and answers which invitation a token's digest is for.

| Member | Summary |
|---|---|
| `CurrentPermissions = "current_permissions"` | The rights the caller may read, as rows of [`SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>`](queries.md#seatrightttenantid-tseatid-tunitid-troleid): `"TenantId"`, `"SeatId"`, `"UnitId"`, `"RoleId"`, `"Permission"`, `"StartsAt"` and `"EndsAt"`. |
| `TenantUnitPaths = "tenant_unit_paths"` | The closure of the organization's tree, as rows of [`OrganizationUnitPath<TTenantId, TUnitId>`](queries.md#organizationunitpathttenantid-tunitid): `"TenantId"`, `"AncestorId"`, `"DescendantId"` and `"Distance"`. |
| `TenantUnits = "tenant_units"` | The units, as rows of [`OrganizationUnitRow<TTenantId, TUnitId>`](queries.md#organizationunitrowttenantid-tunitid): `"Id"`, `"TenantId"`, `"ParentId"` and `"Status"`; never the name, nor a column the application added. |
| `TenantRoles = "tenant_roles_with_starter_roles"` | The roles, as rows of [`RoleRow<TTenantId, TRoleId>`](queries.md#rolerowttenantid-troleid): `"Id"`, `"TenantId"`, `"FromStarterRole"`, `"Status"` and `"Permissions"`, an array of text however the roles' table stores them; never the name. |
| `TenantPlacements = "tenant_placements"` | Where seats are placed, as rows of [`PlacementRow<TSeatId, TUnitId>`](queries.md#placementrowtseatid-tunitid): `"SeatId"`, `"UnitId"` and `"IsPrimary"`, with the seat's `"TenantId"`, which the row is kept to its tenant by. |
| `TenantSeats = "tenant_seats"` | The seats, as rows of [`SeatRow<TTenantId, TSeatId>`](queries.md#seatrowttenantid-tseatid): `"Id"`, `"TenantId"` and `"Status"`; never the identity, nor a field the application adds to its seat class. |
| `TenantAdministrators = "tenant_administrators"` | The tenant's administrators, as rows of `"SeatId"` and `"RoleId"`: no parameters. Answered to a seat that may read the grants at the root: one that manages grants, seats or units there, or roles for the whole tenant. |
| `PermissionsAUnitMoveChanges = "permissions_a_unit_move_changes"` | The rights a move of a unit changes, as rows of `"UnitId"`, `"Permission"`, `"EndsAt"`, `"Parent"` and `"OfCurrentSeat"`: its parameters are the unit's parent and the parent it would get. Answered to a seat that manages units at both: its own rights, and another seat's only where it reaches one parent and not the other, which is where the move changes it. |
| `SeatsHoldingPermissionAt = "seats_holding_permission_at"` | The seats that hold a permission at a unit, as rows of `"SeatId"`: its parameters are the permission and the unit. A seat is answered each holder whose grant there it may read: itself, and any other seat at a unit where it manages grants, seats or units, or anywhere when it manages roles for the whole tenant. |
| `UnitsNamed = "units_named"` | What units of the caller's tenant are called, as rows of `"Id"`, `"Name"`, `"Path"` and `"Depth"`: its parameter is the array of the units asked about. The name and path of each of those that is the tenant's, and nothing more of it, whatever the seat reads of the organization: a seat on a project at a unit it is not placed under, or one an application's restrictive rule keeps out of the units, still reads what that unit is called. |
| `TenantNamesOfIdentity = "identity_tenant_names"` | What the tenants the caller's identity has a seat in are called, whatever the status of the seat or of the tenant, as rows of `"Id"`, `"Slug"` and `"Name"`: no parameters. The tenant picker reads the names through it, before a tenant is chosen, so it names every tenant by its organization's name whatever an application's restrictive rule on the organization lets the caller read of it; it answers nothing more of one. |
| `RolePermissionsInUse = "role_permissions_in_use"` | The permissions stored on the roles of every tenant, each once, as text: no parameters. One of the three reads across tenants: answered to Tenancy's own system work, begun in no tenant, and to nobody else. |
| `TenantsToSweep = "tenants_to_sweep"` | The ids of the tenants a round of system work visits, the active and the suspended ones: no parameters. Answered to system work of any module, begun in no tenant, which then works in each tenant under its own scope: an id names no person and no row. |
| `SeatsOfIdentity = "seats_of_identity"` | The seats a person has, in every tenant and whatever their status, as rows of `"TenantId"` and `"SeatId"`: its parameter is the person's verified identity. Answered to Tenancy's own system work, begun in no tenant, and to nobody else. |
| `InvitationOfDigest = "invitation_of_digest"` | The invitation a token's digest is for, in whatever tenant, as a row of `"TenantId"`, `"InvitationId"` and `"IssuedBy"`: its parameter is the digest. Written only where the context maps invitations. Answered to Tenancy's own system work, begun in no tenant, and to nobody else: it is the one way a digest is read at all, and it answers ids for a digest somebody already has, never a digest. |

## Attribution

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

### `TenancyActedByAccessor`

```csharp
public sealed class TenancyActedByAccessor : IActedByAccessor
```

Tells a record that keeps who acted, an event log for one, what Tenancy knows of the caller: a seat as the seat, an operator by its verified identity, the application's own work as the system with its scope, and a link's token as the seat it stands for ([`TenancyUser.Actor`](access.md#tenancyuserttenantid-tseatid)). Where there is no Tenancy user, or the caller is nobody, the accessor it wraps answers from the toolkit's current user.

`AddTenancy` puts it around the accessor that was registered before it, or around the toolkit's default. It is a singleton that reads the ambient Tenancy user each time it is asked, keeps nothing between two answers and takes no scoped service, so it serves a context taken from a pool as it serves any other.

| Member | Summary |
|---|---|
| [`TenancyActedByAccessor(IActedByAccessor)`](#tenancyactedbyaccessoriactedbyaccessor) | The primary constructor: the type's summary says what it makes. |
| [`Current`](#current) | Who is acting now. |

#### `TenancyActedByAccessor(IActedByAccessor)`

```csharp
public TenancyActedByAccessor(IActedByAccessor inner)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `inner`: The accessor that answers where Tenancy has no actor to name.

#### `Current`

```csharp
public ActedBy Current { get; }
```

Who is acting now.

**Exceptions**

- `NoCurrentUserException`: Nobody is calling, and the host requires every flow of work to say who it runs as.

### `TenancyAttribution`

```csharp
public static class TenancyAttribution
```

Who wrote a row first and who changed it last, kept on the row itself: six columns a module adds to an entity it keeps to a tenant, which every save fills in.

| Member | Summary |
|---|---|
| `CreatedBySeat = "CreatedBySeat"` | The seat that wrote the row first, when a seat did. |
| `CreatedByKind = "CreatedByKind"` | What kind of actor wrote the row first: one of [`TenancyActorKinds`](access.md#tenancyactorkinds). |
| `CreatedByIdentity = "CreatedByIdentity"` | The verified identity of the operator the row was first written for, when it was one. |
| `ChangedBySeat = "ChangedBySeat"` | The seat that changed the row last, when a seat did. |
| `ChangedByKind = "ChangedByKind"` | What kind of actor changed the row last: one of [`TenancyActorKinds`](access.md#tenancyactorkinds). |
| `ChangedByIdentity = "ChangedByIdentity"` | The verified identity of the operator the row was last changed for, when it was one. |
| `KindLength = 32` | The longest kind a column holds. |
| `static IReadOnlyList<string> Columns { get; }` | The six columns, in one order for everything that reads them by position: who wrote the row first as seat, kind and identity, then who changed it last as seat, kind and identity. |
| [`RecordsWhoChanged(EntityTypeBuilder<T>)`](#recordswhochangedentitytypebuildert) | Adds six shadow columns to an entity that is kept to a tenant, and has every save fill them in: who wrote the row first and who changed it last, each as a kind ([`TenancyActorKinds`](access.md#tenancyactorkinds)), a seat when a seat did it, and an operator's verified identity when it was done for an operator. |

#### `RecordsWhoChanged(EntityTypeBuilder<T>)`

```csharp
public static EntityTypeBuilder<T> RecordsWhoChanged<T, TSeatId>(this EntityTypeBuilder<T> entity)
    where T : class
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Adds six shadow columns to an entity that is kept to a tenant, and has every save fill them in: who wrote the row first and who changed it last, each as a kind ([`TenancyActorKinds`](access.md#tenancyactorkinds)), a seat when a seat did it, and an operator's verified identity when it was done for an operator. The application's own work leaves a kind and neither id.

```csharp
modelBuilder.Entity<Project>(project =>
{
    project.ScopeToTenant(row => row.TenantId);
    project.RecordsWhoChanged<Project, SeatId>();
});
```

Tenancy's save interceptor, which `UseDDDToolkit` adds, fills them from the Tenancy user of the save ([`TenancyUser.Actor`](access.md#tenancyuserttenantid-tseatid)): both sets for a new row, and the last three for a row that changes. A row whose own columns did not change, because only a row of one of its entities in another table did, keeps who changed it last; an aggregate with a version changes its row with every change of its parts. The first three are fixed once the row is there: Entity Framework refuses a save that changes one, and the privileges an export writes leave them out of `UPDATE`.

They are ids, never a name: what a seat is shown by is the application's, which Tenancy's managers answer by id. They are shadow properties, so the entity's class does not change; read one with `EF.Property<SeatId?>(row, TenancyAttribution.ChangedBySeat)`. Adding them to a table that exists is a migration of your own, as any change to the model is.

On Postgres, `DDDToolkit.Supporting.Tenancy.Postgres` writes a trigger on the table that holds a signed-in user to its own seat in these columns, and the application's own work to a kind that is no seat, so a statement that goes around the model cannot write somebody else's name.

The call without the seat's id is generated into the project that declares the seat class, and into the projects of its module (`TemplateRegistrationAttribute`). A module of its own names the id: `project.RecordsWhoChanged<Project, SeatId>()`.

**Parameters**

- `entity`: The entity's builder.

**Type parameters**

- `T`: The entity, which the model keeps to a tenant with `ScopeToTenant`. In a hierarchy stored in one table it is the type at the root, so every row of the table keeps the columns; the first save refuses a model that leaves either out.
- `TSeatId`: The application's seat id.

**Exceptions**

- `ArgumentNullException`: `entity` is null.

## History

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

### `TenancyEventLogExtensions`

```csharp
public static class TenancyEventLogExtensions
```

Chooses which of Tenancy's domain events its access history keeps.

| Member | Summary |
|---|---|
| [`AddTenancyEventLog(EventLogOptions)`](#addtenancyeventlogeventlogoptions) | Keeps, in the event log of Tenancy's context, every event of Tenancy's that changes who may do what: the history a tenant's access is answered for afterwards. |

#### `AddTenancyEventLog(EventLogOptions)`

```csharp
public static EventLogOptions AddTenancyEventLog<TTenantId, TSeatId, TUnitId, TRoleId>(this EventLogOptions log)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Keeps, in the event log of Tenancy's context, every event of Tenancy's that changes who may do what: the history a tenant's access is answered for afterwards.

```csharp
options.UseOutbox<TenancyContext>(outbox => outbox
    .AddTenancyDomainEvents()
    .KeepEventLog(log => log.AddTenancyEventLog()));
```

Both calls without type arguments are generated into the project that declares the application's classes, closed over their four ids, or into a project of the same module that declares none, where the context is (`TemplateRegistrationAttribute`). This is the method `log.AddTenancyEventLog()` calls, which can be called as well, with the four ids written out.

- A tenant provisioned, activated, suspended, reactivated or closed, and its shape changed.
- A unit added, moved or archived.
- A seat added, suspended, reactivated or deactivated; placed in a unit, withdrawn from one, and its primary placement changed.
- A role granted to a seat or revoked from it.
- A role created, its permissions changed, with the permissions that came in and went out, or archived; and a role that followed its starter role once the application changed the starter role, with the same.

A rename changes nobody's access, so the three events that say a tenant's organization, a unit or a role is called something else are left out. They are still stored in the outbox like the rest. A seat has no name in Tenancy, so it is never renamed here; an application that renames its own seats keeps an event of its own for it, with a `Keep` next to this call when it belongs in the history.

Each row is the event as the outbox stores it, written by the save that raised it: the ids, the permissions, the dates and who made the change (`By`, which every event of Tenancy's carries), and never what a tenant, a unit, a seat or a role is called. The row's own columns say the same actor again and the tenant, so a history is read and kept apart per tenant without opening a payload. Map the table with `modelBuilder.AddTenancyEventLogTable(Database)`.

The choices of an event log add up: an event of the application's own that belongs in the history is kept with one more `Keep` next to this call, and calling `KeepEventLog()` with nothing chosen keeps every event of the context instead, renames included.

**Parameters**

- `log`: The event log of Tenancy's outbox, as `KeepEventLog` hands it over.

**Type parameters**

- `TTenantId`: The application's tenant id.
- `TSeatId`: The application's seat id.
- `TUnitId`: The application's unit id.
- `TRoleId`: The application's role id.

**Exceptions**

- `ArgumentNullException`: `log` is null.

### `TenancyEventLogTable`

```csharp
public static class TenancyEventLogTable
```

Tenancy's access history: the toolkit's event log with the tenant of each event on its row, so the history of one tenant is read without opening a payload, and a database can keep a tenant's history to that tenant.

| Member | Summary |
|---|---|
| `TenantId = "TenantId"` | The column that holds the tenant an event is about: a shadow property of the log's row. |
| [`AddTenancyEventLogTable(ModelBuilder, DatabaseFacade, string, string?, TimeSpan?, Action<EntityTypeBuilder<EventLogEntry>>?)`](#addtenancyeventlogtablemodelbuilder-databasefacade-string-string-timespan-actionentitytypebuildereventlogentry) | Maps the toolkit's event log into the context Tenancy lives in, with a [`TenantId`](#tenancyeventlogtable) column of the application's tenant id and an index on the tenant and the time a row was written, which is what a reader of one tenant's history pages by. |

#### `AddTenancyEventLogTable(ModelBuilder, DatabaseFacade, string, string?, TimeSpan?, Action<EntityTypeBuilder<EventLogEntry>>?)`

```csharp
public static ModelBuilder AddTenancyEventLogTable<TTenantId>(
    this ModelBuilder modelBuilder,
    DatabaseFacade database,
    string tableName = "EventLog",
    string? schema = "ddd",
    TimeSpan? keepFor = null,
    Action<EntityTypeBuilder<EventLogEntry>>? configure = null)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

Maps the toolkit's event log into the context Tenancy lives in, with a [`TenantId`](#tenancyeventlogtable) column of the application's tenant id and an index on the tenant and the time a row was written, which is what a reader of one tenant's history pages by. Call it from `OnModelCreating` next to `AddTenancy`, and keep the events with the outbox's `KeepEventLog`:

```csharp
modelBuilder.AddTenancy(database: Database);
modelBuilder.AddDomainEventOutbox(Database);
modelBuilder.AddTenancyEventLogTable(Database);

options.UseOutbox<TenancyContext>(outbox => outbox
    .AddTenancyDomainEvents()
    .KeepEventLog(log => log.AddTenancyEventLog()));
```

Every row says which tenant its event is about, taken from the event itself, which every event of Tenancy's carries, and who acted, from the Tenancy user of the save: a seat, an operator, the system or a token ([`TenancyActedByAccessor`](#tenancyactedbyaccessor)). An event of the application's own that names no tenant gets the tenant the save runs in. The payload is the event as the outbox stores it: ids, permissions and dates, who made the change, and never what a tenant, a unit, a seat or a role is called. [`TenancyEventLogExtensions.AddTenancyEventLog<TTenantId, TSeatId, TUnitId, TRoleId>`](#addtenancyeventlogeventlogoptions) chooses the events that change access.

The rows are not kept to the caller's tenant by a query filter, since the toolkit's retention reads the table whole: code that reads the history names the tenant itself, and asks `tenancy.history.view` for the whole tenant first. On Postgres, `DDDToolkit.Supporting.Tenancy.Postgres` writes the table's policies: a seat adds rows about itself in its tenant and reads its tenant's with that permission, and system work adds and reads its tenant's. It writes them with those of Tenancy's own tables, so the log belongs in the context that maps them: an export refuses it in any other, where it would be left without a policy.

The call without type arguments is generated into the project that declares the tenant class, and into the projects of its module (`TemplateRegistrationAttribute`). Everything else is `AddEventLog`'s: the table only grows, and its name, its schema and how long it keeps a row are that method's to explain.

**Parameters**

- `modelBuilder`: The model being built.
- `database`: The context's `Database`, read for its provider name only.
- `tableName`: The table to map to.
- `schema`: The schema to put it in, or `null` for the provider's default.
- `keepFor`: How long a row is kept before it may be deleted; `null`, the default, keeps every row.
- `configure`: Further configuration of the table, after the tenant's column is there.

**Type parameters**

- `TTenantId`: The application's tenant id.

**Exceptions**

- `ArgumentNullException`: `modelBuilder` or `database` is null.
- `ArgumentException`: `tableName` is empty or white space.
- `ArgumentOutOfRangeException`: `keepFor` is zero, negative or longer than a thousand years.

## Invitations

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

### `TenancyInvitationDigest<TInvitationId, TTenantId>`

```csharp
public sealed class TenancyInvitationDigest<TInvitationId, TTenantId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

The digest of an invitation's token, in a table of its own next to the invitations: one row per invitation, written with it and never changed.

It is kept apart so that reading invitations never reads a digest. Whoever manages seats lists the invitations into their units; the digest is of no use to them, and a table nobody is let read gives it to nobody. On Postgres, `DDDToolkit.Supporting.Tenancy.Postgres` writes this table no policy that lets a caller read it: whoever issues an invitation adds its row, and the one question asked of it, which invitation a digest is for, is answered by a function.

**Type parameters**

- `TInvitationId`: The application's invitation id.
- `TTenantId`: The application's tenant id.

| Member | Summary |
|---|---|
| `TInvitationId InvitationId { get; set; }` | The invitation the token is for. |
| `TTenantId TenantId { get; set; }` | The invitation's tenant, which the row is kept to its tenant by. |
| `byte[] Digest { get; set; }` | The SHA-256 digest of the token's bytes: 32 bytes, unique. |

## Start-up checks

Namespace `DDDToolkit.Supporting.Tenancy.EntityFramework`.

### `TenancyChecks`

```csharp
public static class TenancyChecks
```

Checks an application can run against its Tenancy storage: that a context is wired so its saves are checked, and which permissions stored on roles the catalogue no longer knows.

`AddTenancy` registers them as start-up checks, by the names below, which a host runs with `services.RunStartupChecks()`: the catalogue builds, every registered context is wired as [`EnsureWired`](#ensurewireddbcontext) asks, and every permission a role holds that the catalogue does not know is logged as a warning. The methods stay for a host that runs them by hand.

| Member | Summary |
|---|---|
| `CatalogueBuildsCheck = "tenancy.catalogue-builds"` | The start-up check that the catalogue builds: asked for the first time at start-up, so a catalogue that does not hold together, two modules that declare one permission say, stops the start rather than the first request that needs it. Every module has added its permissions by then, whatever order they were registered in. |
| `ContextsWiredCheck = "tenancy.contexts-wired"` | The start-up check that every registered context that keeps rows to a tenant checks its saves ([`EnsureWired`](#ensurewireddbcontext)). Every context the container knows, so a module added later is checked without anyone remembering to add it; one without such rows passes as it is. |
| `UnknownStoredPermissionsCheck = "tenancy.unknown-stored-permissions"` | The start-up check that logs, as a warning, every permission a role holds that the catalogue does not know ([`UnknownStoredPermissionsAsync<TRole, TRoleId, TTenantId>`](#unknownstoredpermissionsasyncdbcontext-tenancycatalogue-cancellationtoken)): removed from the code rather than retired. Such a permission gives nothing and does no harm, so it refuses no start. It reads what is stored, so it runs with the checks of the database, and after those of Tenancy's own that prove it may read it. |
| [`EnsureWired(DbContext)`](#ensurewireddbcontext) | Throws when `context` has entity types kept to a tenant, Tenancy's own or those of `ScopeToTenant`, and its saves would not be checked: the context was configured with `UseDDDToolkitCore` and no `UseTenancy`, or without the toolkit, or `UseTenancy` was called before the toolkit's interceptors, or Tenancy is not registered at all, so `UseDDDToolkit` had no save check to add. |
| [`UnknownStoredPermissionsAsync(DbContext, TenancyCatalogue, CancellationToken)`](#unknownstoredpermissionsasyncdbcontext-tenancycatalogue-cancellationtoken) | The permissions stored on roles, in every tenant, that `catalogue` does not know: removed from the code rather than retired. |

#### `EnsureWired(DbContext)`

```csharp
public static void EnsureWired(DbContext context)
```

Throws when `context` has entity types kept to a tenant, Tenancy's own or those of `ScopeToTenant`, and its saves would not be checked: the context was configured with `UseDDDToolkitCore` and no `UseTenancy`, or without the toolkit, or `UseTenancy` was called before the toolkit's interceptors, or Tenancy is not registered at all, so `UseDDDToolkit` had no save check to add. `UseDDDToolkit` adds it in its place once `AddTenancy` is registered. The store calls it before every save; call it at start-up, or before the save of a unit of work of your own, for every other context that keeps entities to a tenant.

A context without the save check at all lacks a part its model requires, since `ScopeToTenant` states it, and is told so in the words of `ContextPartRequirements`, which the toolkit refuses its first save with and the start-up check `entity-framework.toolkit-wired` as well: `AddTenancy` named where nothing registered Tenancy, `UseTenancy` where the context was given the base alone. What this adds is what only Tenancy knows: the order, and who acted.

Without it a context without the save check is silent: rows of another tenant are written and the stored rights are not. A save check added too early would check the rows before the domain event handlers changed them, and before the invariants had refused the save.

A context that keeps Tenancy's access history (`AddTenancyEventLogTable`) is refused as well when the accessor that says who acted is not Tenancy's: `AddTenancy` puts [`TenancyActedByAccessor`](#tenancyactedbyaccessor) around the accessor registered before it, and one registered after it takes its place. Every row of the history would then name the signed-in identity where it should name the seat, and on Postgres the history's policies would refuse a seat's save.

What the model holds is worked out once per model; the interceptors are read from the context's options each time, because two contexts of one type can be given different options.

**Parameters**

- `context`: The context to check.

**Exceptions**

- `ArgumentNullException`: `context` is null.
- `InvalidOperationException`: The context's saves would not be checked, or would be checked too early; or the context keeps the access history and another accessor than Tenancy's says who acted.

#### `UnknownStoredPermissionsAsync(DbContext, TenancyCatalogue, CancellationToken)`

```csharp
public static Task<IReadOnlyList<string>> UnknownStoredPermissionsAsync<TRole, TRoleId, TTenantId>(
    DbContext context,
    TenancyCatalogue catalogue,
    CancellationToken cancellationToken)
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
```

The permissions stored on roles, in every tenant, that `catalogue` does not know: removed from the code rather than retired. Such a permission gives no rights, and may stay on its role; this names them, so a start-up check can report them. The query reads the roles' permissions and nothing else, past every filter: a role an application's own filter hides still holds its permissions.

It is one of the few reads across tenants Tenancy makes: one query, on a context of its own made in a new scope of the application's services, as a user begun around it alone, so neither the caller of this method nor a transaction open on `context` decides who it runs as. Where the database keeps the rights ([`TenancyStoreOptions.DatabaseKeepsRights`](#tenancystoreoptions), as on Postgres with row level security), that caller is Tenancy's scoped system work in no tenant, a role the policies hold, and the query asks the database's function [`TenancyFunctionNames.RolePermissionsInUse`](#tenancyfunctionnames), which answers the permissions and nothing else of any role. Anywhere else it is the toolkit's `CurrentUserSnapshot.System`, the application itself, reading the roles' table.

**Parameters**

- `context`: A context of Tenancy's, resolved from the application's services: it names the context type and the services the read makes its own context from, and is not queried itself.
- `catalogue`: The catalogue the application runs with.
- `cancellationToken`: Cancels the query.

**Returns**: The unknown permissions, each once, in ordinal order.

**Exceptions**

- `ArgumentNullException`: `context` or `catalogue` is null.
- `InvalidOperationException`: `context` was not made by the application's services.
