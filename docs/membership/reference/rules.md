---
title: "Membership API: Rules and permissions"
sidebar_label: Rules and permissions
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Membership API: Rules and permissions

The rules of one kind of resource, as one declaration: `MembershipRules.For<TResource>` and the mark that hands them to the export, where the roles come from, the permissions declared on the resource and their settings, reach from above, and who the caller is as a member. The codes a resource refuses under are with the refusals, under [`MembershipCodes`](refusals.md#membershipcodes).

## Rules

Namespace `DDDToolkit.Supporting.Membership.Access`.

### `MembershipRules`

```csharp
public sealed class MembershipRules
```

The rules of access through the members of one kind of resource, as one declaration: the resource's permissions, where the roles come from, who the caller is as a member, whether something above the resource reaches it, and whose system work holds everything on it.

```csharp
public static MembershipRules Rules { get; } = MembershipRules.For<Board>(
    Roles.Custom(starters: [new("editor", [BoardPermissions.View, BoardPermissions.Edit, BoardPermissions.Comment])]));
```

Made through the resource's type, which says its name and its codes ([`IMembershipResource`](resource-and-members.md#imembershipresource)), and from the permissions declared on it, the constants of a `[Permissions]` class marked [`OnAttribute<TResource>`](#onattributetresource): the owner holds those marked `Owner`, every member the one marked `EveryMember`, and a role those marked `InRoles`. So nothing of the resource is said twice.

They say who holds a permission: the owner by owning the resource, a member by being one and through its roles. They do not say who may add a member, give or take a role, or hand the resource to another owner. That is the application's to decide, command by command: each of its commands requires what the application chooses, usually one of the resource's permissions held on it, and whatever further rule it has it writes itself.

It is data, checked once when it is made, so everything that decides access reads the same thing: the questions asked in C# before a handler runs, the functions that answer the same questions in the database, and whatever writes those functions. Declare one per kind of resource. Nothing here is shared between two resources, so an application with documents and folders declares two, and neither touches the other.

Three things are said apart from each other, and any of the one goes with any of the others. Who a member is: what follows from the member id, the caller's seat for the seat id of a package that keeps an organization's seats and the caller's user id for one over a Guid, or a claim of its token, or an id the application resolves for the caller ([`Members`](#membershiprules)). Where the roles come from: in code, made by a customer, or kept elsewhere ([`Roles`](#membershiprules)). And whether a permission held where the resource sits, or above it, reaches the resource ([`ReachFromAbove`](#membershiprules)). So a resource beside an organization can have the organization's people as members with roles of its own, in code or made by a customer, or with the organization's roles, with reach from above or without.

| Member | Summary |
|---|---|
| `Type Resource { get; }` | The resource's class. |
| `string Name { get; }` | The resource's name: what its codes and its functions are named after. |
| `MembershipCodes Codes { get; }` | The codes the resource refuses with. |
| `MembershipFunctions Functions { get; }` | The names of the resource's set functions in the database, which follow from its name. |
| `MemberSource Members { get; }` | Who the caller is as a member. |
| `Roles Roles { get; }` | Where the roles a member holds come from, and the roles declared in code. |
| `ReachFromAbove? ReachFromAbove { get; }` | Says that a permission held where the resource hangs, or above it, reaches the resource, or `null` when a resource is reached through its members and by its owner alone. |
| `IReadOnlyList<ResourcePermission> Permissions { get; }` | The permissions on the resource, each once, as they are declared: every permission asked about on a resource of this kind. |
| `IReadOnlyList<string> OwnerPermissions { get; }` | The permissions the owner holds by owning the resource, those marked `Owner`, in the order they are declared. The owner holds no role, so this is all it holds by owning it. |
| `string? EveryMemberPermission { get; }` | The permission every member holds by being one, whatever roles it holds, or `null`: usually the one that sees the resource. Without one a resource is still seen by its members, and by its owner, and that is all membership alone gives. Where a resource is reached from above ([`ReachFromAbove`](#membershiprules)) it is the permission that sees it from there as well. |
| `RolePermissions RolePermissions { get; }` | Which permissions a member's role can give on the resource, wherever the roles come from. |
| [`MembersLockPermission`](#memberslockpermission) | The permission the application's own commands require to change the members of a resource, the one marked `LocksMembers`, or `null`. |
| [`OwnerLockPermission`](#ownerlockpermission) | The permission the application's own command requires to hand a resource to another owner, the one marked `LocksOwner`, or `null`. |
| `IReadOnlyList<string> SystemScopes { get; }` | The scopes whose work is the application's own for this resource: work that runs as `Caller.SystemIn` in one of them holds every permission on every resource of this kind, as `Caller.System` does. The module's own scope unless the rules say otherwise; work in any other scope holds nothing on a resource, since a scope is a module's own and another module's work is not above this resource's rules. |
| [`For(Roles, MemberSource?, ReachFromAbove?, IReadOnlyList<string>?)`](#forroles-membersource-reachfromabove-ireadonlyliststring) | The rules of the resource `TResource`. |
| [`MembershipGives(string)`](#membershipgivesstring) | Whether being a member gives `permission` by itself, whatever roles the member holds: so for [`EveryMemberPermission`](#membershiprules), and for nothing else. |
| [`OwnerHolds(string)`](#ownerholdsstring) | Whether a resource's owner holds `permission` on it by being its owner: so for every one of [`OwnerPermissions`](#membershiprules), and for no other permission, with no end. |
| [`MembersHold(string)`](#membersholdstring) | Whether a member can hold `permission` on the resource at all, by being a member or through a role: [`EveryMemberPermission`](#membershiprules), and every permission within [`RolePermissions`](#membershiprules). |
| [`ReachesFromAbove(string)`](#reachesfromabovestring) | Whether `permission` held where the resource sits, or above it, reaches the resource: where the rules let a resource be reached from above, for every permission but the permissions marked `FromAbove = false`. |
| [`IsOwnWork(Caller)`](#isownworkcaller) | Whether `caller` is the application's own work for this resource, which holds every permission on every resource of this kind: `Caller.System`, always, and `Caller.SystemIn` in one of [`SystemScopes`](#membershiprules). |
| [`RolesWith(string)`](#roleswithstring) | The fixed roles that give `permission` on the resource: those that list it, when a member's role can give it at all, in the order they are declared. |
| [`PermissionsOf(NamedRole)`](#permissionsofnamedrole) | The permissions `role` gives a member on the resource: the permissions it lists that a member's role can give. |
| [`Knows(NamedRole)`](#knowsnamedrole) | Whether `role` is one of the roles declared in code: a fixed role, or a starter role a customer's roles are made from. |

#### `MembersLockPermission`

```csharp
public string? MembersLockPermission { get; }
```

The permission the application's own commands require to change the members of a resource, the one marked `LocksMembers`, or `null`. It is here for a database that checks every row itself, and for nothing else: whatever writes the database's policies holds both member tables to it, for every role the functions are granted to, and a row is written only by a caller that holds it on the resource, or the permission that locks the owner. No code of the package gates a command by it.

The database asks what the caller holds when each statement runs. Mark it `Owner`, as the default is, where a caller opens a resource as itself: the owner's own first rows are then its to write.

#### `OwnerLockPermission`

```csharp
public string? OwnerLockPermission { get; }
```

The permission the application's own command requires to hand a resource to another owner, the one marked `LocksOwner`, or `null`. As [`MembersLockPermission`](#memberslockpermission), it is here for a database that checks every row itself: a change of a resource leaves its owner as it was unless the caller holds it there.

Whoever holds it by owning the resource alone holds it no longer once the owner has changed, and what a hand-over writes after that is then not its to write. A hand-over by the owner itself is saved as the application's own work; one by somebody who holds it through a role, or from above, as the caller.

#### `For(Roles, MemberSource?, ReachFromAbove?, IReadOnlyList<string>?)`

```csharp
public static MembershipRules For<TResource>(
    Roles roles,
    MemberSource? members = null,
    ReachFromAbove? reachFromAbove = null,
    IReadOnlyList<string>? systemScopes = null)
    where TResource : class, IMembershipResource
```

The rules of the resource `TResource`.

**Parameters**

- `roles`: Where the roles a member holds come from: [`Roles.Fixed`](#fixedireadonlylistdeclaredrole) in code, [`Roles.Custom`](#customireadonlylistdeclaredrole-otherpermissions) made by a customer, or [`Roles.External`](#externaldatabasefunction-otherpermissions) kept elsewhere.
- `members`: Who the caller is as a member; left out, it follows from what a member is known by ([`MemberSource.Inferred`](#inferred)): the caller's seat for the seat id of a package that keeps an organization's seats, the caller's user id for an id over a Guid.
- `reachFromAbove`: Says that a permission held where the resource hangs, or above it, reaches the resource ([`ReachFromAbove`](#reachfromabove)): `true` beside a package that keeps an organization's units, or the function of the application's own that answers where the caller holds a permission. Left out, a resource is reached through its members and by its owner alone. Said, the resource's class marks the place it hangs at `[At]`, and one permission is held by every member ([`OnAttribute.EveryMember`](#everymember)): what is held above sees a resource by that permission.
- `systemScopes`: The scopes whose system work holds every permission on every resource of this kind ([`SystemScopes`](#membershiprules)). Left out, the scope of the module that declares the resource ([`IMembershipResource.MembershipScope`](resource-and-members.md#imembershipresource)): `boards` for the module `Boards`, so work begun as `Caller.SystemIn("boards")` does what it needs on boards. Said, the scopes named and no other: name the module's own among them to keep it, and say `[]` for no system work in a scope at all.

**Type parameters**

- `TResource`: The resource: the aggregate root a member class names, which the generator makes one.

**Exceptions**

- `ArgumentNullException`: `roles` is null.
- `ArgumentException`: The rules do not hold together; the message says which entry, and what to write.

#### `MembershipGives(string)`

```csharp
public bool MembershipGives(string permission)
```

Whether being a member gives `permission` by itself, whatever roles the member holds: so for [`EveryMemberPermission`](#membershiprules), and for nothing else.

**Parameters**

- `permission`: A permission.

#### `OwnerHolds(string)`

```csharp
public bool OwnerHolds(string permission)
```

Whether a resource's owner holds `permission` on it by being its owner: so for every one of [`OwnerPermissions`](#membershiprules), and for no other permission, with no end.

**Parameters**

- `permission`: A permission.

#### `MembersHold(string)`

```csharp
public bool MembersHold(string permission)
```

Whether a member can hold `permission` on the resource at all, by being a member or through a role: [`EveryMemberPermission`](#membershiprules), and every permission within [`RolePermissions`](#membershiprules).

**Parameters**

- `permission`: A permission.

#### `ReachesFromAbove(string)`

```csharp
public bool ReachesFromAbove(string permission)
```

Whether `permission` held where the resource sits, or above it, reaches the resource: where the rules let a resource be reached from above, for every permission but the permissions marked `FromAbove = false`.

**Parameters**

- `permission`: A permission.

#### `IsOwnWork(Caller)`

```csharp
public bool IsOwnWork(Caller caller)
```

Whether `caller` is the application's own work for this resource, which holds every permission on every resource of this kind: `Caller.System`, always, and `Caller.SystemIn` in one of [`SystemScopes`](#membershiprules). Work in any other scope is not, and holds nothing.

**Parameters**

- `caller`: The caller a question is asked for.

**Exceptions**

- `ArgumentNullException`: `caller` is null.

#### `RolesWith(string)`

```csharp
public IReadOnlyList<NamedRole> RolesWith(string permission)
```

The fixed roles that give `permission` on the resource: those that list it, when a member's role can give it at all, in the order they are declared. None where the roles are rows, a customer's or kept elsewhere, since which of those give a permission is read where they are kept.

**Parameters**

- `permission`: A permission.

#### `PermissionsOf(NamedRole)`

```csharp
public IReadOnlyList<string> PermissionsOf(NamedRole role)
```

The permissions `role` gives a member on the resource: the permissions it lists that a member's role can give. None for a role the rules do not declare. For a customer's roles, the permissions the starter role of that name is made with.

**Parameters**

- `role`: A role.

#### `Knows(NamedRole)`

```csharp
public bool Knows(NamedRole role)
```

Whether `role` is one of the roles declared in code: a fixed role, or a starter role a customer's roles are made from.

**Parameters**

- `role`: A role.

### `MembershipRulesAttribute<TMember>`

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class MembershipRulesAttribute<TMember> : Attribute
    where TMember : class
```

Marks the rules of one kind of resource, the ones it is registered with, as those of the members of `TMember`, so the program that exports the database's policies writes the resource's functions from the same rules:

```csharp
public static class DocumentMembership
{
    [MembershipRules<DocumentShare>]
    public static MembershipRules Rules { get; } = MembershipRules.For<Document>(Roles.Fixed([...]));
}

// Where the resource is registered
services.AddDocumentMembership<DocumentsContext>(DocumentMembership.Rules);
```

Membership on Postgres writes, for every kind of resource, the five set functions that answer its membership questions in the database, and the lock that holds its members' rows to the permissions its rules name. The export runs before the application starts, and the rules the registration is called with are a value the build cannot read, so the application marks them: the export writes the Membership package's contribution once for every member so marked, closed over the member class the mark names. Two kinds of resource are two marks.

The member is a static property or field of type [`MembershipRules`](#membershiprules), readable from the project that runs the export, which references the project that declares it: in a library it is public, in public types, which that library's own build checks (DDD00070). One member is marked for each member class: the export reports two, and one of another type (DDD00071); an application that references Membership on Postgres and marks none is told that nothing of it is written (DDD00054).

**Type parameters**

- `TMember`: The member class of the resource, declared with `[Member<...>]`.

### `MembershipFunctions`

```csharp
public sealed record MembershipFunctions
```

The names of the five set functions a database answers a resource's membership questions with, for the caller of the connection. They are the resource's own names, because an application may have several kinds of resource with members in one schema. They follow from the resource's name ([`For`](#forstring)), so a resource that keeps its name keeps them; one that is renamed with `[Members("...")]` gets new ones. Rules do not ask them by name: they ask the last three by the resource's id, through a `[ResourceAccessContract<TKey>]`, and the export writes the policy with whatever the names are.

| Member | Summary |
|---|---|
| [`MembershipFunctions(string, string, string, string, string)`](#membershipfunctionsstring-string-string-string-string) | The primary constructor: the type's summary says what it makes. |
| `string AsMember { get; init; }` | Answers the ids of the resources the caller is a member of now. |
| `string AsMemberWith { get; init; }` | Answers, for a permission, the ids of the resources where the caller is a member now and holds a role now that gives the permission. |
| `string Seen { get; init; }` | Answers the ids of the resources the caller sees: those it is a member of now, those it owns, from the moment a resource's row names it as owner, and, where the rules say so, those reached from above. |
| `string HeldOn { get; init; }` | Answers, for a permission, the ids of the resources the caller holds the permission on. |
| `string FellowMembers { get; init; }` | Answers the ids of the members, now, of every resource the caller is a member of now, the caller among them: the people on the projects a seat works on, which a read rule on the application's seat class may ask. |
| [`For(string)`](#forstring) | The names a resource's functions have: `documents_as_member`, `documents_as_member_with`, `documents_i_see`, `documents_where_i_hold` and `documents_fellow_members` for the resource named `documents`. |

#### `MembershipFunctions(string, string, string, string, string)`

```csharp
public MembershipFunctions(
    string AsMember,
    string AsMemberWith,
    string Seen,
    string HeldOn,
    string FellowMembers)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `AsMember`: Answers the ids of the resources the caller is a member of now.
- `AsMemberWith`: Answers, for a permission, the ids of the resources where the caller is a member now and holds a role now that gives the permission.
- `Seen`: Answers the ids of the resources the caller sees: those it is a member of now, those it owns, from the moment a resource's row names it as owner, and, where the rules say so, those reached from above.
- `HeldOn`: Answers, for a permission, the ids of the resources the caller holds the permission on.
- `FellowMembers`: Answers the ids of the members, now, of every resource the caller is a member of now, the caller among them: the people on the projects a seat works on, which a read rule on the application's seat class may ask.

#### `For(string)`

```csharp
public static MembershipFunctions For(string name)
```

The names a resource's functions have: `documents_as_member`, `documents_as_member_with`, `documents_i_see`, `documents_where_i_hold` and `documents_fellow_members` for the resource named `documents`. They follow from the name alone, so they stay what they are for as long as the resource keeps its name.

**Parameters**

- `name`: The resource's name, as its rules have it. A dot or a dash in it is written as an underscore.

## Roles

Namespace `DDDToolkit.Supporting.Membership.Access`.

### `Roles`

```csharp
public sealed class Roles
```

Where the roles a member holds on a resource come from: one choice of three, which the rules are made with.

```csharp
Roles.Fixed([new("contributor", [DocumentPermissions.View, DocumentPermissions.Edit]), new("onlooker", [DocumentPermissions.View])])
Roles.Custom(starters: [new("editor", [BoardPermissions.View, BoardPermissions.Edit])])
Roles.External(OrganizationFunctions.RolesWithPermission)
```

- **Fixed**: roles in code, the same for everybody, changed with a deploy. A member holds one by its name, as a [`NamedRole`](resource-and-members.md#namedrole).
- **Custom**: rows a customer makes for itself while the application runs, of the application's role class ([`CustomRoleAttribute<TRoleId, TResource>`](resource-and-members.md#customroleattributetroleid-tresource)), with a name, permissions of the resource's and an archive. A member holds one by the class's id. The starter roles are what a customer's first rows are made from.
- **External**: roles the application keeps in a store of its own, the roles of its organization say, which it answers for through [`IMemberRoles<TResourceId, TRoleId>`](use-cases.md#imemberrolestresourceid-troleid) and, in a database that answers the questions itself, through a function. A member holds one by that store's id.

Whichever it is, what a role gives a member is held to the resource's permissions: only a permission marked `InRoles` ([`OnAttribute.InRoles`](#inroles)), and, where the roles take them, permissions of other modules ([`OtherPermissions`](#roles-1)). The owner holds no role: it holds the permissions marked `Owner` by owning the resource.

| Member | Summary |
|---|---|
| `RolesKind Kind { get; }` | Which of the three it is. |
| `IReadOnlyList<DeclaredRole> Declared { get; }` | The roles declared in code: the roles a member holds, for [`RolesKind.Fixed`](#roleskind); the starter roles a customer's first rows are made from, for [`RolesKind.Custom`](#roleskind); none for [`RolesKind.External`](#roleskind). |
| `DatabaseFunction? Function { get; }` | For [`RolesKind.External`](#roleskind), the function that answers in the database which of the application's roles give a permission, or `null` where the database does not answer the questions itself. |
| `OtherPermissions? OtherPermissions { get; }` | Which permissions of other modules a role gives on the resource besides its permissions, or `null` for none: then a role gives the resource's permissions marked `InRoles` and nothing else. |
| [`Fixed(IReadOnlyList<DeclaredRole>)`](#fixedireadonlylistdeclaredrole) | Roles in code, each with the permissions it gives on the resource, held by name: the same for every resource of the kind, and changed with a deploy. |
| [`Custom(IReadOnlyList<DeclaredRole>?, OtherPermissions?)`](#customireadonlylistdeclaredrole-otherpermissions) | Roles a customer makes for itself while the application runs: rows of the application's role class ([`CustomRoleAttribute<TRoleId, TResource>`](resource-and-members.md#customroleattributetroleid-tresource)), each with a name and the permissions it gives, which a customer renames, gives other permissions and archives, held by the class's id. |
| [`External(DatabaseFunction?, OtherPermissions?)`](#externaldatabasefunction-otherpermissions) | Roles the application keeps in a store of its own, such as the roles of its organization, held by that store's id. |

#### `Fixed(IReadOnlyList<DeclaredRole>)`

```csharp
public static Roles Fixed(IReadOnlyList<DeclaredRole> roles)
```

Roles in code, each with the permissions it gives on the resource, held by name: the same for every resource of the kind, and changed with a deploy.

**Parameters**

- `roles`: The roles, at least one, each named once, with permissions of the resource that a role can give ([`OnAttribute.InRoles`](#inroles)).

**Exceptions**

- `ArgumentException`: `roles` is empty, or a role has no name, the name of another, or a permission twice.

#### `Custom(IReadOnlyList<DeclaredRole>?, OtherPermissions?)`

```csharp
public static Roles Custom(
    IReadOnlyList<DeclaredRole>? starters = null,
    OtherPermissions? otherPermissions = null)
```

Roles a customer makes for itself while the application runs: rows of the application's role class ([`CustomRoleAttribute<TRoleId, TResource>`](resource-and-members.md#customroleattributetroleid-tresource)), each with a name and the permissions it gives, which a customer renames, gives other permissions and archives, held by the class's id.

**Parameters**

- `starters`: The roles a customer starts with: what its first rows are made from ([`StarterRoles`](use-cases.md#starterroles)), none when left out. A starter role is no role a member holds; the row made from it is.
- `otherPermissions`: Which permissions of other modules a role may hold besides the resource's permissions; none when left out.

**Exceptions**

- `ArgumentException`: A starter role has no name, the name of another, or a permission twice.

#### `External(DatabaseFunction?, OtherPermissions?)`

```csharp
public static Roles External(
    DatabaseFunction? function = null,
    OtherPermissions? otherPermissions = null)
```

Roles the application keeps in a store of its own, such as the roles of its organization, held by that store's id. In C# the application answers which roles there are and which give a permission, through ports it registers with the resource; in a database that answers the questions itself, `function` answers which roles give a permission.

**Parameters**

- `function`: The function that answers the roles that give a permission in the database, one a package ships or the application declares: the organization package's `roles_with_permission` for the roles of its organization. It takes the permission as text and answers the ids of the roles, as a member's row stores them. Left out by an application whose database does not answer the questions itself; whatever writes the resource's functions refuses rules that name none. It is asked by the resource's functions, which run as their owner, so it reads every row whatever policies its tables have: where the roles are some customer's, it keeps to the caller's by a condition of its own, never by a policy. It is never asked without a permission.
- `otherPermissions`: Which permissions of other modules a role gives on the resource besides the resource's permissions: none when left out. Roles of an organization usually hold permissions of many modules, so say it where those reach the resource.

### `RolesKind`

```csharp
public enum RolesKind
```

The kinds of [`Roles`](#roles-1).

| Member | Summary |
|---|---|
| `Fixed = 0` | Roles in code, held by name: [`Roles.Fixed`](#fixedireadonlylistdeclaredrole). |
| `Custom = 1` | Rows a customer makes: [`Roles.Custom`](#customireadonlylistdeclaredrole-otherpermissions). |
| `External = 2` | The application's own store of roles: [`Roles.External`](#externaldatabasefunction-otherpermissions). |

### `DeclaredRole`

```csharp
public sealed record DeclaredRole
```

A role declared in code: a name, and the permissions a member that holds it has on the resource. Of fixed roles ([`Roles.Fixed`](#fixedireadonlylistdeclaredrole)) a member holds it as a [`NamedRole`](resource-and-members.md#namedrole) of the same name.

```csharp
new DeclaredRole("contributor", [DocumentPermissions.View, DocumentPermissions.Edit])
```

Of a customer's roles ([`Roles.Custom`](#customireadonlylistdeclaredrole-otherpermissions)) it is a starter role instead: what a first role is made from, with this name and these permissions, and nothing a member holds.

| Member | Summary |
|---|---|
| [`DeclaredRole(string, IReadOnlyList<string>)`](#declaredrolestring-ireadonlyliststring) | The primary constructor: the type's summary says what it makes. |
| `string Name { get; init; }` | The role's name, unique among the roles of its rules: 1 to [`NamedRole.MaxLength`](resource-and-members.md#namedrole) characters, with no white space around it. It is what a member's row stores, so a name once given out stays. |
| `IReadOnlyList<string> Permissions { get; init; }` | The permissions the role gives on the resource, each once. |

#### `DeclaredRole(string, IReadOnlyList<string>)`

```csharp
public DeclaredRole(string Name, IReadOnlyList<string> Permissions)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Name`: The role's name, unique among the roles of its rules: 1 to [`NamedRole.MaxLength`](resource-and-members.md#namedrole) characters, with no white space around it. It is what a member's row stores, so a name once given out stays.
- `Permissions`: The permissions the role gives on the resource, each once.

### `RolePermissions`

```csharp
public sealed class RolePermissions
```

Which permissions a member's role can give on a resource, wherever the roles come from: the one cut the rules work out from the resource's permissions, and that the C# questions and the functions in the database both apply. A role that holds a permission outside it gives nothing with that permission on the resource.

Either the permissions listed and no other, the permissions marked `InRoles` ([`Excepts`](#rolepermissions) false), or every permission but the ones listed, where the roles take permissions of other modules ([`OtherPermissions`](#otherpermissions)).

| Member | Summary |
|---|---|
| `bool Excepts { get; }` | Whether [`Permissions`](#rolepermissions) are the permissions no role gives, rather than the only ones it does. |
| `IReadOnlyList<string> Permissions { get; }` | The permissions listed, each once: the only ones a role gives, or the ones it never gives. |
| [`Allows(string)`](#allowsstring) | Whether a member's role can give `permission`. |

#### `Allows(string)`

```csharp
public bool Allows(string permission)
```

Whether a member's role can give `permission`.

**Parameters**

- `permission`: A permission.

### `OtherPermissions`

```csharp
public sealed class OtherPermissions
```

Which permissions of other modules a role gives on a resource besides the resource's own permissions: for roles that are rows, a customer's or the application's own, which hold permissions of every module that acts on the resource. A crew's role on a project, say, gives the permission to record an inspection there, which the module of inspections declares and the module of projects never names.

```csharp
Roles.Custom(starters: [...], otherPermissions: OtherPermissions.AllBut(OrganizationPermissions.RolesManage))
```

Without it a role gives the resource's permissions marked `InRoles` and nothing else. With it, a role gives those and every other permission it holds, except the resource's permissions marked `InRoles = false`, the permissions the classes that declare the resource's permissions declare at another level, and the permissions listed here.

| Member | Summary |
|---|---|
| `static OtherPermissions All { get; }` | A role gives every permission of another module it holds. |
| `IReadOnlyList<string> Excepted { get; }` | The permissions no role gives on the resource, each once. |
| [`AllBut(params string[])`](#allbutparams-string) | A role gives every permission of another module it holds but these. |

#### `AllBut(params string[])`

```csharp
public static OtherPermissions AllBut(params string[] permissions)
```

A role gives every permission of another module it holds but these.

**Parameters**

- `permissions`: The permissions no role gives on the resource, such as the permissions that manage the organization itself.

**Exceptions**

- `ArgumentException`: One of `permissions` is blank.

## Permissions

Namespace `DDDToolkit.Supporting.Membership.Access`.

### `OnAttribute<TResource>`

```csharp
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class OnAttribute<TResource> : PermissionLevelAttribute
    where TResource : class
```

The level of a permission that is asked on one resource with members, `TResource`, and how it is held there: by the resource's owner, by every member, through a role. A constant of a `PermissionsAttribute` class carries it.

```csharp
[Permissions]
public static class BoardPermissions
{
    /// <summary>See a board and who is on it.</summary>
    [On<Board>(EveryMember = true)]
    public const string View = "boards.view";

    /// <summary>Change a board.</summary>
    [On<Board>]
    public const string Edit = "boards.edit";

    /// <summary>Say who is on a board, and in which role.</summary>
    [On<Board>(InRoles = false, LocksMembers = true, LocksOwner = true)]
    public const string ManageMembers = "boards.members.manage";
}
```

**Type parameters**

- `TResource`: The resource the permission is asked on.

**Remarks**

The package's generator collects every permission on a resource into its rules, `MembershipRules.For<Board>`, in the project that makes them, from the classes of that project and of the projects it references. So the C# check in front of a handler and the functions in the database answer from the same declaration. The defaults are what most permissions of a resource are: the owner holds it, a role can give it, nobody holds it by being a member alone, and it locks nothing.

`TResource` is the aggregate root a member class names, `[Member<..., Board>]`; another class is DDD00093. Of the permissions on one resource, one at most is held by every member, one at most locks the members, and one at most locks the owner (DDD00093).

| Member | Summary |
|---|---|
| [`Owner`](#owner) | Whether the resource's owner holds the permission by owning it: the member its `[Owner]` property names, with no role, from the moment the resource names it and for as long as it does. |
| [`EveryMember`](#everymember) | Whether every member holds the permission by being a member, whatever roles it holds, or none. |
| [`InRoles`](#inroles) | Whether a role can give the permission to a member. |
| [`LocksMembers`](#locksmembers) | Whether this is the permission the application's commands require to add or remove a member and to give or take a role, so a database that checks every row itself holds the member rows to it. |
| [`LocksOwner`](#locksowner) | Whether this is the permission the application's command requires to hand the resource to another owner, so a database that checks every row itself holds the owner column to it. |
| [`FromAbove`](#fromabove) | Whether the permission reaches the resource from above, where its rules let a resource be reached so: held where the resource sits, or above it. |

#### `Owner`

```csharp
public bool Owner { get; set; }
```

Whether the resource's owner holds the permission by owning it: the member its `[Owner]` property names, with no role, from the moment the resource names it and for as long as it does. `true` unless said.

`Owner = false`: the owner holds it only as anybody would, through a role it is given or from above; a permission the owner is never meant to hold by owning, such as one held from above alone.

The owner holds nothing through a role: what an owner may do is exactly the permissions said here with `Owner = true`. So a request that requires such a permission on the resource lets the owner through already, and `[OrOwn]` beside it says nothing more (DDD00096).

#### `EveryMember`

```csharp
public bool EveryMember { get; set; }
```

Whether every member holds the permission by being a member, whatever roles it holds, or none. Usually the permission that sees the resource. `false` unless said.

`EveryMember = true`: being put on a resource is enough to hold it, for as long as the membership runs. Where the rules let a resource be reached from above it is also the permission that sees it from there: a caller that holds it where the resource sits sees the resource. One permission of a resource at most.

#### `InRoles`

```csharp
public bool InRoles { get; set; }
```

Whether a role can give the permission to a member. `true` unless said.

`InRoles = false`: no role can contain this permission, so only the owner (and reach from above) holds it. A role declared in code that lists it is refused when the rules are made, and a role a customer makes that lists it is refused with `permission-not-for-members`; a role that holds it from before the rules changed gives nothing with it. Say it of a permission a customer must never hand out, such as the one that says who is on the resource.

#### `LocksMembers`

```csharp
public bool LocksMembers { get; set; }
```

Whether this is the permission the application's commands require to add or remove a member and to give or take a role, so a database that checks every row itself holds the member rows to it. `false` unless said; one permission of a resource at most.

`LocksMembers = true`: whatever writes the database's policies adds a lock to both member tables, and a member row is written only by a caller that holds this permission on the resource, or the one that locks the owner. Without it, whoever may change a resource in any way writes its member rows, a role for itself included, by a statement that never went through a command. No code of the package gates a command by it: the commands still say what they require.

#### `LocksOwner`

```csharp
public bool LocksOwner { get; set; }
```

Whether this is the permission the application's command requires to hand the resource to another owner, so a database that checks every row itself holds the owner column to it. `false` unless said; one permission of a resource at most.

`LocksOwner = true`: whatever writes the database's policies pins the owner column, and a change of a resource leaves its owner as it was unless the caller holds this permission on it. The owner holds what it holds by owning, so whoever may write that column could make itself everything.

#### `FromAbove`

```csharp
public bool FromAbove { get; set; }
```

Whether the permission reaches the resource from above, where its rules let a resource be reached so: held where the resource sits, or above it. `true` unless said.

`FromAbove = false`: holding it where the resource sits gives nothing on the resource; it is held there by the owner, every member or a role alone. Not on the permission every member holds, which is the one that sees the resource from above.

### `ResourcePermission`

```csharp
public sealed record ResourcePermission
```

One permission on a resource with members, as a constant marked [`OnAttribute<TResource>`](#onattributetresource) declares it: its permission, and how it is held there. The rules of the resource are made from every one of them ([`MembershipRules.Permissions`](#membershiprules)).

| Member | Summary |
|---|---|
| [`ResourcePermission(string, bool, bool, bool, bool, bool, bool)`](#resourcepermissionstring-bool-bool-bool-bool-bool-bool) | The primary constructor: the type's summary says what it makes. |
| `string Name { get; init; }` | The permission's name, such as `boards.edit`. |
| `bool Owner { get; init; }` | Whether the owner holds it by owning the resource ([`OnAttribute.Owner`](#owner)). |
| `bool EveryMember { get; init; }` | Whether every member holds it by being one ([`OnAttribute.EveryMember`](#everymember)). |
| `bool InRoles { get; init; }` | Whether a role can give it ([`OnAttribute.InRoles`](#inroles)). |
| `bool LocksMembers { get; init; }` | Whether the database holds the member rows to it ([`OnAttribute.LocksMembers`](#locksmembers)). |
| `bool LocksOwner { get; init; }` | Whether the database holds the owner column to it ([`OnAttribute.LocksOwner`](#locksowner)). |
| `bool FromAbove { get; init; }` | Whether it reaches the resource from above ([`OnAttribute.FromAbove`](#fromabove)). |

#### `ResourcePermission(string, bool, bool, bool, bool, bool, bool)`

```csharp
public ResourcePermission(
    string Name,
    bool Owner = true,
    bool EveryMember = false,
    bool InRoles = true,
    bool LocksMembers = false,
    bool LocksOwner = false,
    bool FromAbove = true)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Name`: The permission's name, such as `boards.edit`.
- `Owner`: Whether the owner holds it by owning the resource ([`OnAttribute.Owner`](#owner)).
- `EveryMember`: Whether every member holds it by being one ([`OnAttribute.EveryMember`](#everymember)).
- `InRoles`: Whether a role can give it ([`OnAttribute.InRoles`](#inroles)).
- `LocksMembers`: Whether the database holds the member rows to it ([`OnAttribute.LocksMembers`](#locksmembers)).
- `LocksOwner`: Whether the database holds the owner column to it ([`OnAttribute.LocksOwner`](#locksowner)).
- `FromAbove`: Whether it reaches the resource from above ([`OnAttribute.FromAbove`](#fromabove)).

## Reach from above

Namespace `DDDToolkit.Supporting.Membership.Access`.

### `ReachFromAbove`

```csharp
public sealed class ReachFromAbove
```

Says that a resource is reached from above as well as through its members: it hangs at a place, a unit of an organization or a bay of a depot, which its class marks `[At]`, and a caller that holds a permission at that place, or above it, holds the permission on the resource without being a member of it.

```csharp
// Beside a package that keeps an organization: where a permission is held is that package's to answer, and the rules
// say only that it reaches.
MembershipRules.For<Board>(Roles.Custom(...), reachFromAbove: true)

// Beside a place of the application's own: the function that answers it, which the application declares once.
MembershipRules.For<Crate>(Roles.Fixed(...), reachFromAbove: DepotFunctions.BaysWhereIHold)
```

Two pictures. Beside an organization, Ada holds the administrators' role at its root, and reaches every board of the organization without being on one; Ben holds `boards.edit` at Pier 7, and edits the boards that hang at Pier 7, and no other. Without one, in a depot of the application's own, porter Piet holds `crates.scrap` at bay A, and scraps crate 17, which stands there, without being one of its porters.

Why it matters: whoever runs a part of an organization sees and acts on what hangs there, without being added to each resource; and a resource whose owner has left is still reached by somebody, who can hand it to a new owner. Through its members alone, a board whose owner left would be nobody's to hand on.

A resource the caller does not see does not exist for it, whatever it holds there. So rules that let a resource be reached from above have a permission every member holds ([`OnAttribute.EveryMember`](#everymember), which is required then): whoever holds that permission where the resource hangs sees the resource, as its members do, and without one nobody above would.

A permission held from above is not cut by what a role gives ([`OnAttribute.InRoles`](#inroles)): that is about a member's roles on the resource, and a permission held above is held through the place, by a role of the organization, a permission marked `InRoles = false` included. Only a permission marked `FromAbove = false` is not reached this way. A caller that holds a permission both ways holds it as a member ([`MemberVia.Members`](use-cases.md#membervia)).

Where a caller holds a permission is the application's to answer, or the organization package's: in C# through an `IPlacesReached<TResourceId, TPlaceId>` registered with the resource, which the package that stores the resources asks inside its own statement, and which such a package's join with this one writes; in a database that answers the questions itself, through a function. Beside such a package that function is its own, found by the type of the place, its unit id; beside a place of the application's own it is the one named here. The package keeps no places and no tree of them.

| Member | Summary |
|---|---|
| `DatabaseFunction? Function { get; }` | The function that answers in the database where the caller holds a permission, or `null` where it follows from the type of the place: the one the organization package's contribution names for its unit id. A function that takes the permission as text and answers the places the caller's hold of it reaches, each place it is held at and every place below. It is asked by the resource's functions, which run as their owner, so it answers for the caller by a condition of its own, never by a policy. |
| [`implicit operator ReachFromAbove?(bool)`](#implicit-operator-reachfromabovebool) | `reachFromAbove: true`: a resource is reached from above, by the function that follows from the type of the place it hangs at. |
| [`implicit operator ReachFromAbove(DatabaseFunction)`](#implicit-operator-reachfromabovedatabasefunction) | `reachFromAbove: DepotFunctions.BaysWhereIHold`: a resource is reached from above, by a function the application declares, beside a place of its own. |

#### `implicit operator ReachFromAbove?(bool)`

```csharp
public static implicit operator ReachFromAbove?(bool reaches)
```

`reachFromAbove: true`: a resource is reached from above, by the function that follows from the type of the place it hangs at. `false` is no reach from above, as leaving the argument out is.

**Parameters**

- `reaches`: Whether a permission held above the resource reaches it.

#### `implicit operator ReachFromAbove(DatabaseFunction)`

```csharp
public static implicit operator ReachFromAbove(DatabaseFunction function)
```

`reachFromAbove: DepotFunctions.BaysWhereIHold`: a resource is reached from above, by a function the application declares, beside a place of its own.

**Parameters**

- `function`: The function that answers where the caller holds a permission, one the application declares once.

## Who a member is

Namespace `DDDToolkit.Supporting.Membership.Access`.

### `MemberSource`

```csharp
public sealed record MemberSource
```

Where the caller's member id comes from: which fact about the caller is compared with the members of a resource. It is data, so the code that asks in C# and the functions that ask in the database read the same declaration.

It is one of three things a resource's rules say apart from each other: who a member is, which is this; where the roles a member holds come from ([`MembershipRules.Roles`](#membershiprules)); and whether something above the resource reaches it ([`MembershipRules.ReachFromAbove`](#membershiprules)). Each stands by itself, so any member source goes with any kind of roles, with reach from above or without.

Rules say none, usually: who a member is follows from what a member is known by, the second type of `[Member<...>]` ([`Inferred`](#inferred)). The others are for what that cannot tell.

| Member | Summary |
|---|---|
| [`Inferred`](#inferred) | Who a member is follows from what it is known by, the member id of `[Member<TId, TMemberId, TRoleId, TResource>]`: the default, which rules that say no other source have. |
| `static MemberSource CallerId { get; }` | The caller's user id, the `sub` of its token: for members that are users, known by an id over a `Guid`. What [`Inferred`](#inferred) takes for such an id, said outright. |
| `MemberSourceKind Kind { get; }` | Which fact about the caller it is. |
| `string? ClaimPath { get; }` | The claim's path for [`MemberSourceKind.Claim`](#membersourcekind); `null` for every other kind. |
| `DatabaseFunction? Function { get; }` | For [`MemberSourceKind.Resolved`](#membersourcekind), the function that answers the caller's member id in the database, or `null` when the rules name none; `null` for every other kind. |
| [`Claim(string)`](#claimstring) | A claim of the caller's token, as text: for members known by something an identity provider says of a user, such as a staff number. |
| [`Resolved(DatabaseFunction?)`](#resolveddatabasefunction) | An id the application resolves for the caller: for members that are not the caller itself but something the application gives its callers, such as the place somebody has in an organization, which a token does not carry and which stops counting when the organization says so. |

#### `Inferred`

```csharp
public static MemberSource Inferred { get; }
```

Who a member is follows from what it is known by, the member id of `[Member<TId, TMemberId, TRoleId, TResource>]`: the default, which rules that say no other source have.

- The seat id of a package that keeps an organization's seats: the caller's seat. Such a package answers it, in C# through the `ICallerMember` its join with this package registers for the resource, and in the database through the function its contribution names for its seat id, where the context that maps the resource maps that package's read model.
- An id over a `Guid` otherwise: the caller's user id, the `sub` of its token, as [`CallerId`](#membersource).

A member known by anything else is a member the application says: [`Claim`](#claimstring) for a claim of the token, [`Resolved`](#resolveddatabasefunction) for what it resolves itself.

#### `Claim(string)`

```csharp
public static MemberSource Claim(string path)
```

A claim of the caller's token, as text: for members known by something an identity provider says of a user, such as a staff number. Read claims only the server can change, `app_metadata`, never a user's own `user_metadata`.

**Parameters**

- `path`: The claim, by its path into the token's claims: `app_metadata.staff`.

**Exceptions**

- `ArgumentException`: `path` is not names of letters, digits and underscores joined by dots; or it reads `user_metadata`, which a user changes for itself.

#### `Resolved(DatabaseFunction?)`

```csharp
public static MemberSource Resolved(DatabaseFunction? function = null)
```

An id the application resolves for the caller: for members that are not the caller itself but something the application gives its callers, such as the place somebody has in an organization, which a token does not carry and which stops counting when the organization says so.

In C# the application answers it, through an [`ICallerMember<TResourceId, TMemberId>`](#icallermembertresourceid-tmemberid) it registers with the resource; a resource registered without one is refused, so a member is never silently read from the caller's own id. In a database that answers the questions itself, a function of the application's answers it, named here.

**Parameters**

- `function`: The function that answers the caller's member id in the database, one a package ships or the application declares: one without parameters that answers the id as the member's column stores it, or `NULL` for a caller that is nobody's member. Left out by an application whose database does not answer the questions itself; whatever writes the resource's functions refuses rules that name none. It is asked by the resource's functions, which run as their owner, so it reads every row whatever policies its tables have: it answers for the caller by a condition of its own, the caller's id or a claim, never by a policy.

### `MemberSourceKind`

```csharp
public enum MemberSourceKind
```

The kinds of [`MemberSource`](#membersource).

| Member | Summary |
|---|---|
| `Inferred = 0` | Who a member is follows from what it is known by: [`MemberSource.Inferred`](#inferred). |
| `CallerId = 1` | The caller's user id: [`MemberSource.CallerId`](#membersource). |
| `Claim = 2` | A claim of the caller's token: [`MemberSource.Claim`](#claimstring). |
| `Resolved = 3` | An id the application resolves for the caller: [`MemberSource.Resolved`](#resolveddatabasefunction). |

### `ICallerMember<TResourceId, TMemberId>`

```csharp
public interface ICallerMember<TResourceId, TMemberId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
```

Who the caller is as a member of one kind of resource, where the application resolves that itself ([`MemberSource.Resolved`](#resolveddatabasefunction)): the place somebody has in an organization, say, which no token carries. An application implements it where it knows its callers, and registers it with the resource.

It is asked once for every question, about a caller that signed in, and its answer is compared with the members of a resource inside one statement. So it answers from what the application knows of the caller already, without reading anything: an application that has to look its caller up does so once, where the request begins, and answers from that here.

It is asked for by the resource, so two kinds of resource whose members are known by the same id each have their own, or share one class registered for both.

**Type parameters**

- `TResourceId`: The id of the kind of resource the members are of.
- `TMemberId`: What a member is known by.

| Member | Summary |
|---|---|
| [`Find(Caller)`](#findcaller) | The member id `caller` has, or `null` when it has none that counts now: somebody the application does not know, or knows and no longer counts. |
| [`Require(Caller)`](#requirecaller) | Refuses a caller the questions cannot be asked about at all, with the application's own refusal: somebody who is nobody in the organization the resources belong to. |

#### `Find(Caller)`

```csharp
TMemberId? Find(Caller caller)
```

The member id `caller` has, or `null` when it has none that counts now: somebody the application does not know, or knows and no longer counts. Such a caller is nobody's member, and holds only what reaches a resource from above. It refuses nobody.

**Parameters**

- `caller`: The caller a question is asked for: a signed-in user.

#### `Require(Caller)`

```csharp
void Require(Caller caller)
```

Refuses a caller the questions cannot be asked about at all, with the application's own refusal: somebody who is nobody in the organization the resources belong to. It is asked before anything is read, by the questions that refuse, about every caller that is not the application's own work. It returns for everybody unless the application says otherwise: a caller that is nobody's member then simply reaches nothing.

**Parameters**

- `caller`: The caller a question is asked for.

**Exceptions**

- `RefusalException`: The caller is nobody the questions can be asked about.
