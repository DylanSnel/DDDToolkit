---
title: "Membership API: Postgres"
sidebar_label: Postgres
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Membership API: Postgres

The public API of `DDDToolkit.Supporting.Membership.Postgres`, by folder: its registration, the row access contribution the Supabase export writes for each kind of resource, and the start-up check that the database has the functions the rules say.

## Registration

Namespace `DDDToolkit.Supporting.Membership.Postgres`.

### `MembershipPostgresServiceCollectionExtensions`

```csharp
public static class MembershipPostgresServiceCollectionExtensions
```

Registers what Membership needs on Postgres with row level security.

| Member | Summary |
|---|---|
| [`AddMembershipPostgres(IServiceCollection)`](#addmembershippostgresiservicecollection) | Registers the package's start-up check, [`MembershipPostgresChecks.FunctionsInPlaceCheck`](#membershippostgreschecks), which a host runs with `services.RunStartupChecks()`: for every kind of resource with members the application registered, the database has the functions and the lock its rules write, as [`MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync`](#ensurefunctionsareinplaceasynciserviceprovider-cancellationtoken) says. |

#### `AddMembershipPostgres(IServiceCollection)`

```csharp
public static IServiceCollection AddMembershipPostgres(this IServiceCollection services)
```

Registers the package's start-up check, [`MembershipPostgresChecks.FunctionsInPlaceCheck`](#membershippostgreschecks), which a host runs with `services.RunStartupChecks()`: for every kind of resource with members the application registered, the database has the functions and the lock its rules write, as [`MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync`](#ensurefunctionsareinplaceasynciserviceprovider-cancellationtoken) says. Call it where the resources are registered, next to the registration generated for each:

```csharp
services.AddDocumentMembership<FilingContext>(DocumentMembership.Rules);
services.AddMembershipPostgres();
```

What the database holds comes with the access files the export writes, which makes [`MembershipRowAccessContribution<TMember>`](#membershiprowaccesscontributiontmember) from the rules the application marks, and nothing at run time depends on this call but the check. That is also why nothing else brings the check: a host that leaves this call out runs every other start-up check and not this one, without a word. Calling it more than once is harmless.

**Parameters**

- `services`: The application's services.

**Exceptions**

- `ArgumentNullException`: `services` is null.

## Row access contribution

Namespace `DDDToolkit.Supporting.Membership.Postgres`.

### `MembershipRowAccessContribution<TMember>`

```csharp
public class MembershipRowAccessContribution<TMember> : IRowAccessContribution
    where TMember : class
```

The membership of one kind of resource in the database: the five set functions that answer, for the caller of the connection, the questions the access questions answer in C#, written from the resource's rules and from the model of the context that maps it; and, where the rules name the permissions that change the members and the owner, the lock that holds a caller that reaches the database to those permissions. This package declares it with `[assembly: RowAccessContribution]`, so the Supabase export of every application that references the package writes it, once for each kind of resource whose rules the application marks, closed over the member class the mark names:

```csharp
[MembershipRules<DocumentShare>]
public static MembershipRules Rules { get; } = MembershipRules.For<Document>(Roles.Fixed([...]));
```

The rules are the ones the resource is registered with: the functions say what the rules say, and a start-up check holds the database to it ([`MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync`](#ensurefunctionsareinplaceasynciserviceprovider-cancellationtoken)). The export makes the class before the application starts, so the rules are a declaration it can reach without the application's services, and the mark is how it finds them ([`MembershipRulesAttribute<TMember>`](rules.md#membershiprulesattributetmember)). A script written by hand, or a test, makes it with `new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules)`.

**Type parameters**

- `TMember`: The application's member class of the resource.

**Remarks**

For the context that maps the resource with `HasMembers`, in its default schema, under the names the rules give them ([`MembershipFunctions`](rules.md#membershipfunctions)):

- `as member`: the ids of the resources the caller is a member of now.
- `as member with (permission)`: those where the caller is a member now and holds a role now that gives the permission, as the rules say which role gives which permission and which permissions a member's role can give at all.
- `seen`: the resources the caller sees: those it is a member of now; those it owns, by the owner column, from the moment a resource's row names it, so a rule that reads a resource through this function lets its owner write the member rows of a resource it has just opened; and, where the rules name a permission for seeing and let a resource be reached from above, those that sit where the caller holds that permission.
- `held on (permission)`: the resources the caller holds the permission on, however the rules let it be held: through a role, by being a member for the one permission that gives, by owning the resource, whose owner holds every permission of it, or from above.

Each runs as its owner with an empty search path, so it reads the member rows without the policies that ask it, and is executable by the database roles the resource's row access rules are written for, and by nobody else: the user's, a token role's, the application's own, never the anonymous caller's, who is nobody's member; and the role scoped system work runs as, where the rules name a scope whose work is the application's own. So a signed-in user asks them through the rules for users, an operator's token role whose own rule asks them is granted them with that rule, and an import that runs as system work in the module's scope asks them as that role. Each records the roles in a line of its body, which the start-up check holds the database's grants to. Who the caller is as a member is what the rules say: what follows from the member id, the caller's seat for the seat id of a package that keeps an organization's seats or the caller's user id for an id over a Guid, a claim of its token, or what a function of the application's answers. Whether a period applies is asked of the database's clock.

What the rules have somebody else answer is asked of functions by their logical names, `owner/name`: who the caller is as a member, and where the caller holds a permission above, of the function a contribution of the export answers for the type of the member or of the place (`ICurrentUserFunctions`), an organization package's for its seat and its unit, or of the one the rules name ([`MemberSource.Resolved`](rules.md#resolveddatabasefunction), [`MembershipRules.ReachFromAbove`](rules.md#membershiprules)); which of the roles kept elsewhere give a permission, of the one [`Roles.External`](rules.md#externaldatabasefunction-otherpermissions) names. The script finds a function wherever it is defined: in this context's script or another's it is written with. A name nothing defines there is refused when the script is written, and rules that leave a function they need to nobody are refused here. What a role kept elsewhere gives a member is cut in these functions as it is in C#: only a permission the rules let a role give.

Roles a customer makes ([`Roles.Custom`](rules.md#customireadonlylistdeclaredrole-otherpermissions-string)) are read from the table of the application's role class, which the context maps with `IsCustomRole`: a role in use that holds the permission, cut the same way. No function is asked for them, and the functions read those rows as they read the member rows, as their owner: which rows a caller may read of that table is the application's own rule, and the functions do not lean on it. The owner holds no role, so no role is kept in use for it.

The application's own work in a scope the rules name ([`MembershipRules.SystemScopes`](rules.md#membershiprules)) is answered every resource by `seen` and `held on`, as it is in C#: its claims say the role scoped work runs as, and that scope, which may ask them wherever the rules name a scope. Work in any other scope is nobody's member, and is answered nothing.

It keeps no table to itself, and allows nothing. What a caller may read and change of a resource is the application's own row access rules to say, and they ask `seen` and `held on` by the resource's id, with no function's name in it: `user.Sees<DocumentId>()` and `user.Holds<DocumentId>(permission)`. The two functions say which set they answer for the resource (`ResourceAccessAnswer`), and the script writes such a rule with them, whatever the rules call them. The member tables follow the resource's rules, as every table of an aggregate's entities does. The functions' owner is the rules' name, a dot in it written as a dash, so their logical names are `documents/documents_i_see` and so on, which SQL of the application's own may ask as well.

That the member tables follow the resource's rules means whoever those rules let change a resource writes its member rows and its owner column, by any statement that reaches the database: a caller that only edits a resource could give itself a role on it, or make itself its owner. So the permissions the application's commands require for that are marked `ManagesMembers` and `NamesOwner`, and the lock is written from them, for the database roles that may ask the functions:

- [`MembershipRules.ManagesMembersPermission`](rules.md#managesmemberspermission): a restrictive policy on both member tables for adding, changing and removing a row. A row is written only on a resource the caller holds that permission on, or the one that names the owner, where there is one: naming an owner writes member rows too. It narrows what the application's rules allow and allows nothing itself. Where a customer makes the roles, a second one on the table of the roles a member holds, for adding and changing a row: the role is one the caller sees in the role table, asked as the caller, so the application's own rules on that table decide it. The functions find a role by its id, whoever's it is; this keeps a role the caller does not see, another customer's, off a member list it writes.
- [`MembershipRules.NamesOwnerPermission`](rules.md#namesownerpermission): a trigger on the resource's table that refuses, as a policy does, a statement that would leave a row with another owner, unless the caller held that permission on the resource before.

The lock asks what the caller holds when each statement runs, as every policy does, and a change of a resource's members is several statements. Two things follow. A resource is opened with its owner on the list, and those first rows pass the lock like any other: an owner holds the permissions marked `Owner` ([`MembershipRules.OwnerPermissions`](rules.md#membershiprules)), so the one that manages the members is one of them wherever a caller opens a resource as itself. And an owner that hands its own resource on, holding the permissions by owning it alone, holds them no longer once the owner column has changed: the rows written after that are not its to write, under the lock or under an application's own rule that asks what the caller holds. Somebody who holds the permission through a role, or from above, hands a resource on as itself; an owner's own hand-over is saved as the application's own work, once the command's check let it through.

The lock holds the database roles that may ask the functions, and no other: a role no rule of the resource is for holds no permission, and nothing is written for it. The role scoped system work runs as is held as well, so work in a scope the rules do not let through writes no member row; work in one they do holds every permission, and passes. The role that owns the tables is not held, and where no role may ask the functions no lock is written.

The lock is for a resource the application gave row access rules: on a resource without any, the policies would be the only ones on the member tables, and nobody would read a row of them. Rules with neither permission leave the tables to the resource's own rules, and the start-up check says so ([`MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync`](#ensurefunctionsareinplaceasynciserviceprovider-cancellationtoken)).

A context that does not map the member class is none of its business, so an application with several kinds of resource marks the rules of each, the export makes one contribution for each, and each writes for the context of its own resource.

| Member | Summary |
|---|---|
| [`MembershipRowAccessContribution(MembershipRules)`](#membershiprowaccesscontributionmembershiprules) | The functions of the resource whose members are of `TMember`, written from `rules`. |
| `MembershipRules Rules { get; }` | The rules the functions are written from. |
| [`Owner`](#owner) | The owner of the functions it defines, lower case letters, digits and `-`: their logical names are `owner/name`, which rules and other contributions ask them by. |
| [`Contribute(DbContext, RowAccessExport)`](#contributedbcontext-rowaccessexport) | What this contribution writes for `context`, or `null` when the context is none of its business. |

#### `MembershipRowAccessContribution(MembershipRules)`

```csharp
public MembershipRowAccessContribution(MembershipRules rules)
```

The functions of the resource whose members are of `TMember`, written from `rules`.

**Parameters**

- `rules`: The resource's rules: the ones it is registered with, which the export takes from the member the application marks `[MembershipRules<TMember>]`.

**Exceptions**

- `ArgumentNullException`: `rules` is null.

#### `Owner`

```csharp
public string Owner { get; }
```

The owner of the functions it defines, lower case letters, digits and `-`: their logical names are `owner/name`, which rules and other contributions ask them by.

**Remarks**

The rules' name, a dot in it written as a dash: what a question an application declares for these functions names as its owner.

#### `Contribute(DbContext, RowAccessExport)`

```csharp
public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
```

What this contribution writes for `context`, or `null` when the context is none of its business. It may be asked about one context several times in one export, to learn the names of its functions, what it asks and what it writes, and again whenever the export is run, so the answer is written from the model and what the contribution was made with, the same every time and cheaply.

**Parameters**

- `context`: The context whose script is being written; it is never opened.
- `export`: What the script is written with: the roles and the current user functions.

**Exceptions**

- `ArgumentNullException`: `context` or `export` is null.
- `InvalidOperationException`: Who the caller is as a member, or where it holds a permission above, is answered by nobody: the rules name no function, and no contribution answers one for the type of the member or of the place; the rules let the resource be reached from above, and it marks no place it hangs at; the roles are fixed, and the members hold roles that are not [`NamedRole`](resource-and-members.md#namedrole); a customer makes the roles, and the model maps no role class for the resource, or one a member does not hold by its id; or the model stores a member, a moment of a period or a role's permissions as something the functions cannot compare; or a permission names the owner and a role the lock cannot be written for may ask the functions; or a trigger's name would be longer than Postgres keeps one.

## Start-up checks

Namespace `DDDToolkit.Supporting.Membership.Postgres`.

### `MembershipPostgresChecks`

```csharp
public static class MembershipPostgresChecks
```

A check a host runs at start-up, before it serves anything, that the database has the functions its row access rules ask about the members of its resources, as the rules it runs with say them. It throws `InvalidOperationException` naming what is wrong and how to put it right, so a host whose second lock would answer from other rules than its first does not start.

[`MembershipPostgresServiceCollectionExtensions.AddMembershipPostgres`](#addmembershippostgresiservicecollection) registers it as a start-up check, [`FunctionsInPlaceCheck`](#membershippostgreschecks), which a host runs with `services.RunStartupChecks()`. The method stays for a host that runs it by hand:

```csharp
var app = builder.Build();
await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(app.Services, cancellationToken);
```

| Member | Summary |
|---|---|
| `FunctionsInPlaceCheck = "membership.functions-in-place"` | The start-up check that the database has the functions and the lock of every registered resource's membership, written from the rules the resource is registered with ([`EnsureFunctionsAreInPlaceAsync`](#ensurefunctionsareinplaceasynciserviceprovider-cancellationtoken)). It asks the database, so it runs with the checks of the database, once the migrations are found applied. |
| [`EnsureFunctionsAreInPlaceAsync(IServiceProvider, CancellationToken)`](#ensurefunctionsareinplaceasynciserviceprovider-cancellationtoken) | Proves that, for every kind of resource with members the application registered, the database has the five functions of its membership in the schema of the context that maps it, under the names its name gives them, each running as its owner with an empty search path and answering a set, each written from the rules the resource is registered with by this version of the package, and each executable by the database roles the export granted it to and by no other. |

#### `EnsureFunctionsAreInPlaceAsync(IServiceProvider, CancellationToken)`

```csharp
public static Task<IReadOnlyList<string>> EnsureFunctionsAreInPlaceAsync(
    IServiceProvider services,
    CancellationToken cancellationToken)
```

Proves that, for every kind of resource with members the application registered, the database has the five functions of its membership in the schema of the context that maps it, under the names its name gives them, each running as its owner with an empty search path and answering a set, each written from the rules the resource is registered with by this version of the package, and each executable by the database roles the export granted it to and by no other.

The functions come with the access files the export writes, which makes [`MembershipRowAccessContribution<TMember>`](#membershiprowaccesscontributiontmember) for every resource whose rules the application marks. Rules it does not mark get no functions, and functions written from other rules, a role that gives another permission say, would have the database answer otherwise than the application's own check, and nothing else would say so. The rules a function was written from are read from its body, which says them in its first line. That line is what is compared: a body changed by hand below it is not found.

Who may execute a function follows from the row access rules of the resource, which the host does not see: the export grants each function to every role a rule of the resource is for, never the anonymous caller's, and to the role scoped system work runs as where the rules name a scope, and records them in a line of the function's body. The grants are asked of the database and held to that line: a grant taken back by hand would fail every policy that asks the function, and one given to `PUBLIC`, or to one more role, would let a role no rule names ask a function that runs as its owner.

Where a permission manages the members, or names the owner, the lock written from it is held to as well: a restrictive policy on both member tables for each of those roles, where a customer makes the roles the one that holds a role given to one the caller sees, and the trigger on the owner column. Where a customer makes the roles, their table is one a caller must not write as it likes: a role's row says what everybody who holds it may do. A role table without row security that one of those roles may write is refused. Which rows a caller reads and changes there stays the application's own rules to say.

It asks the database on the context of each resource, made in a scope of its own, as the application itself (`CurrentUserSnapshot.System`, begun around the question and outside any transaction the caller holds), and reads the catalog of Postgres alone: no table of the application's, and nobody's rows.

**Parameters**

- `services`: The application's services, with the context of each resource registered.
- `cancellationToken`: Cancels the check.

**Returns**: What the check remarks on without refusing, one line for each resource it concerns, empty when there is nothing: a resource whose rules name no permission that changes its members, or none that changes its owner, so the database leaves those rows to whoever the application's row access rules let change the resource. Each line is logged as a warning as well, where the services have a logger.

**Exceptions**

- `ArgumentNullException`: `services` is null.
- `InvalidOperationException`: No resource is registered; a resource's context is not registered, does not map its members, marks no place a resource hangs at that its rules reach from above, or maps no role class for a resource whose roles a customer makes; a function is missing, does not run as its owner, has no empty search path, answers no set, was written from other rules, may be executed by every role or by one it was not granted to, or may not be executed by a role it was granted to; the lock a permission asks for is not in place; or the table of the roles a customer makes has no row security and may be written by a role the functions are granted to. Each finding is named, with the resource it is of.
