---
title: "Membership API: Managers and access"
sidebar_label: Managers and access
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Membership API: Managers and access

The managers the generator writes for a resource's member class and for its role class, `BoardMemberManager` and `BoardRoleManager`, and what they take; what the application answers about who may be a member and which roles go to one; the access manager of one kind of resource, the requirements a request states, and the contracts that publish a resource's access to row access rules. A generated manager is named after your class and closed over your types: each section shows the package's class it derives from, with the methods it inherits. The access manager is asked for by the resource's class, `MembershipAccessManager<Board>`, and the requirements by the resource's id, so those of documents never answer for folders. Their registration is with Entity Framework, under [Registration](entity-framework.md#registration).

## Member manager

Namespace `DDDToolkit.Supporting.Membership.Managers`.

### `MemberManager<TResource, TResourceId, TMember, TId, TMemberId, TRoleId>`

```csharp
public class MemberManager<TResource, TResourceId, TMember, TId, TMemberId, TRoleId>
    where TResource : AggregateRoot<TResourceId>, IMembershipResource, IMembershipResource<TMember, TMemberId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

The members of one kind of resource: adding them, giving and taking their roles, removing them, leaving, naming another owner, opening a new resource, and listing who is on one. Your code takes it as `BoardMemberManager`, a class the package's generator writes for your member class, closed over your resource and its ids, and the container makes per scope.

```csharp
public sealed class AddMemberHandler(BoardMemberManager members) : ICommandHandler<AddMember, Board>
{
    public async ValueTask<Board> Handle(AddMember command, CancellationToken cancellationToken)
        => await members.AddAsync(command.Board, command.Seat, command.Role, until: null, cancellationToken);
}
```

Each change takes the resource, or its id. Both do the same: the manager loads the resource itself, tracked, and answers it as it changed and saved it. A member and a role are ids, since a member is somebody of another module and a role is a row of your role class or a name the rules declare. After the cancellation token comes the version the caller last read, for a command that names one: a resource at another version is a lost race.

**What a change checks, in order.** The caller's right first, as the access manager refuses: the caller's own refusal when it is nobody, `not-found` for a resource it may not see, `not-permitted` naming the permission. A change of the members needs the permission the rules mark `ManagesMembers`, and naming an owner the one marked `NamesOwner`; a resource that marks none lets only system work make the change. Where the request in hand was checked for that permission on that resource, the manager takes the check's answer and asks nothing again. Then whether the member and the role may go on the list: an active member where the application registered an [`IMemberDirectory<TResourceId, TMemberId>`](#imemberdirectorytresourceid-tmemberid), a role in use that goes to a member. Then the resource's guard, `OnChangingMembers()`, and the member list's rules, then the save.

**The caller's own place.** A caller that takes a role from itself, takes itself off, leaves, or hands on the resource it owns can lose the very right the save needs, where a database checks every row. Such a save runs as the resource's own work in its scope, for the caller ([`IOwnPlaceWork<TResourceId, TMemberId>`](#iownplaceworktresourceid-tmemberid)), when the request in hand is the one whose check read the resource and the unit of work holds nothing but the resource. Otherwise every save runs as the caller.

**What stays yours.** The resource, its fields and its constructor, which opens its member list. The hooks the generator declares on it: the guard, `OnChangingMembers()`, and the events, `OnMemberAdded`, `OnMemberRemoved`, `OnRoleGiven`, `OnRoleTaken` and `OnOwnerNamed`. A rule of your own, such as that nobody gives a role for longer than they hold the permission, asked with the access manager before the call.

**Type parameters**

- `TResource`: The resource's aggregate.
- `TResourceId`: The resource's id.
- `TMember`: The application's member class.
- `TId`: The member class's own id.
- `TMemberId`: What a member is known by.
- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| `MostResources = 200` | The most resources one list of several is asked about. |
| [`MemberManager(MembershipAccessManager<TResource>, IMemberStore<TResource, TResourceId, TMemberId>, ICurrentUser, TimeProvider, IMemberRoles<TResourceId, TRoleId>?, IMemberDirectory<TResourceId, TMemberId>?, IMemberRolePolicy<TResourceId, TRoleId>?, IOwnPlaceWork<TResourceId, TMemberId>?)`](#membermanagermembershipaccessmanagertresource-imemberstoretresource-tresourceid-tmemberid-icurrentuser-timeprovider-imemberrolestresourceid-troleid-imemberdirectorytresourceid-tmemberid-imemberrolepolicytresourceid-troleid-iownplaceworktresourceid-tmemberid) | The member manager of one kind of resource. |
| `MembershipRules Rules { get; }` | The rules of the resource, with the codes it refuses under. |
| [`OpenAsync(TResource, CancellationToken)`](#openasynctresource-cancellationtoken) | Opens a new resource: the owner its constructor put on its member list is admitted, and the resource is added and saved. |
| [`AddAsync(TResourceId, TMemberId, TRoleId?, DateTimeOffset?, CancellationToken, long?)`](#addasynctresourceid-tmemberid-troleid-datetimeoffset-cancellationtoken-long) | Adds a member, from now until `until` or for good, with `role` for the same period where one is given. |
| [`AddAsync(TResource, TMemberId, TRoleId?, DateTimeOffset?, CancellationToken, long?)`](#addasynctresource-tmemberid-troleid-datetimeoffset-cancellationtoken-long) | Adds a member, as [`AddAsync`](#addasynctresourceid-tmemberid-troleid-datetimeoffset-cancellationtoken-long) does with the resource's id. |
| [`GiveRoleAsync(TResourceId, TMemberId, TRoleId, DateTimeOffset?, CancellationToken, long?)`](#giveroleasynctresourceid-tmemberid-troleid-datetimeoffset-cancellationtoken-long) | Gives a member a role, from now until `until` or for good, next to whatever roles it holds. |
| [`GiveRoleAsync(TResource, TMemberId, TRoleId, DateTimeOffset?, CancellationToken, long?)`](#giveroleasynctresource-tmemberid-troleid-datetimeoffset-cancellationtoken-long) | Gives a member a role, as [`GiveRoleAsync`](#giveroleasynctresourceid-tmemberid-troleid-datetimeoffset-cancellationtoken-long) does with the resource's id. |
| [`TakeRoleAsync(TResourceId, TMemberId, TRoleId, CancellationToken, long?)`](#takeroleasynctresourceid-tmemberid-troleid-cancellationtoken-long) | Takes a role from a member, who stays a member with whatever else it holds. |
| [`TakeRoleAsync(TResource, TMemberId, TRoleId, CancellationToken, long?)`](#takeroleasynctresource-tmemberid-troleid-cancellationtoken-long) | Takes a role from a member, as [`TakeRoleAsync`](#takeroleasynctresourceid-tmemberid-troleid-cancellationtoken-long) does with the resource's id. |
| [`RemoveAsync(TResourceId, TMemberId, CancellationToken, long?)`](#removeasynctresourceid-tmemberid-cancellationtoken-long) | Takes a member off the resource, with every role it holds. |
| [`RemoveAsync(TResource, TMemberId, CancellationToken, long?)`](#removeasynctresource-tmemberid-cancellationtoken-long) | Takes a member off the resource, as [`RemoveAsync`](#removeasynctresourceid-tmemberid-cancellationtoken-long) does with the resource's id. |
| [`LeaveAsync(TResourceId, CancellationToken, long?)`](#leaveasynctresourceid-cancellationtoken-long) | Takes the caller off the resource, with every role it holds there. |
| [`LeaveAsync(TResource, CancellationToken, long?)`](#leaveasynctresource-cancellationtoken-long) | Takes the caller off the resource, as [`LeaveAsync`](#leaveasynctresourceid-cancellationtoken-long) does with the resource's id. |
| [`NameOwnerAsync(TResourceId, TMemberId, FormerOwner<TRoleId>, CancellationToken, long?)`](#nameownerasynctresourceid-tmemberid-formerownertroleid-cancellationtoken-long) | Names another owner: a member from now on and with no end, added when not a member yet, holding no role. |
| [`NameOwnerAsync(TResource, TMemberId, FormerOwner<TRoleId>, CancellationToken, long?)`](#nameownerasynctresource-tmemberid-formerownertroleid-cancellationtoken-long) | Names another owner, as [`NameOwnerAsync`](#nameownerasynctresourceid-tmemberid-formerownertroleid-cancellationtoken-long) does with the resource's id. |
| [`ListAsync(TResourceId, CancellationToken)`](#listasynctresourceid-cancellationtoken) | The members of a resource the caller sees, read for showing: the owner first, then by when each membership starts, then by member, and each member's roles by when each starts, then by role. |
| [`ListAsync(TResource, CancellationToken)`](#listasynctresource-cancellationtoken) | The members of a resource, as [`ListAsync`](#listasynctresourceid-cancellationtoken) answers them with the resource's id. |
| [`ListAsync(IReadOnlyCollection<TResourceId>, CancellationToken)`](#listasyncireadonlycollectiontresourceid-cancellationtoken) | The members of each of `resources` the caller sees, in one statement whatever their number, each list in the order [`ListAsync`](#listasynctresourceid-cancellationtoken) answers in. |

#### `MemberManager(MembershipAccessManager<TResource>, IMemberStore<TResource, TResourceId, TMemberId>, ICurrentUser, TimeProvider, IMemberRoles<TResourceId, TRoleId>?, IMemberDirectory<TResourceId, TMemberId>?, IMemberRolePolicy<TResourceId, TRoleId>?, IOwnPlaceWork<TResourceId, TMemberId>?)`

```csharp
public MemberManager(
    MembershipAccessManager<TResource> access,
    IMemberStore<TResource, TResourceId, TMemberId> store,
    ICurrentUser user,
    TimeProvider clock,
    IMemberRoles<TResourceId, TRoleId>? roles = null,
    IMemberDirectory<TResourceId, TMemberId>? directory = null,
    IMemberRolePolicy<TResourceId, TRoleId>? policy = null,
    IOwnPlaceWork<TResourceId, TMemberId>? ownPlace = null)
```

The member manager of one kind of resource. The container makes it; the generated class has the same constructor.

**Parameters**

- `access`: The resource's access manager, which asks what the caller holds.
- `store`: Where the resource is loaded and saved.
- `user`: Who is calling.
- `clock`: What "now" is.
- `roles`: Which roles there are; the rules answer it themselves where the roles are fixed.
- `directory`: Who may be made a member, or `null` when everybody may.
- `policy`: Which roles may go to a member, or `null` when every one may.
- `ownPlace`: How the save of a change to the caller's own place begins, or `null` for system work in the resource's scope.

#### `OpenAsync(TResource, CancellationToken)`

```csharp
public Task<TResource> OpenAsync(TResource resource, CancellationToken cancellationToken)
```

Opens a new resource: the owner its constructor put on its member list is admitted, and the resource is added and saved. Who may open one is the request's to say: there is no resource yet to hold a permission on.

**Parameters**

- `resource`: The resource, just made, with its member list opened in its constructor.
- `cancellationToken`: Cancels the work.

**Returns**: The resource, saved.

**Exceptions**

- `RefusalException`: `member-not-active` for an owner the application's directory does not admit.

**Example**

```csharp
var board = await members.OpenAsync(new Board(BoardId.Create(), command.Name, owner, clock.GetUtcNow()), cancellationToken);
```

#### `AddAsync(TResourceId, TMemberId, TRoleId?, DateTimeOffset?, CancellationToken, long?)`

```csharp
public Task<TResource> AddAsync(
    TResourceId resource,
    TMemberId member,
    TRoleId? role,
    DateTimeOffset? until,
    CancellationToken cancellationToken,
    long? expectedVersion = null)
```

Adds a member, from now until `until` or for good, with `role` for the same period where one is given. Who adds it is the caller, as a member of the resource.

**Parameters**

- `resource`: The resource.
- `member`: The member.
- `role`: The role it holds from the start, or `null` for none.
- `until`: When the membership ends, or `null` for no end.
- `cancellationToken`: Cancels the work.
- `expectedVersion`: The version of the resource the caller last read, or `null`.

**Returns**: The resource, with the member on it.

**Exceptions**

- `RefusalException`: `not-found`, `not-permitted` without the permission that manages the members, `member-not-active`, `role-not-for-members`, what the resource's guard refuses, `invalid-period` and `already-member`, under the resource's codes.
- `ConcurrencyConflictException`: The resource is not at `expectedVersion`, or changed while this was saved.

**Example**

```csharp
var board = await members.AddAsync(command.Board, command.Seat, command.Role, until: null, cancellationToken);
```

#### `AddAsync(TResource, TMemberId, TRoleId?, DateTimeOffset?, CancellationToken, long?)`

```csharp
public Task<TResource> AddAsync(
    TResource resource,
    TMemberId member,
    TRoleId? role,
    DateTimeOffset? until,
    CancellationToken cancellationToken,
    long? expectedVersion = null)
```

Adds a member, as [`AddAsync`](#addasynctresourceid-tmemberid-troleid-datetimeoffset-cancellationtoken-long) does with the resource's id.

**Parameters**

- `resource`: The resource.
- `member`: The member.
- `role`: The role it holds from the start, or `null` for none.
- `until`: When the membership ends, or `null` for no end.
- `cancellationToken`: Cancels the work.
- `expectedVersion`: The version of the resource the caller last read, or `null`.

**Returns**: The resource, with the member on it.

**Exceptions**

- `RefusalException`: `not-found`, `not-permitted` without the permission that manages the members, `member-not-active`, `role-not-for-members`, what the resource's guard refuses, `invalid-period` and `already-member`, under the resource's codes.
- `ConcurrencyConflictException`: The resource is not at `expectedVersion`, or changed while this was saved.

**Example**

```csharp
var board = await members.AddAsync(command.Board, command.Seat, command.Role, until: null, cancellationToken);
```

#### `GiveRoleAsync(TResourceId, TMemberId, TRoleId, DateTimeOffset?, CancellationToken, long?)`

```csharp
public Task<TResource> GiveRoleAsync(
    TResourceId resource,
    TMemberId member,
    TRoleId role,
    DateTimeOffset? until,
    CancellationToken cancellationToken,
    long? expectedVersion = null)
```

Gives a member a role, from now until `until` or for good, next to whatever roles it holds. Who gives it is the caller, as a member of the resource.

**Parameters**

- `resource`: The resource.
- `member`: The member.
- `role`: The role.
- `until`: When the role ends, or `null` for no end.
- `cancellationToken`: Cancels the work.
- `expectedVersion`: The version of the resource the caller last read, or `null`.

**Returns**: The resource, with the role given.

**Exceptions**

- `RefusalException`: `not-found`, `not-permitted` without the permission that manages the members, `role-not-for-members`, what the resource's guard refuses, `invalid-period`, `member-not-found`, `role-held`, and `owner-protected` for the owner, under the resource's codes.
- `ConcurrencyConflictException`: The resource is not at `expectedVersion`, or changed while this was saved.

#### `GiveRoleAsync(TResource, TMemberId, TRoleId, DateTimeOffset?, CancellationToken, long?)`

```csharp
public Task<TResource> GiveRoleAsync(
    TResource resource,
    TMemberId member,
    TRoleId role,
    DateTimeOffset? until,
    CancellationToken cancellationToken,
    long? expectedVersion = null)
```

Gives a member a role, as [`GiveRoleAsync`](#giveroleasynctresourceid-tmemberid-troleid-datetimeoffset-cancellationtoken-long) does with the resource's id.

**Parameters**

- `resource`: The resource.
- `member`: The member.
- `role`: The role.
- `until`: When the role ends, or `null` for no end.
- `cancellationToken`: Cancels the work.
- `expectedVersion`: The version of the resource the caller last read, or `null`.

**Returns**: The resource, with the role given.

**Exceptions**

- `RefusalException`: `not-found`, `not-permitted` without the permission that manages the members, `role-not-for-members`, what the resource's guard refuses, `invalid-period`, `member-not-found`, `role-held`, and `owner-protected` for the owner, under the resource's codes.
- `ConcurrencyConflictException`: The resource is not at `expectedVersion`, or changed while this was saved.

#### `TakeRoleAsync(TResourceId, TMemberId, TRoleId, CancellationToken, long?)`

```csharp
public Task<TResource> TakeRoleAsync(
    TResourceId resource,
    TMemberId member,
    TRoleId role,
    CancellationToken cancellationToken,
    long? expectedVersion = null)
```

Takes a role from a member, who stays a member with whatever else it holds. Any role can be taken, one that is no longer in use too. A role the caller takes from itself is saved as the resource's own work (see the class).

**Parameters**

- `resource`: The resource.
- `member`: The member.
- `role`: The role.
- `cancellationToken`: Cancels the work.
- `expectedVersion`: The version of the resource the caller last read, or `null`.

**Returns**: The resource, with the role taken.

**Exceptions**

- `RefusalException`: `not-found`, `not-permitted` without the permission that manages the members, what the resource's guard refuses, `member-not-found` and `role-not-held`, under the resource's codes.
- `ConcurrencyConflictException`: The resource is not at `expectedVersion`, or changed while this was saved.

#### `TakeRoleAsync(TResource, TMemberId, TRoleId, CancellationToken, long?)`

```csharp
public Task<TResource> TakeRoleAsync(
    TResource resource,
    TMemberId member,
    TRoleId role,
    CancellationToken cancellationToken,
    long? expectedVersion = null)
```

Takes a role from a member, as [`TakeRoleAsync`](#takeroleasynctresourceid-tmemberid-troleid-cancellationtoken-long) does with the resource's id.

**Parameters**

- `resource`: The resource.
- `member`: The member.
- `role`: The role.
- `cancellationToken`: Cancels the work.
- `expectedVersion`: The version of the resource the caller last read, or `null`.

**Returns**: The resource, with the role taken.

**Exceptions**

- `RefusalException`: `not-found`, `not-permitted` without the permission that manages the members, what the resource's guard refuses, `member-not-found` and `role-not-held`, under the resource's codes.
- `ConcurrencyConflictException`: The resource is not at `expectedVersion`, or changed while this was saved.

#### `RemoveAsync(TResourceId, TMemberId, CancellationToken, long?)`

```csharp
public Task<TResource> RemoveAsync(
    TResourceId resource,
    TMemberId member,
    CancellationToken cancellationToken,
    long? expectedVersion = null)
```

Takes a member off the resource, with every role it holds. The owner stays until somebody else is named owner. A caller that takes itself off is saved as the resource's own work (see the class).

**Parameters**

- `resource`: The resource.
- `member`: The member.
- `cancellationToken`: Cancels the work.
- `expectedVersion`: The version of the resource the caller last read, or `null`.

**Returns**: The resource, without the member.

**Exceptions**

- `RefusalException`: `not-found`, `not-permitted` without the permission that manages the members, what the resource's guard refuses, `member-not-found` and `owner-protected`, under the resource's codes.
- `ConcurrencyConflictException`: The resource is not at `expectedVersion`, or changed while this was saved.

#### `RemoveAsync(TResource, TMemberId, CancellationToken, long?)`

```csharp
public Task<TResource> RemoveAsync(
    TResource resource,
    TMemberId member,
    CancellationToken cancellationToken,
    long? expectedVersion = null)
```

Takes a member off the resource, as [`RemoveAsync`](#removeasynctresourceid-tmemberid-cancellationtoken-long) does with the resource's id.

**Parameters**

- `resource`: The resource.
- `member`: The member.
- `cancellationToken`: Cancels the work.
- `expectedVersion`: The version of the resource the caller last read, or `null`.

**Returns**: The resource, without the member.

**Exceptions**

- `RefusalException`: `not-found`, `not-permitted` without the permission that manages the members, what the resource's guard refuses, `member-not-found` and `owner-protected`, under the resource's codes.
- `ConcurrencyConflictException`: The resource is not at `expectedVersion`, or changed while this was saved.

#### `LeaveAsync(TResourceId, CancellationToken, long?)`

```csharp
public Task<TResource> LeaveAsync(
    TResourceId resource,
    CancellationToken cancellationToken,
    long? expectedVersion = null)
```

Takes the caller off the resource, with every role it holds there. It needs no permission: the caller sees the resource, is a member of it and is not its owner. Saved as the resource's own work (see the class).

**Parameters**

- `resource`: The resource.
- `cancellationToken`: Cancels the work.
- `expectedVersion`: The version of the resource the caller last read, or `null`.

**Returns**: The resource, without the caller.

**Exceptions**

- `RefusalException`: The caller's own refusal when it is nobody; `not-found` for a resource it does not see; what the resource's guard refuses; `member-not-found` for a caller that is no member; `owner-protected` for the owner. Under the resource's codes.
- `ConcurrencyConflictException`: The resource is not at `expectedVersion`, or changed while this was saved.

#### `LeaveAsync(TResource, CancellationToken, long?)`

```csharp
public Task<TResource> LeaveAsync(
    TResource resource,
    CancellationToken cancellationToken,
    long? expectedVersion = null)
```

Takes the caller off the resource, as [`LeaveAsync`](#leaveasynctresourceid-cancellationtoken-long) does with the resource's id.

**Parameters**

- `resource`: The resource.
- `cancellationToken`: Cancels the work.
- `expectedVersion`: The version of the resource the caller last read, or `null`.

**Returns**: The resource, without the caller.

**Exceptions**

- `RefusalException`: The caller's own refusal when it is nobody; `not-found` for a resource it does not see; what the resource's guard refuses; `member-not-found` for a caller that is no member; `owner-protected` for the owner. Under the resource's codes.
- `ConcurrencyConflictException`: The resource is not at `expectedVersion`, or changed while this was saved.

#### `NameOwnerAsync(TResourceId, TMemberId, FormerOwner<TRoleId>, CancellationToken, long?)`

```csharp
public Task<TResource> NameOwnerAsync(
    TResourceId resource,
    TMemberId owner,
    FormerOwner<TRoleId> formerOwner,
    CancellationToken cancellationToken,
    long? expectedVersion = null)
```

Names another owner: a member from now on and with no end, added when not a member yet, holding no role. The owner before becomes what `formerOwner` says: a member in a role, or nobody on the list. The resource's `OnOwnerNamed` hook hears everything that changed. An owner that hands on its own resource is saved as the resource's own work (see the class).

**Parameters**

- `resource`: The resource.
- `owner`: The member that is to own the resource.
- `formerOwner`: `FormerOwner.StaysAs(role)`, or `FormerOwner.Leaves`.
- `cancellationToken`: Cancels the work.
- `expectedVersion`: The version of the resource the caller last read, or `null`.

**Returns**: The resource, with its new owner.

**Exceptions**

- `RefusalException`: `not-found`, `not-permitted` without the permission that names the owner, `member-not-active` for the new owner, `role-not-for-members` for the role the owner before stays in, what the resource's guard refuses, and `already-owner`, under the resource's codes.
- `ConcurrencyConflictException`: The resource is not at `expectedVersion`, or changed while this was saved.

#### `NameOwnerAsync(TResource, TMemberId, FormerOwner<TRoleId>, CancellationToken, long?)`

```csharp
public Task<TResource> NameOwnerAsync(
    TResource resource,
    TMemberId owner,
    FormerOwner<TRoleId> formerOwner,
    CancellationToken cancellationToken,
    long? expectedVersion = null)
```

Names another owner, as [`NameOwnerAsync`](#nameownerasynctresourceid-tmemberid-formerownertroleid-cancellationtoken-long) does with the resource's id.

**Parameters**

- `resource`: The resource.
- `owner`: The member that is to own the resource.
- `formerOwner`: `FormerOwner.StaysAs(role)`, or `FormerOwner.Leaves`.
- `cancellationToken`: Cancels the work.
- `expectedVersion`: The version of the resource the caller last read, or `null`.

**Returns**: The resource, with its new owner.

**Exceptions**

- `RefusalException`: `not-found`, `not-permitted` without the permission that names the owner, `member-not-active` for the new owner, `role-not-for-members` for the role the owner before stays in, what the resource's guard refuses, and `already-owner`, under the resource's codes.
- `ConcurrencyConflictException`: The resource is not at `expectedVersion`, or changed while this was saved.

#### `ListAsync(TResourceId, CancellationToken)`

```csharp
public Task<IReadOnlyList<TMember>> ListAsync(
    TResourceId resource,
    CancellationToken cancellationToken)
```

The members of a resource the caller sees, read for showing: the owner first, then by when each membership starts, then by member, and each member's roles by when each starts, then by role. Members whose membership has ended, or is still to start, are in it too; `member.AppliesAt(now)` and `member.HoldsAt(role, now)` say what counts at a moment.

**Parameters**

- `resource`: The resource.
- `cancellationToken`: Cancels the query.

**Returns**: Its members, your member class, as they were read.

**Exceptions**

- `RefusalException`: The caller's own refusal when it is nobody; `not-found` for a resource it does not see.

#### `ListAsync(TResource, CancellationToken)`

```csharp
public Task<IReadOnlyList<TMember>> ListAsync(
    TResource resource,
    CancellationToken cancellationToken)
```

The members of a resource, as [`ListAsync`](#listasynctresourceid-cancellationtoken) answers them with the resource's id.

**Parameters**

- `resource`: The resource.
- `cancellationToken`: Cancels the query.

**Returns**: Its members, your member class, as they were read.

**Exceptions**

- `RefusalException`: The caller's own refusal when it is nobody; `not-found` for a resource it does not see.

#### `ListAsync(IReadOnlyCollection<TResourceId>, CancellationToken)`

```csharp
public Task<IReadOnlyDictionary<TResourceId, IReadOnlyList<TMember>>> ListAsync(
    IReadOnlyCollection<TResourceId> resources,
    CancellationToken cancellationToken)
```

The members of each of `resources` the caller sees, in one statement whatever their number, each list in the order [`ListAsync`](#listasynctresourceid-cancellationtoken) answers in. A resource the caller does not see is not in the answer.

**Parameters**

- `resources`: The resources, at most [`MostResources`](#membermanagertresource-tresourceid-tmember-tid-tmemberid-troleid) different ones.
- `cancellationToken`: Cancels the query.

**Returns**: The members of each resource the caller sees, by its id.

**Exceptions**

- `ArgumentException`: More than [`MostResources`](#membermanagertresource-tresourceid-tmember-tid-tmemberid-troleid) different resources are asked about.
- `RefusalException`: The caller's own refusal when it is nobody.

### `IMemberStore<TResource, TResourceId, TMemberId>`

```csharp
public interface IMemberStore<TResource, TResourceId, TMemberId>
    where TResource : class, IEntity<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
```

Where the member manager of one kind of resource loads, adds and saves the resource: the unit of work of the scope. A storage package implements it and registers it with the resource; an application never takes it.

**Type parameters**

- `TResource`: The resource's aggregate.
- `TResourceId`: The resource's id.
- `TMemberId`: What a member is known by.

| Member | Summary |
|---|---|
| `TMemberId? CurrentMember { get; }` | Who the current caller is as a member of the resource, as its rules say, or `null` for a caller that is nobody's member, system work among them: who a change is recorded as made by. |
| [`FindAsync(TResourceId, long?, CancellationToken)`](#findasynctresourceid-long-cancellationtoken) | The resource with its members, tracked by the unit of work for a change and its save; or `null` when it is not there for the caller. |
| [`ReadAsync(IReadOnlyCollection<TResourceId>, MemberReach<TResource>, CancellationToken)`](#readasyncireadonlycollectiontresourceid-memberreachtresource-cancellationtoken) | The resources of `resources` within `reach`, each with its members, read for showing: the unit of work does not track them. |
| [`Add(TResource)`](#addtresource) | Adds a new resource to the unit of work. |
| [`ChangedBesides(IReadOnlySet<object>)`](#changedbesidesireadonlysetobject) | What the unit of work would write besides `parts`: the name of each kind of thing it holds a change to that is none of them, or none. |
| [`SaveAsync(CancellationToken)`](#saveasynccancellationtoken) | Saves everything the unit of work holds, with the domain events raised, in one transaction. |

#### `FindAsync(TResourceId, long?, CancellationToken)`

```csharp
Task<TResource?> FindAsync(
    TResourceId resource,
    long? expectedVersion,
    CancellationToken cancellationToken)
```

The resource with its members, tracked by the unit of work for a change and its save; or `null` when it is not there for the caller.

**Parameters**

- `resource`: The resource.
- `expectedVersion`: The version the caller last read, or `null` to change the resource as it is now: a resource at another version is a lost race.
- `cancellationToken`: Cancels the query.

**Exceptions**

- `ConcurrencyConflictException`: The resource is not at `expectedVersion`.

#### `ReadAsync(IReadOnlyCollection<TResourceId>, MemberReach<TResource>, CancellationToken)`

```csharp
Task<IReadOnlyList<TResource>> ReadAsync(
    IReadOnlyCollection<TResourceId> resources,
    MemberReach<TResource> reach,
    CancellationToken cancellationToken)
```

The resources of `resources` within `reach`, each with its members, read for showing: the unit of work does not track them.

**Parameters**

- `resources`: The resources asked about.
- `reach`: The resources the caller sees, as the resource's access manager made it.
- `cancellationToken`: Cancels the query.

#### `Add(TResource)`

```csharp
void Add(TResource resource)
```

Adds a new resource to the unit of work.

**Parameters**

- `resource`: The resource, just made.

#### `ChangedBesides(IReadOnlySet<object>)`

```csharp
IReadOnlyList<string> ChangedBesides(IReadOnlySet<object> parts)
```

What the unit of work would write besides `parts`: the name of each kind of thing it holds a change to that is none of them, or none.

**Parameters**

- `parts`: The resource, its members and the roles they hold, each by reference.

#### `SaveAsync(CancellationToken)`

```csharp
Task SaveAsync(CancellationToken cancellationToken)
```

Saves everything the unit of work holds, with the domain events raised, in one transaction.

**Parameters**

- `cancellationToken`: Cancels the save.

**Exceptions**

- `ConcurrencyConflictException`: Somebody else saved the same resource first.

### `IOwnPlaceWork<TResourceId, TMemberId>`

```csharp
public interface IOwnPlaceWork<TResourceId, TMemberId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
```

Begins the resource's own work for the save of a change to the caller's own place on it: a role it takes from itself, itself taken off, leaving, or handing on the resource it owns. Such a change can take away the very right the save needs where a database checks every row, so the member manager saves it as the resource's own work in its scope, for the request in hand whose check read the resource, when the unit of work holds nothing else.

Optional. Without one the manager begins system work in that scope for the current user, `access.BeginSystem(scope)` on the core's `AccessManager`. For rules that name no scope it begins `access.BeginSystemBypassingRowAccess()`. The join with an organization's package registers one that begins it in the caller's organization, for the caller's seat.

**Type parameters**

- `TResourceId`: The id of the kind of resource the members are of.
- `TMemberId`: What a member is known by.

| Member | Summary |
|---|---|
| [`Begin(TMemberId, string?)`](#begintmemberid-string) | Begins the resource's own work in `scope`, for `member`, the caller. |

#### `Begin(TMemberId, string?)`

```csharp
IDisposable Begin(TMemberId member, string? scope)
```

Begins the resource's own work in `scope`, for `member`, the caller.

**Parameters**

- `member`: The caller, as a member of the resource.
- `scope`: The scope of the resource's own work, one its rules name; `null` for rules that name none, whose own work is the application's, past every row access rule.

**Returns**: What ends the work when disposed.

## Role manager

Namespace `DDDToolkit.Supporting.Membership.Managers`.

### `CustomRoleManager<TRole, TRoleId, TResource>`

```csharp
public class CustomRoleManager<TRole, TRoleId, TResource>
    where TRole : CustomRoleAggregate<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TResource : class
```

The roles a customer makes for one kind of resource: making them, the starter roles among them, renaming them, setting their permissions and archiving them, and reading them. Your code takes it as `BoardRoleManager`, a class the package's generator writes for your role class, closed over it and the resource, and the container makes per scope.

```csharp
public sealed class RenameBoardRoleHandler(BoardRoleManager roles) : ICommandHandler<RenameBoardRole, BoardRole>
{
    public async ValueTask<BoardRole> Handle(RenameBoardRole command, CancellationToken cancellationToken)
        => await roles.RenameAsync(command.Role, command.Name, command.Description, cancellationToken);
}
```

Each change takes the role, or its id; both load the role and answer it as it was changed and saved. The manager hands the role the resource's rules, so no handler does. Each change needs the permission the rules name for it ([`Roles.ManagedWith`](rules.md#roles-1)), held for the whole of what the roles are kept for: the manager asks it of what the application registered for that ([`ICustomRoleAccess<TResource>`](#icustomroleaccesstresource)), which the join with an organization's package answers for the whole organization. Where the rules name none, or nothing answers, only system work changes the roles.

**What stays yours.** The role class, its constructor and the fields it adds, such as the customer a role is of. Its events: the generator declares the partial methods `OnRenamed()`, `OnPermissionsSet(set)` and `OnArchived()` on it, and the role calls each after a change that changed something. A role class kept per a scope of your own, a garden say, passes that scope to the reads and to the starter roles: `scope: role => role.GardenId == garden`.

The reads answer the roles there are for the caller: what the context's own rules let through, the roles of the caller's organization for a class marked with the organization package's marker. They ask no permission.

**Type parameters**

- `TRole`: The application's role class.
- `TRoleId`: The role class's id.
- `TResource`: The resource the roles are of.

| Member | Summary |
|---|---|
| `MostIds = 200` | How many ids one question by id takes. |
| [`CustomRoleManager(ICustomRoleStore<TRole, TRoleId>, MembershipAccessManager<TResource>, ICurrentUser, ICustomRoleAccess<TResource>?)`](#customrolemanagericustomrolestoretrole-troleid-membershipaccessmanagertresource-icurrentuser-icustomroleaccesstresource) | The role manager of one role class. |
| `MembershipRules Rules { get; }` | The rules of the resource the roles are of: what a role's constructor takes. |
| [`CreateAsync(TRole, CancellationToken)`](#createasynctrole-cancellationtoken) | Adds a role made with your class's constructor, and saves it. |
| [`CreateStarterRolesAsync(Func<CustomRoleDraft, TRole>, CancellationToken, Expression<Func<TRole, bool>>?)`](#createstarterrolesasyncfunccustomroledraft-trole-cancellationtoken-expressionfunctrole-bool) | Makes the starter roles the rules declare that the roles there are for the caller do not have yet, each through `make`, and saves them. |
| [`RenameAsync(TRoleId, string, string?, CancellationToken)`](#renameasynctroleid-string-string-cancellationtoken) | Renames a role and says again what it is for. |
| [`RenameAsync(TRole, string, string?, CancellationToken)`](#renameasynctrole-string-string-cancellationtoken) | Renames a role, as [`RenameAsync`](#renameasynctroleid-string-string-cancellationtoken) does with its id. |
| [`SetPermissionsAsync(TRoleId, IReadOnlyCollection<string>, CancellationToken)`](#setpermissionsasynctroleid-ireadonlycollectionstring-cancellationtoken) | Sets the permissions a role gives, for every member that holds it. |
| [`SetPermissionsAsync(TRole, IReadOnlyCollection<string>, CancellationToken)`](#setpermissionsasynctrole-ireadonlycollectionstring-cancellationtoken) | Sets the permissions a role gives, as [`SetPermissionsAsync`](#setpermissionsasynctroleid-ireadonlycollectionstring-cancellationtoken) does with its id. |
| [`ArchiveAsync(TRoleId, CancellationToken)`](#archiveasynctroleid-cancellationtoken) | Archives a role: it stays on every member that holds it, gives nothing from now on, and is not given again. |
| [`ArchiveAsync(TRole, CancellationToken)`](#archiveasynctrole-cancellationtoken) | Archives a role, as [`ArchiveAsync`](#archiveasynctroleid-cancellationtoken) does with its id. |
| [`ListAsync(CancellationToken, Expression<Func<TRole, bool>>?)`](#listasynccancellationtoken-expressionfunctrole-bool) | Every role there is for the caller, archived ones included, by name: your class, whole, read for showing. |
| [`ByIdAsync(IReadOnlyCollection<TRoleId>, CancellationToken)`](#byidasyncireadonlycollectiontroleid-cancellationtoken) | The roles of `roles` there are for the caller, read for showing: for a list that names the roles its rows hold by id. |

#### `CustomRoleManager(ICustomRoleStore<TRole, TRoleId>, MembershipAccessManager<TResource>, ICurrentUser, ICustomRoleAccess<TResource>?)`

```csharp
public CustomRoleManager(
    ICustomRoleStore<TRole, TRoleId> store,
    MembershipAccessManager<TResource> access,
    ICurrentUser user,
    ICustomRoleAccess<TResource>? roleAccess = null)
```

The role manager of one role class. The container makes it; the generated class has the same constructor.

**Parameters**

- `store`: Where the roles are loaded and saved.
- `access`: The access manager of the resource the roles are of, for its rules.
- `user`: Who is calling.
- `roleAccess`: Whether the caller may change the roles, or `null` where only system work may.

#### `CreateAsync(TRole, CancellationToken)`

```csharp
public Task<TRole> CreateAsync(TRole role, CancellationToken cancellationToken)
```

Adds a role made with your class's constructor, and saves it.

**Parameters**

- `role`: The role, just made.
- `cancellationToken`: Cancels the work.

**Returns**: The role, saved.

**Exceptions**

- `RefusalException`: `not-permitted` without the permission that changes the roles; whatever your context refuses a second role of one name with.

**Example**

```csharp
var role = await roles.CreateAsync(new BoardRole(BoardRoleId.Create(), organization, new CustomRoleDraft(command.Name, command.Description, command.Permissions)), cancellationToken);
```

#### `CreateStarterRolesAsync(Func<CustomRoleDraft, TRole>, CancellationToken, Expression<Func<TRole, bool>>?)`

```csharp
public Task<IReadOnlyList<TRole>> CreateStarterRolesAsync(
    Func<CustomRoleDraft, TRole> make,
    CancellationToken cancellationToken,
    Expression<Func<TRole, bool>>? scope = null)
```

Makes the starter roles the rules declare that the roles there are for the caller do not have yet, each through `make`, and saves them. A starter role is told by what it was made from, so one a customer renamed or archived is not made again, and asking twice makes nothing the second time.

**Parameters**

- `make`: Makes a role of your class from a starter role's draft: its name, its permissions and the starter role it is made from.
- `cancellationToken`: Cancels the work.
- `scope`: The roles of one scope of your own, for a role class kept per that scope; left out, every role there is for the caller.

**Returns**: The roles it made, in the order the rules declare them; none when every one is there.

**Exceptions**

- `RefusalException`: `not-permitted` without the permission that changes the roles.

**Example**

```csharp
var made = await roles.CreateStarterRolesAsync(draft => new BoardRole(BoardRoleId.Create(), organization, draft), cancellationToken);
```

#### `RenameAsync(TRoleId, string, string?, CancellationToken)`

```csharp
public Task<TRole> RenameAsync(
    TRoleId role,
    string name,
    string? description,
    CancellationToken cancellationToken)
```

Renames a role and says again what it is for. The same name and description again change nothing.

**Parameters**

- `role`: The role.
- `name`: Its name, as it is to be.
- `description`: What it is for, as it is to be; `null` clears it.
- `cancellationToken`: Cancels the work.

**Returns**: The role, renamed.

**Exceptions**

- `RefusalException`: `not-permitted` without the permission that changes the roles; `role-not-found`, `role-archived`, `role-name-invalid`, under the resource's codes; whatever your context refuses a second role of one name with.

#### `RenameAsync(TRole, string, string?, CancellationToken)`

```csharp
public Task<TRole> RenameAsync(
    TRole role,
    string name,
    string? description,
    CancellationToken cancellationToken)
```

Renames a role, as [`RenameAsync`](#renameasynctroleid-string-string-cancellationtoken) does with its id.

**Parameters**

- `role`: The role.
- `name`: Its name, as it is to be.
- `description`: What it is for, as it is to be; `null` clears it.
- `cancellationToken`: Cancels the work.

**Returns**: The role, renamed.

**Exceptions**

- `RefusalException`: `not-permitted` without the permission that changes the roles; `role-not-found`, `role-archived`, `role-name-invalid`, under the resource's codes; whatever your context refuses a second role of one name with.

#### `SetPermissionsAsync(TRoleId, IReadOnlyCollection<string>, CancellationToken)`

```csharp
public Task<TRole> SetPermissionsAsync(
    TRoleId role,
    IReadOnlyCollection<string> permissions,
    CancellationToken cancellationToken)
```

Sets the permissions a role gives, for every member that holds it. The same permissions again change nothing.

**Parameters**

- `role`: The role.
- `permissions`: Every permission it gives from now on.
- `cancellationToken`: Cancels the work.

**Returns**: The role, with its new permissions.

**Exceptions**

- `RefusalException`: `not-permitted` without the permission that changes the roles; `role-not-found`, `role-archived`, `permission-not-for-members`, under the resource's codes.

#### `SetPermissionsAsync(TRole, IReadOnlyCollection<string>, CancellationToken)`

```csharp
public Task<TRole> SetPermissionsAsync(
    TRole role,
    IReadOnlyCollection<string> permissions,
    CancellationToken cancellationToken)
```

Sets the permissions a role gives, as [`SetPermissionsAsync`](#setpermissionsasynctroleid-ireadonlycollectionstring-cancellationtoken) does with its id.

**Parameters**

- `role`: The role.
- `permissions`: Every permission it gives from now on.
- `cancellationToken`: Cancels the work.

**Returns**: The role, with its new permissions.

**Exceptions**

- `RefusalException`: `not-permitted` without the permission that changes the roles; `role-not-found`, `role-archived`, `permission-not-for-members`, under the resource's codes.

#### `ArchiveAsync(TRoleId, CancellationToken)`

```csharp
public Task<TRole> ArchiveAsync(TRoleId role, CancellationToken cancellationToken)
```

Archives a role: it stays on every member that holds it, gives nothing from now on, and is not given again.

**Parameters**

- `role`: The role.
- `cancellationToken`: Cancels the work.

**Returns**: The role, archived.

**Exceptions**

- `RefusalException`: `not-permitted` without the permission that changes the roles; `role-not-found` and `role-archived`, under the resource's codes.

#### `ArchiveAsync(TRole, CancellationToken)`

```csharp
public Task<TRole> ArchiveAsync(TRole role, CancellationToken cancellationToken)
```

Archives a role, as [`ArchiveAsync`](#archiveasynctroleid-cancellationtoken) does with its id.

**Parameters**

- `role`: The role.
- `cancellationToken`: Cancels the work.

**Returns**: The role, archived.

**Exceptions**

- `RefusalException`: `not-permitted` without the permission that changes the roles; `role-not-found` and `role-archived`, under the resource's codes.

#### `ListAsync(CancellationToken, Expression<Func<TRole, bool>>?)`

```csharp
public Task<IReadOnlyList<TRole>> ListAsync(
    CancellationToken cancellationToken,
    Expression<Func<TRole, bool>>? scope = null)
```

Every role there is for the caller, archived ones included, by name: your class, whole, read for showing.

**Parameters**

- `cancellationToken`: Cancels the query.
- `scope`: The roles of one scope of your own; left out, every role there is for the caller.

#### `ByIdAsync(IReadOnlyCollection<TRoleId>, CancellationToken)`

```csharp
public Task<IReadOnlyList<TRole>> ByIdAsync(
    IReadOnlyCollection<TRoleId> roles,
    CancellationToken cancellationToken)
```

The roles of `roles` there are for the caller, read for showing: for a list that names the roles its rows hold by id. A role that is not there for the caller is not in the answer.

**Parameters**

- `roles`: The roles, at most [`MostIds`](#customrolemanagertrole-troleid-tresource) different ones.
- `cancellationToken`: Cancels the query.

**Exceptions**

- `ArgumentException`: More than [`MostIds`](#customrolemanagertrole-troleid-tresource) different roles are asked about.

### `ICustomRoleStore<TRole, TRoleId>`

```csharp
public interface ICustomRoleStore<TRole, TRoleId>
    where TRole : CustomRoleAggregate<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

Where the role manager of one role class loads, adds and saves roles: the unit of work of the scope, under the rules the application's context keeps its rows to, such as the customer a role is of. A storage package implements it and registers it with the resource the roles are of; an application never takes it.

**Type parameters**

- `TRole`: The application's role class.
- `TRoleId`: The role class's id.

| Member | Summary |
|---|---|
| [`FindAsync(TRoleId, CancellationToken)`](#findasynctroleid-cancellationtoken) | The role, tracked for a change and its save; or `null` when it is not there for the caller. |
| [`ListAsync(Expression<Func<TRole, bool>>?, bool, CancellationToken)`](#listasyncexpressionfunctrole-bool-bool-cancellationtoken) | The roles there are for the caller, archived ones included, within `scope` where one is given: tracked, for the starter roles made beside them, or read for showing. |
| [`ByIdAsync(IReadOnlyCollection<TRoleId>, CancellationToken)`](#byidasyncireadonlycollectiontroleid-cancellationtoken-1) | The roles of `roles` there are for the caller, read for showing. |
| [`Add(TRole)`](#addtrole) | Adds a new role to the unit of work. |
| [`SaveAsync(CancellationToken)`](#saveasynccancellationtoken-1) | Saves everything the unit of work holds, with the domain events raised, in one transaction. |

#### `FindAsync(TRoleId, CancellationToken)`

```csharp
Task<TRole?> FindAsync(TRoleId role, CancellationToken cancellationToken)
```

The role, tracked for a change and its save; or `null` when it is not there for the caller.

**Parameters**

- `role`: The role.
- `cancellationToken`: Cancels the query.

#### `ListAsync(Expression<Func<TRole, bool>>?, bool, CancellationToken)`

```csharp
Task<IReadOnlyList<TRole>> ListAsync(
    Expression<Func<TRole, bool>>? scope,
    bool tracked,
    CancellationToken cancellationToken)
```

The roles there are for the caller, archived ones included, within `scope` where one is given: tracked, for the starter roles made beside them, or read for showing.

**Parameters**

- `scope`: The roles of one scope of the application's own, or `null` for every role there is for the caller.
- `tracked`: Whether the unit of work tracks them.
- `cancellationToken`: Cancels the query.

#### `ByIdAsync(IReadOnlyCollection<TRoleId>, CancellationToken)`

```csharp
Task<IReadOnlyList<TRole>> ByIdAsync(
    IReadOnlyCollection<TRoleId> roles,
    CancellationToken cancellationToken)
```

The roles of `roles` there are for the caller, read for showing.

**Parameters**

- `roles`: The roles asked about.
- `cancellationToken`: Cancels the query.

#### `Add(TRole)`

```csharp
void Add(TRole role)
```

Adds a new role to the unit of work.

**Parameters**

- `role`: The role, just made.

#### `SaveAsync(CancellationToken)`

```csharp
Task SaveAsync(CancellationToken cancellationToken)
```

Saves everything the unit of work holds, with the domain events raised, in one transaction.

**Parameters**

- `cancellationToken`: Cancels the save.

### `ICustomRoleAccess<TResource>`

```csharp
public interface ICustomRoleAccess<TResource>
    where TResource : class
```

Whether the caller may change the roles a customer makes for one kind of resource: holds the permission the rules name for it ([`Roles.ManagedWith`](rules.md#roles-1)) for the whole of what the roles are kept for. The roles are a customer's as a whole, not one resource's, so Membership cannot answer it from a resource's members: the role manager asks this.

Optional. Without one only system work changes the roles. The join with an organization's package registers one that asks the organization whether the caller holds the permission for the whole of it.

**Type parameters**

- `TResource`: The resource the roles are of.

| Member | Summary |
|---|---|
| [`RequireAsync(string, CancellationToken)`](#requireasyncstring-cancellationtoken) | Requires the caller to hold `permission` for the whole of what the roles are kept for. |

#### `RequireAsync(string, CancellationToken)`

```csharp
ValueTask RequireAsync(string permission, CancellationToken cancellationToken)
```

Requires the caller to hold `permission` for the whole of what the roles are kept for.

**Parameters**

- `permission`: The permission the rules name for changing the roles.
- `cancellationToken`: Cancels the question.

**Exceptions**

- `RefusalException`: The caller does not hold it.

## Who may be a member

Namespace `DDDToolkit.Supporting.Membership.Managers`.

### `IMemberDirectory<TResourceId, TMemberId>`

```csharp
public interface IMemberDirectory<TResourceId, TMemberId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
```

Says whether somebody can be made a member of one kind of resource: whether the id is one the application knows, and whoever it stands for is active. An application implements it where it keeps its users, and the member manager of that kind of resource asks it before a member is added, an owner named or a resource opened.

Optional. Without one every id is admitted: a resource whose members are users of an identity provider often has nowhere to ask. The join with an organization's package answers it for members that are seats.

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

Says whether a role that exists may go to a member of one kind of resource: a rule of the application's own on top of the roles there are, such as roles an organization keeps for itself and never gives on a resource. The member manager of that kind of resource asks it when a role is given; what was given once can always be taken back, so it is not asked when a role is taken.

Optional. Without one every role there is may go to a member. It is asked about the role alone: whether this caller may give it is the manager's to check by the permission that manages the members, and a rule of the application's own beyond that is asked in its handler, before the manager is called.

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
| [`RequireCurrentUser()`](#requirecurrentuser) | Refuses a caller nothing can be answered for at all, with that caller's own refusal. |
| [`Reach(string)`](#reachstring) | The resources the caller reaches for `permission`, as a set a statement of your own takes: the lower level under `db.Boards.WhereIHold(permission)`, for a statement that reads how each resource is held, or one on another context than the request's. |
| [`ReachSeen()`](#reachseen) | The resources the caller sees, as a set a statement of your own takes: those it is a member of now, whatever roles it holds there; those it owns, from the moment the resource names it; and, where the rules let a resource be reached from above and name the permission every member holds ([`MembershipRules.EveryMemberPermission`](rules.md#membershiprules)), those that sit where the caller holds that permission, or below. |
| [`HoldAsync(TResource, string, CancellationToken)`](#holdasynctresource-string-cancellationtoken) | What the caller holds on `resource`, in one statement: `null` when the caller does not see it, which is also the answer for a resource that no longer exists; otherwise the resource, the version it is at, and how the caller holds `permission` on it and until when, if it does. |
| [`RequireAsync(TResource, string, CancellationToken)`](#requireasynctresource-string-cancellationtoken) | Requires the caller to hold `permission` on `resource`: what a command about one resource passes before it changes it. |
| [`ViaAsync(TResource, string, CancellationToken)`](#viaasynctresource-string-cancellationtoken) | How the caller holds `permission` on a resource it may see, or `null` when it does not hold it: for a query that answers with what its caller may do, where not holding the permission is an answer and not a refusal. |
| [`PermissionsOnAsync(IReadOnlyCollection<TResource>, IReadOnlyCollection<string>, CancellationToken)`](#permissionsonasyncireadonlycollectiontresource-ireadonlycollectionstring-cancellationtoken) | The permissions of `permissions` the caller holds on each of `resources` it sees, in one statement whatever their number, keyed by the resources that were given. |

#### `RequireCurrentUser()`

```csharp
public void RequireCurrentUser()
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
| [`RequireCurrentUser()`](#requirecurrentuser-1) | Refuses a caller nothing can be answered for at all, with that caller's own refusal. |
| [`Reach(string)`](#reachstring-1) | The resources the caller reaches for `permission`, as a set the storage puts into a statement: what [`MembershipAccessManager.Reach`](#reachstring) answers. |
| [`ReachSeen()`](#reachseen-1) | The resources the caller sees, as a set the storage puts into a statement: what [`MembershipAccessManager.ReachSeen`](#reachseen) answers. |
| [`HoldAsync(TResourceId, string, CancellationToken)`](#holdasynctresourceid-string-cancellationtoken) | What the caller holds on one resource, in one statement: `null` when the caller does not see the resource, which is also the answer for a resource that does not exist; otherwise the resource, its version, and how the caller holds `permission` on it and until when, if it does. |
| [`PermissionsOnAsync(IReadOnlyCollection<TResourceId>, IReadOnlyCollection<string>, CancellationToken)`](#permissionsonasyncireadonlycollectiontresourceid-ireadonlycollectionstring-cancellationtoken) | The permissions of `permissions` the caller holds on each of `resources` it sees, in one statement whatever their number. |

#### `RequireCurrentUser()`

```csharp
void RequireCurrentUser()
```

Refuses a caller nothing can be answered for at all, with that caller's own refusal. Where the caller's member id is its own id or a claim there is no such caller: one that is nobody's member simply reaches nothing, so this returns for everybody. Where the application resolves who a caller is as a member ([`MemberSource.Resolved`](rules.md#resolveddatabasefunction)), a caller the application refuses outright is refused here, before anything is read ([`ICurrentMember.Require`](rules.md#requirecurrentusersnapshot)).

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

Where a permission on the resource manages the members ([`MembershipRules.ManagesMembersPermission`](rules.md#managesmemberspermission)), a database that checks every row lets a member row change only for a caller that holds that permission, so a caller who takes itself off without it is refused there. The handler then saves the caller's own place as the application's own work, begun for that caller once its request is the one in hand (`RequestInHand`): the check decided it.

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
