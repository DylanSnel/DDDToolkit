---
title: "Membership API: Use cases and access"
sidebar_label: Use cases and access
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Membership API: Use cases and access

What a handler asks before it changes the members, what a caller is shown of them, the starter roles of a resource whose customers make its roles, the access manager of one kind of resource, the requirements a request states, and the contracts that publish a resource's access to row access rules. The access manager is asked for by the resource's class, `MembershipAccessManager<Board>`. The admission and the requirements are asked for by the resource's id. So those of documents never answer for folders. Their registration is with Entity Framework, under [Registration](entity-framework.md#registration).

## Admission

Namespace `DDDToolkit.Supporting.Membership.UseCases`.

### `MemberAdmission<TResourceId, TMemberId, TRoleId>`

```csharp
public sealed class MemberAdmission<TResourceId, TMemberId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

What a use case asks before it changes the members of one kind of resource, where the answer is not the member list's to give: whether the one to be made a member is known, whether a role is one there is for this resource. A handler asks, and passes the answer to the aggregate:

```csharp
await admission.RequireMemberAsync(command.With, cancellationToken);
await admission.RequireRoleAsync(command.Role, cancellationToken);
document.ShareWith(command.With, command.Role, MemberPeriod.Between(clock.GetUtcNow(), command.Until), by);
```

It asks what makes a change well formed, whoever makes it: a member that exists, a role that exists. It does not ask who may make the change. Who may add a member or give a role, which roles and for how long, is the application's to decide: by the permission its command requires, and by any rule of its own in the handler, in front of these.

One per kind of resource, asked for by the resource's id, and it refuses under that resource's codes. It keeps nothing between two questions.

**Type parameters**

- `TResourceId`: The id of the kind of resource the members are of.
- `TMemberId`: What a member is known by.
- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| [`MemberAdmission(MembershipRules, IMemberRoles<TResourceId, TRoleId>, IMemberDirectory<TResourceId, TMemberId>?, IMemberRolePolicy<TResourceId, TRoleId>?)`](#memberadmissionmembershiprules-imemberrolestresourceid-troleid-imemberdirectorytresourceid-tmemberid-imemberrolepolicytresourceid-troleid) | The admission of one kind of resource. |
| [`RequireMemberAsync(TMemberId, CancellationToken)`](#requirememberasynctmemberid-cancellationtoken) | Requires `member` to be somebody who may be made a member, or named owner: known and active, as the application's directory says. |
| [`RequireRoleAsync(TRoleId, CancellationToken)`](#requireroleasynctroleid-cancellationtoken) | Requires `role` to be one that goes to a member: a role there is for this resource, which the application's own rule, where it registered one ([`IMemberRolePolicy<TResourceId, TRoleId>`](#imemberrolepolicytresourceid-troleid)), does not keep off a member list. |

#### `MemberAdmission(MembershipRules, IMemberRoles<TResourceId, TRoleId>, IMemberDirectory<TResourceId, TMemberId>?, IMemberRolePolicy<TResourceId, TRoleId>?)`

```csharp
public MemberAdmission(
    MembershipRules rules,
    IMemberRoles<TResourceId, TRoleId> roles,
    IMemberDirectory<TResourceId, TMemberId>? directory = null,
    IMemberRolePolicy<TResourceId, TRoleId>? policy = null)
```

The admission of one kind of resource.

**Parameters**

- `rules`: The resource's rules, for the codes it refuses under.
- `roles`: The roles there are for its members.
- `directory`: Who may be made a member, or `null` when everybody may.
- `policy`: Which of the roles may go to a member, or `null` when every one may.

#### `RequireMemberAsync(TMemberId, CancellationToken)`

```csharp
public ValueTask RequireMemberAsync(TMemberId member, CancellationToken cancellationToken)
```

Requires `member` to be somebody who may be made a member, or named owner: known and active, as the application's directory says. Admitted without a question where there is no directory.

**Parameters**

- `member`: The one to be made a member.
- `cancellationToken`: Cancels the lookup.

**Exceptions**

- `RefusalException`: `member-not-active`, with the `Member`, under the resource's codes.

#### `RequireRoleAsync(TRoleId, CancellationToken)`

```csharp
public ValueTask RequireRoleAsync(TRoleId role, CancellationToken cancellationToken)
```

Requires `role` to be one that goes to a member: a role there is for this resource, which the application's own rule, where it registered one ([`IMemberRolePolicy<TResourceId, TRoleId>`](#imemberrolepolicytresourceid-troleid)), does not keep off a member list. What the role gives is not asked, and neither is who gives it.

**Parameters**

- `role`: The role to be given.
- `cancellationToken`: Cancels the lookup.

**Exceptions**

- `RefusalException`: `role-not-for-members`, with the `Role`, under the resource's codes.

### `IMemberDirectory<TResourceId, TMemberId>`

```csharp
public interface IMemberDirectory<TResourceId, TMemberId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
```

Says whether somebody can be made a member of one kind of resource: whether the id is one the application knows, and whoever it stands for is active. An application implements it where it keeps its users, and the admission of that kind of resource asks it before a member is added or an owner named.

Optional. Without one every id is admitted: a resource whose members are users of an identity provider often has nowhere to ask.

It is asked for by the resource, so two kinds of resource whose members are known by the same id each have their own, or share one registered for both.

**Type parameters**

- `TResourceId`: The id of the kind of resource the members are of.
- `TMemberId`: What a member is known by.

| Member | Summary |
|---|---|
| [`IsActiveAsync(TMemberId, CancellationToken)`](#isactiveasynctmemberid-cancellationtoken) | Whether `member` is known and active, so that it may be made a member. |

#### `IsActiveAsync(TMemberId, CancellationToken)`

```csharp
ValueTask<bool> IsActiveAsync(TMemberId member, CancellationToken cancellationToken)
```

Whether `member` is known and active, so that it may be made a member.

**Parameters**

- `member`: The one to be made a member.
- `cancellationToken`: Cancels the lookup.

### `IMemberRolePolicy<TResourceId, TRoleId>`

```csharp
public interface IMemberRolePolicy<TResourceId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Says whether a role that exists may go to a member of one kind of resource: a rule of the application's own on top of the roles there are, such as roles an organization keeps for itself and never gives on a resource. The admission of that kind of resource asks it when a role is given; what was given once can always be taken back, so it is not asked when a role is taken.

Optional. Without one every role there is may go to a member. It is asked about the role alone: whether this caller may give it is for the application's command to decide, where the caller is known.

**Type parameters**

- `TResourceId`: The id of the kind of resource the members are of.
- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| [`MayHoldAsync(TRoleId, CancellationToken)`](#mayholdasynctroleid-cancellationtoken) | Whether `role` may be given to a member of the resource. |

#### `MayHoldAsync(TRoleId, CancellationToken)`

```csharp
ValueTask<bool> MayHoldAsync(TRoleId role, CancellationToken cancellationToken)
```

Whether `role` may be given to a member of the resource.

**Parameters**

- `role`: A role that exists.
- `cancellationToken`: Cancels the lookup.

### `IMemberRoles<TResourceId, TRoleId>`

```csharp
public interface IMemberRoles<TResourceId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

The roles there are for the members of one kind of resource, as the admission asks about them: whether a role exists.

Where the roles are fixed ([`Roles.Fixed`](rules.md#fixedireadonlylistdeclaredrole)), the package answers it from the rules ([`NamedRoles<TResourceId>`](#namedrolestresourceid)). Where a customer makes them ([`Roles.Custom`](rules.md#customireadonlylistdeclaredrole-otherpermissions)), the package that stores them answers it from their rows: a role in use. Where they are kept elsewhere ([`Roles.External`](rules.md#externaldatabasefunction-otherpermissions)), the roles an organization makes for itself say, the application answers it from there, registered with the resource.

**Type parameters**

- `TResourceId`: The id of the kind of resource the members are of.
- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| [`ExistsAsync(TRoleId, CancellationToken)`](#existsasynctroleid-cancellationtoken) | Whether `role` is a role in use that a member can be given. |

#### `ExistsAsync(TRoleId, CancellationToken)`

```csharp
ValueTask<bool> ExistsAsync(TRoleId role, CancellationToken cancellationToken)
```

Whether `role` is a role in use that a member can be given.

**Parameters**

- `role`: The role.
- `cancellationToken`: Cancels the lookup.

### `NamedRoles<TResourceId>`

```csharp
public sealed class NamedRoles<TResourceId> : IMemberRoles<TResourceId, NamedRole>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

The fixed roles a resource's rules declare, as the admission asks about them: for a resource whose members hold [`NamedRole`](resource-and-members.md#namedrole)s. It reads nothing but the rules.

**Type parameters**

- `TResourceId`: The id of the kind of resource the members are of.

| Member | Summary |
|---|---|
| [`NamedRoles(MembershipRules)`](#namedrolesmembershiprules) | The primary constructor: the type's summary says what it makes. |
| [`ExistsAsync(NamedRole, CancellationToken)`](#existsasyncnamedrole-cancellationtoken) | Whether `role` is a role in use that a member can be given. |

#### `NamedRoles(MembershipRules)`

```csharp
public NamedRoles(MembershipRules rules)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `rules`: The resource's rules.

#### `ExistsAsync(NamedRole, CancellationToken)`

```csharp
public ValueTask<bool> ExistsAsync(NamedRole role, CancellationToken cancellationToken)
```

Whether `role` is a role in use that a member can be given.

**Parameters**

- `role`: The role.
- `cancellationToken`: Cancels the lookup.

## Overview

Namespace `DDDToolkit.Supporting.Membership.UseCases`.

### `MemberOverviews`

```csharp
public static class MemberOverviews
```

What turns the members a query read into what a caller is shown, in the one order every answer lists members in. It reads nothing, so it costs no statement.

| Member | Summary |
|---|---|
| [`From(IEnumerable<MemberEntity<TId, TMemberId, TRoleId>>, TMemberId, DateTimeOffset)`](#fromienumerablememberentitytid-tmemberid-troleid-tmemberid-datetimeoffset) | The members of a resource: the owner first, then by when each membership starts, then by member, and each member's roles by when each starts, then by role. |

#### `From(IEnumerable<MemberEntity<TId, TMemberId, TRoleId>>, TMemberId, DateTimeOffset)`

```csharp
public static IReadOnlyList<MemberOverview<TMemberId, TRoleId>> From<TId, TMemberId, TRoleId>(
    IEnumerable<MemberEntity<TId, TMemberId, TRoleId>> members,
    TMemberId owner,
    DateTimeOffset now)
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>, IComparable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>, IComparable<TRoleId>
```

The members of a resource: the owner first, then by when each membership starts, then by member, and each member's roles by when each starts, then by role.

An order of the members' own facts, the same every time: the package has no names to order by, and a screen that shows names orders by them itself. An id over a text, such as a named role, is ordered by its characters and not by the language of whoever asks, so a list reads the same for every reader. A role is said to count now only when its own period applies and the membership it is held in does.

**Parameters**

- `members`: The members, as a query read them: the resource's own member collection.
- `owner`: The resource's owner.
- `now`: The moment a membership, and a role held in it, must apply at to count now.

**Type parameters**

- `TId`: The member class's own id.
- `TMemberId`: What a member is known by.
- `TRoleId`: What a role is known by.

### `MemberOverview<TMemberId, TRoleId>`

```csharp
public sealed record MemberOverview<TMemberId, TRoleId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

A member of a resource as a caller is shown it: who, for which period, and the roles it holds, each by its id.

**Type parameters**

- `TMemberId`: What a member is known by.
- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| [`MemberOverview(TMemberId, bool, DateTimeOffset, DateTimeOffset?, bool, IReadOnlyList<MemberRoleOverview<TRoleId>>)`](#memberoverviewtmemberid-bool-datetimeoffset-datetimeoffset-bool-ireadonlylistmemberroleoverviewtroleid) | The primary constructor: the type's summary says what it makes. |
| `TMemberId Member { get; init; }` | Who the member is. |
| `bool IsOwner { get; init; }` | Whether the member owns the resource. |
| `DateTimeOffset StartsAt { get; init; }` | When the membership starts to count. |
| `DateTimeOffset? EndsAt { get; init; }` | When it stops, or `null`. |
| `bool AppliesNow { get; init; }` | Whether it counts now. |
| `IReadOnlyList<MemberRoleOverview<TRoleId>> Roles { get; init; }` | The roles the member holds, each with its own period: by when each starts, then by role. |

#### `MemberOverview(TMemberId, bool, DateTimeOffset, DateTimeOffset?, bool, IReadOnlyList<MemberRoleOverview<TRoleId>>)`

```csharp
public MemberOverview(
    TMemberId Member,
    bool IsOwner,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool AppliesNow,
    IReadOnlyList<MemberRoleOverview<TRoleId>> Roles)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Member`: Who the member is.
- `IsOwner`: Whether the member owns the resource.
- `StartsAt`: When the membership starts to count.
- `EndsAt`: When it stops, or `null`.
- `AppliesNow`: Whether it counts now.
- `Roles`: The roles the member holds, each with its own period: by when each starts, then by role.

### `MemberRoleOverview<TRoleId>`

```csharp
public sealed record MemberRoleOverview<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

A role a member holds, by its id, with the period it holds it for.

**Type parameters**

- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| [`MemberRoleOverview(TRoleId, DateTimeOffset, DateTimeOffset?, bool)`](#memberroleoverviewtroleid-datetimeoffset-datetimeoffset-bool) | The primary constructor: the type's summary says what it makes. |
| `TRoleId Role { get; init; }` | The role. |
| `DateTimeOffset StartsAt { get; init; }` | When the role starts to count. |
| `DateTimeOffset? EndsAt { get; init; }` | When it stops, or `null`. |
| `bool AppliesNow { get; init; }` | Whether it counts now: its own period applies, and so does the membership it is held in. |

#### `MemberRoleOverview(TRoleId, DateTimeOffset, DateTimeOffset?, bool)`

```csharp
public MemberRoleOverview(
    TRoleId Role,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool AppliesNow)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Role`: The role.
- `StartsAt`: When the role starts to count.
- `EndsAt`: When it stops, or `null`.
- `AppliesNow`: Whether it counts now: its own period applies, and so does the membership it is held in.

## Starter roles

Namespace `DDDToolkit.Supporting.Membership.UseCases`.

### `StarterRoles`

```csharp
public static class StarterRoles
```

The first roles of a resource whose roles a customer makes ([`Roles.Custom`](rules.md#customireadonlylistdeclaredrole-otherpermissions)): the starter roles its rules declare, each made once into a row a customer can rename, give other permissions and archive.

Roles are kept in scopes the package does not know: one set for each customer of an application, or one for the whole of it. So the application says which scope it means by handing over the roles that scope has, and makes the ones that are missing with its own constructor, which sets what it added:

```csharp
var existing = await db.PlotRoles.Where(role => role.GardenId == garden).ToListAsync(cancellationToken);
foreach (var draft in StarterRoles.Missing(PlotMembership.Rules, existing))
{
    db.PlotRoles.Add(new PlotRole(PlotRoleId.Create(), garden, draft));
}
```

Asked again for the same scope, it answers nothing: a starter role is made once, and is told by what it was made from ([`CustomRoleAggregate.MadeFrom`](resource-and-members.md#customrole-customroleaggregatetroleid)), not by its name, so a role a customer renamed or archived is not made a second time. Two requests that ask at the same moment are kept apart by a unique index the application declares over its scope and that property.

| Member | Summary |
|---|---|
| [`Missing(MembershipRules, IEnumerable<CustomRoleAggregate<TRoleId>>)`](#missingmembershiprules-ienumerablecustomroleaggregatetroleid) | The starter roles a scope does not have yet, each as the draft its role is made from: its name as the rules declare it, the permissions the rules let it give, and the starter role it is, in the order the rules declare them. |

#### `Missing(MembershipRules, IEnumerable<CustomRoleAggregate<TRoleId>>)`

```csharp
public static IReadOnlyList<CustomRoleDraft> Missing<TRoleId>(
    MembershipRules rules,
    IEnumerable<CustomRoleAggregate<TRoleId>> rolesOfTheScope)
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

The starter roles a scope does not have yet, each as the draft its role is made from: its name as the rules declare it, the permissions the rules let it give, and the starter role it is, in the order the rules declare them.

**Parameters**

- `rules`: The rules of the resource the roles are of.
- `rolesOfTheScope`: Every role the scope has, archived ones included.

**Type parameters**

- `TRoleId`: The application's id of a role.

**Exceptions**

- `ArgumentNullException`: `rules` or `rolesOfTheScope` is null.
- `ArgumentException`: The rules do not say a customer makes the resource's roles.

## Access manager

Namespace `DDDToolkit.Supporting.Membership.Access`.

### `MembershipAccessManager`

```csharp
public static class MembershipAccessManager
```

Makes the access manager of one kind of resource over what a storage answers. A storage package calls it where it registers the resource; an application takes the manager it made, [`MembershipAccessManager<TResource>`](#membershipaccessmanagertresource).

| Member | Summary |
|---|---|
| `MostResources = 200` | The most resources one question about several is asked about. |
| [`Over(IMembershipStorage<TResource, TResourceId>)`](#overimembershipstoragetresource-tresourceid) | The access manager of `TResource`, over what `storage` answers. |

#### `Over(IMembershipStorage<TResource, TResourceId>)`

```csharp
public static MembershipAccessManager<TResource> Over<TResource, TResourceId>(IMembershipStorage<TResource, TResourceId> storage)
    where TResource : class, IEntity<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

The access manager of `TResource`, over what `storage` answers.

**Parameters**

- `storage`: What the storage answers about the resource, for the current caller.

**Type parameters**

- `TResource`: The resource's aggregate.
- `TResourceId`: The resource's id.

**Exceptions**

- `ArgumentNullException`: `storage` is null.

### `MembershipAccessManager<TResource>`

```csharp
public sealed class MembershipAccessManager<TResource>
    where TResource : class
```

What the current caller may do on the resources of one kind, such as `MembershipAccessManager<Board>`: whether it holds a permission on a board, how and until when, and which permissions it holds on several. Your code takes it from the container, as any service:

```csharp
public sealed class ArchiveBoardHandler(IBoardRepository boards, MembershipAccessManager<Board> boardAccess) : ICommandHandler<ArchiveBoard>
{
    public async ValueTask<Unit> Handle(ArchiveBoard command, CancellationToken cancellationToken)
    {
        var board = await boards.GetAsync(command.Board, cancellationToken);
        var hold = await boardAccess.HoldAsync(board, BoardPermissions.Edit, cancellationToken);
        // ...
    }
}
```

Each question takes the resource, and has an overload that takes its id, for a caller that has only the id, as a request does: `boardAccess.HoldAsync(command.Board, ...)` ([`MembershipAccessManagerExtensions`](#membershipaccessmanagerextensions)). An answer about a resource names it as it was asked about: the resource for the resource, the id for an id.

Which resources the caller holds a permission on is asked of the resource's own set, inside a statement of your own: `db.Boards.WhereIHold(BoardPermissions.View)`, which the storage package writes. [`Reach`](#reachstring) is the lower level under it, for a statement that reads more than which resources are held.

The order of what a caller is refused with is the same for every question: the caller's own refusal when it is nobody; then `not-found` for a resource it may not see, exactly as for one that does not exist, so nobody learns a resource is there; then `not-permitted`, naming the permission, for a resource it sees without holding the permission. All under the codes of the resource ([`MembershipRules.Codes`](rules.md#membershiprules)).

It answers what a caller holds and nothing more. Who may add a member, give a role or hand a resource on is the application's to decide, by requiring a permission of its choice on each of its commands, and by whatever further rule it writes over the answer. The storage package registers it for each kind of resource, per scope; every question is asked for the caller that is current when it is asked, so it follows every `Begin` made after it was handed out.

**Type parameters**

- `TResource`: The resource's aggregate: the application's class that holds the members.

| Member | Summary |
|---|---|
| `MembershipRules Rules { get; }` | The rules of the resource, with the codes it refuses under. |
| [`RequireCaller()`](#requirecaller) | Refuses a caller nothing can be answered for at all, with that caller's own refusal. |
| [`Reach(string)`](#reachstring) | The resources the caller reaches for `permission`, as a set a statement of your own takes: the lower level under `db.Boards.WhereIHold(permission)`, for a statement that reads how each resource is held, or one on another context than the request's. |
| [`ReachSeen()`](#reachseen) | The resources the caller sees, as a set a statement of your own takes: those it is a member of now, whatever roles it holds there; those it owns, from the moment the resource names it; and, where the rules let a resource be reached from above and name the permission every member holds ([`MembershipRules.EveryMemberPermission`](rules.md#membershiprules)), those that sit where the caller holds that permission, or below. |
| [`HoldAsync(TResource, string, CancellationToken)`](#holdasynctresource-string-cancellationtoken) | What the caller holds on `resource`, in one statement: `null` when the caller does not see it, which is also the answer for a resource that no longer exists; otherwise the resource, the version it is at, and how the caller holds `permission` on it and until when, if it does. |
| [`RequireAsync(TResource, string, CancellationToken)`](#requireasynctresource-string-cancellationtoken) | Requires the caller to hold `permission` on `resource`: what a command about one resource passes before it changes it. |
| [`ViaAsync(TResource, string, CancellationToken)`](#viaasynctresource-string-cancellationtoken) | How the caller holds `permission` on a resource it may see, or `null` when it does not hold it: for a query that answers with what its caller may do, where not holding the permission is an answer and not a refusal. |
| [`PermissionsOnAsync(IReadOnlyCollection<TResource>, IReadOnlyCollection<string>, CancellationToken)`](#permissionsonasyncireadonlycollectiontresource-ireadonlycollectionstring-cancellationtoken) | The permissions of `permissions` the caller holds on each of `resources` it sees, in one statement whatever their number, keyed by the resources that were given. |

#### `RequireCaller()`

```csharp
public void RequireCaller()
```

Refuses a caller nothing can be answered for at all, with that caller's own refusal. Where the caller's member id is its own id or a claim there is no such caller: one that is nobody's member simply reaches nothing, so this returns for everybody. Where the application resolves who a caller is as a member ([`MemberSource.Resolved`](rules.md#resolveddatabasefunction)), a caller the application refuses outright, somebody who is nobody in the organization the resources belong to, is refused here, before anything is read.

**Exceptions**

- `RefusalException`: The caller is nobody the questions can be asked about.

#### `Reach(string)`

```csharp
public MemberReach<TResource> Reach(string permission)
```

The resources the caller reaches for `permission`, as a set a statement of your own takes: the lower level under `db.Boards.WhereIHold(permission)`, for a statement that reads how each resource is held, or one on another context than the request's. Those it is a member of now with a role that gives the permission, for a permission that being a member gives those it is a member of at all, for a permission of the resource those it owns, and, where the rules let a resource be reached from above, those that sit where the caller holds the permission, or below. Every resource for the application's own work; none for a caller that is nobody.

A reach says where one permission is held, and nothing of the others. Through its members a resource is held only where it is seen. From above it is seen by the permission every member holds ([`MembershipRules.EveryMemberPermission`](rules.md#membershiprules)), so a caller that holds another permission there and not that one is within this reach without seeing the resource. [`HoldAsync`](#holdasynctresource-string-cancellationtoken) and [`PermissionsOnAsync`](#permissionsonasyncireadonlycollectiontresource-ireadonlycollectionstring-cancellationtoken) answer only for what the caller sees; a statement of your own that should do the same puts the reach for the permission that sees in front of this one.

**Parameters**

- `permission`: A permission.

#### `ReachSeen()`

```csharp
public MemberReach<TResource> ReachSeen()
```

The resources the caller sees, as a set a statement of your own takes: those it is a member of now, whatever roles it holds there; those it owns, from the moment the resource names it; and, where the rules let a resource be reached from above and name the permission every member holds ([`MembershipRules.EveryMemberPermission`](rules.md#membershiprules)), those that sit where the caller holds that permission, or below. What [`HoldAsync`](#holdasynctresource-string-cancellationtoken) and [`PermissionsOnAsync`](#permissionsonasyncireadonlycollectiontresource-ireadonlycollectionstring-cancellationtoken) answer about, and what a statement of your own puts in front of a reach for another permission so it answers about the same resources. Every resource for the application's own work.

#### `HoldAsync(TResource, string, CancellationToken)`

```csharp
public Task<MemberHold<TResource>?> HoldAsync(
    TResource resource,
    string permission,
    CancellationToken cancellationToken)
```

What the caller holds on `resource`, in one statement: `null` when the caller does not see it, which is also the answer for a resource that no longer exists; otherwise the resource, the version it is at, and how the caller holds `permission` on it and until when, if it does. It refuses nobody: a module builds its own answers on it.

**Parameters**

- `resource`: The resource.
- `permission`: A permission.
- `cancellationToken`: Cancels the query.

**Exceptions**

- `ArgumentNullException`: `resource` is null.

#### `RequireAsync(TResource, string, CancellationToken)`

```csharp
public Task<MemberHold<TResource>> RequireAsync(
    TResource resource,
    string permission,
    CancellationToken cancellationToken)
```

Requires the caller to hold `permission` on `resource`: what a command about one resource passes before it changes it. One statement.

**Parameters**

- `resource`: The resource.
- `permission`: The permission needed on it.
- `cancellationToken`: Cancels the query.

**Returns**: The resource, with the version it was checked at and how the permission is held.

**Exceptions**

- `RefusalException`: The caller's own refusal when it is nobody; `not-found` when it may not see the resource; `not-permitted`, with the `Permission`, when it sees the resource but does not hold the permission on it.

#### `ViaAsync(TResource, string, CancellationToken)`

```csharp
public Task<MemberVia?> ViaAsync(
    TResource resource,
    string permission,
    CancellationToken cancellationToken)
```

How the caller holds `permission` on a resource it may see, or `null` when it does not hold it: for a query that answers with what its caller may do, where not holding the permission is an answer and not a refusal. One statement.

**Parameters**

- `resource`: The resource.
- `permission`: A permission.
- `cancellationToken`: Cancels the query.

**Exceptions**

- `RefusalException`: The caller's own refusal when it is nobody; `not-found` when the caller may not see the resource.

#### `PermissionsOnAsync(IReadOnlyCollection<TResource>, IReadOnlyCollection<string>, CancellationToken)`

```csharp
public Task<IReadOnlyDictionary<TResource, IReadOnlySet<string>>> PermissionsOnAsync(
    IReadOnlyCollection<TResource> resources,
    IReadOnlyCollection<string> permissions,
    CancellationToken cancellationToken)
```

The permissions of `permissions` the caller holds on each of `resources` it sees, in one statement whatever their number, keyed by the resources that were given. A resource the caller does not see, or holds none of the permissions on, is not in the answer. What is answered is for showing: whether a command may run is asked again when it runs.

**Parameters**

- `resources`: The resources asked about, at most [`MembershipAccessManager.MostResources`](#membershipaccessmanager) different ones.
- `permissions`: Permissions.
- `cancellationToken`: Cancels the query.

**Exceptions**

- `ArgumentException`: More than [`MembershipAccessManager.MostResources`](#membershipaccessmanager) different resources are asked about.

### `MembershipAccessManagerExtensions`

```csharp
public static class MembershipAccessManagerExtensions
```

The questions of [`MembershipAccessManager<TResource>`](#membershipaccessmanagertresource) by the resource's id, for a caller that has only the id, as a request does. Each answers what the question by the resource answers, and names the resource by the id it was given:

```csharp
var hold = await boardAccess.HoldAsync(command.Board, BoardPermissions.Edit, cancellationToken);   // a MemberHold<BoardId>
```

The resource's id is inferred from the argument, and has to be the id the resource is known by.

| Member | Summary |
|---|---|
| [`HoldAsync(MembershipAccessManager<TResource>, TResourceId, string, CancellationToken)`](#holdasyncmembershipaccessmanagertresource-tresourceid-string-cancellationtoken) | What the caller holds on the resource `resource` names, in one statement: `null` when the caller does not see it, which is also the answer for a resource that does not exist; otherwise its id, the version it is at, and how the caller holds `permission` on it and until when, if it does. |
| [`RequireAsync(MembershipAccessManager<TResource>, TResourceId, string, CancellationToken)`](#requireasyncmembershipaccessmanagertresource-tresourceid-string-cancellationtoken) | Requires the caller to hold `permission` on the resource `resource` names: what a command or a query about one resource passes before its handler. |
| [`ViaAsync(MembershipAccessManager<TResource>, TResourceId, string, CancellationToken)`](#viaasyncmembershipaccessmanagertresource-tresourceid-string-cancellationtoken) | How the caller holds `permission` on the resource `resource` names, or `null` when it does not hold it. |
| [`PermissionsOnAsync(MembershipAccessManager<TResource>, IReadOnlyCollection<TResourceId>, IReadOnlyCollection<string>, CancellationToken)`](#permissionsonasyncmembershipaccessmanagertresource-ireadonlycollectiontresourceid-ireadonlycollectionstring-cancellationtoken) | The permissions of `permissions` the caller holds on each of the resources `resources` names that it sees, in one statement whatever their number, keyed by id. |

#### `HoldAsync(MembershipAccessManager<TResource>, TResourceId, string, CancellationToken)`

```csharp
public static Task<MemberHold<TResourceId>?> HoldAsync<TResource, TResourceId>(
    this MembershipAccessManager<TResource> access,
    TResourceId resource,
    string permission,
    CancellationToken cancellationToken)
    where TResource : class, IEntity<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

What the caller holds on the resource `resource` names, in one statement: `null` when the caller does not see it, which is also the answer for a resource that does not exist; otherwise its id, the version it is at, and how the caller holds `permission` on it and until when, if it does. It refuses nobody.

**Parameters**

- `access`: The access manager of the resource's kind.
- `resource`: The resource's id.
- `permission`: A permission.
- `cancellationToken`: Cancels the query.

**Type parameters**

- `TResource`: The resource's aggregate.
- `TResourceId`: The resource's id.

**Exceptions**

- `ArgumentNullException`: `access` is null.

#### `RequireAsync(MembershipAccessManager<TResource>, TResourceId, string, CancellationToken)`

```csharp
public static Task<MemberHold<TResourceId>> RequireAsync<TResource, TResourceId>(
    this MembershipAccessManager<TResource> access,
    TResourceId resource,
    string permission,
    CancellationToken cancellationToken)
    where TResource : class, IEntity<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

Requires the caller to hold `permission` on the resource `resource` names: what a command or a query about one resource passes before its handler. One statement.

**Parameters**

- `access`: The access manager of the resource's kind.
- `resource`: The resource's id.
- `permission`: The permission needed on it.
- `cancellationToken`: Cancels the query.

**Type parameters**

- `TResource`: The resource's aggregate.
- `TResourceId`: The resource's id.

**Returns**: The resource's id, with the version it was checked at and how the permission is held.

**Exceptions**

- `RefusalException`: The caller's own refusal when it is nobody; `not-found` when it may not see the resource, for a missing one too; `not-permitted`, with the `Permission`, when it sees the resource but does not hold the permission.

#### `ViaAsync(MembershipAccessManager<TResource>, TResourceId, string, CancellationToken)`

```csharp
public static Task<MemberVia?> ViaAsync<TResource, TResourceId>(
    this MembershipAccessManager<TResource> access,
    TResourceId resource,
    string permission,
    CancellationToken cancellationToken)
    where TResource : class, IEntity<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

How the caller holds `permission` on the resource `resource` names, or `null` when it does not hold it. One statement.

**Parameters**

- `access`: The access manager of the resource's kind.
- `resource`: The resource's id.
- `permission`: A permission.
- `cancellationToken`: Cancels the query.

**Type parameters**

- `TResource`: The resource's aggregate.
- `TResourceId`: The resource's id.

**Exceptions**

- `RefusalException`: The caller's own refusal when it is nobody; `not-found` when the caller may not see the resource.

#### `PermissionsOnAsync(MembershipAccessManager<TResource>, IReadOnlyCollection<TResourceId>, IReadOnlyCollection<string>, CancellationToken)`

```csharp
public static Task<IReadOnlyDictionary<TResourceId, IReadOnlySet<string>>> PermissionsOnAsync<TResource, TResourceId>(
    this MembershipAccessManager<TResource> access,
    IReadOnlyCollection<TResourceId> resources,
    IReadOnlyCollection<string> permissions,
    CancellationToken cancellationToken)
    where TResource : class, IEntity<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

The permissions of `permissions` the caller holds on each of the resources `resources` names that it sees, in one statement whatever their number, keyed by id. A resource the caller does not see, or holds none of the permissions on, is not in the answer, so asking about an id tells nothing about it.

**Parameters**

- `access`: The access manager of the resource's kind.
- `resources`: The resources' ids, at most [`MembershipAccessManager.MostResources`](#membershipaccessmanager) different ones.
- `permissions`: Permissions.
- `cancellationToken`: Cancels the query.

**Type parameters**

- `TResource`: The resource's aggregate.
- `TResourceId`: The resource's id.

**Exceptions**

- `ArgumentException`: More than [`MembershipAccessManager.MostResources`](#membershipaccessmanager) different resources are asked about. A use case that takes the ids from a request refuses its caller, under a code of its own, before it asks.

### `MemberHold<TResource>`

```csharp
public sealed record MemberHold<TResource>
    where TResource : notnull
```

What the access manager read of one resource the caller may see: how the caller holds the permission asked about and until when, and the version the resource was at. It names the resource as it was asked about: the resource itself where the question was given the resource (a `MemberHold<Board>`), its id where it was given an id (a `MemberHold<BoardId>`).

**Type parameters**

- `TResource`: The resource as it was asked about: the application's aggregate, or its id.

**Remarks**

[`MemberAccessCheck<TResource, TResourceId>`](#memberaccesschecktresource-tresourceid) keeps the one it read for a request, by the resource's id, in `Checked<T>`, for the expert hold: a context that asks for it holds every save of the resource to [`Version`](#memberholdtresource). A handler on the default path takes nothing from there. A rule of its own that reads [`Via`](#memberholdtresource) or [`Until`](#until) asks the access manager for a hold ([`MembershipAccessManager.HoldAsync`](#holdasynctresource-string-cancellationtoken)).

| Member | Summary |
|---|---|
| [`MemberHold(TResource, MemberVia?, long, DateTimeOffset?)`](#memberholdtresource-membervia-long-datetimeoffset) | The primary constructor: the type's summary says what it makes. |
| `TResource Resource { get; init; }` | The resource that was read, as it was asked about: the resource, or its id. |
| `MemberVia? Via { get; init; }` | How the caller holds the permission on it, or `null` when the caller sees the resource without holding the permission. So never `null` on a hold the permission's requirement let through ([`MemberAccess.On`](#memberaccesson)), and `null` on one the caller's own way through let through ([`MemberAccess.Sees`](#memberaccesssees)) where the caller does not hold the permission as well. |
| `long Version { get; init; }` | The resource's version when it was read. Under the expert hold a save of the resource at another version is a lost race, so what is changed is what was checked. |
| [`Until`](#until) | The first moment the caller no longer holds the permission on the resource, as its membership and its roles stand now, or `null` when the hold has no end: so for the application's own work, and for a permission the resource's owner holds by owning it, the permissions marked `Owner` ([`MembershipRules.OwnerPermissions`](rules.md#membershiprules)). |

#### `MemberHold(TResource, MemberVia?, long, DateTimeOffset?)`

```csharp
public MemberHold(TResource Resource, MemberVia? Via, long Version, DateTimeOffset? Until)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Resource`: The resource that was read, as it was asked about: the resource, or its id.
- `Via`: How the caller holds the permission on it, or `null` when the caller sees the resource without holding the permission. So never `null` on a hold the permission's requirement let through ([`MemberAccess.On`](#memberaccesson)), and `null` on one the caller's own way through let through ([`MemberAccess.Sees`](#memberaccesssees)) where the caller does not hold the permission as well.
- `Version`: The resource's version when it was read. Under the expert hold a save of the resource at another version is a lost race, so what is changed is what was checked.
- `Until`: The first moment the caller no longer holds the permission on the resource, as its membership and its roles stand now, or `null` when the hold has no end: so for the application's own work, and for a permission the resource's owner holds by owning it, the permissions marked `Owner` ([`MembershipRules.OwnerPermissions`](rules.md#membershiprules)). The owner holds no other permission by owning, and holds no role. A member holds a permission that being a member gives until its membership ends, and a permission a role gives until the last of its roles that give the permission now ends, or its membership if that ends sooner; a role that only starts later does not lengthen it. `null` as well when the permission is not held (`Via` is `null`).

  A permission held from above has no end the package knows: it is held where the application says, and the application knows until when. It is held on a resource for as long as the caller sees that resource. So it is `null` where the caller sees the resource from above as well, by holding the permission that sees there, whatever its membership and its roles say; and where the caller sees the resource as a member alone, it is the end of that membership, since the resource is not there for it after.

  The package refuses nothing by it. It is there for a rule of the application's own, such as that nobody gives a role for longer than they hold the permission they give it with.

**Remarks**

[`MemberAccessCheck<TResource, TResourceId>`](#memberaccesschecktresource-tresourceid) keeps the one it read for a request, by the resource's id, in `Checked<T>`, for the expert hold: a context that asks for it holds every save of the resource to [`Version`](#memberholdtresource). A handler on the default path takes nothing from there. A rule of its own that reads [`Via`](#memberholdtresource) or [`Until`](#until) asks the access manager for a hold ([`MembershipAccessManager.HoldAsync`](#holdasynctresource-string-cancellationtoken)).

#### `Until`

```csharp
public DateTimeOffset? Until { get; init; }
```

The first moment the caller no longer holds the permission on the resource, as its membership and its roles stand now, or `null` when the hold has no end: so for the application's own work, and for a permission the resource's owner holds by owning it, the permissions marked `Owner` ([`MembershipRules.OwnerPermissions`](rules.md#membershiprules)). The owner holds no other permission by owning, and holds no role. A member holds a permission that being a member gives until its membership ends, and a permission a role gives until the last of its roles that give the permission now ends, or its membership if that ends sooner; a role that only starts later does not lengthen it. `null` as well when the permission is not held (`Via` is `null`).

A permission held from above has no end the package knows: it is held where the application says, and the application knows until when. It is held on a resource for as long as the caller sees that resource. So it is `null` where the caller sees the resource from above as well, by holding the permission that sees there, whatever its membership and its roles say; and where the caller sees the resource as a member alone, it is the end of that membership, since the resource is not there for it after.

The package refuses nothing by it. It is there for a rule of the application's own, such as that nobody gives a role for longer than they hold the permission they give it with.

### `MemberVia`

```csharp
public enum MemberVia
```

How a caller holds a permission on a resource. An application that shows it has its own word for each, "staff" for its members say, and maps these at its edge.

| Member | Summary |
|---|---|
| `Members = 0` | Through the resource's members: the caller is a member now and holds a role there, now, that gives the permission; or the permission is one that being a member gives by itself; or the caller is the owner, who holds the permissions on the resource that its owner holds. |
| `Above = 1` | From above the resource: the caller holds the permission where the resource sits, or above it, and does not hold it as one of the resource's members, which comes first. Only a resource whose rules say it is reached from above ([`MembershipRules.ReachFromAbove`](rules.md#membershiprules)) is reached this way. |
| `System = 2` | The application's own work, which holds every permission. |

### `MemberReach<TResource>`

```csharp
public abstract class MemberReach<TResource>
    where TResource : class
```

Which resources of one kind the caller reaches for one permission, at one moment: the lower level under `db.Boards.WhereIHold(permission)`, for a statement that reads more than which resources are held, such as how each is held, or one made on another context than the one that asks.

It is made by the access manager of the resource ([`MembershipAccessManager.Reach`](#reachstring)), for the caller the request runs as, and the storage that manager reads through is what puts it into a query (`Within` and `Reached` of the Entity Framework package). What a query is filtered by is therefore always what the rules decided, and never a set somebody put together elsewhere: a storage takes only the reaches its own manager made. What is public here is what a use case reads of it.

**Type parameters**

- `TResource`: The resource's aggregate.

| Member | Summary |
|---|---|
| [`MemberReach(string, DateTimeOffset, bool)`](#memberreachstring-datetimeoffset-bool) | For the access manager of a storage. |
| [`MemberReach(DateTimeOffset, bool)`](#memberreachdatetimeoffset-bool) | For the access manager of a storage: the reach of what the caller sees, under rules that name no permission for seeing ([`MembershipRules.EveryMemberPermission`](rules.md#membershiprules)). |
| `string? Permission { get; }` | The permission the reach is for. `null` only for the reach of what the caller sees under rules that name no permission for seeing. |
| `DateTimeOffset Now { get; }` | The moment a membership, and a role held in it, must apply at to count. One moment for the whole statement, and the one an answer made from it compares periods with. |
| `bool Everything { get; }` | Whether the caller is the application's own work, which reaches every resource: a statement then leaves the filter out. |
| [`Via(bool, bool)`](#viabool-bool) | How a resource read with this reach is reached, from what the statement found. |

#### `MemberReach(string, DateTimeOffset, bool)`

```csharp
protected MemberReach(string permission, DateTimeOffset now, bool everything)
```

For the access manager of a storage.

**Parameters**

- `permission`: The permission the reach is for.
- `now`: The moment a membership, and a role held in it, must apply at.
- `everything`: Whether the caller reaches every resource: the application's own work.

#### `MemberReach(DateTimeOffset, bool)`

```csharp
protected MemberReach(DateTimeOffset now, bool everything)
```

For the access manager of a storage: the reach of what the caller sees, under rules that name no permission for seeing ([`MembershipRules.EveryMemberPermission`](rules.md#membershiprules)). A resource is seen by its members and its owner all the same, so there is such a reach, and no permission it is for.

**Parameters**

- `now`: The moment a membership must apply at.
- `everything`: Whether the caller reaches every resource: the application's own work.

#### `Via(bool, bool)`

```csharp
public MemberVia? Via(bool asMember, bool fromAbove = false)
```

How a resource read with this reach is reached, from what the statement found. The members come first: a member that also holds the permission from above reaches the resource as a member.

**Parameters**

- `asMember`: Whether the statement found the caller among the members, now, with what this reach asks of a membership.
- `fromAbove`: Whether the statement found the permission held where the resource sits, or above it.

**Returns**: How, or `null` when the resource is not within this reach.

### `IMembershipStorage<TResource, TResourceId>`

```csharp
public interface IMembershipStorage<TResource, TResourceId>
    where TResource : class, IEntity<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

What a storage package answers about one kind of resource, for the current caller: the part of the access manager that reads where the resources are kept. An application never takes it: it takes the access manager of the resource, [`MembershipAccessManager<TResource>`](#membershipaccessmanagertresource), which a storage package registers over this ([`MembershipAccessManager.Over<TResource, TResourceId>`](#overimembershipstoragetresource-tresourceid)).

A caller holds a permission on a resource through its members: it is a member now, and holds a role there, now, that gives the permission. A role counts only inside its membership, and only for the permissions a member's role can give ([`MembershipRules.RolePermissions`](rules.md#membershiprules)). Being a member gives one permission by itself, when the rules name one ([`MembershipRules.EveryMemberPermission`](rules.md#membershiprules)). The owner of a resource holds the permissions marked `Owner` ([`MembershipRules.OwnerPermissions`](rules.md#membershiprules)), by owning it, and nothing through a role, and the application's own work holds every permission: the application itself, and its work in a scope the rules name ([`MembershipRules.IsOwnWork`](rules.md#isownworkcurrentusersnapshot)). Where the rules say so ([`MembershipRules.ReachFromAbove`](rules.md#membershiprules)), a permission held where the resource sits, or above it, is held on the resource as well, by a caller that is no member of it. A resource the caller reaches in no way does not exist for it.

The storage answers, because whether a membership and a role apply now is compared where the rows are, inside one statement. What is refused, and in which order, is not the storage's: the access manager decides that over [`HoldAsync`](#holdasynctresourceid-string-cancellationtoken), the same for every storage.

**Type parameters**

- `TResource`: The resource's aggregate.
- `TResourceId`: The resource's id.

| Member | Summary |
|---|---|
| `MembershipRules Rules { get; }` | The rules of the resource, with the codes it refuses under. |
| [`RequireCaller()`](#requirecaller-1) | Refuses a caller nothing can be answered for at all, with that caller's own refusal. |
| [`Reach(string)`](#reachstring-1) | The resources the caller reaches for `permission`, as a set the storage puts into a statement: what [`MembershipAccessManager.Reach`](#reachstring) answers. |
| [`ReachSeen()`](#reachseen-1) | The resources the caller sees, as a set the storage puts into a statement: what [`MembershipAccessManager.ReachSeen`](#reachseen) answers. |
| [`HoldAsync(TResourceId, string, CancellationToken)`](#holdasynctresourceid-string-cancellationtoken) | What the caller holds on one resource, in one statement: `null` when the caller does not see the resource, which is also the answer for a resource that does not exist; otherwise the resource, its version, and how the caller holds `permission` on it and until when, if it does. |
| [`PermissionsOnAsync(IReadOnlyCollection<TResourceId>, IReadOnlyCollection<string>, CancellationToken)`](#permissionsonasyncireadonlycollectiontresourceid-ireadonlycollectionstring-cancellationtoken) | The permissions of `permissions` the caller holds on each of `resources` it sees, in one statement whatever their number. |

#### `RequireCaller()`

```csharp
void RequireCaller()
```

Refuses a caller nothing can be answered for at all, with that caller's own refusal. Where the caller's member id is its own id or a claim there is no such caller: one that is nobody's member simply reaches nothing, so this returns for everybody. Where the application resolves who a caller is as a member ([`MemberSource.Resolved`](rules.md#resolveddatabasefunction)), a caller the application refuses outright is refused here, before anything is read ([`ICallerMember.Require`](rules.md#requirecurrentusersnapshot)).

**Exceptions**

- `RefusalException`: The caller is nobody the questions can be asked about.

#### `Reach(string)`

```csharp
MemberReach<TResource> Reach(string permission)
```

The resources the caller reaches for `permission`, as a set the storage puts into a statement: what [`MembershipAccessManager.Reach`](#reachstring) answers.

**Parameters**

- `permission`: A permission.

#### `ReachSeen()`

```csharp
MemberReach<TResource> ReachSeen()
```

The resources the caller sees, as a set the storage puts into a statement: what [`MembershipAccessManager.ReachSeen`](#reachseen) answers.

#### `HoldAsync(TResourceId, string, CancellationToken)`

```csharp
Task<MemberHold<TResourceId>?> HoldAsync(
    TResourceId resource,
    string permission,
    CancellationToken cancellationToken)
```

What the caller holds on one resource, in one statement: `null` when the caller does not see the resource, which is also the answer for a resource that does not exist; otherwise the resource, its version, and how the caller holds `permission` on it and until when, if it does. It refuses nobody.

**Parameters**

- `resource`: The resource.
- `permission`: A permission.
- `cancellationToken`: Cancels the query.

#### `PermissionsOnAsync(IReadOnlyCollection<TResourceId>, IReadOnlyCollection<string>, CancellationToken)`

```csharp
Task<IReadOnlyDictionary<TResourceId, IReadOnlySet<string>>> PermissionsOnAsync(
    IReadOnlyCollection<TResourceId> resources,
    IReadOnlyCollection<string> permissions,
    CancellationToken cancellationToken)
```

The permissions of `permissions` the caller holds on each of `resources` it sees, in one statement whatever their number. A resource the caller does not see, or holds none of the permissions on, is not in the answer.

**Parameters**

- `resources`: The resources asked about, at most [`MembershipAccessManager.MostResources`](#membershipaccessmanager) different ones.
- `permissions`: Permissions.
- `cancellationToken`: Cancels the query.

**Exceptions**

- `ArgumentException`: More than [`MembershipAccessManager.MostResources`](#membershipaccessmanager) different resources are asked about.

## Requirements

Namespace `DDDToolkit.Supporting.Membership.Access`.

### `MemberAccess`

```csharp
public static class MemberAccess
```

What a request about a resource with members requires of its caller, said where the request is declared:

```csharp
public sealed record ShareDocument(DocumentId Document, UserId With, NamedRole Role, long? ExpectedVersion = null) : IDocumentsRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(DocumentPermissions.Share, Document, ExpectedVersion);
}
```

The cases are [`MemberAccess<TResourceId>`](#memberaccesstresourceid)'s, closed over the id of the resource the request is about, which is what tells the requirements of one kind of resource from those of another: [`MemberAccessCheck<TResource, TResourceId>`](#memberaccesschecktresource-tresourceid) decides the cases of its own resource and no other.

Where the requirement reads no value of the request but the permission, the resource and the version it carries, the request says it with attributes instead, and the toolkit's generator writes the same call:

```csharp
[RequiresPermission(DocumentPermissions.Share)]
public sealed record ShareDocument([OnResource] DocumentId Document, UserId With, NamedRole Role, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;

[SeenWith<DocumentId>(DocumentPermissions.View)]
public sealed record ListDocuments : IDocumentsRequest;
```

([`OnResourceAttribute`](#onresourceattribute), [`ExpectedVersionAttribute`](#expectedversionattribute), [`SeenWithAttribute<TResourceId>`](#seenwithattributetresourceid)). These methods stay for a requirement computed from the request's values.

Which permission a request requires is the application's to say, for the commands that change the members as for any other: the package has no permission of its own for adding a member, giving a role or handing a resource on. A command an application wants only some callers to run requires a permission those callers hold.

| Member | Summary |
|---|---|
| [`On(string, TResourceId, long?)`](#onstring-tresourceid-long) | The caller holds `permission` on `resource`, through its members or however else the resource's rules let a permission be held. |
| [`SeenWith(string)`](#seenwithstring) | The caller is shown only the resources it holds `permission` on. |
| [`Sees(string, TResourceId, long?)`](#seesstring-tresourceid-long) | The caller sees `resource`, whether or not it holds `permission` there: what a request about the caller's own on the resource requires of it, beside the caller's own, where the permission is the other way through. |

#### `On(string, TResourceId, long?)`

```csharp
public static On On<TResourceId>(
    string permission,
    TResourceId resource,
    long? expectedVersion = null)
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

The caller holds `permission` on `resource`, through its members or however else the resource's rules let a permission be held.

**Parameters**

- `permission`: The permission the request needs on the resource.
- `resource`: The resource, from the request.
- `expectedVersion`: The version of the resource the caller last read, for a command that says so.

**Type parameters**

- `TResourceId`: The resource's id.

**Exceptions**

- `ArgumentException`: `permission` is blank.

#### `SeenWith(string)`

```csharp
public static SeenWith SeenWith<TResourceId>(string permission)
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

The caller is shown only the resources it holds `permission` on. For queries only.

**Parameters**

- `permission`: The permission that decides what the query shows.

**Type parameters**

- `TResourceId`: The id of the kind of resource the query is about.

**Exceptions**

- `ArgumentException`: `permission` is blank.

#### `Sees(string, TResourceId, long?)`

```csharp
public static Sees Sees<TResourceId>(
    string permission,
    TResourceId resource,
    long? expectedVersion = null)
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

The caller sees `resource`, whether or not it holds `permission` there: what a request about the caller's own on the resource requires of it, beside the caller's own, where the permission is the other way through. `[OrOwn]` beside `[OnResource]` says it:

```csharp
[RequiresPermission(DocumentPermissions.Share)]                                    // take yourself off, or anybody with the permission
public sealed record LeaveDocument([OnResource] DocumentId Document, [OrOwn] UserId Member) : IDocumentsRequest;
```

is `AccessRequirement.Any(AccessRequirement.All(AccessRequirement.Own(request.Member), MemberAccess.Sees(DocumentPermissions.Share, request.Document)), MemberAccess.On(DocumentPermissions.Share, request.Document))`.

**Parameters**

- `permission`: The permission the other way through requires on the resource: asked about, and not required.
- `resource`: The resource, from the request.
- `expectedVersion`: The version of the resource the caller last read, for a command that says so.

**Type parameters**

- `TResourceId`: The resource's id.

**Exceptions**

- `ArgumentException`: `permission` is blank.

### `MemberAccess<TResourceId>`

```csharp
public abstract record MemberAccess<TResourceId> : AccessRequirement
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

The requirements about one kind of resource with members. A closed set: the constructor is private, so the cases below are all there are, and each is a record, so two requirements that say the same are equal and a test can hold every request to the one it is meant to declare.

What a requirement cannot say stays in the handler, in plain sight: whether the one to be made a member is active, which roles go to a member, a second permission a command's own rule asks for, a rule of the application's own about what its caller may give. A rule that needs how or until when the caller holds the permission asks for it ([`MembershipAccessManager.HoldAsync`](#holdasynctresource-string-cancellationtoken)).

**Type parameters**

- `TResourceId`: The resource's id.

| Member | Summary |
|---|---|
| `string Permission { get; }` | The permission the requirement is about. |

### `MemberAccess.On`

```csharp
public sealed record On : MemberAccess<TResourceId>
```

The permission is held by the caller on [`Resource`](#memberaccesson). A resource the caller may not see is `not-found`, exactly as one that does not exist; one it may see without holding the permission is `not-permitted`, naming the permission. Both under the resource's own codes.

| Member | Summary |
|---|---|
| `TResourceId Resource { get; }` | The resource, from the request. |
| `long? ExpectedVersion { get; }` | The version of the resource the caller last read, for a command that says so, or `null`. It is compared only once the caller is known to see the resource and to hold the permission, and any other version is a lost race (`ConcurrencyConflictException`). In that order, so a caller without access learns nothing from a version: whatever it sends, it is answered as if it had sent none. |

### `MemberAccess.SeenWith`

```csharp
public sealed record SeenWith : MemberAccess<TResourceId>
```

The caller is shown only what it holds the permission on. Nothing is refused for want of the permission: the query's own statement leaves out what the caller does not reach (`db.Boards.WhereIHold(permission)`), so a signed-in caller who reaches nothing gets an empty answer. A caller who did not sign in is refused, `not-permitted` naming the permission: it reaches nothing under any rules, and no statement is sent for it. For queries only; a command that declared it would change things unchecked.

### `MemberAccess.Sees`

```csharp
public sealed record Sees : MemberAccess<TResourceId>
```

The caller sees [`Resource`](#memberaccesssees), whether or not it holds [`MemberAccess.Permission`](#memberaccesstresourceid) there: what a request about the caller's own on a resource requires of the resource, beside `AccessRequirement.Own<TValue>`, where the permission on it is the alternative. A member who takes itself off a document sees the document, as every member does; it need not hold the permission that takes anybody off. A resource the caller may not see is `not-found`, exactly as one that does not exist, under the resource's own codes, and nothing else is refused.

**Remarks**

It reads what [`MemberAccess.On`](#memberaccesson) reads, in one statement, and keeps it the same way, a [`MemberHold<TResource>`](#memberholdtresource) with the request in hand, so the caller's own way through holds a save of the resource to the version read as the permission's way does, under the expert hold too, and compares the version the request names. The permission is asked about so that what is kept says whether the caller holds it as well: [`MemberHold.Via`](#memberholdtresource) is `null` where it does not. `[RequiresPermission(permission)]` with `[OnResource]` on the resource and `[OrOwn]` on the member is `Any(All(Own(member), Sees(permission, resource)), On(permission, resource))`.

| Member | Summary |
|---|---|
| `TResourceId Resource { get; }` | The resource, from the request. |
| `long? ExpectedVersion { get; }` | The version of the resource the caller last read, for a command that says so, or `null`. It is compared once the caller is known to see the resource, and any other version is a lost race (`ConcurrencyConflictException`), as for [`MemberAccess.On.ExpectedVersion`](#memberaccesson). |

### `OnResourceAttribute`

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
public sealed class OnResourceAttribute : Attribute
```

Marks the resource on which the permission of a request's `RequiresPermissionAttribute` is held, through its members or however else the resource's rules let a permission be held: what [`MemberAccess.On<TResourceId>`](#onstring-tresourceid-long) says, written on the member of the request it reads.

```csharp
[RequiresPermission(DocumentPermissions.Share)]
public sealed record ShareDocument([OnResource] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
```

is `MemberAccess.On(DocumentPermissions.Share, request.Document, request.ExpectedVersion)`.

**Remarks**

It goes on a positional parameter of a record as it is, or on a property, and `[property: OnResource]` is read as well. The member is the id of a resource with members: an aggregate root a class declared with `[Member<TId, TMemberId, TRoleId, TResource>]` names as its resource. On another id it is DDD00081, where the project sees such a class. [`ExpectedVersionAttribute`](#expectedversionattribute) beside it marks the version the caller read, for a command that names one.

The resource is what the request acts on, so `OrOwnAttribute` goes beside it: the request is about the caller's own there, or the caller holds the permission on the resource.

```csharp
[RequiresPermission(ProjectPermissionNames.ManageCrew)]                                    // take yourself off, or manage the crew
public sealed record LeaveCrew([OnResource] ProjectId Project, [OrOwn] SeatId Seat) : IProjectsRequest;
```

is `AccessRequirement.Any(AccessRequirement.All(AccessRequirement.Own(request.Seat), MemberAccess.Sees(ProjectPermissionNames.ManageCrew, request.Project)), MemberAccess.On(ProjectPermissionNames.ManageCrew, request.Project))`. Either way the project's check reads the project, a caller on its own needs only to see it, compares the version [`ExpectedVersionAttribute`](#expectedversionattribute) marks, and keeps what the expert hold holds the save to. The handler still finds the seat on that resource, and refuses one that is not: the caller's own lets it act on itself, not on a resource it has nothing to do with.

Where a permission on the resource locks the members ([`MembershipRules.MembersLockPermission`](rules.md#memberslockpermission)), a database that checks every row lets a member row change only for a caller that holds that permission, so a caller who takes itself off without it is refused there. The handler then saves the caller's own place as the application's own work, begun for that caller once its request is the one in hand (`RequestInHand`): the check decided it.

### `ExpectedVersionAttribute`

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
public sealed class ExpectedVersionAttribute : Attribute
```

Marks the version of the resource the caller last read, beside [`OnResourceAttribute`](#onresourceattribute): the third argument of [`MemberAccess.On<TResourceId>`](#onstring-tresourceid-long).

```csharp
[RequiresPermission(DocumentPermissions.Share)]
public sealed record ShareDocument([OnResource] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
```

is `MemberAccess.On(DocumentPermissions.Share, request.Document, request.ExpectedVersion)`, so the check compares it only once the caller may act, and a caller that may not is refused rather than told the resource moved on.

**Remarks**

It goes with [`OnResourceAttribute`](#onresourceattribute) and nowhere else: on a request that marks no resource it is DDD00080. The member is a `long?`, or a `long`, as the call's parameter takes; another type is DDD00081.

### `SeenWithAttribute<TResourceId>`

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class SeenWithAttribute<TResourceId> : Attribute
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

Says on a query that it shows the caller only the resources it holds `permission` on: what [`MemberAccess.SeenWith<TResourceId>`](#seenwithstring) says, written where the query is declared.

```csharp
[SeenWith<DocumentId>(DocumentPermissions.View)]
public sealed record ListDocuments : IQuery<IReadOnlyList<DocumentListing>>, IDocumentsRequest;
```

is `MemberAccess.SeenWith<DocumentId>(DocumentPermissions.View)`.

**Type parameters**

- `TResourceId`: The id of the kind of resource the query is about.

**Remarks**

For queries only. It refuses nobody for want of the permission: it lets a signed-in caller through, one that holds nothing too, and says what the query's own statement filters by, which is the check. On a message of the Mediator library that is no query, a command or a plain request, it would let the handler run unchecked, so there it is DDD00082.

| Member | Summary |
|---|---|
| [`SeenWithAttribute(string)`](#seenwithattributestring) | The primary constructor: the type's summary says what it makes. |
| `string Permission { get; }` | The permission that decides what the query shows. |

#### `SeenWithAttribute(string)`

```csharp
public SeenWithAttribute(string permission)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `permission`: The permission that decides what the query shows: a constant, one of the resource's rules.

**Remarks**

For queries only. It refuses nobody for want of the permission: it lets a signed-in caller through, one that holds nothing too, and says what the query's own statement filters by, which is the check. On a message of the Mediator library that is no query, a command or a plain request, it would let the handler run unchecked, so there it is DDD00082.

### `MemberAccessCheck<TResource, TResourceId>`

```csharp
public sealed class MemberAccessCheck<TResource, TResourceId> : IAccessCheck
    where TResource : class, IEntity<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
```

Decides the requirements about one kind of resource with members ([`MemberAccess<TResourceId>`](#memberaccesstresourceid)) before a handler runs. A module registers it for its request interface, once for each kind of resource its requests are about:

```csharp
services.AddAccessCheck<IDocumentsRequest, MemberAccessCheck<Document, DocumentId>>();
```

The order of what a caller is refused with is the same for every request: the caller's own refusal when it is nobody; then `not-found` for a resource it may not see; then `not-permitted` for a permission it does not hold; then, for a command that names the version its caller read, a lost race when the resource is at another one. Only then does the handler run, and whatever else it refuses comes after these.

A query that shows what a permission is held on ([`MemberAccess.SeenWith`](#memberaccessseenwith)) refuses nobody for want of the permission: its own statement leaves out what the caller does not reach. Only a caller who did not sign in is refused there, `not-permitted` naming the permission, after the caller's own refusal: under no rules is such a caller anybody's member or holds a permission from above, so it reaches nothing, and its query is not run at all. A database that checks every row may refuse such a caller outright, rather than answer nothing, so its statement is never sent.

The handler needs nothing of it. It loads the resource its request names, which is the one that was checked, with the version the caller named, `context.ExpectVersion(document, command.ExpectedVersion)`, so a resource changed since the caller read it is a lost race at the load as well as here; the save compares the version it loaded, the aggregate keeps its own rules, and a database that checks every row checks the write again. A rule that needs how or until when the caller holds the permission asks the access manager for it ([`MembershipAccessManager.HoldAsync`](#holdasynctresource-string-cancellationtoken)).

What it read it keeps all the same, as a [`MemberHold<TResource>`](#memberholdtresource) in `Checked<T>`, with the request in hand: that costs nothing, and it is what the expert hold holds a save to, where a context asks for it (`UseMemberHolds` of the Entity Framework package). Then a save that changes the resource is refused when it is at another version than the check read, and one that changes a resource no check of the request in hand read is refused outright. A request whose requirement names several resources, two documents of `AccessRequirement.All` say, has a hold kept for each, and each save is held to its own.

The caller's own way through a request about a resource ([`MemberAccess.Sees`](#memberaccesssees), which `[OrOwn]` beside `[OnResource]` writes) is decided the same way, without the permission: `not-found` for a resource the caller may not see, the version compared, and the hold kept.

It decides the cases of its own resource only. A module with two kinds of resource registers two, and a requirement about a kind nobody registered a check for stops the request rather than letting it through.

**Type parameters**

- `TResource`: The resource's aggregate, which a lost race names.
- `TResourceId`: The resource's id.

| Member | Summary |
|---|---|
| [`MemberAccessCheck(MembershipAccessManager<TResource>, Checked<MemberHold<TResourceId>>, ICurrentUser)`](#memberaccesscheckmembershipaccessmanagertresource-checkedmemberholdtresourceid-icurrentuser) | The primary constructor: the type's summary says what it makes. |
| [`Decides(AccessRequirement)`](#decidesaccessrequirement) | Whether `requirement` is one of this check's cases. |
| [`RequireAsync(AccessRequirement, IRequireAccess, CancellationToken)`](#requireasyncaccessrequirement-irequireaccess-cancellationtoken) | Holds the caller to `requirement`: returns when the caller meets it, and throws when it does not. |

#### `MemberAccessCheck(MembershipAccessManager<TResource>, Checked<MemberHold<TResourceId>>, ICurrentUser)`

```csharp
public MemberAccessCheck(
    MembershipAccessManager<TResource> access,
    Checked<MemberHold<TResourceId>> kept,
    ICurrentUser user)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `access`: The access manager of this kind of resource.
- `kept`: Where what was read of a request's resource is kept, for the expert hold.
- `user`: Who is calling: what tells a caller who did not sign in.

#### `Decides(AccessRequirement)`

```csharp
public bool Decides(AccessRequirement requirement)
```

Whether `requirement` is one of this check's cases. It reads nothing and refuses nobody: it is asked for every requirement of the requests the check is registered for, and by a test that holds every requirement a module declares to having a check.

**Parameters**

- `requirement`: What a request declared.

#### `RequireAsync(AccessRequirement, IRequireAccess, CancellationToken)`

```csharp
public ValueTask RequireAsync(
    AccessRequirement requirement,
    IRequireAccess request,
    CancellationToken cancellationToken)
```

Holds the caller to `requirement`: returns when the caller meets it, and throws when it does not. Called only for a requirement `IAccessCheck.Decides` said yes to.

**Parameters**

- `requirement`: What the request declared, read from it once.
- `request`: The very request being handled, which is what anything kept for its handler is kept under.
- `cancellationToken`: Stops the reading.

**Exceptions**

- `RefusalException`: The caller is not who the request requires.
- `ConcurrencyConflictException`: The resource is not at the version the request expects.
- `InvalidOperationException`: The requirement is not one of this check's cases.

## Resource access contracts

From `DDDToolkit.Abstractions`: how the module that owns a resource with members publishes the sets its access answers, for the row access rules of any module to ask by the resource's id.

Namespace `DDDToolkit.Abstractions.Attributes`.

### `ResourceAccessContractAttribute<TKey>`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ResourceAccessContractAttribute<TKey> : Attribute
```

Publishes one set the access to a resource answers, the resources the caller sees or those it holds a permission on, or the members of the resources it is a member of, for row access rules to ask by the resource's id, typed, with no function's name anywhere. Apply, in the contracts of the module that owns the resource, to an empty `static partial class`; the generator adds `Name` and `Ids(...)`:

```csharp
// Projects.Contracts
[ModuleContract]
[ResourceAccessContract<ProjectId>(ResourceAccessSet.Seen)]
public static partial class ProjectsISee;

[ModuleContract]
[ResourceAccessContract<ProjectId>(ResourceAccessSet.HeldOn)]
public static partial class ProjectsWhereIHold;

// The people on the projects the caller works on: their ids are the members', so the contract says which
[ModuleContract]
[ResourceAccessContract<ProjectId>(ResourceAccessSet.FellowMembers)]
public static partial class PeopleOnMyProjects
{
    public static partial AccessSet<SeatId> Ids();
}

// A rule of Inspections, and one of Projects' own, alike
public static bool Allows(Inspection inspection, ICurrentUser user) => ProjectsISee.Ids().Contains(inspection.ProjectId);
public static bool Allows(Project project, ICurrentUser user) => ProjectsWhereIHold.Ids(ProjectPermissionNames.Edit).Contains(project.Id);

// A read rule on the application's seat class
public static bool Allows(Seat seat, ICurrentUser user) => PeopleOnMyProjects.Ids().Contains(seat.Id);
```

**Type parameters**

- `TKey`: The id of the resource's aggregate: what the set holds, but for the members' set, and how the export finds the resource. One declared with `[EntityId<T>]`, or, in the project that declares the aggregate, the one the toolkit writes for an aggregate root declared with a value, `BoardId` for `[AggregateRoot<Guid>] public partial class Board`.

**Remarks**

Which SQL function answers is not the contract's to say. The function is written by the package that keeps the resource's access, under the name that package gives it: the Membership package writes the functions of a resource with members under the names of its rules, which are taken from the resource's name unless the rules keep names a database already has. The export finds the function that answers the set for the resource whose id is `TKey`, and writes the policy with its name. So the name is said in one place or in none, and a rule that asks a set no package answers is refused when its access file is written, naming the rule.

A rule of another module asks the contract, and so references neither the owning module's aggregate nor the package that answers: what the owning module publishes is that its projects are seen and held, and how is its own. Its own rules ask the same contract, so a question has one name in every module. And only the owning module declares one: a contract of an id whose assembly declares another module is DDD00038, so what a module publishes of its resources stays its own to say, and another module asks what it publishes.

The class may declare its question itself, to write its own documentation on it, and the generator implements it: `static partial AccessSet<ProjectId> Ids();` for [`ResourceAccessSet.Seen`](#resourceaccessset), and `static partial AccessSet<ProjectId> Ids(string permission);` for [`ResourceAccessSet.HeldOn`](#resourceaccessset). For [`ResourceAccessSet.FellowMembers`](#resourceaccessset) it declares it, always, with the id its members are known by: `static partial AccessSet<SeatId> Ids();`. Called in C#, `Ids` throws: only the database answers it.

| Member | Summary |
|---|---|
| [`ResourceAccessContractAttribute(ResourceAccessSet)`](#resourceaccesscontractattributeresourceaccessset) | The primary constructor: the type's summary says what it makes. |
| `ResourceAccessSet Set { get; }` | Which set the contract publishes. |

#### `ResourceAccessContractAttribute(ResourceAccessSet)`

```csharp
public ResourceAccessContractAttribute(ResourceAccessSet set)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `set`: Which set the contract publishes.

**Remarks**

Which SQL function answers is not the contract's to say. The function is written by the package that keeps the resource's access, under the name that package gives it: the Membership package writes the functions of a resource with members under the names of its rules, which are taken from the resource's name unless the rules keep names a database already has. The export finds the function that answers the set for the resource whose id is `TKey`, and writes the policy with its name. So the name is said in one place or in none, and a rule that asks a set no package answers is refused when its access file is written, naming the rule.

A rule of another module asks the contract, and so references neither the owning module's aggregate nor the package that answers: what the owning module publishes is that its projects are seen and held, and how is its own. Its own rules ask the same contract, so a question has one name in every module. And only the owning module declares one: a contract of an id whose assembly declares another module is DDD00038, so what a module publishes of its resources stays its own to say, and another module asks what it publishes.

The class may declare its question itself, to write its own documentation on it, and the generator implements it: `static partial AccessSet<ProjectId> Ids();` for [`ResourceAccessSet.Seen`](#resourceaccessset), and `static partial AccessSet<ProjectId> Ids(string permission);` for [`ResourceAccessSet.HeldOn`](#resourceaccessset). For [`ResourceAccessSet.FellowMembers`](#resourceaccessset) it declares it, always, with the id its members are known by: `static partial AccessSet<SeatId> Ids();`. Called in C#, `Ids` throws: only the database answers it.

### `ResourceAccessSet`

```csharp
public enum ResourceAccessSet
```

Which set a [`ResourceAccessContractAttribute<TKey>`](#resourceaccesscontractattributetkey) publishes: one of the questions the access to a resource answers for the caller of the connection, with the ids of the resources it answers yes for, or, for [`FellowMembers`](#resourceaccessset), with the ids of the members it finds.

**Remarks**

The package that keeps the resource's access writes the function that answers each, under whatever name it gives it, and says which set the function answers. The Membership package answers all three for a resource with members.

| Member | Summary |
|---|---|
| `Seen = 0` | The resources the caller sees, asked without arguments: `Ids()`. A resource with members is seen by its members, by its owner, and, where its rules let a permission held above reach it, by whoever holds the permission that sees it there. |
| `HeldOn = 1` | The resources the caller holds a permission on, asked with the permission: `Ids(string permission)`. On a resource with members a permission is held through a role, by being a member for the permission that being one gives, by owning the resource, or from above. |
| `FellowMembers = 2` | The members of every resource the caller is a member of now, the caller among them, asked without arguments: `Ids()`. Its ids are the members', not the resources': the people on the projects a seat works on, say, which a read rule on the application's seat class asks to let a seat read who it works with. The contract declares its `Ids` itself, with the id its members are known by, `static partial AccessSet<SeatId> Ids();`, since the resource's id does not say it. |
