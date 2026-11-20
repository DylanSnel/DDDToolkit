---
title: "Membership API: Rules and permissions"
sidebar_label: Rules and permissions
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Membership API: Rules and permissions

The rules of one kind of resource, as one declaration: `MembershipRules.For<TResource>` and the mark that hands them to the export, where the roles come from, the permissions declared on the resource and their settings, reach from above, and who the caller is as a member. The codes a resource refuses under are with the refusals, under [`MembershipCodes`](refusals.md#membershipcodes).

## Rules

Namespace `DTS.Membership.Access`.

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

They say who holds a permission: the owner by owning the resource, a member by being one and through its roles. They also name the permission that manages the members and the one that names the owner (`ManagesMembers`, `NamesOwner`), which the resource's member service requires, and, where a customer makes the roles, the permission that manages those roles ([`Roles.ManagedWith`](#roles-1)). A rule of the application's own beyond those, such as that nobody gives a role for longer than they hold the permission, it writes itself.

It is data, checked once when it is made, so everything that decides access reads the same thing: the questions asked in C# before a handler runs, the functions that answer the same questions in the database, and whatever writes those functions. Declare one per kind of resource. Nothing here is shared between two resources, so an application with documents and folders declares two, and neither touches the other. The one thing read from another resource is the kinds of a permission it declares that a role here gives ([`KindsOf`](#kindsofstring)), which the host checks when it starts.

Three things are said apart from each other, and any of the one goes with any of the others. Who a member is: what follows from the member id, the caller's seat for the seat id of a package that keeps an organization's seats and the caller's user id for one over a Guid, or a claim of its token, or an id the application resolves for the caller ([`Members`](#membershiprules)). Where the roles come from: in code, made by a customer, or kept elsewhere ([`Roles`](#membershiprules)). And whether a permission held where the resource sits, or above it, reaches the resource ([`ReachFromAbove`](#reachfromabove)). So a resource beside an organization can have the organization's people as members with roles of its own, in code or made by a customer, or with the organization's roles, with reach from above or without.

| Member | Summary |
|---|---|
| `Type Resource { get; }` | The resource's class. |
| `string Name { get; }` | The resource's name: what its codes and its functions are named after. |
| `MembershipCodes Codes { get; }` | The codes the resource refuses with. |
| `MembershipFunctions Functions { get; }` | The names of the resource's set functions in the database, which follow from its name. |
| `MemberSource Members { get; }` | Who the caller is as a member. |
| `Roles Roles { get; }` | Where the roles a member holds come from, and the roles declared in code. |
| [`ReachFromAbove`](#reachfromabove) | Whether a resource is reached from above as well as through its members. |
| `IReadOnlyList<ResourcePermission> Permissions { get; }` | The permissions on the resource, each once, as they are declared: every permission asked about on a resource of this kind. |
| `IReadOnlyList<string> OwnerPermissions { get; }` | The permissions the owner holds by owning the resource, those marked `Owner`, in the order they are declared. The owner holds no role, so this is all it holds by owning it. |
| `string? EveryMemberPermission { get; }` | The permission every member holds by being one, whatever roles it holds, or `null`: usually the one that sees the resource. Without one a resource is still seen by its members, and by its owner, and that is all membership alone gives. Where a resource is reached from above ([`ReachFromAbove`](#reachfromabove)) it is the permission that sees it from there as well. |
| `RolePermissions RolePermissions { get; }` | Which permissions a member's role can give on the resource, wherever the roles come from. |
| [`ManagesMembersPermission`](#managesmemberspermission) | The permission that manages the members of a resource, the one marked `ManagesMembers`, or `null`. |
| [`NamesOwnerPermission`](#namesownerpermission) | The permission that names the owner of a resource, the one marked `NamesOwner`, or `null`. |
| `IReadOnlyList<string> SystemScopes { get; }` | The scopes whose work is the application's own for this resource: work that runs as `CurrentUserSnapshot.SystemIn` in one of them holds every permission on every resource of this kind, as `CurrentUserSnapshot.System` does. The module's own scope unless the rules say otherwise; work in any other scope holds nothing on a resource, since a scope is a module's own and another module's work is not above this resource's rules. |
| `IReadOnlyList<RoleSet> Sets { get; }` | The role sets, at least one, in the order they are declared. The first is the resource's own. |
| `RoleSet FirstSet { get; }` | The first role set: the resource's own, managed with the permission marked `ManagesMembers`. |
| [`For(Roles, MemberSource?, bool, IReadOnlyList<string>?)`](#forroles-membersource-bool-ireadonlyliststring) | The rules of the resource `TResource`. |
| [`MembershipGives(string)`](#membershipgivesstring) | Whether being a member gives `permission` by itself, whatever roles the member holds: so for [`EveryMemberPermission`](#membershiprules), and for nothing else. |
| [`OwnerHolds(string)`](#ownerholdsstring) | Whether a resource's owner holds `permission` on it by being its owner: so for every one of [`OwnerPermissions`](#membershiprules), and for no other permission, with no end. |
| [`MembersHold(string)`](#membersholdstring) | Whether a member can hold `permission` on the resource at all, by being a member or through a role: [`EveryMemberPermission`](#membershiprules), and every permission within [`RolePermissions`](#membershiprules). |
| [`ReachesFromAbove(string)`](#reachesfromabovestring) | Whether `permission` held where the resource sits, or above it, reaches the resource: where the rules let a resource be reached from above, for every permission but the permissions marked `FromAbove = false`. |
| [`IsOwnWork(CurrentUserSnapshot)`](#isownworkcurrentusersnapshot) | Whether `user` is system work for this resource, which holds every permission on every resource of this kind: `CurrentUserSnapshot.System`, always, and `CurrentUserSnapshot.SystemIn` in one of [`SystemScopes`](#membershiprules). |
| [`RolesWith(string)`](#roleswithstring) | The fixed roles that give `permission` on the resource: those that list it, when a member's role can give it at all, in the order they are declared. |
| [`PermissionsOf(NamedRole)`](#permissionsofnamedrole) | The permissions `role` gives a member on the resource: the permissions it lists that a member's role can give. |
| [`Knows(NamedRole)`](#knowsnamedrole) | Whether `role` is one of the roles declared in code: a fixed role, or a starter role a customer's roles are made from. |
| [`KindsOf(string)`](#kindsofstring) | The kinds of seat that may hold `permission` on the resource ([`OnAttribute.Kinds`](#kinds)). |
| [`MayHold(string, MemberKind)`](#mayholdstring-memberkind) | Whether a member of `kind` may hold `permission`: the list holds the kind's value, or the group of its side. |
| [`MayHoldInside(string)`](#mayholdinsidestring) | Whether `permission`'s list holds `SeatKinds.InOrganization`. |
| [`MayHoldOutside(string)`](#mayholdoutsidestring) | Whether `permission`'s list holds `SeatKinds.OutsideOrganization`: every kind outside the organization may hold it. |
| [`KindValuesOf(string)`](#kindvaluesofstring) | The kinds `permission`'s list names by their value, such as `stakeholder`, without the two groups. |
| [`SetNamed(string?)`](#setnamedstring) | The set named `name`, by its name or its code; the first set for `null` or the first set's name. |
| [`SetOf(NamedRole)`](#setofnamedrole) | The set a fixed or starter role is declared in, or `null` for a role the rules do not declare. |
| [`SetFor(MemberKind)`](#setformemberkind) | The one set whose kinds hold `kind`, by its value or the group of its side, or `null`. |
| [`MembersManagedWith(RoleSet)`](#membersmanagedwithroleset) | The permission that manages the members of `set`: the one marked `ManagesMembers` for the first set, and the set's own for every other. |
| [`ProblemsWith(IReadOnlyCollection<MemberKind>)`](#problemswithireadonlycollectionmemberkind) | What of these rules does not fit the kinds of seat an application declares, one sentence per problem; none when they fit. |

#### `ReachFromAbove`

```csharp
public bool ReachFromAbove { get; }
```

Whether a resource is reached from above as well as through its members. A resource hangs at places, the properties its class marks `[At]`, one of each kind. A caller that holds a permission at one of those places, or above it, holds the permission on the resource without being a member of it. `false` when a resource is reached through its members and by its owner alone.

```csharp
[AggregateRoot<BoardId>]
public sealed partial class Board
{
    [At] public OrganizationUnitId UnitId { get; private set; }   // a unit of the organization
    [At] public BerthId? BerthId { get; private set; }            // a berth of the harbor's own, or none
}

MembershipRules.For<Board>(Roles.Custom(...), reachFromAbove: true)
```

Two pictures. Beside an organization, Ada holds the administrators' role at its root, and reaches every board of the organization without being on one. Ben holds `boards.edit` at Pier 7, and edits the boards that hang at Pier 7, and no other. In a harbor of the application's own, Sam holds `boards.view` at Quay 1, and sees every board that hangs at a berth below Quay 1.

Whoever runs a part of an organization sees and acts on what hangs there, without being added to each resource. A resource whose owner has left is still reached by somebody, who can hand it to a new owner. Through its members alone, a board whose owner left would be nobody's to hand on.

The kind of a place is the type of its id, without `Nullable<T>`. The kind says who answers where a caller holds a permission at a place. A package that keeps the places answers its own kind, as a package that keeps an organization answers its unit id. The application answers a kind of its own. In C# that is an `IPlacesReached<TResourceId, TPlaceId>` registered with the resource. On Postgres it is a function the application marks once for the kind ([`PlacesWhereIHold`](#placeswhereihold)). A place that nothing answers reaches nothing. An empty place reaches nothing either. Rules that reach from above a resource none of whose places is answered are refused.

A resource the caller does not see does not exist for it, whatever it holds there. So rules that let a resource be reached from above have a permission every member holds ([`OnAttribute.EveryMember`](#everymember), which is required then). Whoever holds that permission at one of the resource's places, or above it, sees the resource, as its members do.

A permission held from above is not cut by what a role gives ([`OnAttribute.InRoles`](#inroles)). That setting is about a member's roles on the resource. A permission held above is held through the place, by a role of the organization, a permission marked `InRoles = false` included. A permission marked `FromAbove = false` is reached from no place. A caller that holds a permission both ways holds it as a member ([`MemberVia.Members`](services.md#membervia)). A hold from any place is [`MemberVia.Above`](services.md#membervia), which does not say the place.

#### `ManagesMembersPermission`

```csharp
public string? ManagesMembersPermission { get; }
```

The permission that manages the members of a resource, the one marked `ManagesMembers`, or `null`. The resource's member service requires it on the resource to add or remove a member and to give or take a role, and lets only system work make those changes where it is `null`. A database that checks every row itself holds both member tables to it as well: whatever writes the database's policies writes a row only for a caller that holds it on the resource, or the permission that names the owner.

The database asks what the caller holds when each statement runs. Mark it `Owner`, as the default is, where a caller opens a resource as itself: the owner's own first rows are then its to write.

#### `NamesOwnerPermission`

```csharp
public string? NamesOwnerPermission { get; }
```

The permission that names the owner of a resource, the one marked `NamesOwner`, or `null`. The resource's member service requires it on the resource to hand the resource to another owner, and lets only system work do that where it is `null`. A database that checks every row itself holds the owner column to it as well: a change of a resource leaves its owner as it was unless the caller holds it there.

Whoever holds it by owning the resource alone holds it no longer once the owner has changed, and what a hand-over writes after that is then not its to write. A hand-over by the owner itself is saved as the application's own work; one by somebody who holds it through a role, or from above, as the caller.

#### `For(Roles, MemberSource?, bool, IReadOnlyList<string>?)`

```csharp
public static MembershipRules For<TResource>(
    Roles roles,
    MemberSource? members = null,
    bool reachFromAbove = false,
    IReadOnlyList<string>? systemScopes = null)
    where TResource : class, IMembershipResource
```

The rules of the resource `TResource`.

**Parameters**

- `roles`: Where the roles a member holds come from: [`Roles.Fixed`](#fixedireadonlylistdeclaredrole-ireadonlylistroleset) in code, [`Roles.Custom`](#customireadonlylistdeclaredrole-otherpermissions-string-ireadonlylistroleset) made by a customer, or [`Roles.External`](#externaldatabasefunction-otherpermissions) kept elsewhere.
- `members`: Who the caller is as a member; left out, it follows from what a member is known by ([`MemberSource.Inferred`](#inferred)): the caller's seat for the seat id of a package that keeps an organization's seats, the caller's user id for an id over a Guid.
- `reachFromAbove`: `true` lets a permission held where the resource hangs, or above it, reach the resource ([`ReachFromAbove`](#reachfromabove)). The resource is reached from each of its places, the properties its class marks `[At]`, that something answers for the place's kind. The rules name no place and no function. Left out, or `false`, a resource is reached through its members and by its owner alone. Said, one permission is held by every member ([`OnAttribute.EveryMember`](#everymember)), and what is held above sees a resource by that permission.
- `systemScopes`: The scopes whose system work holds every permission on every resource of this kind ([`SystemScopes`](#membershiprules)). Left out, the scope of the module that declares the resource ([`IMembershipResource.MembershipScope`](resource-and-members.md#imembershipresource)): `boards` for the module `Boards`, so work begun as `CurrentUserSnapshot.SystemIn("boards")` does what it needs on boards. Said, the scopes named and no other: name the module's own among them to keep it, and say `[]` for no system work in a scope at all.

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

#### `IsOwnWork(CurrentUserSnapshot)`

```csharp
public bool IsOwnWork(CurrentUserSnapshot user)
```

Whether `user` is system work for this resource, which holds every permission on every resource of this kind: `CurrentUserSnapshot.System`, always, and `CurrentUserSnapshot.SystemIn` in one of [`SystemScopes`](#membershiprules). Work in any other scope is not, and holds nothing.

**Parameters**

- `user`: The caller a question is asked for.

**Exceptions**

- `ArgumentNullException`: `user` is null.

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

#### `KindsOf(string)`

```csharp
public IReadOnlyList<string> KindsOf(string permission)
```

The kinds of seat that may hold `permission` on the resource ([`OnAttribute.Kinds`](#kinds)). A permission declared without a list gives `[SeatKinds.InOrganization]`.

A permission the rules do not declare, which a role gives through `otherPermissions`, has the kinds that the resource that declares it lists. A role on a folder that gives `documents.view` holds it for the kinds that `[On<Document>(Kinds = [...])]` lists. Such a permission that no resource with members declares, and every other permission the rules do not declare, gives `[SeatKinds.InOrganization]`.

The kinds of another resource's permission are read when they are asked, so they are known once the module that declares them is loaded. A host registers every resource before it starts, and the start-up check of the roles reads them then (`ProblemsWithOtherResources`).

**Parameters**

- `permission`: A permission.

**Exceptions**

- `InvalidOperationException`: A role may give `permission`, the rules do not declare it, and two other resources declare it with different kinds.

#### `MayHold(string, MemberKind)`

```csharp
public bool MayHold(string permission, MemberKind kind)
```

Whether a member of `kind` may hold `permission`: the list holds the kind's value, or the group of its side. Reach from above asks the same of the caller's own kind ([`ICurrentMember.KindOf`](#kindofcurrentusersnapshot)).

**Parameters**

- `permission`: A permission.
- `kind`: The kind on the member's row, or the caller's kind.

#### `MayHoldInside(string)`

```csharp
public bool MayHoldInside(string permission)
```

Whether `permission`'s list holds `SeatKinds.InOrganization`. The owner holds a permission only then, since an owner is inside the organization.

**Parameters**

- `permission`: A permission.

#### `MayHoldOutside(string)`

```csharp
public bool MayHoldOutside(string permission)
```

Whether `permission`'s list holds `SeatKinds.OutsideOrganization`: every kind outside the organization may hold it.

**Parameters**

- `permission`: A permission.

#### `KindValuesOf(string)`

```csharp
public IReadOnlyList<string> KindValuesOf(string permission)
```

The kinds `permission`'s list names by their value, such as `stakeholder`, without the two groups. The checks in C# and in the database compare a member row's kind with these, and its side with [`MayHoldInside`](#mayholdinsidestring) and [`MayHoldOutside`](#mayholdoutsidestring).

**Parameters**

- `permission`: A permission.

#### `SetNamed(string?)`

```csharp
public RoleSet? SetNamed(string? name)
```

The set named `name`, by its name or its code; the first set for `null` or the first set's name. `null` for a name the rules do not declare.

**Parameters**

- `name`: A set's name, its code, or `null`.

#### `SetOf(NamedRole)`

```csharp
public RoleSet? SetOf(NamedRole role)
```

The set a fixed or starter role is declared in, or `null` for a role the rules do not declare.

**Parameters**

- `role`: A role.

#### `SetFor(MemberKind)`

```csharp
public RoleSet? SetFor(MemberKind kind)
```

The one set whose kinds hold `kind`, by its value or the group of its side, or `null`.

**Parameters**

- `kind`: A member's kind.

**Exceptions**

- `InvalidOperationException`: Two sets hold the kind: a group and a kind by value of that side overlap.

#### `MembersManagedWith(RoleSet)`

```csharp
public string? MembersManagedWith(RoleSet set)
```

The permission that manages the members of `set`: the one marked `ManagesMembers` for the first set, and the set's own for every other.

**Parameters**

- `set`: A set of these rules.

#### `ProblemsWith(IReadOnlyCollection<MemberKind>)`

```csharp
public IReadOnlyList<string> ProblemsWith(IReadOnlyCollection<MemberKind> declared)
```

What of these rules does not fit the kinds of seat an application declares, one sentence per problem; none when they fit. The rules name kinds by value without knowing them, so the join with the package that keeps the seats asks this when the host starts.

**Parameters**

- `declared`: The kinds of seat the application declares, each with its side of the organization.

**Exceptions**

- `ArgumentNullException`: `declared` is null.

**Remarks**

Two problems are found:

- a permission or a role set names a kind by value that `declared` does not hold;
- a declared kind is in two role sets, through the group of its side in one and by its value in the other.

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

The names of the five set functions a database answers a resource's membership questions with, for the caller of the connection. They are the resource's own names, because an application may have several kinds of resource with members in one schema. They follow from the resource's name ([`For`](#forstring)), so a resource that keeps its name keeps them; one that is renamed with `[Members("...")]` gets new ones. Rules do not ask them by name: they ask the last three by the resource's id, `user.Sees<TKey>()`, `user.Holds<TKey>(permission)` and `user.FellowMembers<TKey, TMemberId>()`, and the export writes the policy with whatever the names are.

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

Namespace `DTS.Membership.Access`.

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
- **External**: roles the application keeps in a repository of its own, the roles of its organization say, which it answers for through [`IMemberRoles<TResourceId, TRoleId>`](#imemberrolestresourceid-troleid) and, in a database that answers the questions itself, through a function. A member holds one by that repository's id.

Whichever it is, what a role gives a member is held to the resource's permissions: only a permission marked `InRoles` ([`OnAttribute.InRoles`](#inroles)), and, where the roles take them, permissions of other modules ([`OtherPermissions`](#roles-1)). The owner holds no role: it holds the permissions marked `Owner` by owning the resource.

| Member | Summary |
|---|---|
| `IReadOnlyList<RoleSet> Sets { get; }` | The role sets, at least one, in the order they are declared: the first is the resource's own. Roles without sets have one set, with no name, for the kinds inside the organization, holding every declared role. |
| `RolesKind Kind { get; }` | Which of the three it is. |
| `IReadOnlyList<DeclaredRole> Declared { get; }` | The roles declared in code, of every set, in order: the roles a member holds, for [`RolesKind.Fixed`](#roleskind); the starter roles a customer's first rows are made from, for [`RolesKind.Custom`](#roleskind); none for [`RolesKind.External`](#roleskind). |
| `DatabaseFunction? Function { get; }` | For [`RolesKind.External`](#roleskind), the function that answers in the database which of the application's roles give a permission, or `null` where the database does not answer the questions itself. |
| `OtherPermissions? OtherPermissions { get; }` | Which permissions of other modules a role gives on the resource besides its permissions, or `null` for none: then a role gives the resource's permissions marked `InRoles` and nothing else. |
| `string? ManagedWith { get; }` | For [`RolesKind.Custom`](#roleskind), the permission that makes, renames, gives permissions to and archives the roles, or `null`. The roles are a customer's as a whole, not one resource's, so it is held for the whole of what they are kept for: the role service the generator writes for the role class, `BoardRoleService`, asks it of what the application registered for that, which the join with an organization's package answers for the whole organization. Where nothing answers, and where it is `null`, only system work changes the roles. |
| [`Fixed(IReadOnlyList<DeclaredRole>?, IReadOnlyList<RoleSet>?)`](#fixedireadonlylistdeclaredrole-ireadonlylistroleset) | Roles in code, each with the permissions it gives on the resource, held by name: the same for every resource of the kind, and changed with a deploy. |
| [`Custom(IReadOnlyList<DeclaredRole>?, OtherPermissions?, string?, IReadOnlyList<RoleSet>?)`](#customireadonlylistdeclaredrole-otherpermissions-string-ireadonlylistroleset) | Roles a customer makes for itself while the application runs: rows of the application's role class ([`CustomRoleAttribute<TRoleId, TResource>`](resource-and-members.md#customroleattributetroleid-tresource)), each with a name and the permissions it gives, which a customer renames, gives other permissions and archives, held by the class's id. |
| [`External(DatabaseFunction?, OtherPermissions?)`](#externaldatabasefunction-otherpermissions) | Roles the application keeps in a repository of its own, such as the roles of its organization, held by that store's id. |

#### `Fixed(IReadOnlyList<DeclaredRole>?, IReadOnlyList<RoleSet>?)`

```csharp
public static Roles Fixed(
    IReadOnlyList<DeclaredRole>? roles = null,
    IReadOnlyList<RoleSet>? sets = null)
```

Roles in code, each with the permissions it gives on the resource, held by name: the same for every resource of the kind, and changed with a deploy.

**Parameters**

- `roles`: The roles, at least one, each named once, with permissions of the resource that a role can give ([`OnAttribute.InRoles`](#inroles)). Left out where `sets` are given.
- `sets`: The role sets, each with its fixed roles ([`RoleSet.Named`](#namedstring-ireadonlyliststring-ireadonlylistdeclaredrole-ireadonlylistdeclaredrole-string) with `roles:`), in place of `roles`. A role's set is the set it is declared in.

**Exceptions**

- `ArgumentException`: Both or neither of `roles` and `sets` are given, no role is declared, a set is given starter roles, or a role has no name, the name of another, or a permission twice.

#### `Custom(IReadOnlyList<DeclaredRole>?, OtherPermissions?, string?, IReadOnlyList<RoleSet>?)`

```csharp
public static Roles Custom(
    IReadOnlyList<DeclaredRole>? starters = null,
    OtherPermissions? otherPermissions = null,
    string? managedWith = null,
    IReadOnlyList<RoleSet>? sets = null)
```

Roles a customer makes for itself while the application runs: rows of the application's role class ([`CustomRoleAttribute<TRoleId, TResource>`](resource-and-members.md#customroleattributetroleid-tresource)), each with a name and the permissions it gives, which a customer renames, gives other permissions and archives, held by the class's id.

```csharp
Roles.Custom(starters: [new("editor", [BoardPermissions.View, BoardPermissions.Edit])], managedWith: BoardPermissions.ManageRoles)
```

**Parameters**

- `starters`: The roles a customer starts with: what its first rows are made from (the role service's `CreateStarterRolesAsync`), none when left out. A starter role is no role a member holds; the row made from it is.
- `otherPermissions`: Which permissions of other modules a role may hold besides the resource's permissions; none when left out.
- `managedWith`: The permission that changes the roles, held for the whole of what they are kept for ([`ManagedWith`](#roles-1)). Left out, only system work changes them.
- `sets`: The role sets, each with its starter roles ([`RoleSet.Named`](#namedstring-ireadonlyliststring-ireadonlylistdeclaredrole-ireadonlylistdeclaredrole-string) with `starters:`), in place of `starters`. Left out, the rules have one set, for the kinds inside the organization. One `managedWith` changes the roles of every set.

**Exceptions**

- `ArgumentException`: A starter role has no name, the name of another, or a permission twice; `managedWith` is blank; both `starters` and `sets` are given; or a set is given fixed roles.

#### `External(DatabaseFunction?, OtherPermissions?)`

```csharp
public static Roles External(
    DatabaseFunction? function = null,
    OtherPermissions? otherPermissions = null)
```

Roles the application keeps in a repository of its own, such as the roles of its organization, held by that store's id. In C# the application answers which roles there are and which give a permission, through ports it registers with the resource; in a database that answers the questions itself, `function` answers which roles give a permission.

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
| `Fixed = 0` | Roles in code, held by name: [`Roles.Fixed`](#fixedireadonlylistdeclaredrole-ireadonlylistroleset). |
| `Custom = 1` | Rows a customer makes: [`Roles.Custom`](#customireadonlylistdeclaredrole-otherpermissions-string-ireadonlylistroleset). |
| `External = 2` | The application's own repository of roles: [`Roles.External`](#externaldatabasefunction-otherpermissions). |

### `DeclaredRole`

```csharp
public sealed record DeclaredRole
```

A role declared in code: a name, and the permissions a member that holds it has on the resource. Of fixed roles ([`Roles.Fixed`](#fixedireadonlylistdeclaredrole-ireadonlylistroleset)) a member holds it as a [`NamedRole`](resource-and-members.md#namedrole) of the same name.

```csharp
new DeclaredRole("contributor", [DocumentPermissions.View, DocumentPermissions.Edit])
```

Of a customer's roles ([`Roles.Custom`](#customireadonlylistdeclaredrole-otherpermissions-string-ireadonlylistroleset)) it is a starter role instead: what a first role is made from, with this name and these permissions, and nothing a member holds.

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

A permission of another module that a resource with members declares is held by the kinds of seat that resource lists for it ([`MembershipRules.KindsOf`](#kindsofstring)). Any other permission of another module is held by the kinds inside the organization.

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

### `IMemberRoles<TResourceId, TRoleId>`

```csharp
public interface IMemberRoles<TResourceId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

The roles there are for the members of one kind of resource: whether a role is in use, and in which role set, which the member service asks before it gives one.

Where the roles are fixed ([`Roles.Fixed`](#fixedireadonlylistdeclaredrole-ireadonlylistroleset)), the package answers it from the rules. Where a customer makes them ([`Roles.Custom`](#customireadonlylistdeclaredrole-otherpermissions-string-ireadonlylistroleset)), the package that stores them answers it from their rows: a role in use. Where they are kept elsewhere ([`Roles.External`](#externaldatabasefunction-otherpermissions)), the roles an organization makes for itself say, the application answers it from there, registered with the resource, or the join with that organization's package answers it. Roles kept elsewhere are in the first set.

**Type parameters**

- `TResourceId`: The id of the kind of resource the members are of.
- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| [`FindAsync(TRoleId, CancellationToken)`](#findasynctroleid-cancellationtoken) | The role set of `role`, or `null` where it is no role in use that a member can be given. |

#### `FindAsync(TRoleId, CancellationToken)`

```csharp
ValueTask<FoundRole?> FindAsync(TRoleId role, CancellationToken cancellationToken)
```

The role set of `role`, or `null` where it is no role in use that a member can be given.

**Parameters**

- `role`: The role.
- `cancellationToken`: Cancels the lookup.

### `FoundRole`

```csharp
public sealed record FoundRole
```

A role in use, found for a member ([`IMemberRoles.FindAsync`](#findasynctroleid-cancellationtoken)).

| Member | Summary |
|---|---|
| [`FoundRole(string?)`](#foundrolestring) | The primary constructor: the type's summary says what it makes. |
| `string? Set { get; init; }` | The role set it is in, as a role row stores it: `null` for the first set ([`RoleSet.Stored`](#roleset)). |

#### `FoundRole(string?)`

```csharp
public FoundRole(string? Set)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Set`: The role set it is in, as a role row stores it: `null` for the first set ([`RoleSet.Stored`](#roleset)).

### `RoleSet`

```csharp
public sealed class RoleSet
```

A set of roles of a resource, for the members of some kinds of seat. A member is in the one set its kind is in, and holds only roles of that set. Each set but the first is managed with a permission of its own.

```csharp
Roles.Custom(
    sets:
    [
        RoleSet.Named("Team", kinds: [SeatKinds.InOrganization],
            starters: [new("engineer", [ProjectPermissions.View, ProjectPermissions.Edit])]),
        RoleSet.Named("Stakeholders", kinds: [HarborSeatKinds.Stakeholder],
            starters: [new("client-contact", [ProjectPermissions.View])],
            membersManagedWith: ProjectPermissions.StakeholdersManage),
    ],
    managedWith: ProjectPermissions.ManageRoles)
```

In Harbor Works, Rhea is an employee, so she is in Team. Mira, a client's site manager, is a stakeholder, so she is in Stakeholders. A seat that holds `projects.stakeholders.manage` on a project adds Mira. The set follows from the kind, so an add never names a set, and a member row stores no set.

Rules that declare no sets have one set, with no name, for the kinds inside the organization, holding the roles the rules declare.

| Member | Summary |
|---|---|
| `MaxNameLength = 64` | The longest name a set may have. |
| `string? Name { get; }` | The set's name, such as `Stakeholders`; `null` for the one set of rules that declare none. |
| `string Code { get; }` | The name as a code is written, such as `stakeholders`: converted as `[Members]` converts a name. Empty for the one set of rules that declare none. |
| `string? Stored { get; }` | What a role row of this set stores: `null` for the first set, and [`Code`](#roleset) for every other. |
| `IReadOnlyList<string> Kinds { get; }` | The kinds of seat in the set: kinds by their value, and the groups `SeatKinds.InOrganization` and `SeatKinds.OutsideOrganization`. |
| `IReadOnlyList<DeclaredRole> Declared { get; }` | The roles declared in the set: its starter roles, where a customer makes the roles, or its fixed roles. |
| `string? MembersManagedWith { get; }` | The permission that manages the members of this set, or `null` for the first set, which is managed with the permission marked `ManagesMembers` ([`MembershipRules.MembersManagedWith`](#membersmanagedwithroleset)). |
| `bool HoldsInside { get; }` | Whether the set lists `SeatKinds.InOrganization`: every kind inside the organization is in it. |
| `bool HoldsOutside { get; }` | Whether the set lists `SeatKinds.OutsideOrganization`: every kind outside the organization is in it. |
| `IReadOnlyList<string> KindValues { get; }` | The kinds the set names by their value, such as `stakeholder`, without the two groups. |
| [`Named(string, IReadOnlyList<string>, IReadOnlyList<DeclaredRole>?, IReadOnlyList<DeclaredRole>?, string?)`](#namedstring-ireadonlyliststring-ireadonlylistdeclaredrole-ireadonlylistdeclaredrole-string) | A set of roles for the members of the kinds named. |
| [`Holds(MemberKind)`](#holdsmemberkind) | Whether the set holds `kind`, by its value or by the group of its side. |

#### `Named(string, IReadOnlyList<string>, IReadOnlyList<DeclaredRole>?, IReadOnlyList<DeclaredRole>?, string?)`

```csharp
public static RoleSet Named(
    string name,
    IReadOnlyList<string> kinds,
    IReadOnlyList<DeclaredRole>? starters = null,
    IReadOnlyList<DeclaredRole>? roles = null,
    string? membersManagedWith = null)
```

A set of roles for the members of the kinds named.

**Parameters**

- `name`: The set's name, such as `Stakeholders`: letters and digits, with `-` or `_` between words, at most [`MaxNameLength`](#roleset) characters.
- `kinds`: The kinds of seat in the set, at least one.
- `starters`: The starter roles of the set, where a customer makes the roles ([`Roles.Custom`](#customireadonlylistdeclaredrole-otherpermissions-string-ireadonlylistroleset)).
- `roles`: The roles of the set, where the roles are in code ([`Roles.Fixed`](#fixedireadonlylistdeclaredrole-ireadonlylistroleset)).
- `membersManagedWith`: The permission that manages the members of the set. Every set but the first names one. Its `Kinds` say which kinds of seat may hold it, kinds outside the organization included.

**Exceptions**

- `ArgumentException`: The name cannot be converted to a code or is too long, a kind entry is not one, an entry is listed twice, both `starters` and `roles` are given, or a role is declared wrongly.

#### `Holds(MemberKind)`

```csharp
public bool Holds(MemberKind kind)
```

Whether the set holds `kind`, by its value or by the group of its side.

**Parameters**

- `kind`: A member's kind.

## Permissions

Namespace `DTS.Membership.Access`.

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
    [On<Board>(InRoles = false, ManagesMembers = true, NamesOwner = true)]
    public const string ManageMembers = "boards.members.manage";
}
```

**Type parameters**

- `TResource`: The resource the permission is asked on.

**Remarks**

The package's generator collects every permission on a resource into its rules, `MembershipRules.For<Board>`, in the project that makes them, from the classes of that project and of the projects it references. So the C# check in front of a handler and the functions in the database answer from the same declaration. The defaults are what most permissions of a resource are: the owner holds it, a role can give it, nobody holds it by being a member alone, and it neither manages the members nor names the owner.

`TResource` is the aggregate root a member class names, `[Member<..., Board>]`; another class is DDD00093. Of the permissions on one resource, one at most is held by every member, one at most manages the members, and one at most names the owner (DDD00093).

| Member | Summary |
|---|---|
| [`Owner`](#owner) | Whether the resource's owner holds the permission by owning it: the member its `[Owner]` property names, with no role, from the moment the resource names it and for as long as it does. |
| [`EveryMember`](#everymember) | Whether every member holds the permission by being a member, whatever roles it holds, or none. |
| [`InRoles`](#inroles) | Whether a role can give the permission to a member. |
| [`ManagesMembers`](#managesmembers) | Whether this is the permission that manages the members: the one the resource's member service requires to add or remove a member and to give or take a role. |
| [`NamesOwner`](#namesowner) | Whether this is the permission that names the owner: the one the resource's member service requires to hand the resource to another owner. |
| [`FromAbove`](#fromabove) | Whether the permission reaches the resource from above, where its rules let a resource be reached so: held where the resource sits, or above it. |
| [`Kinds`](#kinds) | The kinds of seat that may hold the permission on the resource. |

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

#### `ManagesMembers`

```csharp
public bool ManagesMembers { get; set; }
```

Whether this is the permission that manages the members: the one the resource's member service requires to add or remove a member and to give or take a role. `false` unless said; one permission of a resource at most.

`ManagesMembers = true`: the service the generator writes for the resource's member class, `BoardMemberService`, requires it on the resource for each of those changes, and a resource that marks no permission so lets only system work change its members there. Whatever writes the database's policies adds a lock to both member tables as well, and a member row is written only by a caller that holds this permission on the resource, or the one that names the owner. Without it, whoever may change a resource in any way writes its member rows, a role for itself included, by a statement that never went through a command.

It is held by the kinds its [`Kinds`](#kinds) lists, as every permission is. A list that names a kind outside the organization lets a member of that kind manage the members, where its role gives the permission.

#### `NamesOwner`

```csharp
public bool NamesOwner { get; set; }
```

Whether this is the permission that names the owner: the one the resource's member service requires to hand the resource to another owner. `false` unless said; one permission of a resource at most.

`NamesOwner = true`: the member service requires it on the resource to name an owner, and a resource that marks no permission so lets only system work name one there. Whatever writes the database's policies pins the owner column as well, and a change of a resource leaves its owner as it was unless the caller holds this permission on it. The owner holds what it holds by owning, so whoever may write that column could make itself everything.

#### `FromAbove`

```csharp
public bool FromAbove { get; set; }
```

Whether the permission reaches the resource from above, where its rules let a resource be reached so: held where the resource sits, or above it. `true` unless said.

`FromAbove = false`: holding it where the resource sits gives nothing on the resource; it is held there by the owner, every member or a role alone. Not on the permission every member holds, which is the one that sees the resource from above.

#### `Kinds`

```csharp
public string[]? Kinds { get; set; }
```

The kinds of seat that may hold the permission on the resource. An entry is a kind by its value, such as `stakeholder`, or a group: `SeatKinds.InOrganization` for every kind inside the organization, and `SeatKinds.OutsideOrganization` for every kind outside it. The list is complete. Left out, it is `[SeatKinds.InOrganization]`.

A member holds the permission only when the kind on its member row is in the list, whatever its roles give. Reach from above gives it only to a caller whose own kind is in the list. The owner holds it only when the list holds `SeatKinds.InOrganization`, since an owner is inside the organization. An empty list, a blank entry or an entry twice is DDD11001.

A role on another resource may give the permission through `otherPermissions`. The rules of that resource read this list for it ([`MembershipRules.KindsOf`](#kindsofstring)), so a role at a place and reach from above to this resource hold a member to one list.

```csharp
[On<Project>(EveryMember = true, Kinds = [SeatKinds.InOrganization, HarborSeatKinds.Stakeholder])]
public const string View = "projects.view";
```

### `ResourcePermission`

```csharp
public sealed record ResourcePermission
```

One permission on a resource with members, as a constant marked [`OnAttribute<TResource>`](#onattributetresource) declares it: its permission, and how it is held there. The rules of the resource are made from every one of them ([`MembershipRules.Permissions`](#membershiprules)).

| Member | Summary |
|---|---|
| [`ResourcePermission(string, bool, bool, bool, bool, bool, bool, IReadOnlyList<string>?)`](#resourcepermissionstring-bool-bool-bool-bool-bool-bool-ireadonlyliststring) | The primary constructor: the type's summary says what it makes. |
| `string Name { get; init; }` | The permission's name, such as `boards.edit`. |
| `bool Owner { get; init; }` | Whether the owner holds it by owning the resource ([`OnAttribute.Owner`](#owner)). |
| `bool EveryMember { get; init; }` | Whether every member holds it by being one ([`OnAttribute.EveryMember`](#everymember)). |
| `bool InRoles { get; init; }` | Whether a role can give it ([`OnAttribute.InRoles`](#inroles)). |
| `bool ManagesMembers { get; init; }` | Whether the database holds the member rows to it ([`OnAttribute.ManagesMembers`](#managesmembers)). |
| `bool NamesOwner { get; init; }` | Whether the database holds the owner column to it ([`OnAttribute.NamesOwner`](#namesowner)). |
| `bool FromAbove { get; init; }` | Whether it reaches the resource from above ([`OnAttribute.FromAbove`](#fromabove)). |
| `IReadOnlyList<string>? Kinds { get; init; }` | The kinds of seat that may hold it ([`OnAttribute.Kinds`](#kinds)), or `null` for the kinds inside the organization. Two permissions compare their lists entry by entry. |

#### `ResourcePermission(string, bool, bool, bool, bool, bool, bool, IReadOnlyList<string>?)`

```csharp
public ResourcePermission(
    string Name,
    bool Owner = true,
    bool EveryMember = false,
    bool InRoles = true,
    bool ManagesMembers = false,
    bool NamesOwner = false,
    bool FromAbove = true,
    IReadOnlyList<string>? Kinds = null)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Name`: The permission's name, such as `boards.edit`.
- `Owner`: Whether the owner holds it by owning the resource ([`OnAttribute.Owner`](#owner)).
- `EveryMember`: Whether every member holds it by being one ([`OnAttribute.EveryMember`](#everymember)).
- `InRoles`: Whether a role can give it ([`OnAttribute.InRoles`](#inroles)).
- `ManagesMembers`: Whether the database holds the member rows to it ([`OnAttribute.ManagesMembers`](#managesmembers)).
- `NamesOwner`: Whether the database holds the owner column to it ([`OnAttribute.NamesOwner`](#namesowner)).
- `FromAbove`: Whether it reaches the resource from above ([`OnAttribute.FromAbove`](#fromabove)).
- `Kinds`: The kinds of seat that may hold it ([`OnAttribute.Kinds`](#kinds)), or `null` for the kinds inside the organization. Two permissions compare their lists entry by entry.

## Reach from above

Namespace `DTS.Membership.Access`.

### `PlacesWhereIHold`

```csharp
public sealed record PlacesWhereIHold
```

The function that answers on Postgres where the caller holds a permission at a place of the application's own. It answers for every resource that hangs at a place of that kind. The application marks it once for each kind of place of its own ([`PlacesWhereIHoldAttribute`](#placeswhereiholdattribute)), in any project the export sees:

```csharp
public static class DepotFunctions
{
    [PlacesWhereIHold]
    public static PlacesWhereIHold Bays { get; } = PlacesWhereIHold.Of<BayId>(new("depot", "bays_where_i_hold"));
}
```

A crate marks `[At] public BayId BayId`, and its rules say `reachFromAbove: true`. The export then reaches crates from their bay through this function. A pallet that marks a bay as well is reached the same way, with nothing more written. A place of a package's own needs no mark: a package that keeps an organization answers its unit id itself.

The function takes the permission as text. It answers the places the caller's hold of that permission reaches: each place the permission is held at, and every place below. The resource's functions ask it, and they run as their owner, so the function answers for the caller by a condition of its own and never by a policy.

In C# the mark does nothing. C# reaches a resource from a place through an `IPlacesReached<TResourceId, TPlaceId>` registered with the resource. The start-up check holds the two to the same places: a database that reaches a resource from other places than C# does is refused.

| Member | Summary |
|---|---|
| [`PlacesWhereIHold(Type, DatabaseFunction)`](#placeswhereiholdtype-databasefunction) | The function for the places whose id is `place`. |
| `Type Place { get; }` | The kind of place: the type of the place's id, without `Nullable<T>`. |
| `DatabaseFunction Function { get; }` | The function that answers where the caller holds a permission at a place of that kind. |
| [`Of(DatabaseFunction)`](#ofdatabasefunction) | The function for the places whose id is `TPlaceId`. |

#### `PlacesWhereIHold(Type, DatabaseFunction)`

```csharp
public PlacesWhereIHold(Type place, DatabaseFunction function)
```

The function for the places whose id is `place`.

**Parameters**

- `place`: The kind of place: the type of the place's id, never `Nullable<T>`.
- `function`: The function that answers where the caller holds a permission at a place of that kind.

**Exceptions**

- `ArgumentNullException`: `place` or `function` is null.
- `ArgumentException`: `place` is a `Nullable<T>`.

#### `Of(DatabaseFunction)`

```csharp
public static PlacesWhereIHold Of<TPlaceId>(DatabaseFunction function)
    where TPlaceId : struct, IEntityId, IEquatable<TPlaceId>
```

The function for the places whose id is `TPlaceId`.

**Parameters**

- `function`: The function that answers where the caller holds a permission at a place of that kind.

**Type parameters**

- `TPlaceId`: The kind of place: the type of the place's id.

**Exceptions**

- `ArgumentNullException`: `function` is null.

### `PlacesWhereIHoldAttribute`

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class PlacesWhereIHoldAttribute : Attribute
```

Marks a static property of type [`PlacesWhereIHold`](#placeswhereihold), so the program that exports the database's policies reaches a resource from its place of that kind through the function it names. An application marks one for each kind of place of its own. A package that keeps places answers its own kind, and nothing is marked for it.

## Who a member is

Namespace `DTS.Membership.Access`.

### `MemberSource`

```csharp
public sealed record MemberSource
```

Where the caller's member id comes from: which fact about the caller is compared with the members of a resource. It is data, so the code that asks in C# and the functions that ask in the database read the same declaration.

It is one of three things a resource's rules say apart from each other: who a member is, which is this; where the roles a member holds come from ([`MembershipRules.Roles`](#membershiprules)); and whether something above the resource reaches it ([`MembershipRules.ReachFromAbove`](#reachfromabove)). Each stands by itself, so any member source goes with any kind of roles, with reach from above or without.

Rules say none, usually: who a member is follows from what a member is known by, the second type of `[Member<...>]` ([`Inferred`](#inferred)). The others are for what that cannot tell.

| Member | Summary |
|---|---|
| [`Inferred`](#inferred) | Who a member is follows from what it is known by, the member id of `[Member<TId, TMemberId, TRoleId, TResource>]`: the default, which rules that say no other source have. |
| `static MemberSource CurrentUserId { get; }` | The caller's user id, the `sub` of its token: for members that are users, known by an id over a `Guid`. What [`Inferred`](#inferred) takes for such an id, said outright. |
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

- The seat id of a package that keeps an organization's seats: the caller's seat. Such a package answers it, in C# through the `ICurrentMember` its join with this package registers for the resource, and in the database through the function its contribution names for its seat id, where the context that maps the resource maps that package's read model.
- An id over a `Guid` otherwise: the caller's user id, the `sub` of its token, as [`CurrentUserId`](#membersource).

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

In C# the application answers it, through an [`ICurrentMember<TResourceId, TMemberId>`](#icurrentmembertresourceid-tmemberid) it registers with the resource; a resource registered without one is refused, so a member is never silently read from the caller's own id. In a database that answers the questions itself, a function of the application's answers it, named here.

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
| `CurrentUserId = 1` | The caller's user id: [`MemberSource.CurrentUserId`](#membersource). |
| `Claim = 2` | A claim of the caller's token: [`MemberSource.Claim`](#claimstring). |
| `Resolved = 3` | An id the application resolves for the caller: [`MemberSource.Resolved`](#resolveddatabasefunction). |

### `ICurrentMember<TResourceId, TMemberId>`

```csharp
public interface ICurrentMember<TResourceId, TMemberId>
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
| [`Find(CurrentUserSnapshot)`](#findcurrentusersnapshot) | The member id `user` has, or `null` when it has none that counts now: somebody the application does not know, or knows and no longer counts. |
| [`KindOf(CurrentUserSnapshot)`](#kindofcurrentusersnapshot) | The kind of member `user` is, or `null` when it has no kind that counts now. |
| [`Require(CurrentUserSnapshot)`](#requirecurrentusersnapshot) | Refuses a caller the questions cannot be asked about at all, with the application's own refusal: somebody who is nobody in the organization the resources belong to. |

#### `Find(CurrentUserSnapshot)`

```csharp
TMemberId? Find(CurrentUserSnapshot user)
```

The member id `user` has, or `null` when it has none that counts now: somebody the application does not know, or knows and no longer counts. Such a caller is nobody's member, and holds only what reaches a resource from above. It refuses nobody.

**Parameters**

- `user`: The caller a question is asked for: a signed-in user.

#### `KindOf(CurrentUserSnapshot)`

```csharp
MemberKind? KindOf(CurrentUserSnapshot user)
```

The kind of member `user` is, or `null` when it has no kind that counts now. Reach from above gives the caller a permission only when this kind may hold it ([`MembershipRules.MayHold`](#mayholdstring-memberkind)). A caller without a kind holds nothing from above. Like [`Find`](#findcurrentusersnapshot), it answers from what the application knows of the caller, without reading anything. The join with a package that keeps seats answers the kind of the caller's seat. An implementation that leaves it out answers [`MemberKind.Member`](#memberkind) for every caller, a kind inside the organization.

**Parameters**

- `user`: The caller a question is asked for: a signed-in user.

#### `Require(CurrentUserSnapshot)`

```csharp
void Require(CurrentUserSnapshot user)
```

Refuses a caller the questions cannot be asked about at all, with the application's own refusal: somebody who is nobody in the organization the resources belong to. It is asked before anything is read, by the questions that refuse, about every caller that is not the application's own work. It returns for everybody unless the application says otherwise: a caller that is nobody's member then simply reaches nothing.

**Parameters**

- `user`: The caller a question is asked for.

**Exceptions**

- `RefusalException`: The caller is nobody the questions can be asked about.

### `MemberKind`

```csharp
public sealed record MemberKind
```

The kind of a member: the kind of seat it is, copied onto its member row when it is added. A permission on a resource lists the kinds that may hold it ([`OnAttribute.Kinds`](#kinds)), and a member is in the role set its kind is in ([`RoleSet`](#roleset)).

In Harbor Works, Rhea is an `employee`, inside the organization, and Mira, a client's site manager, is a `stakeholder`, outside it. Membership without a package that keeps seats has no kinds: every member is [`Member`](#memberkind).

| Member | Summary |
|---|---|
| `MaxLength = 64` | The longest value a kind may have, as a member row stores it. |
| [`MemberKind(string, bool)`](#memberkindstring-bool) | The primary constructor: the type's summary says what it makes. |
| `string Name { get; init; }` | The kind's value, as the seat stores it, such as `stakeholder`. |
| `bool InOrganization { get; init; }` | Whether the kind is inside the organization. |
| `static MemberKind Member { get; }` | The one kind of a member where nothing keeps kinds: `member`, inside the organization. |

#### `MemberKind(string, bool)`

```csharp
public MemberKind(string Name, bool InOrganization)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Name`: The kind's value, as the seat stores it, such as `stakeholder`.
- `InOrganization`: Whether the kind is inside the organization.

### `IMemberKinds<TResourceId, TMemberId>`

```csharp
public interface IMemberKinds<TResourceId, TMemberId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
```

The kind of the one to be made a member of one kind of resource, which the member service copies onto the member row. The join with a package that keeps seats answers it from the seat. Where nothing answers it, every member is [`MemberKind.Member`](#memberkind).

**Type parameters**

- `TResourceId`: The id of the kind of resource the members are of.
- `TMemberId`: What a member is known by.

| Member | Summary |
|---|---|
| [`OfAsync(TMemberId, CancellationToken)`](#ofasynctmemberid-cancellationtoken) | The kind of `member`, or `null` when it is not known. |

#### `OfAsync(TMemberId, CancellationToken)`

```csharp
ValueTask<MemberKind?> OfAsync(TMemberId member, CancellationToken cancellationToken)
```

The kind of `member`, or `null` when it is not known.

**Parameters**

- `member`: The one to be made a member.
- `cancellationToken`: Cancels the lookup.

### `MemberKindSource`

```csharp
public sealed record MemberKindSource
```

Where the database finds the kind of seat of the members of one member class: a function that answers one seat by its id, with its `Kind` and whether it is `InOrganization`. Where an application marks one ([`MemberKindSourceAttribute`](#memberkindsourceattribute)), the export holds the member rows to it: a member row carries its seat's kind, and the owner of a resource is a seat inside the organization. The functions read the caller's own kind from it as well. Reach from above gives the caller a permission only where that kind may hold it.

The join with a package that keeps seats writes the mark for every resource whose members are seats, with that package's function. An application writes none itself.

| Member | Summary |
|---|---|
| [`MemberKindSource(Type, DatabaseFunction)`](#memberkindsourcetype-databasefunction) | The primary constructor: the type's summary says what it makes. |
| `Type MemberClass { get; init; }` | The application's member class, whose rows the guard holds. |
| `DatabaseFunction Seat { get; init; }` | The function that answers one seat by its id, with the columns `Kind` and `InOrganization`, and no row for a seat the caller may not admit. It answers whatever seats the caller reads, so the guard admits a seat the caller cannot list. |

#### `MemberKindSource(Type, DatabaseFunction)`

```csharp
public MemberKindSource(Type MemberClass, DatabaseFunction Seat)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `MemberClass`: The application's member class, whose rows the guard holds.
- `Seat`: The function that answers one seat by its id, with the columns `Kind` and `InOrganization`, and no row for a seat the caller may not admit. It answers whatever seats the caller reads, so the guard admits a seat the caller cannot list.

### `MemberKindSourceAttribute`

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class MemberKindSourceAttribute : Attribute
```

Marks a static property of type [`MemberKindSource`](#memberkindsource), so the program that exports the database's policies writes the kind guard of that member class. The join with a package that keeps seats writes it. An application marks nothing itself.
