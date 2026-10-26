---
title: "Tenancy API: Postgres"
sidebar_label: Postgres
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Tenancy API: Postgres

The public API of `DDDToolkit.Supporting.Tenancy.Postgres`, by folder. Its registration, `AddTenancyPostgres`, is with the use cases, under [Registration](use-cases.md#registration).

## Policies

Namespace `DDDToolkit.Supporting.Tenancy.Postgres`.

### `TenancyRowAccessContribution`

```csharp
public class TenancyRowAccessContribution : IRowAccessContribution, ICallerFunctions
```

Tenancy's row level security, written from each context's model when the policies are exported: the SQL functions rules ask ([`TenancyRowAccess`](questions.md#tenancyrowaccess)), the policies on Tenancy's own tables and on every table a module keeps to a tenant, and the triggers that check at commit what no policy can see. This package declares it with `[assembly: RowAccessContribution]`, so the Supabase export of every application that references the package writes it, and the application writes nothing for it but, where it has them, two marks:

```csharp
[TenancyCatalogue]
public static ApplicationCatalogue Application { get; } = new(Packs: [...]);

[TenancyOperators]
public static IReadOnlyList<string> OperatorTokenRoles { get; } = ["operator"];
```

The catalogue is the one the application runs with, the application's part and every module's permissions: its marks decide which roles manage access, its [`ApplicationCatalogue.ContainAccessManagingPermissions`](catalogue.md#containaccessmanagingpermissions) whether the policies contain the grants of those roles, and its packs which roles a settings manager may add. When any of them changes, a permission added to a pack or to the administrators' pack included, the next export writes the access file again. The export makes the class before the application starts, so it builds the catalogue as the host's registration does, without the application's services: from the member marked [`TenancyCatalogueAttribute`](catalogue.md#tenancycatalogueattribute), or `new ApplicationCatalogue()` where none is, and the lists every module the exporting project references marks with `TenancyPermissionsAttribute` ([`TenancyRowAccessContribution`](#tenancyrowaccesscontributionienumerableienumerablepermission-applicationcatalogue-ireadonlycollectionstring)). A script written by hand, or a test, makes it with the catalogue built ([`TenancyRowAccessContribution`](#tenancyrowaccesscontributiontenancycatalogue-ireadonlycollectionstring)).

**Remarks**

For the context that maps Tenancy's tables, in its default schema: the functions `caller_seat`, `caller_tenant`, `system_tenant`, `units_where_i_hold_permission`, `readable_units`, `seats_in_my_units`, `roles_with_permission`, `holds_permission`, `holds_at_tenant`, `identity_tenants`, `unit_parent`, `permission_manages_access`, `permission_is_contained` and `pack_permissions`, executable by signed-in users and by system work in a tenant; the policies on Tenancy's tables, which only this contribution writes; and the triggers that keep an administrator in every active or suspended tenant, every right backed by a grant, the closure of the tree exact, and fixed what a seat, a placement and a grant are about.

Who reads the seats, and who besides the seats managers at its unit reads an invitation, are defaults (`ContributedDefault`), since what people may see of each other is the application's choice: a `[RowAccess]` rule of the application's on its seat class, or on its invitation class, that allows `Read` to signed-in users takes the place of the default, held to the calling seat's tenant. What Tenancy's own work reads stays whatever the rule says: a person's own seats, every seat of the tenant to a seat that manages seats, grants or units anywhere or roles for the whole tenant, and an invitation to the seats managers at its unit. The functions that answer about the caller and about rights run as their owner and are no read of a table, so a rule changes nothing they answer; `tenant_seats`, which runs as its caller, answers the seats the rule lets the caller read. Every other read, and every write, stays this contribution's.

The database keeps the rights: a trigger on the grants, the seats and the roles writes each seat's rights as they change, from `permission_is_live`, which says which permissions the catalogue has live, and no caller writes one. A seat reads its own rights and no other seat's; a grant is read by its seat, by the seats that manage grants, seats or units at its unit, held there or above it, and by the seats that manage roles for the whole tenant. What a seat may learn of other seats' rights, three functions answer, executable by signed-in users alone, as ids, permissions and dates: `tenant_administrators` and `seats_holding_permission_at` each right whose grant the seat may read, and `permissions_a_move_changes` to a seat that manages units at both parents of the move, of another seat's rights only those the move changes, which reach one parent and not the other. Tenancy's own system work in a tenant writes that tenant's rights again with `rewrite_tenant_rights`, after rows were written past the trigger.

A module reads Tenancy through six more functions, one for each row of the read model, which its context maps with `AddTenancyReadFunctions`: `caller_permissions`, `tenant_unit_paths`, `tenant_units`, `tenant_roles_with_permissions`, `tenant_placements` and `tenant_seats`. They are executable by signed-in users and by system work in a tenant, run as their caller, so the policies on Tenancy's tables decide their rows, and are written for Postgres to fold into the query that asks them: a module's question is planned over the tables, with their indexes. They answer under column names of their own and a status as the name of its enum member, whatever the application calls Tenancy's tables and columns and however it stores them. They answer access facts only: ids, permissions, periods, statuses, a unit's parent, and a role's pack and permissions, and never what a seat, a unit or a role is called. A start-up check compares the columns each answers with the read model's ([`TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync`](#ensurepoliciesareinplaceasynciserviceprovider-cancellationtoken)).

What has to be read before any tenant is known, three functions answer, to system work alone, which asks them in no tenant, where the policies show it no row: `role_permissions_in_use`, the permissions stored on every tenant's roles, and `seats_of_identity`, the seats a person has in every tenant, to Tenancy's own work and to no other scope's; and `tenants_to_sweep`, the ids of the active and the suspended tenants, to the work of every module, which then visits each tenant under its own scope. So nothing of Tenancy's runs as the application itself, past the policies.

Five questions take the tenant as an argument instead of reading it from the connection, for a policy that runs where the application's connection is not the one asking, on the path of a stored file or on a channel: `seat_in_tenant`, `seated_in_tenant`, `holds_permission_in_tenant`, `units_where_i_hold_permission_in_tenant` and `roles_with_permission_in_tenant`, executable by signed-in users alone. Each answers for the seat the verified identity has in that tenant, while that seat and the tenant are active, and nothing to anyone else: naming a tenant says nothing of it to someone who has no seat there.

For every context, the tables of the entities it keeps to a tenant with `ScopeToTenant` get row level security, restrictive policies that keep a signed-in user to the calling seat's tenant and system work to its own, and that let anonymous callers reach nothing, and a policy that lets system work of any scope read and write its tenant. What a signed-in user may do inside the tenant is the module's rules' to say; without one, nothing. A rule for a role of the application's own, named by its database name rather than as a signed-in user, system work or an anonymous caller, is not kept to a tenant: a contribution does not see the rules, so that rule must name the tenant itself.

A token role the host mapped to a database role of its own, `RowAccessRoleNames.TokenRoles` of the export, holds no seat: Tenancy's tables and every table kept to a tenant are closed to it by a restrictive policy, as they are to anonymous callers, so a module's rule for `RowAccessRoles.Token(...)` on such a table lets nothing through. A token role mapped to the role of a signed-in user is that role, kept to the calling seat's tenant like any other user.

The token roles of the application's operators, given to the constructor, are the exception: staff who look across tenants and hold no seat. The database role of each reads every row of Tenancy's tables, of the tables of the application's entities on them and of Tenancy's access history, in every tenant, and writes none of them. On a module's table kept to a tenant its restrictive policies let it read and nothing else, so it reads there what a rule of the module for `RowAccessRoles.Token(...)` admits, and never writes, whatever a rule says. What an operator asks for is carried out by system work that names it. An operator's token role is mapped to a database role of its own, which no other token role shares: one that is not is refused when the policies are written, since the policies would then open every tenant to callers who are no operators.

A table whose entity keeps who changed its rows (`RecordsWhoChanged`) gets a trigger that holds callers to it: a signed-in user writes its own seat and no other as who wrote or changed the row, the application's own work never writes a seat's kind, and neither changes who wrote the row first. A role that is no caller's, the tables' owner in a migration say, is not held.

Tenancy's access history, where the context maps one with `AddTenancyEventLogTable`, is kept to this contribution like Tenancy's tables: a person adds rows about their own seat, the calling one, in the tenant the connection names, and a seat reads its tenant's rows with `tenancy.history.view` held for the whole tenant; system work reads its tenant's rows, and adds to them in Tenancy's own scope; and where the export writes privileges and the host has a role for its own bookkeeping, that role reads and removes rows, which the table's guard lets go only once they are old enough.

Invitations, where the context maps them with `AddTenancyInvitations`, are kept to this contribution as well. An invitation is read by the seats that manage seats at its unit; added by a seat that could add the seat and make the grant itself, open and as its own; and changed by a seat to cancelled and nothing else, while a trigger keeps what it offers as it was issued, for every role. The digest of an invitation's token is in a table of its own that no caller's role reads, an operator's included: whoever issues an invitation adds its row in the same transaction, and `invitation_of_digest` answers which invitation a digest is for, as ids, to Tenancy's own system work in no tenant, which is how accepting one finds the tenant its token names.

A signed-in user is found as the active seat of the verified identity in the tenant the connection names, in an active tenant; the tenant is the application's word and cannot reach a seat the identity does not have. This is a second lock behind the application, against its own queries that forget a condition, not against SQL an attacker runs on its connection, which can switch roles and set anything.

| Member | Summary |
|---|---|
| [`TenancyRowAccessContribution(IEnumerable<IEnumerable<Permission>>, ApplicationCatalogue?, IReadOnlyCollection<string>?)`](#tenancyrowaccesscontributionienumerableienumerablepermission-applicationcatalogue-ireadonlycollectionstring) | The contribution as the Supabase export makes it, in the project that runs the export, from what the application marks: the catalogue built from `application` and `modules`, as the host's registration builds it from `TenancyOptions.Catalogue` and the modules' permissions it registers. |
| [`TenancyRowAccessContribution(TenancyCatalogue, IReadOnlyCollection<string>?)`](#tenancyrowaccesscontributiontenancycatalogue-ireadonlycollectionstring) | A contribution for an application whose catalogue is `catalogue`, as a script written by hand makes it. |
| `TenancyCatalogue Catalogue { get; }` | The catalogue the functions are written from. |
| `IReadOnlyList<string> OperatorTokenRoles { get; }` | The token roles of the application's operators, each once, in ordinal order. |
| `string Owner { get; }` | The owner of the functions it defines, lower case letters, digits and `-`: their logical names are `owner/name`, which rules and other contributions ask them by. |
| [`Contribute(DbContext, RowAccessExport)`](#contributedbcontext-rowaccessexport) | What this contribution writes for `context`, or `null` when the context is none of its business. |
| [`CallerAs(DbContext, Type)`](#callerasdbcontext-type) | The function that answers who the caller is as an `id`, without parameters, or `null` where this keeps no such id in the model of `context`. |
| [`PlacesWhereCallerHolds(DbContext, Type)`](#placeswherecallerholdsdbcontext-type) | The function that answers, for a key given as text, the places of type `place` the caller's hold of it reaches, or `null` where this keeps no such place in the model of `context`. |

#### `TenancyRowAccessContribution(IEnumerable<IEnumerable<Permission>>, ApplicationCatalogue?, IReadOnlyCollection<string>?)`

```csharp
public TenancyRowAccessContribution(
    IEnumerable<IEnumerable<Permission>> modules,
    ApplicationCatalogue? application = null,
    IReadOnlyCollection<string>? operatorTokenRoles = null)
```

The contribution as the Supabase export makes it, in the project that runs the export, from what the application marks: the catalogue built from `application` and `modules`, as the host's registration builds it from `TenancyOptions.Catalogue` and the modules' permissions it registers.

**Parameters**

- `modules`: Every list of permissions a module marks with `TenancyPermissionsAttribute`, as the host registers them with `AddTenancyPermissionsOfModules()`: those of every project the exporting project references.
- `application`: The application's part of the catalogue, the member it marks with [`TenancyCatalogueAttribute`](catalogue.md#tenancycatalogueattribute); `new ApplicationCatalogue()` when it marks none, as when `TenancyOptions.Catalogue` is left unset.
- `operatorTokenRoles`: The token roles of the application's operators, the member it marks with [`TenancyOperatorsAttribute`](use-cases.md#tenancyoperatorsattribute); none when it marks none, and then nothing is written for operators.

**Exceptions**

- `ArgumentNullException`: `modules` is null.
- [`TenancyCatalogueException`](catalogue.md#tenancycatalogueexception): The catalogue does not hold together; every problem found is listed.
- `ArgumentException`: An operator token role has no name.

#### `TenancyRowAccessContribution(TenancyCatalogue, IReadOnlyCollection<string>?)`

```csharp
public TenancyRowAccessContribution(
    TenancyCatalogue catalogue,
    IReadOnlyCollection<string>? operatorTokenRoles = null)
```

A contribution for an application whose catalogue is `catalogue`, as a script written by hand makes it.

**Parameters**

- `catalogue`: The catalogue the application runs with, built as its registration builds it: which permissions are live, which of them manage access, and whether those stay contained.
- `operatorTokenRoles`: The token roles of the application's operators, as `TenancyOptions.OperatorTokenRoles` lists them: each is given policies that let its database role read every tenant and write nothing. None when left out, and then nothing is written for operators.

  ```csharp
  var tenancy = new TenancyRowAccessContribution(TenancyCatalogue.Build(ShopCatalogue.Application, TenancyPermissionsOfModules.All), ["operator"]);
  ```

**Exceptions**

- `ArgumentNullException`: `catalogue` is null.
- `ArgumentException`: An operator token role has no name.

#### `Contribute(DbContext, RowAccessExport)`

```csharp
public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
```

What this contribution writes for `context`, or `null` when the context is none of its business. It may be asked about one context several times in one export, to learn the names of its functions, what it asks and what it writes, and again whenever the export is run, so the answer is written from the model and what the contribution was made with, the same every time and cheaply.

**Parameters**

- `context`: The context whose script is being written; it is never opened.
- `export`: What the script is written with: the roles and the caller functions.

**Exceptions**

- `ArgumentNullException`: `context` or `export` is null.
- `InvalidOperationException`: The context maps some of Tenancy's tables but not all of them, stores a role's permissions in neither an array nor a JSON array, keeps a type to a tenant in a table of its own apart from the type it derives from, or maps Tenancy's access history without Tenancy's tables; or an operator's token role is mapped to the role of a signed-in user, or to a role another token role shares.

#### `CallerAs(DbContext, Type)`

```csharp
public DatabaseFunction? CallerAs(DbContext context, Type id)
```

The function that answers who the caller is as an `id`, without parameters, or `null` where this keeps no such id in the model of `context`.

**Parameters**

- `context`: The context whose functions ask it.
- `id`: The type of the id, such as the application's seat id.

**Remarks**

[`TenancyFunctions.CallerSeat`](questions.md#tenancyfunctions) for the application's seat id, where the context maps Tenancy's tables or its read model, which knows it: so a resource whose members are seats has its functions ask the caller's seat without its rules naming one.

#### `PlacesWhereCallerHolds(DbContext, Type)`

```csharp
public DatabaseFunction? PlacesWhereCallerHolds(DbContext context, Type place)
```

The function that answers, for a key given as text, the places of type `place` the caller's hold of it reaches, or `null` where this keeps no such place in the model of `context`.

**Parameters**

- `context`: The context whose functions ask it.
- `place`: The type of the place, such as the application's unit id.

**Remarks**

[`TenancyFunctions.UnitsWhereIHold`](questions.md#tenancyfunctions) for the application's unit id, where the context maps Tenancy's tables or its read model: so a resource that hangs at a unit is reached from above through Tenancy's grants without its rules naming a function.

### `TenancyRowLevelSecurity`

```csharp
public static class TenancyRowLevelSecurity
```

What Tenancy puts on a Postgres connection for its policies to read.

| Member | Summary |
|---|---|
| [`TenantSetting`](#tenantsetting) | The setting the tenant of the current Tenancy caller travels in, next to the caller's role and claims and in the same statement: the value of its id, written with the invariant culture (a `Guid` as `D`), or `''` when the caller acts in no tenant. |

#### `TenantSetting`

```csharp
public const string TenantSetting = "tenancy.caller_tenant"
```

The setting the tenant of the current Tenancy caller travels in, next to the caller's role and claims and in the same statement: the value of its id, written with the invariant culture (a `Guid` as `D`), or `''` when the caller acts in no tenant. SQL reads it with `current_setting('tenancy.caller_tenant', true)`.

It is the application's word, as every setting is: any statement on the application's connection can set it. SQL that reads it for a signed-in user should find the seat from the verified identity and this tenant, and require that seat and tenant to be active, so a wrong tenant reaches no seat; only work the application begins on purpose, the scoped system caller's, can take it as given.

## Start-up checks

Namespace `DDDToolkit.Supporting.Tenancy.Postgres`.

### `TenancyPostgresChecks`

```csharp
public static class TenancyPostgresChecks
```

Checks a host runs at start-up, before it serves anything, that the database and the host are set up the way Tenancy's policies rely on. Each throws `InvalidOperationException` naming what is wrong and how to put it right, so a host that would run with a hole in its second lock does not start.

[`TenancyPostgresServiceCollectionExtensions.AddTenancyPostgres`](use-cases.md#addtenancypostgresiservicecollection) registers each of them as a start-up check, by the names below, which a host runs with `services.RunStartupChecks()`. The methods stay for a host that runs them by hand:

```csharp
var app = builder.Build();
TenancyPostgresChecks.EnsureExplicitCallers(app.Services);
TenancyPostgresChecks.EnsureSeatedTokenRolesAreSignedInUsers(app.Services);
await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(app.Services, cancellationToken);
await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(app.Services, cancellationToken);
await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(app.Services, cancellationToken);
```

The ones that ask the database do so on a context of Tenancy's own, made in a scope of their own, as the application itself (`Caller.System`, begun around each query and outside any transaction the caller holds), and read the catalog of Postgres alone: no table of Tenancy's, and no tenant's rows. The one that proves the reads across tenants also asks one of their functions, as Tenancy's own scoped system work in no tenant, the way those reads run.

| Member | Summary |
|---|---|
| `ExplicitCallersCheck = "tenancy.explicit-callers"` | The start-up check that the host still requires explicit callers ([`EnsureExplicitCallers`](#ensureexplicitcallersiserviceprovider)). |
| `SeatedTokenRolesCheck = "tenancy.seated-token-roles"` | The start-up check that every token role Tenancy seats reaches the database as a signed-in user ([`EnsureSeatedTokenRolesAreSignedInUsers`](#ensureseatedtokenrolesaresignedinusersiserviceprovider)). |
| `SystemInRoleConfinedCheck = "tenancy.system-in-role-confined"` | The start-up check that the scoped system role cannot leave the tenant its work is in ([`EnsureSystemInRoleIsConfinedAsync`](#ensuresysteminroleisconfinedasynciserviceprovider-cancellationtoken)). |
| `SystemReadsAcrossTenantsCheck = "tenancy.system-reads-across-tenants"` | The start-up check that Tenancy's reads across tenants answer, with nothing running past the policies ([`EnsureSystemReadsAcrossTenantsAsync`](#ensuresystemreadsacrosstenantsasynciserviceprovider-cancellationtoken)). |
| `PoliciesInPlaceCheck = "tenancy.policies-in-place"` | The start-up check that Tenancy's policies, functions, triggers and the unique index on a tenant's root are in the database, written from the catalogue the application runs with ([`EnsurePoliciesAreInPlaceAsync`](#ensurepoliciesareinplaceasynciserviceprovider-cancellationtoken)). |
| `DataApiLoginRole = "authenticator"` | The role PostgREST, and so Supabase's Data API, logs in as before it switches to a caller's role. |
| [`EnsureExplicitCallers(IServiceProvider)`](#ensureexplicitcallersiserviceprovider) | Proves the host still requires explicit callers, as [`TenancyPostgresServiceCollectionExtensions.AddTenancyPostgres`](use-cases.md#addtenancypostgresiservicecollection) registered: the `CallerOptions` the services resolve, and the row level security interceptor the contexts use, which read them when it was built. |
| [`EnsureSeatedTokenRolesAreSignedInUsers(IServiceProvider)`](#ensureseatedtokenrolesaresignedinusersiserviceprovider) | Proves every token role Tenancy seats ([`TenantSelectionOptions.SeatedTokenRoles`](callers.md#tenantselectionoptions)) reaches the database as a signed-in user: the role `authenticated`, the user's role by its own name, or a token role mapped to the user's role (`PostgresRowLevelSecurityOptions.TokenRoles`). |
| [`EnsureSystemReadsAcrossTenantsAsync(IServiceProvider, CancellationToken)`](#ensuresystemreadsacrosstenantsasynciserviceprovider-cancellationtoken) | Proves Tenancy's reads across tenants answer, with nothing running past the policies: the few reads made before any tenant is known, the permissions stored on every tenant's roles among them ([`TenancySystemReads`](entity-framework.md#tenancysystemreads)). |
| [`EnsureSystemInRoleIsConfinedAsync(IServiceProvider, CancellationToken)`](#ensuresysteminroleisconfinedasynciserviceprovider-cancellationtoken) | Proves the scoped system role, `PostgresRowLevelSecurityOptions.SystemInRole`, cannot escape the tenant its policies keep it in: it is not a superuser, has no `BYPASSRLS`, cannot log in and has the privileges of no role that owns a table; neither the user's nor the anonymous caller's role has its privileges, nor does the role of any mapped token role (`PostgresRowLevelSecurityOptions.TokenRoles`), and the Data API's login role ([`DataApiLoginRole`](#tenancypostgreschecks)), where there is one, is not a member of it; no mapped token role has the privileges of the user's role, which would give the holder of such a token, who has no seat, every policy and every function written for signed-in users; no mapped token role may execute a function in Tenancy's schema either, by a grant of its own or with the privileges of another role it was given; and neither the anonymous caller's role nor every role at once, `PUBLIC`, may execute any of the functions in Tenancy's schema that run as their owner, the ones that answer about other seats' rights among them. |
| [`EnsurePoliciesAreInPlaceAsync(IServiceProvider, CancellationToken)`](#ensurepoliciesareinplaceasynciserviceprovider-cancellationtoken) | Proves Tenancy's policies are in the database the application talks to, and that the application leaves to the database what they take from it: every one of Tenancy's tables has row level security on, and so has its access history where the context maps one; the units' table has the unique index that keeps a tenant's organization to a single root; the functions written from the catalogue, which say which permissions manage access, which of them a seat hands on only where it holds them, which permissions a copy of each pack holds and which permissions are live, answer as the catalogue the application runs with does, so a database written with containment on for an application that turned it off, or the other way round, is refused and said to be so; the store leaves the rights to the database ([`TenancyStoreOptions.DatabaseKeepsRights`](entity-framework.md#tenancystoreoptions)), and the trigger that writes them is on the grants, the seats and the roles; the functions the store asks about other seats' rights, the ones system work reads across tenants through and the ones that take the tenant as an argument are there, running as their owner; and the functions a module reads Tenancy through are there, running as their caller and written for Postgres to fold into the module's queries, each answering exactly the columns of its row of the read model, in order. |

#### `EnsureExplicitCallers(IServiceProvider)`

```csharp
public static void EnsureExplicitCallers(IServiceProvider services)
```

Proves the host still requires explicit callers, as [`TenancyPostgresServiceCollectionExtensions.AddTenancyPostgres`](use-cases.md#addtenancypostgresiservicecollection) registered: the `CallerOptions` the services resolve, and the row level security interceptor the contexts use, which read them when it was built. A later registration of the options, or an interceptor made without them, would let work that said nothing about who it runs as run as the application, past every policy.

**Parameters**

- `services`: The application's services.

**Exceptions**

- `ArgumentNullException`: `services` is null.
- `InvalidOperationException`: Either one does not require explicit callers, or row level security is not registered.

#### `EnsureSeatedTokenRolesAreSignedInUsers(IServiceProvider)`

```csharp
public static void EnsureSeatedTokenRolesAreSignedInUsers(IServiceProvider services)
```

Proves every token role Tenancy seats ([`TenantSelectionOptions.SeatedTokenRoles`](callers.md#tenantselectionoptions)) reaches the database as a signed-in user: the role `authenticated`, the user's role by its own name, or a token role mapped to the user's role (`PostgresRowLevelSecurityOptions.TokenRoles`). Tenancy's policies are written for that one database role. A seated token role that runs as another, a mapped role of its own or the anonymous caller's, would be a seat in C# and nobody to the database: every read empty and every write refused, with nothing that says why.

**Parameters**

- `services`: The application's services.

**Exceptions**

- `ArgumentNullException`: `services` is null.
- `InvalidOperationException`: A seated token role does not run as the user's role, named with what puts it right; or Tenancy or row level security is not registered.

#### `EnsureSystemReadsAcrossTenantsAsync(IServiceProvider, CancellationToken)`

```csharp
public static Task EnsureSystemReadsAcrossTenantsAsync(
    IServiceProvider services,
    CancellationToken cancellationToken)
```

Proves Tenancy's reads across tenants answer, with nothing running past the policies: the few reads made before any tenant is known, the permissions stored on every tenant's roles among them ([`TenancySystemReads`](entity-framework.md#tenancysystemreads)). They run as the scoped system role in no tenant, where the policies show it no row, and ask three functions of the database, which run as their owner: [`TenancyFunctionNames.RolePermissionsInUse`](entity-framework.md#tenancyfunctionnames), [`TenancyFunctionNames.TenantsToSweep`](entity-framework.md#tenancyfunctionnames) and [`TenancyFunctionNames.SeatsOfIdentity`](entity-framework.md#tenancyfunctionnames). This proves that

- each is there, runs as its owner, and with an empty search path;
- its owner reads the table it answers from past the policies: a superuser, a role with `BYPASSRLS`, or the table's owner on a table that does not force row level security on its owner;
- the scoped system role may execute it, and neither the user's role, the anonymous caller's nor every role at once, `PUBLIC`, may;
- asked the way Tenancy asks, the permissions in use are answered, on a connection that says who is calling; and asked in another scope, none is.

The last is one of those reads itself: start-up only, on a context of its own.

**Parameters**

- `services`: The application's services, with the context that maps Tenancy's tables.
- `cancellationToken`: Cancels the check.

**Exceptions**

- `ArgumentNullException`: `services` is null.
- `InvalidOperationException`: One of those does not hold, named with what puts it right; there is no scoped system role, or row level security is not registered; the store does not leave the rights to the database, so the reads would not go through the functions at all; or no context maps Tenancy's tables.

#### `EnsureSystemInRoleIsConfinedAsync(IServiceProvider, CancellationToken)`

```csharp
public static Task EnsureSystemInRoleIsConfinedAsync(
    IServiceProvider services,
    CancellationToken cancellationToken)
```

Proves the scoped system role, `PostgresRowLevelSecurityOptions.SystemInRole`, cannot escape the tenant its policies keep it in: it is not a superuser, has no `BYPASSRLS`, cannot log in and has the privileges of no role that owns a table; neither the user's nor the anonymous caller's role has its privileges, nor does the role of any mapped token role (`PostgresRowLevelSecurityOptions.TokenRoles`), and the Data API's login role ([`DataApiLoginRole`](#tenancypostgreschecks)), where there is one, is not a member of it; no mapped token role has the privileges of the user's role, which would give the holder of such a token, who has no seat, every policy and every function written for signed-in users; no mapped token role may execute a function in Tenancy's schema either, by a grant of its own or with the privileges of another role it was given; and neither the anonymous caller's role nor every role at once, `PUBLIC`, may execute any of the functions in Tenancy's schema that run as their owner, the ones that answer about other seats' rights among them.

The access files give Tenancy's functions to signed-in users and to the scoped system role, and take every other grant on the ones they write back. A grant made by hand since, `GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA` next to the tables' privileges say, stays until the next file is applied, and on the trigger functions for good. With it the holder of a token that seats nobody could ask what the functions answer a seat. Only a grant to a role counts here. What every role may execute, through `PUBLIC`, is refused for the functions that run as their owner, whoever the role is, and a function that runs as its caller reads nothing its caller could not. Every function in the schema counts, whoever wrote it: a function of the host's own that a mapped role is to run belongs in another schema.

**Parameters**

- `services`: The application's services, with the context that maps Tenancy's tables.
- `cancellationToken`: Cancels the check.

**Exceptions**

- `ArgumentNullException`: `services` is null.
- `InvalidOperationException`: One of those does not hold, named with the statement that puts it right; the role does not exist; row level security is not registered; or no context maps Tenancy's tables.

#### `EnsurePoliciesAreInPlaceAsync(IServiceProvider, CancellationToken)`

```csharp
public static Task EnsurePoliciesAreInPlaceAsync(
    IServiceProvider services,
    CancellationToken cancellationToken)
```

Proves Tenancy's policies are in the database the application talks to, and that the application leaves to the database what they take from it: every one of Tenancy's tables has row level security on, and so has its access history where the context maps one; the units' table has the unique index that keeps a tenant's organization to a single root; the functions written from the catalogue, which say which permissions manage access, which of them a seat hands on only where it holds them, which permissions a copy of each pack holds and which permissions are live, answer as the catalogue the application runs with does, so a database written with containment on for an application that turned it off, or the other way round, is refused and said to be so; the store leaves the rights to the database ([`TenancyStoreOptions.DatabaseKeepsRights`](entity-framework.md#tenancystoreoptions)), and the trigger that writes them is on the grants, the seats and the roles; the functions the store asks about other seats' rights, the ones system work reads across tenants through and the ones that take the tenant as an argument are there, running as their owner; and the functions a module reads Tenancy through are there, running as their caller and written for Postgres to fold into the module's queries, each answering exactly the columns of its row of the read model, in order. A contribution the export was never told to use, or an access file written from another catalogue or another version of this package and not yet applied, leaves the second lock open or out of date, and nothing else would say so.

The index is the one `AddTenancy` maps when it is given the context's `Database`, and on Postgres it is required. The policies keep a seat from leaving a unit without a parent, and hold neither Tenancy's own system work nor the tables' owner, while a permission held at any unit without a parent is held for the whole tenant: a second root, however it got there, would make whoever holds a permission at it a holder for the whole tenant. It is found by what it does and not by its name: unique, on the units' tenant alone, over the units without a parent, and valid, which an index that `CREATE INDEX CONCURRENTLY` could not finish is not.

Where the application has operators (`TenancyOptions.OperatorTokenRoles`), it also proves they read: each of their token roles is mapped to a database role of its own (`PostgresRowLevelSecurityOptions.TokenRoles`), and that role has the policy that lets it read the tenants. The options name the operators for the application and the contribution names them for the database; when only the first does, an operator is answered an empty directory, in silence.

**Parameters**

- `services`: The application's services, with the context that maps Tenancy's tables and the catalogue.
- `cancellationToken`: Cancels the check.

**Exceptions**

- `ArgumentNullException`: `services` is null.
- `InvalidOperationException`: A table of Tenancy's has no row level security, the units' table has no unique index on a tenant's root, named with how to get it, one of the functions is missing, was written from another catalogue or does not run as it should, a function a module reads Tenancy through answers other columns than the read model has, the trigger that writes the rights is missing from a table, the store does not leave the rights to the database, an operator's token role is mapped to no database role of its own or its role has no policy to read the tenants with, Tenancy is not registered, or no context maps Tenancy's tables.
