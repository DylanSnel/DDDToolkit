---
title: "Membership API: Entity Framework"
sidebar_label: Entity Framework
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Membership API: Entity Framework

The public API of `DDDToolkit.Supporting.Membership.EntityFramework`, by folder: the registration a host calls for each kind of resource, the mapping of a resource's member tables into the application's own context, the queries on the resource's own set, `db.Boards.WhereIHold(permission)`, that put what the access manager decides into a module's own statements, and what an application answers for a resource whose roles or places it keeps itself.

## Registration

Namespace `DDDToolkit.Supporting.Membership.EntityFramework`.

### `MembershipEntityFrameworkServiceCollectionExtensions`

```csharp
public static class MembershipEntityFrameworkServiceCollectionExtensions
```

Registers Membership, stored with Entity Framework.

| Member | Summary |
|---|---|
| [`AddMembership(IServiceCollection, MembershipRules)`](#addmembershipiservicecollection-membershiprules) | Registers one kind of resource with members, kept in `TContext`, the context whose model calls `HasMembers` on it, under `rules`. |
| [`AddMembership(IServiceCollection, MembershipRules)`](#addmembershipiservicecollection-membershiprules-1) | Registers one kind of resource with members, as [`AddMembership<TMember, TId, TMemberId, TRoleId, TContext, TResource, TResourceId>`](#addmembershipiservicecollection-membershiprules) does, together with the class that answers what the resource's rules ask of the application: for a resource beside an organization, the one class that joins the two. |
| [`AddMemberAccess(IServiceCollection)`](#addmemberaccessiservicecollection) | Adds the access check of one kind of resource with members to the checks the requests of one module are held to (`AccessChecks<TRequests>`): what decides the requirements a request declares about that resource ([`MemberAccess<TResourceId>`](managers.md#memberaccesstresourceid)) before its handler runs. |

#### `AddMembership(IServiceCollection, MembershipRules)`

```csharp
public static IServiceCollection AddMembership<TMember, TId, TMemberId, TRoleId, TContext, TResource, TResourceId>(
    this IServiceCollection services,
    MembershipRules rules)
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TContext : DbContext
    where TResource : AggregateRoot<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

Registers one kind of resource with members, kept in `TContext`, the context whose model calls `HasMembers` on it, under `rules`. An application calls the registration generated for its resource, named after it:

```csharp
services.AddDocumentMembership<DocumentsContext>(DocumentMembership.Rules);
```

What it registers is asked for by the resource, so there is one for each kind of resource an application has members on, its documents and its folders, in one container, and none gets another's rules:

- [`MembershipAccessManager<TResource>`](managers.md#membershipaccessmanagertresource), the access manager of the resource, per scope, answered inside the context's own statements, which is what [`MemberAccessCheck<TResource, TResourceId>`](managers.md#memberaccesschecktresource-tresourceid) asks before a handler runs. That check is added for the module's request interface by the registration generated beside this one: `services.AddDocumentMemberAccess<IDocumentsRequest>()` ([`AddMemberAccess<TResource, TResourceId, TRequests>`](#addmemberaccessiservicecollection)).
- What the queries on the resource's own set ask, `db.Documents.WhereIHold(permission)` ([`MemberQueryableExtensions`](#memberqueryableextensions)): they find it in the services the context was made with.
- The managers the generator wrote for the resource, per scope: the member manager, `DocumentShareManager` for the member class `DocumentShare`, and the role manager of the role class kept for the resource, where there is one. Each is read off the resource, which names them, and each loads and saves on the request's own context, through the store registered beside it. The member manager asks the application's [`IMemberDirectory<TResourceId, TMemberId>`](managers.md#imemberdirectorytresourceid-tmemberid) and [`IMemberRolePolicy<TResourceId, TRoleId>`](managers.md#imemberrolepolicytresourceid-troleid) where it registered them, and answers which roles there are from the rules, from the rows of the roles kept for the resource, or from what the application answers for roles kept elsewhere.
- A [`MembershipRegistration`](#membershipregistration), which a start-up check reads.
- The `MemberHoldInterceptor`, one for every resource registered, which nothing uses until a context asks for the expert hold with [`MemberHoldDbContextOptionsExtensions.UseMemberHolds`](#usememberholdsdbcontextoptionsbuilder-iserviceprovider).
- The package's texts for what the resource refuses with, in English and Dutch, under the resource's own codes ([`MembershipCodes.TextKeys`](refusals.md#membershipcodes)): offered to the application's failure localizer, so an application that localizes its failures (`AddDDDToolkitLocalization`) adds no line for them, and one that does not is given nothing. A text of the application's own for one of these codes is asked first, wherever it was added.

The rules say three things apart from each other, and what they say decides what the application has to answer. Rules that need an answer nobody registered are refused here, when the application starts, with what to register, rather than read as something they are not:

- **Who a member is.** The caller's user id ([`MemberSource.CurrentUserId`](rules.md#membersource)), for a member id over a `Guid`; a claim of its token ([`MemberSource.Claim`](rules.md#claimstring)), for one over a text; or what the application resolves for the caller ([`MemberSource.Resolved`](rules.md#resolveddatabasefunction)), which needs an [`ICurrentMember<TResourceId, TMemberId>`](rules.md#icurrentmembertresourceid-tmemberid).
- **Where the roles come from.** The rules declare them, and a member holds them as [`NamedRole`](resource-and-members.md#namedrole); or a customer makes them ([`Roles.Custom`](rules.md#customireadonlylistdeclaredrole-otherpermissions-string)), as rows of the application's role class, which needs nothing registered: the context maps that class with `IsCustomRole`, and the roles are read from there; or they are kept elsewhere ([`Roles.External`](rules.md#externaldatabasefunction-otherpermissions)), which needs an [`IRolesWithPermission<TResourceId, TRoleId>`](#iroleswithpermissiontresourceid-troleid) for which roles give a permission and an [`IMemberRoles<TResourceId, TRoleId>`](rules.md#imemberrolestresourceid-troleid) for which roles there are.
- **Whether a resource is reached from above** ([`MembershipRules.ReachFromAbove`](rules.md#membershiprules)), which needs an [`IPlacesReached<TResourceId, TPlaceId>`](#iplacesreachedtresourceid-tplaceid) for where a caller holds a permission, and the resource marking where it hangs `[At]`.

The application registers what it answers before it registers the resource, or hands the class that answers to the registration that takes one: [`AddMembership<TMember, TId, TMemberId, TRoleId, TContext, TResource, TResourceId, TPorts>`](#addmembershipiservicecollection-membershiprules-1).

The questions read on a context of their own, from the context's factory (`IDbContextFactory<TContext>`), where the application registered one, a pooled one with `AddScopedFromPool` say, since the questions of one request may be asked side by side; otherwise on the request's own context. Where the resource, or the role class of a resource whose roles are kept, has a query filter that reads a member of the context it runs on, the questions read on the request's own context wherever the scope has one, as the member manager does: a rule kept on the request's context applies to both alike, and questions asked side by side then each take a scope of their own. A filter that reads what is around the work, and no member of its context, holds on every context. A `TimeProvider` and an `ICurrentUser` registered before stay; otherwise the system clock and the toolkit's `CurrentUser` are used.

The current user answers system work that no row access rule holds where nothing began a user, the work `AccessManager.BeginSystemBypassingRowAccess` begins. That work holds every permission on every resource. An application whose work always begins its user says so, with `services.RequireExplicitCurrentUser()`, and a request that reaches the questions with no user then fails rather than passes as the application. Work in a scope (`AccessManager.BeginSystem`) holds nothing on a resource unless the rules name that scope ([`MembershipRules.SystemScopes`](rules.md#membershiprules)).

`AddDocumentMembership<DocumentsContext>` is generated into the project that declares the member class, `[Member<DocumentShareId, UserId, NamedRole, Document>]`, closed over that class, its types, the resource it names and the resource's id (`TemplateRegistrationAttribute`), or into a project of the same `[assembly: Module]` that declares none. It is named after the resource whether a project declares one member class or several: `AddDocumentMembership` and `AddFolderMembership`. This is the method they call, and it can be called as well, with all seven types written out.

**Parameters**

- `services`: The service collection.
- `rules`: The resource's rules.

**Type parameters**

- `TMember`: The application's member class.
- `TId`: The member class's own id.
- `TMemberId`: What a member is known by.
- `TRoleId`: What a role is known by: [`NamedRole`](resource-and-members.md#namedrole) where the rules declare the roles, and the id of the application's role class where the roles are kept for the resource.
- `TContext`: The context that maps the resource and its members.
- `TResource`: The resource's aggregate.
- `TResourceId`: The resource's id.

**Exceptions**

- `ArgumentNullException`: `services` or `rules` is null.
- `ArgumentException`: The rules take the caller's member id from its user id for a member id that is not over a `Guid`, or from a claim for one that is not over a text; they declare the roles, and the members hold roles that are not [`NamedRole`](resource-and-members.md#namedrole); they say the roles are kept for the resource, and the members hold them by name; or they need something of the application that is not registered.
- `InvalidOperationException`: The resource is registered already, with other rules, another context or another member class; or the rules, or the member class, are those of another resource already.

#### `AddMembership(IServiceCollection, MembershipRules)`

```csharp
public static IServiceCollection AddMembership<TMember, TId, TMemberId, TRoleId, TContext, TResource, TResourceId, TPorts>(
    this IServiceCollection services,
    MembershipRules rules)
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TContext : DbContext
    where TResource : AggregateRoot<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TPorts : class
```

Registers one kind of resource with members, as [`AddMembership<TMember, TId, TMemberId, TRoleId, TContext, TResource, TResourceId>`](#addmembershipiservicecollection-membershiprules) does, together with the class that answers what the resource's rules ask of the application: for a resource beside an organization, the one class that joins the two.

```csharp
services.AddCrateMembership<DepotContext, CratesInTheDepot>(CrateMembership.Rules);
```

`TPorts` is registered, one for each scope, and every port of this resource it implements is answered by that one instance:

- [`ICurrentMember<TResourceId, TMemberId>`](rules.md#icurrentmembertresourceid-tmemberid): who the caller is as a member, for rules with [`MemberSource.Resolved`](rules.md#resolveddatabasefunction).
- [`IRolesWithPermission<TResourceId, TRoleId>`](#iroleswithpermissiontresourceid-troleid) and [`IMemberRoles<TResourceId, TRoleId>`](rules.md#imemberrolestresourceid-troleid): which roles give a permission, and which roles there are, for rules whose roles are kept elsewhere.
- [`IPlacesReached<TResourceId, TPlaceId>`](#iplacesreachedtresourceid-tplaceid): where the caller holds a permission, for rules that let a resource be reached from above.
- [`IMemberDirectory<TResourceId, TMemberId>`](managers.md#imemberdirectorytresourceid-tmemberid) and [`IMemberRolePolicy<TResourceId, TRoleId>`](managers.md#imemberrolepolicytresourceid-troleid), which no rules need: who can be made a member, and which roles go to one, as the member manager asks.
- [`IOwnPlaceWork<TResourceId, TMemberId>`](managers.md#iownplaceworktresourceid-tmemberid): how the save of a change to the caller's own place begins as the resource's own work. [`ICustomRoleAccess<TResource>`](managers.md#icustomroleaccesstresource): whether the caller may change the roles a customer makes for the resource.

A port registered before stays, so an application can answer one of them elsewhere. What the rules need and neither the class nor the application answers is refused, as by the other registration. The generated `AddCrateMembership<TContext, TPorts>` calls this, closed over the member class as the other is.

**Parameters**

- `services`: The service collection.
- `rules`: The resource's rules.

**Type parameters**

- `TMember`: The application's member class.
- `TId`: The member class's own id.
- `TMemberId`: What a member is known by.
- `TRoleId`: What a role is known by.
- `TContext`: The context that maps the resource and its members.
- `TResource`: The resource's aggregate.
- `TResourceId`: The resource's id.
- `TPorts`: The application's class that answers what the rules ask of it, for this resource.

**Exceptions**

- `ArgumentNullException`: `services` or `rules` is null.
- `ArgumentException`: `TPorts` answers nothing of this resource; or the rules are refused as [`AddMembership<TMember, TId, TMemberId, TRoleId, TContext, TResource, TResourceId>`](#addmembershipiservicecollection-membershiprules) refuses them.
- `InvalidOperationException`: The resource, its rules or its member class are registered otherwise already.

#### `AddMemberAccess(IServiceCollection)`

```csharp
public static IServiceCollection AddMemberAccess<TResource, TResourceId, TRequests>(this IServiceCollection services)
    where TResource : AggregateRoot<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TRequests : class, IRequireAccess
```

Adds the access check of one kind of resource with members to the checks the requests of one module are held to (`AccessChecks<TRequests>`): what decides the requirements a request declares about that resource ([`MemberAccess<TResourceId>`](managers.md#memberaccesstresourceid)) before its handler runs. An application calls the registration generated for its resource, named after it:

```csharp
services.AddDocumentMemberAccess<IDocumentsRequest>();
```

A module calls it for its own request interface, once for each kind of resource its requests are about, next to the resource's registration. A module whose requests are about another module's resource calls it for its own interface as well: a check is asked only about the requests of the interface it was added for.

It registers [`MemberAccessCheck<TResource, TResourceId>`](managers.md#memberaccesschecktresource-tresourceid) and nothing else, so what the check asks, the resource's access questions, is there once the resource itself is registered, in either order. The check is free of any dispatcher, as the package is: whatever calls `AccessChecks.RequireAsync` in front of the module's handlers holds the requests to it. Adding it for an interface more than once is harmless.

`AddDocumentMemberAccess<IDocumentsRequest>` is generated wherever `AddDocumentMembership` is, closed over the resource its member class names and that resource's id (`TemplateRegistrationAttribute`). This is the method it calls, and it can be called as well, with the resource and its id written out.

**Parameters**

- `services`: The service collection.

**Type parameters**

- `TResource`: The resource's aggregate.
- `TResourceId`: The resource's id.
- `TRequests`: The module's request interface, which every command and query of the module implements.

**Exceptions**

- `ArgumentNullException`: `services` is null.

### `MemberHoldDbContextOptionsExtensions`

```csharp
public static class MemberHoldDbContextOptionsExtensions
```

Switches the expert hold on for a context.

| Member | Summary |
|---|---|
| [`UseMemberHolds(DbContextOptionsBuilder, IServiceProvider)`](#usememberholdsdbcontextoptionsbuilder-iserviceprovider) | Holds every save of this context that changes a resource with members to what the access check of the request in hand read of it (`MemberHoldInterceptor`): one changed since the check is a lost race, and one no check read, outside the application's own work, is refused. |

#### `UseMemberHolds(DbContextOptionsBuilder, IServiceProvider)`

```csharp
public static DbContextOptionsBuilder UseMemberHolds(
    this DbContextOptionsBuilder optionsBuilder,
    IServiceProvider serviceProvider)
```

Holds every save of this context that changes a resource with members to what the access check of the request in hand read of it (`MemberHoldInterceptor`): one changed since the check is a lost race, and one no check read, outside the application's own work, is refused. The handlers write nothing for it. One line, after `UseDDDToolkit`, or after `UseDDDToolkitCore` for a context given the toolkit's base alone, so the save it holds is the one the domain event handlers changed as well:

```csharp
services.AddDbContext<FilingContext>((serviceProvider, options) => options
    .UseNpgsql(connectionString)
    .UseDDDToolkit(serviceProvider)
    .UseMemberHolds(serviceProvider));
```

Without it, a command's change is checked as the default path checks it: its request's requirement before the handler, the version its caller named when it loads (`ExpectVersion`), the version it loaded at when it saves, the aggregate's invariants, and, where the database checks every row, the database.

The same call serves a context pool: the interceptor is one instance for the application, and the hold it asks for is the one of the request in hand in the flow that saves, whichever scope the context is of.

It is no part that a registration brings and `UseDDDToolkit` puts on every context: a host switches it on, per context. Like the parts' own calls it adds nothing the options already have, so a second call gives the interceptor once.

**Parameters**

- `optionsBuilder`: The context's options.
- `serviceProvider`: The provider handed to the options callback, of `AddDbContext` or of a context pool: the interceptor is registered with the resources, as a singleton, so a pool's root provider serves as well as a scope's.

**Exceptions**

- `ArgumentNullException`: `optionsBuilder` or `serviceProvider` is null.
- `InvalidOperationException`: No resource with members is registered in `serviceProvider`, or neither `UseDDDToolkit` nor `UseDDDToolkitCore` was called on `optionsBuilder` before: the hold would then run before the domain event handlers, and what they change in the same save would be held to nothing.

### `MembershipRegistration`

```csharp
public sealed record MembershipRegistration
```

One kind of resource with members, as its registration added it: which context maps it, which class its members are of, and the rules it runs with. There is one in the container for each kind of resource, so whatever checks an application at start-up can ask for them all and hold each database to the rules its resource was registered with.

| Member | Summary |
|---|---|
| [`MembershipRegistration(Type, Type, Type, Type, MembershipRules)`](#membershipregistrationtype-type-type-type-membershiprules) | The primary constructor: the type's summary says what it makes. |
| `Type Context { get; init; }` | The context that maps the resource and its members. |
| `Type Resource { get; init; }` | The resource's aggregate. |
| `Type ResourceIdType { get; init; }` | The type of the resource's id, which its access questions are asked for by. |
| `Type Member { get; init; }` | The application's member class. |
| `MembershipRules Rules { get; init; }` | The rules the resource was registered with. |

#### `MembershipRegistration(Type, Type, Type, Type, MembershipRules)`

```csharp
public MembershipRegistration(
    Type Context,
    Type Resource,
    Type ResourceIdType,
    Type Member,
    MembershipRules Rules)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Context`: The context that maps the resource and its members.
- `Resource`: The resource's aggregate.
- `ResourceIdType`: The type of the resource's id, which its access questions are asked for by.
- `Member`: The application's member class.
- `Rules`: The rules the resource was registered with.

## Mapping

Namespace `DDDToolkit.Supporting.Membership.EntityFramework`.

### `MembershipEntityTypeBuilderExtensions`

```csharp
public static class MembershipEntityTypeBuilderExtensions
```

Maps the members of a resource, and the roles a customer makes for one, into the model of the application's own context.

| Member | Summary |
|---|---|
| [`HasMembers(EntityTypeBuilder<TResource>, Expression<Func<TResource, IEnumerable<TMember>?>>, MemberTableNames?)`](#hasmembersentitytypebuildertresource-expressionfunctresource-ienumerabletmember-membertablenames) | Maps the members of a resource: the members in a table of their own, owned by the resource, and the roles they hold in a second one, owned by the member. |
| [`IsCustomRole(EntityTypeBuilder<TRole>)`](#iscustomroleentitytypebuildertrole) | Maps the role class of a resource whose roles a customer makes: the application's class declared with the role template, in a table of its own. |

#### `HasMembers(EntityTypeBuilder<TResource>, Expression<Func<TResource, IEnumerable<TMember>?>>, MemberTableNames?)`

```csharp
public static EntityTypeBuilder<TResource> HasMembers<TResource, TMember>(
    this EntityTypeBuilder<TResource> resource,
    Expression<Func<TResource, IEnumerable<TMember>?>> members,
    MemberTableNames? names = null)
    where TResource : class
    where TMember : class
```

Maps the members of a resource: the members in a table of their own, owned by the resource, and the roles they hold in a second one, owned by the member. Call it where the context maps the resource:

```csharp
modelBuilder.Entity<Document>().HasMembers(document => document.Shares);
```

The resource stays the application's aggregate, mapped as it always was; this adds its members to it, so they are loaded and saved with it, under its one version. Nothing is written by hand for the member class: the member, its periods and its roles are the package's, and what the application added to its class is mapped by the conventions, as on any entity.

What the resource says of itself in the domain is read off its class: its owner, the property it marks `[Owner]`, of what a member is known by, and where it hangs, the property it marks `[At]`, where it has one. Both become columns of the resource whatever else the application says about them. Where it hangs is what a permission held from above is compared with: a resource whose rules let it be reached from above ([`MembershipRules.ReachFromAbove`](rules.md#membershiprules)) is held by a caller that holds the permission at that place, or above it, as the application answers where that is ([`IPlacesReached<TResourceId, TPlaceId>`](#iplacesreachedtresourceid-tplaceid)). The package keeps no places: it reads that column, and nothing of what is above it.

The type arguments are inferred from the collection. The member class's own id, what a member is known by and what its roles are known by are read from the class when the model is built.

What it maps:

- The members' table, keyed by the resource and the member row's id, with an index on who the member is: "which resources am I a member of" is what every list asks. Who the member is is fixed once the row is there (`IsFixedAfterInsert`): a save that changed it is refused, and the exported privileges leave the column out of UPDATE.
- The roles' table, keyed by the resource, the member row and the role, so a member holds a role once in the database as well.
- Both under the resource's whole key: what the resource is keyed by when this is called, and in front of it the parts the resource declares with `[KeyPart]`, which join its key when the model is finished. A key given with `HasKey` is said before this call: one said afterwards does not reach the member tables, and is refused where the mapping is read.
- Every moment of a period as its UTC instant, so a database that keeps no offsets compares a period in SQL.
- A role known by its name ([`NamedRole`](resource-and-members.md#namedrole)) as that text.

The ids are stored by the application's generated converters, like the ids of its other entities: register them and `AddDDDToolkitConventions()` in `ConfigureConventions`. Tables and the member's column take the names of `names`, and the tables are in the schema of the resource's own table, as it is when this is called; everything else is named as the context names things.

It leaves a mark on the resource's entity type ([`MembershipModel`](#membershipmodel)), which is how the access questions registered for the resource, and a package that writes database functions, find the members of this resource and of no other. An application with several kinds of resource calls it on each.

**Parameters**

- `resource`: The builder of the resource's entity type.
- `members`: The resource's collection of members.
- `names`: The names of the two tables and of the member's column; named after the resource and the member class when left out.

**Type parameters**

- `TResource`: The resource's aggregate.
- `TMember`: The application's member class, declared with the member template.

**Exceptions**

- `ArgumentNullException`: `resource` or `members` is null.
- `ArgumentException`: `TMember` was not declared with the member template; the resource marks no property `[Owner]`, or one that is not of what the members are known by; it marks two properties `[At]`, or a nullable one; or `members` is not a property of the resource.
- `InvalidOperationException`: The resource has members already: it has one member list.

#### `IsCustomRole(EntityTypeBuilder<TRole>)`

```csharp
public static EntityTypeBuilder<TRole> IsCustomRole<TRole>(this EntityTypeBuilder<TRole> role)
    where TRole : class
```

Maps the role class of a resource whose roles a customer makes: the application's class declared with the role template, in a table of its own. Call it where the context maps that class:

```csharp
modelBuilder.Entity<PlotRole>(role =>
{
    role.IsCustomRole();

    // The application's own: whose a role is, that a name is used once there, and that a starter role is made once there
    role.HasIndex(row => new { row.GardenId, row.Name }).IsUnique().RefusesAs("plots.role-name-taken", "Another role is called {Name} already.");
    role.HasIndex(row => new { row.GardenId, row.MadeFrom }).IsUnique();
});
```

The role class stays the application's aggregate: its table, its other columns, its indexes and the rule that keeps one customer's roles from another's are the application's to say, as for any aggregate.

What it maps is the package's part of a role: the id, which the application makes; the name, the description and the starter role it was made from, each no longer than the role allows; the status as its name; and the permissions as a collection of texts in one column, which is what the access questions and the database's functions read. The id is stored by the application's generated converter, like the ids of its other entities. The starter role a role was made from is fixed once the row is there (`IsFixedAfterInsert`): the starter roles a customer lacks are found by it, so a save that changed it is refused, and the privileges a Postgres export writes leave the column out of what a caller may update.

It leaves a mark on the role's entity type ([`MembershipModel`](#membershipmodel)) that names the resource the roles are of, as the class's template names it. That is how the access questions registered for the resource, and a package that writes database functions, find the roles of this resource and of no other: nothing else has to be registered for them.

A class that marks the organization its rows are of, with the marker the organization's package gives a domain class for it, gets two unique indexes over that property from this mapping. A name is used once in an organization, and an organization has each starter role once, which also keeps two set-ups of one organization at the same moment apart. Entity Framework names them, `IX_{table}_{property}_Name` and `IX_{table}_{property}_MadeFrom`. A context that wants a refusal of its own for a name used twice says it on the same index: `role.HasIndex(row => new { row.OrganizationId, row.Name }).RefusesAs(...)`. A customer of another kind, a garden say, is a column the package knows nothing of, and its indexes are the application's to declare, as above.

A context that calls `AddDDDToolkitConventions()` needs no call of this: the role template names Membership's convention, which maps the class the same way. Call it for a class the context maps otherwise.

**Parameters**

- `role`: The builder of the role class's entity type.

**Type parameters**

- `TRole`: The application's role class, declared with the role template.

**Exceptions**

- `ArgumentNullException`: `role` is null.
- `ArgumentException`: `TRole` was not declared with the role template.

### `MemberTableNames`

```csharp
public sealed record MemberTableNames
```

The names `HasMembers` gives the two member tables of one resource, for an application that has names of its own for them, or tables that were there before:

```csharp
modelBuilder.Entity<Folder>().HasMembers(folder => folder.Staff, folder => folder.Keeper,
    new MemberTableNames("FolderStaff", "FolderStaffRoles", MemberColumn: "StaffCode"));
```

They are one resource's names. An application with several kinds of resource names each one's tables where it maps that resource, and nothing here is shared between two.

Everything else, every other column, the keys and the index, is named as the context names everything, by Entity Framework or by a naming convention on its options. Whatever the tables and columns end up being called, the access questions and the database's functions read the names from the model.

| Member | Summary |
|---|---|
| [`MemberTableNames(string?, string?, string?)`](#membertablenamesstring-string-string) | The primary constructor: the type's summary says what it makes. |
| `string? Members { get; init; }` | The table of the members. Left out, it is named after the resource and the property that holds its members: `DocumentShares` for `Document.Shares`. |
| `string? Roles { get; init; }` | The table of the roles members hold. Left out, it is named after the member class: `DocumentShareRoles` for `DocumentShare`. |
| `string? MemberColumn { get; init; }` | The column that holds who the member is, for an application whose word for a member is its own: `UserId`, `StaffCode`. Left out, it is named as the context names the property `MemberId`. |

#### `MemberTableNames(string?, string?, string?)`

```csharp
public MemberTableNames(string? Members = null, string? Roles = null, string? MemberColumn = null)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Members`: The table of the members. Left out, it is named after the resource and the property that holds its members: `DocumentShares` for `Document.Shares`.
- `Roles`: The table of the roles members hold. Left out, it is named after the member class: `DocumentShareRoles` for `DocumentShare`.
- `MemberColumn`: The column that holds who the member is, for an application whose word for a member is its own: `UserId`, `StaffCode`. Left out, it is named as the context names the property `MemberId`.

### `MembershipModel`

```csharp
public static class MembershipModel
```

What a model built with `HasMembers` says about the members of its resources, for whatever works from the model: the access questions, which compose them into the context's own statements, and a package that writes database functions for them.

A model may map several kinds of resource with members, each marked on its own entity type, so every question here is asked about one resource or one member class.

| Member | Summary |
|---|---|
| `MembersAnnotation = "DDDToolkit:Membership:Members"` | The annotation `HasMembers` leaves on a resource's entity type: the name of the property that holds its members. |
| `OwnerAnnotation = "DDDToolkit:Membership:Owner"` | The annotation `HasMembers` leaves on a resource's entity type: the name of the property that holds its owner. |
| `AtAnnotation = "DDDToolkit:Membership:At"` | The annotation `HasMembers` leaves on the entity type of a resource that says where it sits: the name of the property that holds the place. |
| `RoleOfAnnotation = "DDDToolkit:Membership:RoleOf"` | The annotation `IsCustomRole` leaves on the entity type of a role class: the full name of the resource the roles are of, as its template names it. |
| [`MembersOf(IEntityType)`](#membersofientitytype) | The members of `resource`, as `HasMembers` mapped them, or `null` for an entity type it was not called on. |
| [`For(IModel, Type)`](#forimodel-type) | The resource whose members are of `memberClass`, with those members, or `null` when `model` maps no such resource: a context of another module. |

#### `MembersOf(IEntityType)`

```csharp
public static MemberMapping? MembersOf(IEntityType resource)
```

The members of `resource`, as `HasMembers` mapped them, or `null` for an entity type it was not called on.

**Parameters**

- `resource`: An entity type of the model.

**Exceptions**

- `ArgumentNullException`: `resource` is null.
- `InvalidOperationException`: The entity type is marked, and the model no longer has what the mark names: the mapping was changed after `HasMembers`. Or the resource was given another key after its members were mapped, which does not reach their tables. Or the model maps two role classes for the resource, or a role class without what `IsCustomRole` maps of it.

#### `For(IModel, Type)`

```csharp
public static MemberMapping? For(IModel model, Type memberClass)
```

The resource whose members are of `memberClass`, with those members, or `null` when `model` maps no such resource: a context of another module. A member class belongs to one kind of resource, so there is one answer.

**Parameters**

- `model`: The model of a context.
- `memberClass`: The application's member class, declared with the member template.

**Exceptions**

- `ArgumentNullException`: `model` or `memberClass` is null.

### `MemberMapping`

```csharp
public sealed record MemberMapping
```

A resource with its members, as one model maps them with `HasMembers`: the resource's own entity type, the table of its members and the table of the roles they hold, where the resource keeps its owner, where it sits, for one that says so, and its role class, for one whose roles are kept. What works from the model reads the tables and columns from here, so it follows whatever the application calls them.

| Member | Summary |
|---|---|
| [`MemberMapping(IEntityType, INavigation, IProperty, IEntityType, IEntityType, IProperty?, IEntityType?)`](#membermappingientitytype-inavigation-iproperty-ientitytype-ientitytype-iproperty-ientitytype) | The primary constructor: the type's summary says what it makes. |
| `IEntityType Resource { get; init; }` | The resource's entity type: the application's aggregate. |
| `INavigation Navigation { get; init; }` | The resource's collection of members. |
| `IProperty Owner { get; init; }` | The resource's property that holds its owner, as a member is known. |
| `IEntityType Members { get; init; }` | The members' entity type, in a table of its own, owned by the resource. |
| `IEntityType Roles { get; init; }` | The entity type of the roles members hold, in a table of its own, owned by the member. |
| `IProperty? At { get; init; }` | The resource's property that holds where it sits, for a resource mapped with `at:`, or `null`: what a permission held from above is compared with. |
| `IEntityType? RoleClass { get; init; }` | The entity type of the application's role class for this resource, mapped with `IsCustomRole`, or `null` when the model maps none: the rows a role's permissions are read from, for a resource whose roles are kept. |
| `Type MemberClass { get; }` | The application's member class. |

#### `MemberMapping(IEntityType, INavigation, IProperty, IEntityType, IEntityType, IProperty?, IEntityType?)`

```csharp
public MemberMapping(
    IEntityType Resource,
    INavigation Navigation,
    IProperty Owner,
    IEntityType Members,
    IEntityType Roles,
    IProperty? At = null,
    IEntityType? RoleClass = null)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Resource`: The resource's entity type: the application's aggregate.
- `Navigation`: The resource's collection of members.
- `Owner`: The resource's property that holds its owner, as a member is known.
- `Members`: The members' entity type, in a table of its own, owned by the resource.
- `Roles`: The entity type of the roles members hold, in a table of its own, owned by the member.
- `At`: The resource's property that holds where it sits, for a resource mapped with `at:`, or `null`: what a permission held from above is compared with.
- `RoleClass`: The entity type of the application's role class for this resource, mapped with `IsCustomRole`, or `null` when the model maps none: the rows a role's permissions are read from, for a resource whose roles are kept.

## Queries

Namespace `DDDToolkit.Supporting.Membership.EntityFramework`.

### `MemberQueryableExtensions`

```csharp
public static class MemberQueryableExtensions
```

Asks Membership inside a statement of your own, on the resource's own set: "the boards I may see" and "the permissions I hold on these boards" are each one statement of the context, and never a list of ids fetched first.

```csharp
var boards = await db.Boards.WhereIHold(BoardPermissions.View).OrderBy(board => board.Name).ToListAsync(cancellationToken);
```

The caller is the one current when the query is made, and the moment is fixed then: every part of one query answers for the same caller at the same moment. The rules, and what they ask of the application, come from the services the context was made with: the scope it is bound to (`PooledContexts.BindToScope<TContext>`, which `PooledContexts.AddScopedFromPool<TContext>` does for a scope's own context), or else the services its options were built with, which for `AddDbContext` are the scope's.

What a query is filtered by puts subqueries over the resource's own member rows into it, and, where the rules have rows say which roles give a permission or have the application say where a permission is held above, queries of the context the statement runs on. A query on the set finds its context itself. A query that does not start at a set, one composed of others, hands its context over, as the last argument:

```csharp
var open = db.Boards.Where(board => !board.Archived).WhereIHold(BoardPermissions.View, db);
```

[`Within<TResource>`](#withiniqueryabletresource-memberreachtresource-dbcontext) and [`Reached<TResource>`](#reachediqueryabletresource-memberreachtresource-dbcontext) are the lower level, for a statement that reads how each resource is held: they take a reach the resource's access manager made ([`MembershipAccessManager.Reach`](managers.md#reachstring)).

| Member | Summary |
|---|---|
| [`WhereIHold(DbSet<TResource>, string)`](#whereiholddbsettresource-string) | The resources of the set the caller holds `permission` on: those it is a member of now with a role that gives the permission, for a permission that being a member gives those it is a member of at all, for a permission of the resource those it owns, and, where the rules let a resource be reached from above, those that sit where the caller holds the permission, or below. |
| [`WhereIHold(IQueryable<TResource>, string, DbContext)`](#whereiholdiqueryabletresource-string-dbcontext) | Those of `resources` the caller holds `permission` on, as [`WhereIHold<TResource>`](#whereiholddbsettresource-string) answers it, for a query that does not start at the set. |
| [`WhereIHold(DbSet<TResource>, IReadOnlyCollection<string>)`](#whereiholddbsettresource-ireadonlycollectionstring) | Which of `permissions` the caller holds on which resources of the set: one pair of resource and permission for each permission held on a resource it sees, for a list that shows what its caller may do with each row, in one statement. |
| [`WhereIHold(IQueryable<TResource>, IReadOnlyCollection<string>, DbContext)`](#whereiholdiqueryabletresource-ireadonlycollectionstring-dbcontext) | Which of `permissions` the caller holds on which of `resources`, as [`WhereIHold<TResource>`](#whereiholddbsettresource-ireadonlycollectionstring) answers it, for a query that does not start at the set. |
| [`Within(IQueryable<TResource>, MemberReach<TResource>, DbContext?)`](#withiniqueryabletresource-memberreachtresource-dbcontext) | Those of `resources` that `reach` reaches: every one for the application's own work, none for a caller that is nobody's member. |
| [`Reached(IQueryable<TResource>, MemberReach<TResource>, DbContext?)`](#reachediqueryabletresource-memberreachtresource-dbcontext) | Each of `resources` with how `reach` reaches it, as a step the query goes on from: for a statement that answers a resource's own columns next to whether the caller holds a permission on it. |

#### `WhereIHold(DbSet<TResource>, string)`

```csharp
public static IQueryable<TResource> WhereIHold<TResource>(
    this DbSet<TResource> resources,
    string permission)
    where TResource : class
```

The resources of the set the caller holds `permission` on: those it is a member of now with a role that gives the permission, for a permission that being a member gives those it is a member of at all, for a permission of the resource those it owns, and, where the rules let a resource be reached from above, those that sit where the caller holds the permission, or below. Every resource for the application's own work; none for a caller that is nobody.

**Parameters**

- `resources`: The resource's set on a context.
- `permission`: A permission.

**Type parameters**

- `TResource`: The resource's aggregate.

**Exceptions**

- `ArgumentNullException`: `resources` is null.
- `ArgumentException`: `permission` is blank.
- `InvalidOperationException`: The resource is not registered with Membership in the services the context was made with, or with another context; or what its rules ask of the application is registered per scope, and the context is in none.

#### `WhereIHold(IQueryable<TResource>, string, DbContext)`

```csharp
public static IQueryable<TResource> WhereIHold<TResource>(
    this IQueryable<TResource> resources,
    string permission,
    DbContext context)
    where TResource : class
```

Those of `resources` the caller holds `permission` on, as [`WhereIHold<TResource>`](#whereiholddbsettresource-string) answers it, for a query that does not start at the set.

**Parameters**

- `resources`: A query over the resources, of `context`.
- `permission`: A permission.
- `context`: The context the query runs on.

**Type parameters**

- `TResource`: The resource's aggregate.

**Exceptions**

- `ArgumentNullException`: `resources` or `context` is null.
- `ArgumentException`: `permission` is blank.
- `InvalidOperationException`: The resource is not registered with Membership in the services `context` was made with; what its rules ask of the application is registered per scope, and the context is in none; or the query runs on another context than `context`.

#### `WhereIHold(DbSet<TResource>, IReadOnlyCollection<string>)`

```csharp
public static IQueryable<PermissionOn<TResource>> WhereIHold<TResource>(
    this DbSet<TResource> resources,
    IReadOnlyCollection<string> permissions)
    where TResource : class
```

Which of `permissions` the caller holds on which resources of the set: one pair of resource and permission for each permission held on a resource it sees, for a list that shows what its caller may do with each row, in one statement. A resource the caller does not see is in no pair; the application's own work holds every permission on every resource.

```csharp
var held = await db.Boards.WhereIHold([BoardPermissions.Edit, BoardPermissions.Archive])
    .Select(pair => new { pair.Resource.Id, pair.Permission })
    .ToListAsync(cancellationToken);
```

**Parameters**

- `resources`: The resource's set on a context.
- `permissions`: Permissions, none of them blank.

**Type parameters**

- `TResource`: The resource's aggregate.

**Exceptions**

- `ArgumentNullException`: `resources` or `permissions` is null.
- `ArgumentException`: A permission is blank.
- `InvalidOperationException`: The resource is not registered with Membership in the services the context was made with, or with another context; or what its rules ask of the application is registered per scope, and the context is in none.

#### `WhereIHold(IQueryable<TResource>, IReadOnlyCollection<string>, DbContext)`

```csharp
public static IQueryable<PermissionOn<TResource>> WhereIHold<TResource>(
    this IQueryable<TResource> resources,
    IReadOnlyCollection<string> permissions,
    DbContext context)
    where TResource : class
```

Which of `permissions` the caller holds on which of `resources`, as [`WhereIHold<TResource>`](#whereiholddbsettresource-ireadonlycollectionstring) answers it, for a query that does not start at the set.

**Parameters**

- `resources`: A query over the resources asked about, of `context`.
- `permissions`: Permissions, none of them blank.
- `context`: The context the query runs on.

**Type parameters**

- `TResource`: The resource's aggregate.

**Exceptions**

- `ArgumentNullException`: An argument is null.
- `ArgumentException`: A permission is blank.
- `InvalidOperationException`: The resource is not registered with Membership in the services `context` was made with; what its rules ask of the application is registered per scope, and the context is in none; or the query runs on another context than `context`.

#### `Within(IQueryable<TResource>, MemberReach<TResource>, DbContext?)`

```csharp
public static IQueryable<TResource> Within<TResource>(
    this IQueryable<TResource> resources,
    MemberReach<TResource> reach,
    DbContext? context = null)
    where TResource : class
```

Those of `resources` that `reach` reaches: every one for the application's own work, none for a caller that is nobody's member. The lower level under [`WhereIHold<TResource>`](#whereiholdiqueryabletresource-string-dbcontext), for a reach made once and put into several statements, or made in one place and put into a statement in another.

```csharp
var seen = boardAccess.Reach(BoardPermissions.View);
var page = await db.Boards.Within(seen).OrderBy(board => board.Name).Take(20).ToListAsync(cancellationToken);
```

**Parameters**

- `resources`: A query over the resources.
- `reach`: A reach made by the access manager of this kind of resource.
- `context`: The context the query runs on, for a query that does not run on the request's own and a reach that puts a query of its own into it: the rows of the roles kept for the resource, or what the application answers. Left out otherwise.

**Type parameters**

- `TResource`: The resource's aggregate.

**Exceptions**

- `ArgumentNullException`: `resources` or `reach` is null.
- `ArgumentException`: `reach` was not made by the access manager registered for `TResource`.
- `InvalidOperationException`: The reach puts a query of its own into the query, the roles' rows or what the application answers, and the query runs on another context than `context`, or than the request's own when none is handed over.

#### `Reached(IQueryable<TResource>, MemberReach<TResource>, DbContext?)`

```csharp
public static IQueryable<MemberFound<TResource>> Reached<TResource>(
    this IQueryable<TResource> resources,
    MemberReach<TResource> reach,
    DbContext? context = null)
    where TResource : class
```

Each of `resources` with how `reach` reaches it, as a step the query goes on from: for a statement that answers a resource's own columns next to whether the caller holds a permission on it. Nothing is left out here; put [`WhereIHold<TResource>`](#whereiholdiqueryabletresource-string-dbcontext) or [`Within<TResource>`](#withiniqueryabletresource-memberreachtresource-dbcontext) in front, with the permission that sees, so a caller is answered only about what it may see:

```csharp
var act = boardAccess.Reach(BoardPermissions.Edit);
var answer = await db.Boards.Where(board => board.Id == id)
    .WhereIHold(BoardPermissions.View, db)
    .Reached(act)
    .Select(found => new { found.Resource.Archived, found.AsMember, found.FromAbove, found.Until })
    .FirstOrDefaultAsync(cancellationToken);
var via = answer is null ? null : act.Via(answer.AsMember, answer.FromAbove);
```

[`MemberReach.Via`](managers.md#viabool-bool) makes of the two facts how the permission is held, the application's own work included, and [`MemberFound.Until`](#memberfoundtresource) is until when it is held as a member, for a statement that goes on to it.

**Parameters**

- `resources`: A query over the resources.
- `reach`: A reach made by the access manager of this kind of resource.
- `context`: The context the query runs on, for a query that does not run on the request's own and a reach that puts a query of its own into it. Left out otherwise.

**Type parameters**

- `TResource`: The resource's aggregate.

**Exceptions**

- `ArgumentNullException`: `resources` or `reach` is null.
- `ArgumentException`: `reach` was not made by the access manager registered for `TResource`.
- `InvalidOperationException`: The reach puts a query of its own into the query, and the query runs on another context than `context`, or than the request's own when none is handed over.

### `PermissionOn<TResource>`

```csharp
public sealed class PermissionOn<TResource>
    where TResource : class
```

A permission the caller holds on a resource: one row of what `db.Boards.WhereIHold([BoardPermissions.Edit, BoardPermissions.Archive])` answers, for a list that shows what its caller may do with each row. A permission is one row on a resource, however many ways the caller holds it there, and a resource the caller does not see is in no row.

```csharp
var buttons = await db.Boards.WhereIHold([BoardPermissions.Edit, BoardPermissions.Archive])
    .Select(pair => new { pair.Resource.Id, pair.Permission })
    .ToListAsync(cancellationToken);
```

A row is part of a statement: a query goes on from it to what it reads of the resource, as above.

**Type parameters**

- `TResource`: The resource's aggregate.

| Member | Summary |
|---|---|
| `required TResource Resource { get; init; }` | The resource. |
| `required string Permission { get; init; }` | The permission held on it. |

### `MemberFound<TResource>`

```csharp
public sealed class MemberFound<TResource>
    where TResource : class
```

A resource with how a reach reaches it: a step of a statement, never read as it is. A query goes on from it to what it answers, the resource's own columns next to how the caller holds the permission:

```csharp
.Reached(act).Select(found => new DocumentAnswer(found.Resource.Id, found.Resource.Archived, found.AsMember))
```

It is made by setting its members, which Entity Framework reads back as columns, so each fact is a subquery of the one statement, and one the query does not go on to is not asked.

**Type parameters**

- `TResource`: The resource's aggregate.

| Member | Summary |
|---|---|
| `required TResource Resource { get; init; }` | The resource. |
| `required bool AsMember { get; init; }` | Whether the caller holds the permission on the resource as one of its members: it is a member now, with what the reach asks of a membership, a role that gives the permission, held now, or nothing more for a permission that being a member gives; or it owns the resource, and the permission is one its owner holds by owning it. |
| `required DateTimeOffset? Until { get; init; }` | The first moment the caller no longer holds the permission on the resource that way, as its membership and its roles stand now, or `null` when that has no end, as for the resource's owner, and when [`AsMember`](#memberfoundtresource) is `false`. The end of the membership for a permission that being a member gives; for a permission a role gives, the end of the last role that gives it now, or of the membership if that ends sooner. It says nothing of a hold from above ([`FromAbove`](#memberfoundtresource)), which has no end the package knows: a caller that holds the permission that way as well keeps it past this moment, for as long as it sees the resource. The access questions say that in the hold they answer ([`MemberHold.Until`](managers.md#until)). |
| `required bool FromAbove { get; init; }` | Whether the permission is held where the resource sits, or above it, as the application answers it. Always `false` for a resource whose rules do not let it be reached from above. |

## Answered by the application

Namespace `DDDToolkit.Supporting.Membership.EntityFramework`.

### `IPlacesReached<TResourceId, TPlaceId>`

```csharp
public interface IPlacesReached<TResourceId, TPlaceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TPlaceId : struct, IEntityId, IEquatable<TPlaceId>
```

Where a caller holds a permission, for one kind of resource that is reached from above ([`MembershipRules.ReachFromAbove`](rules.md#membershiprules)): the places a hold of the permission reaches, which a resource that sits at one of them is then held on. An application implements it over whatever keeps who holds what where, the units of an organization say, and registers it with the resource.

It answers a query, not a list: the access questions put it into their own statement, where it becomes a subquery next to the resource's row, so what a caller holds stays one statement. It is therefore asked with the context that statement runs on, and answers over rows that context maps:

```csharp
public IQueryable<BayId> PlacesReached(DbContext context, CurrentUserSnapshot user, string permission)
{
    var id = user.Id;
    return from porter in context.Set<Porter>()
           where porter.UserId == id && porter.Active
           join hold in context.Set<BayHold>() on porter.Id equals hold.PorterId
           where hold.Key == permission
           join path in context.Set<BayPath>() on hold.BayId equals path.AboveId
           select path.BayId;
}
```

The questions of one request may be asked side by side, each on a context of its own, so it keeps nothing between two questions and reads on no context but the one it is handed.

It is asked about every signed-in caller, a member of the resource or not, and also about one the application answered nobody for as a member ([`ICurrentMember.Find`](rules.md#findcurrentusersnapshot)): reach from above is for callers that are on no member list. So whether the caller still counts is asked here as well, of the same rows or the same fact the application answers who a member is from. An application that answers from the caller's id alone lets somebody it no longer counts keep what they held.

What is above what is the application's to know: the package compares where a resource sits (the property its class marks `[At]`) with what this answers, and keeps no places and no tree of them. A permission answered here is held whatever the rules say a member's role gives: that list is about members.

**Type parameters**

- `TResourceId`: The id of the kind of resource that sits somewhere.
- `TPlaceId`: What a place is known by: what the resource's row stores where it sits.

| Member | Summary |
|---|---|
| [`PlacesReached(DbContext, CurrentUserSnapshot, string)`](#placesreacheddbcontext-currentusersnapshot-string) | The places `user`'s hold of `permission` reaches now: each place the permission is held at, and every place below it. |

#### `PlacesReached(DbContext, CurrentUserSnapshot, string)`

```csharp
IQueryable<TPlaceId> PlacesReached(DbContext context, CurrentUserSnapshot user, string permission)
```

The places `user`'s hold of `permission` reaches now: each place the permission is held at, and every place below it. None for a caller that holds the permission nowhere. Not run here: it goes into the statement that asks.

**Parameters**

- `context`: The context the statement runs on: the query is over rows of it.
- `user`: The caller the question is asked for: a signed-in user.
- `permission`: A permission.

### `IRolesWithPermission<TResourceId, TRoleId>`

```csharp
public interface IRolesWithPermission<TResourceId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Which of the roles an application keeps give a permission, for one kind of resource whose rules say its roles are kept elsewhere ([`Roles.External`](rules.md#externaldatabasefunction-otherpermissions)). An application implements it over the rows it keeps its roles in, and registers it with the resource.

It answers a query, not a list: the access questions put it into their own statement, where it becomes a subquery next to the member rows, so what a caller holds stays one statement. It is therefore asked with the context that statement runs on, and answers over rows that context maps, the application's own or the ones another module lets it read:

```csharp
public IQueryable<DutyId> RolesWith(DbContext context, CurrentUserSnapshot user, string permission)
    => context.Set<DutyPermission>().Where(given => given.Permission == permission).Select(given => given.DutyId);
```

The questions of one request may be asked side by side, each on a context of its own, so it keeps nothing between two questions and reads on no context but the one it is handed.

What a role holds here is not all it gives a member: the package asks only for a permission within the rules' [`MembershipRules.RolePermissions`](rules.md#membershiprules), so a role that holds a permission outside it gives nothing with that permission on the resource, whatever this answers.

**Type parameters**

- `TResourceId`: The id of the kind of resource the members are of.
- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| [`RolesWith(DbContext, CurrentUserSnapshot, string)`](#roleswithdbcontext-currentusersnapshot-string) | The roles that give `permission` now: those in use, as `user` has them, in its part of an organization say. |

#### `RolesWith(DbContext, CurrentUserSnapshot, string)`

```csharp
IQueryable<TRoleId> RolesWith(DbContext context, CurrentUserSnapshot user, string permission)
```

The roles that give `permission` now: those in use, as `user` has them, in its part of an organization say. Not run here: it goes into the statement that asks.

**Parameters**

- `context`: The context the statement runs on: the query is over rows of it.
- `user`: The caller the question is asked for: a signed-in user.
- `permission`: A permission a member's role can give on the resource.
