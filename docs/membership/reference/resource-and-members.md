---
title: "Membership API: Resource and members"
sidebar_label: Resource and members
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Membership API: Resource and members

The classes an application declares for a resource with members, and what they keep: the member template and the member list over it, the periods and the roles a member holds, what the resource's class says of itself, and the template of the roles a customer makes. Each template's section opens with the name the application sees, the template in brackets, and the attribute that declares the application's class.

A member class is declared with `[Member<TId, TMemberId, TRoleId, TResource>]`, and the generator derives it from `MemberEntity<TId, TMemberId, TRoleId>`, closed over the first three. What fills the four type parameters:

| Type parameter | Filled with | Constraints |
|---|---|---|
| `TId` | The member class's own id, a `readonly partial record struct` marked `[EntityId<T>]`. |  |
| `TMemberId` | What a member is known by: the id of whoever can be a member of the resource, such as a user's, or that of the place somebody has in an organization, a `readonly partial record struct` marked `[EntityId<T>]`. |  |
| `TRoleId` | What a role is known by: [`NamedRole`](#namedrole) when the roles are the ones the resource's rules declare, or the id of the application's own roles, a `readonly partial record struct` marked `[EntityId<T>]`: that of its role class for this resource ([`CustomRoleAttribute<TRoleId, TResource>`](#customroleattributetroleid-tresource)), or of roles it keeps elsewhere. |  |
| `TResource` | The resource the members are of: the application's aggregate that holds them. | `class` |

A role class, for a resource whose customers make its roles, is declared with `[CustomRole<TRoleId, TResource>]`, and derives from `CustomRoleAggregate<TRoleId>`. Its id is the `TRoleId` the resource's member class holds roles by:

| Type parameter | Filled with | Constraints |
|---|---|---|
| `TRoleId` | The role class's id, a `readonly partial record struct` marked `[EntityId<T>]`: what a member holds a role by. |  |
| `TResource` | The resource the roles are of: the application's aggregate whose members hold them. | `class` |

## Member

Namespace `DDDToolkit.Supporting.Membership`.

### Member (`MemberEntity<TId, TMemberId, TRoleId>`)

Declared in the application with `[Member<Id, MemberId, RoleId, Resource>]`.

```csharp
public abstract class MemberEntity<TId, TMemberId, TRoleId> : Entity<TId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

One member of a resource, for a period, with the roles it holds there. The application declares its own class with [`MemberAttribute<TId, TMemberId, TRoleId, TResource>`](#memberattributetid-tmemberid-troleid-tresource), as a child entity of the aggregate the members belong to: loaded and saved with it, and changed only through it.

Being a member and holding powers are two things, each with its own dates. The membership says the member is on the resource from [`StartsAt`](#member-memberentitytid-tmemberid-troleid) until [`EndsAt`](#member-memberentitytid-tmemberid-troleid). What it may do there comes from its [`Roles`](#member-memberentitytid-tmemberid-troleid), each held for a period of its own, so a role can end while the membership goes on, and a member can hold two roles at once. A role counts only while the membership does ([`HoldsAt`](#holdsattroleid-datetimeoffset)). A member with no role is a member without any rights from it but what the resource's rules give membership alone.

A member belongs to the member list of its resource, and nothing else changes it: every mutator here is internal, and [`MemberList<TMember, TId, TMemberId, TRoleId>`](#memberlisttmember-tid-tmemberid-troleid) checks the rules about the whole list before it calls one. The one rule a member can tell by itself, that it holds a role once, is nested here and runs for the application's class whenever its aggregate is checked.

**Type parameters**

- `TId`: The member class's own id.
- `TMemberId`: What a member is known by.
- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| [`MemberEntity()`](#memberentity) | Parameterless constructor for persistence frameworks and serializers. |
| `TMemberId MemberId { get; }` | Who the member is. |
| `DateTimeOffset StartsAt { get; }` | The first moment the membership counts. |
| `DateTimeOffset? EndsAt { get; }` | The first moment it no longer counts, or `null` when it has no end. |
| `TMemberId? AddedBy { get; }` | The member that added this one, or `null` for system work and for an owner the resource was opened with or that was named owner without being a member. |
| `IReadOnlyList<MemberRole<TMemberId, TRoleId>> Roles { get; }` | The roles the member holds on the resource, at most one per role. A read-only view: only the member list changes them. |
| [`AppliesAt(DateTimeOffset)`](#appliesatdatetimeoffset) | Whether the membership counts at `moment`: it has started, and has not ended. |
| [`HoldsAt(TRoleId, DateTimeOffset)`](#holdsattroleid-datetimeoffset) | Whether the member holds `role` at `moment`: the role's own period applies then, and so does the membership it is held in. |
| [`EndedBy(DateTimeOffset)`](#endedbydatetimeoffset) | Whether the membership has ended by `moment`: it had an end, and that end is not after the moment. |
| [`GetOwnInvariantViolations()`](#getowninvariantviolations) | Runs the rules and `CheckInvariants()` seam of this entity, and returns every rule that is broken, without throwing. |
| [`EnsureOwnInvariants()`](#ensureowninvariants) | Runs the rules and `CheckInvariants()` seam of this entity, and throws when it finds something broken. |
| [`GetInvariantViolations()`](#getinvariantviolations) | Runs the rules and `CheckInvariants()` seam of this entity and of every child entity it holds, and returns every rule that is broken, without throwing. |
| [`EnsureInvariants()`](#ensureinvariants) | Runs the rules and `CheckInvariants()` seam of this entity and of every child entity it holds, and throws when it finds something broken. |

#### `MemberEntity()`

```csharp
protected MemberEntity()
```

Parameterless constructor for persistence frameworks and serializers.

#### `AppliesAt(DateTimeOffset)`

```csharp
public bool AppliesAt(DateTimeOffset moment)
```

Whether the membership counts at `moment`: it has started, and has not ended.

**Parameters**

- `moment`: The moment to ask about, usually now.

#### `HoldsAt(TRoleId, DateTimeOffset)`

```csharp
public bool HoldsAt(TRoleId role, DateTimeOffset moment)
```

Whether the member holds `role` at `moment`: the role's own period applies then, and so does the membership it is held in. A role with no end of its own stops counting when the membership ends.

**Parameters**

- `role`: The role.
- `moment`: The moment to ask about, usually now.

#### `EndedBy(DateTimeOffset)`

```csharp
public bool EndedBy(DateTimeOffset moment)
```

Whether the membership has ended by `moment`: it had an end, and that end is not after the moment. An ended membership is over for good: the member comes back with a new one.

**Parameters**

- `moment`: The moment to ask about.

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

### `MemberAttribute<TId, TMemberId, TRoleId, TResource>`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MemberAttribute<TId, TMemberId, TRoleId, TResource> : Attribute
    where TResource : class
```

Declares the member class of one of the application's resources. The generator derives it from [`MemberEntity<TId, TMemberId, TRoleId>`](#member-memberentitytid-tmemberid-troleid), closed over the first three types given here:

```csharp
[Member<DocumentShareId, UserId, NamedRole, Document>]
public sealed partial class DocumentShare;
```

The resource's aggregate stays the application's own, declared as it always was, and holds its members as a collection of this class. The package's generator writes the member list over that collection on the aggregate, `Members`, which the aggregate's own methods change its members through ([`MemberList<TMember, TId, TMemberId, TRoleId>`](#memberlisttmember-tid-tmemberid-troleid)).

The fourth type is the resource the members are of. The member itself has no use for it: it is what the registrations of the resource are named after and closed over, so an application that declares the members of a `Document` calls `AddDocumentMembership`, and writes neither the resource nor its id there.

An application declares one member class for each kind of resource it has members on, its documents and its folders, each with a class, a table and rules of its own. So the template may be declared more than once in a project, and everything the package offers is asked for by the member class or by the resource.

**Type parameters**

- `TId`: The member class's own id, a `readonly partial record struct` marked `[EntityId<T>]`.
- `TMemberId`: What a member is known by: the id of whoever can be a member of the resource, such as a user's, or that of the place somebody has in an organization, a `readonly partial record struct` marked `[EntityId<T>]`.
- `TRoleId`: What a role is known by: [`NamedRole`](#namedrole) when the roles are the ones the resource's rules declare, or the id of the application's own roles, a `readonly partial record struct` marked `[EntityId<T>]`: that of its role class for this resource ([`CustomRoleAttribute<TRoleId, TResource>`](#customroleattributetroleid-tresource)), or of roles it keeps elsewhere.
- `TResource`: The resource the members are of: the application's aggregate that holds them.

**Remarks**

Membership's Entity Framework package maps the resource's collection of this class, through `AddDDDToolkitConventions()`, as `HasMembers` does: the template names that convention. A model that maps the class without that package in the application is refused while it is built.

### `MemberEntity.OneHoldPerRole`

```csharp
public sealed class OneHoldPerRole : IInvariant<MemberEntity<TId, TMemberId, TRoleId>>
```

A member holds a role once: one hold per role. The member list refuses a second hold before anything changes; this is the net under code that got round it.

It reports the package's own name for the rule, [`MembershipRefusals.RoleHeld`](refusals.md#membershiprefusals), and not a code of the resource: a member does not know whose it is.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(MemberEntity<TId, TMemberId, TRoleId>)`](#checkmemberentitytid-tmemberid-troleid) | Runs the rule. |

#### `Check(MemberEntity<TId, TMemberId, TRoleId>)`

```csharp
public InvariantFailure? Check(MemberEntity<TId, TMemberId, TRoleId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

## Member list

Namespace `DDDToolkit.Supporting.Membership`.

### `MemberList<TMember, TId, TMemberId, TRoleId>`

```csharp
public sealed class MemberList<TMember, TId, TMemberId, TRoleId>
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, ICreatableEntityId<TId>, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

The members of one resource, with the rules about them: who is a member for which period, which roles each member holds for which period, and what the owner may not lose. The resource's aggregate holds one over its own member collection, which the package's generator writes as `Members`. Its constructor opens the list, and every other change goes through the member manager the generator writes for the member class:

```csharp
[AggregateRoot<DocumentId>]
public sealed partial class Document
{
    public Document(DocumentId id, UserId owner, DateTimeOffset now) : base(id)
    {
        OwnerId = owner;
        Members.Open(now);
    }

    [Owner] public UserId OwnerId { get; private set; }

    public partial IReadOnlyList<DocumentShare> Shares { get; }

    partial void OnChangingMembers() => RequireNotArchived();
    partial void OnMemberAdded(DocumentShare share) => RaiseDomainEvent(new DocumentShared(Id, share.MemberId));
}
```

The package's generator writes `Members` on the resource a member class names, from the one collection of the member class, the property marked `[Owner]`, and the codes it writes on the resource, or the one static [`MembershipCodes`](refusals.md#membershipcodes) the resource declares itself. A new member row's id is made by the id itself, with its `Create()`, which the generator writes for an `[EntityId<Guid>]` and an id over anything else declares. A resource the generator cannot tell these of declares the property itself, which the generator then leaves alone:

```csharp
private MemberList<DocumentShare, DocumentShareId, UserId, NamedRole> Members
    => new(_shares, OwnerId, DocumentRefusals.Membership);
```

It is not an aggregate and keeps nothing itself: the members live inside the resource's aggregate, so they share its version, its save and whatever the aggregate refuses for its own reasons. The generator writes the manager's way to it onto the resource as well (`IMembershipResource<TMember, TMemberId, TRoleId>`): each change calls the aggregate's guard, `OnChangingMembers()`, a closed or archived resource say, then the list, then the hook that says what happened, where the aggregate raises its own events under its own names.

Each method refuses what would break a rule with a coded `RefusalException`, under the resource's own codes, before it changes anything, and answers what it did. It raises no events. The rules:

- A member is on the list once, for a period. A membership that has ended is over for good: the member comes back with a new one, without the roles of the old. One that still runs, or is still to start, is the member's one membership: a second is refused, whenever it would start.
- A member holds each role at most once, each for a period of its own. A role counts only while the membership does, so none is given to a member whose membership has ended.
- There is one owner, always a member, with no end, and holding no role: the owner holds what it holds by owning the resource, the permissions marked `Owner`. The owner cannot be removed, nor be given a role, until somebody else is named owner; the owner before then becomes what the call says, a member in a role or nobody on the list ([`FormerOwner`](#formerowner)).

Two of the rules are about the whole list, which only the aggregate has, so they are checks here ([`OneMembershipPerMember`](#onemembershippermember), [`OwnerStays`](#ownerstays)) for two invariants of the aggregate to call, under the aggregate's own codes. The third, one hold per role, a member checks by itself.

Whether a member or a role may go on the list at all cannot be told from the list, and neither can who may change it: the member manager asks both before it calls the list, the caller's permission that manages the members and what the application answers about who may be a member and which roles go to one.

**Type parameters**

- `TMember`: The application's member class.
- `TId`: The member class's own id, which makes a new member row's id with its `Create()`.
- `TMemberId`: What a member is known by.
- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| [`MemberList(List<TMember>, TMemberId, MembershipCodes)`](#memberlistlisttmember-tmemberid-membershipcodes) | The member list of one resource, over the collection its aggregate keeps. |
| [`Open(DateTimeOffset)`](#opendatetimeoffset) | Opens the list of a new resource: puts its owner on it, from `now` on and with no end, holding no role, since the owner holds what it holds by owning the resource. |
| [`Add(TMemberId, MemberPeriod, DateTimeOffset, TMemberId?)`](#addtmemberid-memberperiod-datetimeoffset-tmemberid) | Adds a member for `period`, with no role yet: what else it may do comes with the roles it is given ([`GiveRole`](#giveroletmemberid-troleid-memberperiod-datetimeoffset-tmemberid)). |
| [`Add(TMemberId, TRoleId, MemberPeriod, DateTimeOffset, TMemberId?)`](#addtmemberid-troleid-memberperiod-datetimeoffset-tmemberid) | Adds a member for `period` and gives it `role` for the same period, in one change. |
| [`GiveRole(TMemberId, TRoleId, MemberPeriod, DateTimeOffset, TMemberId?)`](#giveroletmemberid-troleid-memberperiod-datetimeoffset-tmemberid) | Gives a member a role for `period`, next to whatever roles it holds already. |
| [`TakeRole(TMemberId, TRoleId)`](#takeroletmemberid-troleid) | Takes a role from a member, who stays a member with whatever else it holds. |
| [`Remove(TMemberId)`](#removetmemberid) | Takes a member off the list, with every role it holds. |
| [`NameOwner(TMemberId, FormerOwner<TRoleId>, DateTimeOffset)`](#nameownertmemberid-formerownertroleid-datetimeoffset) | Names another owner: a member from now on and with no end, added when not a member yet, holding no role. |
| [`OneMembershipPerMember()`](#onemembershippermember) | The rule that a member is on the list once, checked after the fact: for an invariant of the aggregate to call, under the aggregate's own code for [`MembershipRefusals.AlreadyMember`](refusals.md#membershiprefusals). |
| [`OwnerStays()`](#ownerstays) | The rule that the owner is a member with no end, holding no role, checked after the fact: for an invariant of the aggregate to call, under the aggregate's own code for [`MembershipRefusals.OwnerProtected`](refusals.md#membershiprefusals). |

#### `MemberList(List<TMember>, TMemberId, MembershipCodes)`

```csharp
public MemberList(List<TMember> members, TMemberId owner, MembershipCodes codes)
```

The member list of one resource, over the collection its aggregate keeps.

**Parameters**

- `members`: The aggregate's own member collection: the field behind its generated read-only property.
- `owner`: The resource's owner, as the aggregate keeps it now.
- `codes`: The codes the resource refuses with.

#### `Open(DateTimeOffset)`

```csharp
public TMember Open(DateTimeOffset now)
```

Opens the list of a new resource: puts its owner on it, from `now` on and with no end, holding no role, since the owner holds what it holds by owning the resource. Called once, where the aggregate is constructed.

**Parameters**

- `now`: When the resource is opened.

**Returns**: The owner's membership.

**Exceptions**

- `InvalidOperationException`: The list has members already: a resource is opened once.

#### `Add(TMemberId, MemberPeriod, DateTimeOffset, TMemberId?)`

```csharp
public TMember Add(TMemberId member, MemberPeriod period, DateTimeOffset now, TMemberId? by = null)
```

Adds a member for `period`, with no role yet: what else it may do comes with the roles it is given ([`GiveRole`](#giveroletmemberid-troleid-memberperiod-datetimeoffset-tmemberid)). A member is on the list once: a membership it still has at `now`, one that runs or one that is still to start, is refused, whenever the new one would start. One that has ended by `now` is replaced, with the roles that were held in it, whatever start the new one is given.

**Parameters**

- `member`: The member; the member manager has checked it may be one.
- `period`: When the membership counts.
- `now`: When the member is added: what a membership it had before is judged by. Not the period's start, which may lie ahead: a membership that runs today is not over because another is to start next month.
- `by`: Who adds it, or `null` for system work.

**Returns**: The new membership.

**Exceptions**

- `RefusalException`: `invalid-period`, `already-member`, under the resource's codes.

#### `Add(TMemberId, TRoleId, MemberPeriod, DateTimeOffset, TMemberId?)`

```csharp
public TMember Add(
    TMemberId member,
    TRoleId role,
    MemberPeriod period,
    DateTimeOffset now,
    TMemberId? by = null)
```

Adds a member for `period` and gives it `role` for the same period, in one change.

**Parameters**

- `member`: The member; the member manager has checked it may be one.
- `role`: The role; the member manager has checked it may go to a member.
- `period`: When the membership, and the role, count.
- `now`: When the member is added: what a membership it had before is judged by.
- `by`: Who adds it, or `null` for system work.

**Returns**: The new membership, holding the role.

**Exceptions**

- `RefusalException`: `invalid-period`, `already-member`, under the resource's codes.

#### `GiveRole(TMemberId, TRoleId, MemberPeriod, DateTimeOffset, TMemberId?)`

```csharp
public MemberRole<TMemberId, TRoleId> GiveRole(
    TMemberId member,
    TRoleId role,
    MemberPeriod period,
    DateTimeOffset now,
    TMemberId? by = null)
```

Gives a member a role for `period`, next to whatever roles it holds already. A member holds a role once: a hold it still has at `now`, one that runs or one that is still to start, is refused, whenever the new one would start, and one that has ended by `now` is replaced. A member whose membership has ended by `now` is not a member for this: a role counts only while the membership does, so giving one would give nothing. Add the member again first.

**Parameters**

- `member`: The member.
- `role`: The role; the member manager has checked it may go to a member.
- `period`: When the role counts. It counts only while the membership does.
- `now`: When the role is given: what the membership, and a hold of the role from before, are judged by. Not the period's start, which may lie ahead: a role held today is not over because it is given again from next month.
- `by`: Who gives it, or `null` for system work.

**Returns**: The role as the member holds it now.

**Exceptions**

- `RefusalException`: `invalid-period`, `member-not-found`, `role-held`, and `owner-protected` for the owner, who holds no role, under the resource's codes.

#### `TakeRole(TMemberId, TRoleId)`

```csharp
public MemberRole<TMemberId, TRoleId> TakeRole(TMemberId member, TRoleId role)
```

Takes a role from a member, who stays a member with whatever else it holds. Any role can be taken, one that is no longer in use too: what was given once can always be taken back.

**Parameters**

- `member`: The member.
- `role`: The role to take.

**Returns**: The role as the member held it.

**Exceptions**

- `RefusalException`: `member-not-found`, `role-not-held`, under the resource's codes.

#### `Remove(TMemberId)`

```csharp
public TMember Remove(TMemberId member)
```

Takes a member off the list, with every role it holds. The owner stays until somebody else is named owner.

**Parameters**

- `member`: The member.

**Returns**: The membership as it was, with the roles held in it.

**Exceptions**

- `RefusalException`: `member-not-found`, `owner-protected`, under the resource's codes.

#### `NameOwner(TMemberId, FormerOwner<TRoleId>, DateTimeOffset)`

```csharp
public OwnerNamed<TMemberId, TRoleId> NameOwner(
    TMemberId member,
    FormerOwner<TRoleId> formerOwner,
    DateTimeOffset now)
```

Names another owner: a member from now on and with no end, added when not a member yet, holding no role. The roles it held go, since the owner holds what it holds by owning; a membership that had ended begins anew, and one that was still to start or to end loses that start or end. The owner before becomes what `formerOwner` says: a member with no end in the role it names, or nobody on the list. There is no default, since the owner held no role to fall back to.

The list does not keep who the owner is: the aggregate does, and keeps the answer's [`OwnerNamed.Owner`](#ownernamedtmemberid-troleid) as its owner right after this call. The answer says everything that changed, so the aggregate can raise an event for each change, and can keep the end that was lifted ([`OwnerNamed.EndLifted`](#ownernamedtmemberid-troleid)) if somebody who was on the resource for a while is to leave it at that moment after all: nothing here ends a membership again, short of [`Remove`](#removetmemberid).

**Parameters**

- `member`: The member that is to own the resource; the member manager has checked it may be one.
- `formerOwner`: What the owner before becomes: [`FormerOwner.StaysAs<TRoleId>`](#staysastroleid) a role, which the member manager has checked may go to a member, or [`FormerOwner.Leaves`](#formerowner).
- `now`: When the owner is named.

**Returns**: What happened, for the aggregate to keep its owner by and raise its events from.

**Exceptions**

- `RefusalException`: `already-owner`, under the resource's codes.

#### `OneMembershipPerMember()`

```csharp
public InvariantFailure? OneMembershipPerMember()
```

The rule that a member is on the list once, checked after the fact: for an invariant of the aggregate to call, under the aggregate's own code for [`MembershipRefusals.AlreadyMember`](refusals.md#membershiprefusals).

```csharp
public sealed class OneShareePerDocument : IInvariant<Document>
{
    public string Code => DocumentRefusals.Membership[MembershipRefusals.AlreadyMember];

    public InvariantFailure? Check(Document entity) => entity.Members.OneMembershipPerMember();
}
```

**Returns**: `null` when the rule holds; otherwise what is wrong, with the member that is there twice.

#### `OwnerStays()`

```csharp
public InvariantFailure? OwnerStays()
```

The rule that the owner is a member with no end, holding no role, checked after the fact: for an invariant of the aggregate to call, under the aggregate's own code for [`MembershipRefusals.OwnerProtected`](refusals.md#membershiprefusals).

**Returns**: `null` when the rule holds; otherwise what is wrong.

### `FormerOwner`

```csharp
public static class FormerOwner
```

What the owner before becomes when somebody else is named owner, said every time, since an owner holds no role and nothing can be taken for granted: a member in a role, or nobody on the list.

```csharp
await boardMembers.NameOwnerAsync(board, seat, FormerOwner.StaysAs(editorRole), cancellationToken);    // stays on the board, as an editor
await boardMembers.NameOwnerAsync(board, seat, FormerOwner.Leaves, cancellationToken);                 // leaves the board
```

| Member | Summary |
|---|---|
| `static FormerOwnerLeaves Leaves { get; }` | The owner before goes off the list, as a member removed does. |
| [`StaysAs(TRoleId)`](#staysastroleid) | The owner before stays a member, with no end, holding `role` from the moment the owner is named: a role id where the roles are rows, a [`NamedRole`](#namedrole) where they are fixed. |

#### `StaysAs(TRoleId)`

```csharp
public static FormerOwner<TRoleId> StaysAs<TRoleId>(TRoleId role)
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

The owner before stays a member, with no end, holding `role` from the moment the owner is named: a role id where the roles are rows, a [`NamedRole`](#namedrole) where they are fixed.

**Parameters**

- `role`: The role; the member manager checks it may go to a member.

**Type parameters**

- `TRoleId`: What a role is known by.

### `FormerOwner<TRoleId>`

```csharp
public sealed class FormerOwner<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

What the owner before becomes when somebody else is named owner: see [`FormerOwner`](#formerowner).

**Type parameters**

- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| `TRoleId? Role { get; }` | The role the owner before holds from now on, or `null` when it leaves the list. |
| [`implicit operator FormerOwner<TRoleId>(FormerOwnerLeaves)`](#implicit-operator-formerownertroleidformerownerleaves) | The owner before leaves the list, whatever the role is known by. |

#### `implicit operator FormerOwner<TRoleId>(FormerOwnerLeaves)`

```csharp
public static implicit operator FormerOwner<TRoleId>(FormerOwnerLeaves leaves)
```

The owner before leaves the list, whatever the role is known by.

**Parameters**

- `leaves`: [`FormerOwner.Leaves`](#formerowner).

### `FormerOwnerLeaves`

```csharp
public sealed class FormerOwnerLeaves
```

That the owner before leaves the list: [`FormerOwner.Leaves`](#formerowner).

### `OwnerNamed<TMemberId, TRoleId>`

```csharp
public sealed record OwnerNamed<TMemberId, TRoleId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

What naming an owner did to a member list, for the aggregate to act on: it keeps [`Owner`](#ownernamedtmemberid-troleid) as its owner, and raises its own events for what happened, in this order: the member added, or what changed about the membership it had and the roles it held, what the owner before became, the owner changed.

An owner's place has no end and holds no role, and naming an owner makes it so: a membership that was to end no longer does, also after the resource is handed on again, and the roles the new owner held go, since it holds by owning. Somebody who was on a resource for a week and owned it for a day is on it for good afterwards. [`EndLifted`](#ownernamedtmemberid-troleid) says the end that went, so an aggregate that wants the former owner off again at that moment can keep it and take the member off the list then.

**Type parameters**

- `TMemberId`: What a member is known by.
- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| [`OwnerNamed(TMemberId, TMemberId, bool, bool, IReadOnlyList<MemberRole<TMemberId, TRoleId>>, DateTimeOffset?, DateTimeOffset?, TRoleId?, bool)`](#ownernamedtmemberid-tmemberid-bool-bool-ireadonlylistmemberroletmemberid-troleid-datetimeoffset-datetimeoffset-troleid-bool) | The primary constructor: the type's summary says what it makes. |
| `TMemberId Previous { get; init; }` | The owner before. |
| `TMemberId Owner { get; init; }` | The owner now: what the aggregate keeps in its owner property. |
| `bool Added { get; init; }` | Whether the new owner was put on the member list now, not being a member before. |
| `bool BeganAnew { get; init; }` | Whether the new owner's membership had ended, and began again now. |
| `IReadOnlyList<MemberRole<TMemberId, TRoleId>> RolesDropped { get; init; }` | The roles the new owner held and no longer holds, as they were held; empty when it held none. |
| `DateTimeOffset? EndLifted { get; init; }` | The end the new owner's membership had and no longer has, or `null` when it had none that was still to come. |
| `DateTimeOffset? StartMoved { get; init; }` | The later moment the new owner's membership was to start at, moved to now, or `null`. |
| `TRoleId? PreviousRole { get; init; }` | The role the owner before holds from now on, as a member, or `null` when it left the list (`PreviousLeft`). |
| `bool PreviousLeft { get; init; }` | Whether the owner before was taken off the list. |

#### `OwnerNamed(TMemberId, TMemberId, bool, bool, IReadOnlyList<MemberRole<TMemberId, TRoleId>>, DateTimeOffset?, DateTimeOffset?, TRoleId?, bool)`

```csharp
public OwnerNamed(
    TMemberId Previous,
    TMemberId Owner,
    bool Added,
    bool BeganAnew,
    IReadOnlyList<MemberRole<TMemberId, TRoleId>> RolesDropped,
    DateTimeOffset? EndLifted,
    DateTimeOffset? StartMoved,
    TRoleId? PreviousRole,
    bool PreviousLeft)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Previous`: The owner before.
- `Owner`: The owner now: what the aggregate keeps in its owner property.
- `Added`: Whether the new owner was put on the member list now, not being a member before.
- `BeganAnew`: Whether the new owner's membership had ended, and began again now.
- `RolesDropped`: The roles the new owner held and no longer holds, as they were held; empty when it held none.
- `EndLifted`: The end the new owner's membership had and no longer has, or `null` when it had none that was still to come.
- `StartMoved`: The later moment the new owner's membership was to start at, moved to now, or `null`.
- `PreviousRole`: The role the owner before holds from now on, as a member, or `null` when it left the list (`PreviousLeft`).
- `PreviousLeft`: Whether the owner before was taken off the list.

## Periods and roles held

Namespace `DDDToolkit.Supporting.Membership`.

### `MemberPeriod`

```csharp
public readonly record struct MemberPeriod
```

When a membership, or a role held in one, counts: from [`Starts`](#memberperiod), and until [`Ends`](#memberperiod) when it has an end.

Not a toolkit value object, because it is stored as the two columns of the row that holds it rather than as a value of its own. It is two moments and decides nothing by itself: whether an end may come before a start is the member list's to refuse, under the codes of the resource the members belong to, before anything changes.

| Member | Summary |
|---|---|
| `DateTimeOffset Starts { get; }` | The first moment it counts. |
| `DateTimeOffset? Ends { get; }` | The first moment it no longer counts, or `null` when it has no end. |
| `bool IsEmpty { get; }` | Whether the period covers no moment at all: it ends at or before it starts. The member list refuses such a period with the resource's `invalid-period` code. |
| [`Open(DateTimeOffset)`](#opendatetimeoffset-1) | A period from `starts` with no end. |
| [`Between(DateTimeOffset, DateTimeOffset?)`](#betweendatetimeoffset-datetimeoffset) | A period from `starts` to `ends`, or with no end when that is `null`: what a command that takes an optional end makes of it. |
| [`AppliesAt(DateTimeOffset)`](#appliesatdatetimeoffset-1) | Whether the period covers `moment`: it has started, and has not ended. |

#### `Open(DateTimeOffset)`

```csharp
public static MemberPeriod Open(DateTimeOffset starts)
```

A period from `starts` with no end.

**Parameters**

- `starts`: The first moment it counts.

#### `Between(DateTimeOffset, DateTimeOffset?)`

```csharp
public static MemberPeriod Between(DateTimeOffset starts, DateTimeOffset? ends)
```

A period from `starts` to `ends`, or with no end when that is `null`: what a command that takes an optional end makes of it.

**Parameters**

- `starts`: The first moment it counts.
- `ends`: The first moment it no longer counts.

#### `AppliesAt(DateTimeOffset)`

```csharp
public bool AppliesAt(DateTimeOffset moment)
```

Whether the period covers `moment`: it has started, and has not ended.

**Parameters**

- `moment`: The moment to ask about, usually now.

### `MemberRole<TMemberId, TRoleId>`

```csharp
public sealed class MemberRole<TMemberId, TRoleId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

A role a member holds on a resource, for a period of its own. Within its member it is known by its role: a member holds a role at most once.

Being a member and holding a role are two things, each with its own dates: a role can end while the membership goes on, and a member can hold two roles at once. A role counts only while its own period applies and the membership it is held in does.

A plain class rather than a toolkit entity, and not extended by the application: a held role is a fact about a member, and only the member list changes it. It remembers which role it is, never what the role gives: that is asked where the role is used.

**Type parameters**

- `TMemberId`: What a member is known by.
- `TRoleId`: What a role is known by.

| Member | Summary |
|---|---|
| `TRoleId RoleId { get; }` | The role held. |
| `DateTimeOffset StartsAt { get; }` | The first moment the role counts. |
| `DateTimeOffset? EndsAt { get; }` | The first moment it no longer counts, or `null` when it has no end. |
| `TMemberId? GivenBy { get; }` | The member that gave it, or `null` for system work. |
| [`AppliesAt(DateTimeOffset)`](#appliesatdatetimeoffset-2) | Whether the role's own period applies at `moment`: it has started, and has not ended. |

#### `AppliesAt(DateTimeOffset)`

```csharp
public bool AppliesAt(DateTimeOffset moment)
```

Whether the role's own period applies at `moment`: it has started, and has not ended. Whether the role counts then also takes the membership it is held in ([`MemberEntity.HoldsAt`](#holdsattroleid-datetimeoffset)).

**Parameters**

- `moment`: The moment to ask about, usually now.

### `NamedRole`

```csharp
public readonly struct NamedRole : IEntityId<string>, IEntityId, IComparable<NamedRole>, IParsable<NamedRole>, ISingleValue<NamedRole, string>, IEquatable<NamedRole>
```

A role known by its name: what a member holds when the roles are the ones a resource's rules declare ([`DeclaredRole`](rules.md#declaredrole)), rather than rows: the application's role class ([`CustomRoleAttribute<TRoleId, TResource>`](#customroleattributetroleid-tresource)), or roles it keeps somewhere else.

```csharp
[Member<DocumentShareId, UserId, NamedRole, Document>]
public sealed partial class DocumentShare;
```

It is an id over the name, so it is stored as that text, in a column of at most [`MaxLength`](#namedrole) characters, and compares by it. It checks nothing by itself: whether a name is one of a resource's roles is asked where a role is given, of that resource's rules.

| Member | Summary |
|---|---|
| `MaxLength = 64` | The longest name a role may have. |
| `IdPrefix = ""` | Prefix written by ToString() and accepted (optionally) by Parse/TryParse. |
| [`NamedRole(string)`](#namedrolestring) | *No documentation.* |
| `string Value { get; }` | *No documentation.* |
| `static NamedRole Empty { get; }` | The default id (no value). Never identifies anything. |
| `bool IsEmpty { get; }` | True when this id holds the default value. |
| [`ToString()`](#tostring) | *No documentation.* |
| [`CompareTo(NamedRole)`](#comparetonamedrole) | *No documentation.* |
| [`Parse(string)`](#parsestring) | Parses the textual form produced by ToString(); the prefix is optional. |
| [`TryParse(string?, out NamedRole)`](#tryparsestring-out-namedrole) | Parses the textual form produced by ToString(); the prefix is optional. |
| [`explicit operator string(NamedRole)`](#explicit-operator-stringnamedrole) | *No documentation.* |
| [`explicit operator NamedRole(string)`](#explicit-operator-namedrolestring) | *No documentation.* |

#### `NamedRole(string)`

```csharp
public NamedRole(string value)
```

*No documentation.*

#### `ToString()`

```csharp
public override string ToString()
```

*No documentation.*

#### `CompareTo(NamedRole)`

```csharp
public int CompareTo(NamedRole other)
```

*No documentation.*

#### `Parse(string)`

```csharp
public static NamedRole Parse(string input)
```

Parses the textual form produced by ToString(); the prefix is optional.

**Exceptions**

- `FormatException`: The input is not a valid NamedRole.

#### `TryParse(string?, out NamedRole)`

```csharp
public static bool TryParse(string? input, out NamedRole result)
```

Parses the textual form produced by ToString(); the prefix is optional.

#### `explicit operator string(NamedRole)`

```csharp
public static explicit operator string(NamedRole id)
```

*No documentation.*

#### `explicit operator NamedRole(string)`

```csharp
public static explicit operator NamedRole(string value)
```

*No documentation.*

### `NamedRole.SystemTextJsonConverter`

```csharp
public sealed class SystemTextJsonConverter : JsonConverter<NamedRole>
```

Serializes the id as its underlying value. Applied through \[JsonConverter\] on the type.

| Member | Summary |
|---|---|
| [`Read(ref Utf8JsonReader, Type, JsonSerializerOptions)`](#readref-utf8jsonreader-type-jsonserializeroptions) | *No documentation.* |
| [`Write(Utf8JsonWriter, NamedRole, JsonSerializerOptions)`](#writeutf8jsonwriter-namedrole-jsonserializeroptions) | *No documentation.* |
| [`ReadAsPropertyName(ref Utf8JsonReader, Type, JsonSerializerOptions)`](#readaspropertynameref-utf8jsonreader-type-jsonserializeroptions) | *No documentation.* |
| [`WriteAsPropertyName(Utf8JsonWriter, NamedRole, JsonSerializerOptions)`](#writeaspropertynameutf8jsonwriter-namedrole-jsonserializeroptions) | *No documentation.* |

#### `Read(ref Utf8JsonReader, Type, JsonSerializerOptions)`

```csharp
public override NamedRole Read(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options)
```

*No documentation.*

#### `Write(Utf8JsonWriter, NamedRole, JsonSerializerOptions)`

```csharp
public override void Write(Utf8JsonWriter writer, NamedRole value, JsonSerializerOptions options)
```

*No documentation.*

#### `ReadAsPropertyName(ref Utf8JsonReader, Type, JsonSerializerOptions)`

```csharp
public override NamedRole ReadAsPropertyName(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options)
```

*No documentation.*

#### `WriteAsPropertyName(Utf8JsonWriter, NamedRole, JsonSerializerOptions)`

```csharp
public override void WriteAsPropertyName(
    Utf8JsonWriter writer,
    NamedRole value,
    JsonSerializerOptions options)
```

*No documentation.*

## The resource

What the resource's own class says, or has written onto it: its name, its codes, its owner and where it hangs. `[At]` is Membership's, from `DDDToolkit.Supporting.Membership.Abstractions`. `[Owner]` is the toolkit's, from `DDDToolkit.Abstractions`, and says what it says for every package that reads it.

### `IMembershipResource`

```csharp
public interface IMembershipResource
```

Namespace `DDDToolkit.Supporting.Membership`.

A resource with members, as the package's generator writes it onto the aggregate root a member class names: its name, the codes it refuses under, and the scope of its module's own system work. The rules of the resource read all three through its type, `MembershipRules.For<Board>(...)`, so none of them is written twice.

```csharp
[Member<BoardMemberId, SeatId, BoardRoleId, Board>]
public sealed partial class BoardMember;

// Written onto Board: MembershipName "boards", the codes boards.already-member and the rest, the scope "boards".
```

An application declares nothing for it. The name follows from the class, `Board` as `boards` and `CraneBoard` as `crane-boards`, unless the class says another with [`MembersAttribute`](#membersattribute). The codes are a `Codes` property the generator writes, [`MembershipCodes.Under`](refusals.md#understring) the name, unless the class declares a static [`MembershipCodes`](refusals.md#membershipcodes) of its own, which is then what it refuses under.

| Member | Summary |
|---|---|
| `static abstract string MembershipName { get; }` | The resource's name, as its codes and its functions in the database are named: `boards`, so the codes are `boards.already-member` and so on, and the functions `boards_i_see` and so on. |
| `static abstract MembershipCodes MembershipCodes { get; }` | The codes the resource refuses under: the member list's, the questions' and the member manager's. |
| `static abstract string? MembershipScope { get; }` | The scope of the system work of the module that declares the resource, its name as a scope is written: `boards` for the module `Boards`. `null` for a resource of a project that declares no module. Work begun in this scope holds every permission on the resource unless its rules say otherwise. |

### `MembersAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MembersAttribute : Attribute
```

Namespace `DDDToolkit.Supporting.Membership`.

Names a resource with members otherwise than after its class: what its codes and its functions in the database are named after.

```csharp
[Members("Whiteboards")]
[AggregateRoot<BoardId>]
public sealed partial class Board;
```

Without it a resource is named after its class, the plural of its name in lower case: `Board` is `boards`, `CraneBoard` `crane-boards`, `Category` `categories`, `Box` `boxes`. The name is what the codes a client reads start with, `boards.already-member`, and what the functions in the database are named after, `boards_i_see`, with an underscore where the codes have a dash.

**Remarks**

Say it where the plural is not the class's (`[Members("People")]` on a class `Person`), and after a class is renamed, to keep the name its codes and its functions already have: a new name changes the codes clients read and gives the database new functions, which the build's export warns of (DDD00090). The name is written as a class would be, `People` or `CraneBoards`, or as it comes out, `crane-boards`; it is converted the same way. Letters and digits only, with `-` or `_` between words; a space, any other symbol, or a digit first is DDD00091.

| Member | Summary |
|---|---|
| [`MembersAttribute(string)`](#membersattributestring) | The primary constructor: the type's summary says what it makes. |
| `string Name { get; }` | The resource's name, as it was written. |

#### `MembersAttribute(string)`

```csharp
public MembersAttribute(string name)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `name`: The resource's name, such as `People`.

**Remarks**

Say it where the plural is not the class's (`[Members("People")]` on a class `Person`), and after a class is renamed, to keep the name its codes and its functions already have: a new name changes the codes clients read and gives the database new functions, which the build's export warns of (DDD00090). The name is written as a class would be, `People` or `CraneBoards`, or as it comes out, `crane-boards`; it is converted the same way. Letters and digits only, with `-` or `_` between words; a space, any other symbol, or a digit first is DDD00091.

### `OwnerAttribute`

```csharp
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class OwnerAttribute : Attribute
```

Namespace `DDDToolkit.Abstractions.Attributes`.

Marks the property of an aggregate root that names its owner. A fact about what the aggregate is, said in the domain, and no rule about who may do what with it:

```csharp
[AggregateRoot<BoardId>]
public sealed partial class Board
{
    [Owner] public UserId Owner { get; private set; }
}
```

Who may do what with the owner's own is the request's to say, at the door, in the words a request says the caller's own with: `[OwnOnly]` with `[Own] BoardId Board` lets the board's owner alone through, and `[OrOwn] BoardId Board` beside `[RequiresPermission(permission)]` the owner, or whoever holds the permission.

**Remarks**

The property is of a type the caller has an own of: a `Guid`, the caller's verified identity; Tenancy's seat id; or a kind of the application's own that its `IOwnedByCurrentUser` says the caller's own of. It may be nullable, and a row whose owner is null is nobody's: nobody comes through as its owner. One per aggregate root, on the root itself, and never on an entity inside it, which a request does not name by its own id; on another type, or twice, it is DDD00087, and on a type nothing could say the caller's own of DDD00088.

The check reads that one column of that one row, as the caller, so row level security still decides whether the row is there for the caller at all: a row it does not see is not found. Several owners of one aggregate are members, which Membership keeps.

### `AtAttribute`

```csharp
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class AtAttribute : Attribute
```

Namespace `DDDToolkit.Supporting.Membership`.

Marks the property of an aggregate root that says where it hangs: the unit of an organization a board belongs to, the bay a crate stands in. It is a fact about what the aggregate is, said in the domain beside its `[Owner]`. It is no rule about who may do what with it:

```csharp
[AggregateRoot<BoardId>]
public sealed partial class Board
{
    [Owner] public SeatId OwnerSeatId { get; private set; }

    [At] public OrganizationUnitId UnitId { get; private set; }
}
```

Whether a permission held there reaches the aggregate is the policy's to say. The rules of a resource with members say it with `reachFromAbove`, and read where it hangs from this property. The name pairs with the level a permission is held at, `[AtUnit]`. A permission held at a unit reaches what hangs at it.

**Remarks**

One per aggregate root, on the root itself, of the id of the place. It is never nullable, since an aggregate that hangs nowhere could not be reached from above. Two, or a nullable one, is DDD00097. Rules that reach a resource from above whose class marks none are DDD00098. Membership's generator reports both, and ships in Membership's package.

The marker is in a package of its own, with no Entity Framework and nothing else of Membership's. Membership's own package references it, so a project with Membership has the marker as well.

## Custom role

Namespace `DDDToolkit.Supporting.Membership`.

### CustomRole (`CustomRoleAggregate<TRoleId>`)

Declared in the application with `[CustomRole<RoleId, Resource>]`.

```csharp
public abstract class CustomRoleAggregate<TRoleId> : AggregateRoot<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
```

A role of one kind of resource, kept as a row: a name, what it is for, and the permissions it gives a member that holds it. The application declares its own class with [`CustomRoleAttribute<TRoleId, TResource>`](#customroleattributetroleid-tresource), for a resource whose rules say a customer makes its roles ([`Roles.Custom`](rules.md#customireadonlylistdeclaredrole-otherpermissions-string)): roles a customer makes for itself, where fixed roles are the same for everybody.

A role is made, renamed, given permissions and archived, and the role says each change that changed something to a hook of the application's class: the partial methods `OnRenamed()`, `OnPermissionsSet(set)` and `OnArchived()`, which the package's generator declares on the class. The application raises its own events there, under its own names: the role raises none. The role manager the generator writes for the class, `BoardRoleManager`, makes each change, and asks first whether the caller may: the permission the rules name for it ([`Roles.ManagedWith`](rules.md#roles-1)).

Its permissions are held to the resource's rules: a role gives only permissions the rules let a member's role give ([`MembershipRules.RolePermissions`](rules.md#membershiprules)), the permissions marked `InRoles`, and one that would give another is refused. The rules are handed to everything that changes the role, as the one declaration the application has of them, and a refusal carries the code those rules give it.

An archived role gives nothing from then on and is not given again. It stays, and stays on the members that hold it, so what they held still has a name. A role made from a starter role remembers which ([`MadeFrom`](#customrole-customroleaggregatetroleid)), and only when it is made: that is how the starter roles a customer has not made yet are found, whatever it has called the ones it has. A role called "owner" is a role like any other: the owner of a resource holds no role.

The class is the application's. Whose a role is, a customer's say, is a property the application adds and sets in its own constructor, and keeping one customer's roles from another's is its own rule on its own rows, as for any aggregate. That a name is not used twice is an index it declares over that property and [`Name`](#customrole-customroleaggregatetroleid).

**Type parameters**

- `TRoleId`: The application's id of the role: what a member holds it by.

| Member | Summary |
|---|---|
| `MaxNameLength = 120` | The longest name a role may have. |
| `MaxDescriptionLength = 1000` | The longest description a role may have. |
| [`CustomRoleAggregate(TRoleId, CustomRoleDraft, MembershipRules)`](#customroleaggregatetroleid-customroledraft-membershiprules) | Makes a role from `draft`, in use. |
| [`CustomRoleAggregate()`](#customroleaggregate) | Parameterless constructor for persistence frameworks and serializers. |
| `string Name { get; }` | The role's name, with no white space around it. |
| `string Description { get; }` | What the role is for; empty when nobody said. |
| `IReadOnlyList<string> Permissions { get; }` | The permissions the role holds: distinct and in ordinal order. What it gives a member is asked of [`Gives`](#givesstring-membershiprules). |
| `CustomRoleStatus Status { get; }` | Whether the role is in use. An archived role gives nothing. |
| `string? MadeFrom { get; }` | The name of the starter role this one was made from, as the resource's rules declare it, or `null` for a role a customer made. Set when the role is made and never after, so it outlasts a new name. |
| [`Gives(string, MembershipRules)`](#givesstring-membershiprules) | Whether the role gives `permission` to a member that holds it: it is in use, it holds the permission, and the resource's rules let a member's role give that permission. |
| [`Rename(string, string?, MembershipRules)`](#renamestring-string-membershiprules) | Renames the role and says again what it is for, and calls the class's `OnRenamed()` when that changed anything. |
| [`SetPermissions(IReadOnlyCollection<string>, MembershipRules)`](#setpermissionsireadonlycollectionstring-membershiprules) | Sets the permissions the role gives, for every member that holds it. |
| [`Archive(MembershipRules)`](#archivemembershiprules) | Archives the role: it stays, on every member that holds it, gives nothing from now on, and is not given again. |
| [`GetOwnInvariantViolations()`](#getowninvariantviolations-1) | Runs the rules and `CheckInvariants()` seam of this aggregate, and returns every rule that is broken, without throwing. |
| [`EnsureOwnInvariants()`](#ensureowninvariants-1) | Runs the rules and `CheckInvariants()` seam of this aggregate, and throws when it finds something broken. |
| [`GetInvariantViolations()`](#getinvariantviolations-1) | Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and returns every rule that is broken, without throwing. |
| [`EnsureInvariants()`](#ensureinvariants-1) | Runs the rules and `CheckInvariants()` seam of this aggregate and of every child entity it holds, and throws when it finds something broken. |

#### `CustomRoleAggregate(TRoleId, CustomRoleDraft, MembershipRules)`

```csharp
protected CustomRoleAggregate(TRoleId id, CustomRoleDraft draft, MembershipRules rules)
```

Makes a role from `draft`, in use. The application's class calls it from a constructor of its own, which sets what it added:

```csharp
public PlotRole(PlotRoleId id, GardenId garden, CustomRoleDraft draft) : base(id, draft, PlotMembership.Rules)
    => GardenId = garden;
```

**Parameters**

- `id`: The role's id.
- `draft`: Its name, what it is for, the permissions it gives, and the starter role it is made from, if any.
- `rules`: The rules of the resource the role is of.

**Exceptions**

- `ArgumentNullException`: `draft` or `rules` is null.
- `ArgumentException`: The rules do not say the resource's roles are kept, or the draft names a starter role the rules do not declare.
- `RefusalException`: `role-name-invalid` for a name that is blank or too long, or a description that is too long; `permission-not-for-members` for a permission the rules do not let a member's role give. Under the resource's codes.

#### `CustomRoleAggregate()`

```csharp
protected CustomRoleAggregate()
```

Parameterless constructor for persistence frameworks and serializers.

#### `Gives(string, MembershipRules)`

```csharp
public bool Gives(string permission, MembershipRules rules)
```

Whether the role gives `permission` to a member that holds it: it is in use, it holds the permission, and the resource's rules let a member's role give that permission. The last is asked again here, since the rules may have changed since the role was given its permissions.

**Parameters**

- `permission`: A permission.
- `rules`: The rules of the resource the role is of.

**Exceptions**

- `ArgumentNullException`: `permission` or `rules` is null.

#### `Rename(string, string?, MembershipRules)`

```csharp
public bool Rename(string name, string? description, MembershipRules rules)
```

Renames the role and says again what it is for, and calls the class's `OnRenamed()` when that changed anything.

**Parameters**

- `name`: Its new name; white space around it is dropped.
- `description`: What it is for, or nothing.
- `rules`: The rules of the resource the role is of.

**Returns**: Whether anything changed; `false` for the name and the description it has already.

**Exceptions**

- `ArgumentNullException`: `rules` is null.
- `RefusalException`: `role-archived`, `role-name-invalid`, under the resource's codes.

#### `SetPermissions(IReadOnlyCollection<string>, MembershipRules)`

```csharp
public RolePermissionsSet SetPermissions(
    IReadOnlyCollection<string> permissions,
    MembershipRules rules)
```

Sets the permissions the role gives, for every member that holds it. Every permission is one the resource's rules let a member's role give; white space around a permission is dropped, and a permission said twice is one permission. The class's `OnPermissionsSet(set)` is called when that changed anything.

**Parameters**

- `permissions`: Every permission the role gives from now on.
- `rules`: The rules of the resource the role is of.

**Returns**: The permissions that came in and the permissions that went out; neither for the permissions it has already.

**Exceptions**

- `ArgumentNullException`: `permissions` or `rules` is null.
- `RefusalException`: `role-archived`; `permission-not-for-members`, naming every permission the rules do not let a member's role give. Under the resource's codes.

#### `Archive(MembershipRules)`

```csharp
public void Archive(MembershipRules rules)
```

Archives the role: it stays, on every member that holds it, gives nothing from now on, and is not given again. The class's `OnArchived()` is called after.

**Parameters**

- `rules`: The rules of the resource the role is of.

**Exceptions**

- `ArgumentNullException`: `rules` is null.
- `RefusalException`: `role-archived`: it already is. Under the resource's codes.

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

### `CustomRoleAttribute<TRoleId, TResource>`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CustomRoleAttribute<TRoleId, TResource> : Attribute
    where TResource : class
```

Declares the role class of one of the application's resources: the roles a member of that resource can hold, kept as rows a customer makes and changes. The generator derives it from [`CustomRoleAggregate<TRoleId>`](#customrole-customroleaggregatetroleid), closed over the id given here:

```csharp
[CustomRole<PlotRoleId, Plot>]
public sealed partial class PlotRole
{
    public PlotRole(PlotRoleId id, CustomRoleDraft draft) : base(id, draft, PlotMembership.Rules) { }
}
```

A role is an aggregate of its own, beside the resource: it is made, renamed, given permissions and archived by itself, and a member of the resource holds it by its id (`[Member<PlotGardenerId, UserId, PlotRoleId, Plot>]`). The resource's rules say that its roles are made this way ([`Roles.Custom`](rules.md#customireadonlylistdeclaredrole-otherpermissions-string)).

The second type is the resource the roles are of. The role itself has no use for it: it is how the roles of a resource are found where its members are asked about, so an application with plots and with sheds declares a role class for each, and neither's roles are the other's. So the template may be declared more than once in a project.

The class is the application's: it adds what only it knows, the customer a role belongs to say, in properties of its own, and keeps the rows of one customer from another's by its own rule, as it does for any aggregate. The package assumes no such column.

**Type parameters**

- `TRoleId`: The role class's id, a `readonly partial record struct` marked `[EntityId<T>]`: what a member holds a role by.
- `TResource`: The resource the roles are of: the application's aggregate whose members hold them.

**Remarks**

Membership's Entity Framework package maps the class, through `AddDDDToolkitConventions()`, as `IsCustomRole` does: the template names that convention. A model that maps the class without that package in the application is refused while it is built.

A class that marks the organization its rows are of, with the marker the organization's package gives a domain class for it, gets two unique indexes from that mapping: a name once in an organization, and a starter role once in an organization. A customer of another kind is the application's own column, and its indexes are the application's to declare.

### `CustomRoleDraft`

```csharp
public sealed record CustomRoleDraft
```

What a new role of a resource is made from.

| Member | Summary |
|---|---|
| [`CustomRoleDraft(string, string?, IReadOnlyCollection<string>, string?)`](#customroledraftstring-string-ireadonlycollectionstring-string) | The primary constructor: the type's summary says what it makes. |
| `string Name { get; init; }` | The role's name, 1 to [`CustomRoleAggregate.MaxNameLength`](#customrole-customroleaggregatetroleid) characters; white space around it is dropped. |
| `string? Description { get; init; }` | What the role is for, at most [`CustomRoleAggregate.MaxDescriptionLength`](#customrole-customroleaggregatetroleid) characters, or nothing. |
| `IReadOnlyCollection<string> Permissions { get; init; }` | The permissions it gives: each one a permission the resource's rules let a member's role give. |
| `string? MadeFrom { get; init; }` | The name of the starter role it is made from, one of the roles the resource's rules declare, or `null` for a role a customer makes. The drafts of the starter roles carry it (the role manager's `CreateStarterRolesAsync`); a draft made from what a customer entered leaves it out. |

#### `CustomRoleDraft(string, string?, IReadOnlyCollection<string>, string?)`

```csharp
public CustomRoleDraft(
    string Name,
    string? Description,
    IReadOnlyCollection<string> Permissions,
    string? MadeFrom = null)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Name`: The role's name, 1 to [`CustomRoleAggregate.MaxNameLength`](#customrole-customroleaggregatetroleid) characters; white space around it is dropped.
- `Description`: What the role is for, at most [`CustomRoleAggregate.MaxDescriptionLength`](#customrole-customroleaggregatetroleid) characters, or nothing.
- `Permissions`: The permissions it gives: each one a permission the resource's rules let a member's role give.
- `MadeFrom`: The name of the starter role it is made from, one of the roles the resource's rules declare, or `null` for a role a customer makes. The drafts of the starter roles carry it (the role manager's `CreateStarterRolesAsync`); a draft made from what a customer entered leaves it out.

### `RolePermissionsSet`

```csharp
public sealed record RolePermissionsSet
```

What setting a role's permissions did: the permissions that came in and the permissions that went out. An application raises its own event from it, and says to whoever holds the role what changed for them.

| Member | Summary |
|---|---|
| [`RolePermissionsSet(IReadOnlyList<string>, IReadOnlyList<string>)`](#rolepermissionssetireadonlyliststring-ireadonlyliststring) | The primary constructor: the type's summary says what it makes. |
| `IReadOnlyList<string> Added { get; init; }` | The permissions the role gives now and did not before, in ordinal order. |
| `IReadOnlyList<string> Removed { get; init; }` | The permissions the role gave before and gives no more, in ordinal order. |
| `bool Changed { get; }` | Whether anything changed: setting the permissions a role has already changes nothing. |

#### `RolePermissionsSet(IReadOnlyList<string>, IReadOnlyList<string>)`

```csharp
public RolePermissionsSet(IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Added`: The permissions the role gives now and did not before, in ordinal order.
- `Removed`: The permissions the role gave before and gives no more, in ordinal order.

### `CustomRoleStatus`

```csharp
public enum CustomRoleStatus
```

Whether a role kept for a resource is in use. An archived role gives nothing, on whichever member holds it, and is not given again.

| Member | Summary |
|---|---|
| `Active = 0` | In use. |
| `Archived = 1` | No longer in use. It stays, so the members that hold it still hold something that has a name. |

### `CustomRoleAggregate.NameIsValid`

```csharp
public sealed class NameIsValid : IInvariant<CustomRoleAggregate<TRoleId>>
```

A role has a name of 1 to [`CustomRoleAggregate.MaxNameLength`](#customrole-customroleaggregatetroleid) characters with no white space around it, and a description of at most [`CustomRoleAggregate.MaxDescriptionLength`](#customrole-customroleaggregatetroleid). Everything that names a role refuses another name before anything changes; this is the net under code that got round it.

It reports the package's own name for the rule, [`MembershipRefusals.RoleNameInvalid`](refusals.md#membershiprefusals), and not a code of the resource: a role does not know whose it is.

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(CustomRoleAggregate<TRoleId>)`](#checkcustomroleaggregatetroleid) | Runs the rule. |

#### `Check(CustomRoleAggregate<TRoleId>)`

```csharp
public InvariantFailure? Check(CustomRoleAggregate<TRoleId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.

### `CustomRoleAggregate.PermissionsNormalized`

```csharp
public sealed class PermissionsNormalized : IInvariant<CustomRoleAggregate<TRoleId>>
```

A role's permissions are distinct, not blank, and in ordinal order: the one way a set of permissions is written down, so two roles that give the same permissions hold the same list. Setting a role's permissions writes them so; this is the net under code that got round it.

Whether each permission is one a member's role can give is not asked here. That is the resource's rules to say, which a role does not carry, and it is asked where the permissions are set and again where they are read. It reports the package's own name for the rule, [`MembershipRefusals.RolePermissionsNotNormalized`](refusals.md#membershiprefusals).

| Member | Summary |
|---|---|
| `string Code { get; }` | A stable machine-readable identifier for this rule, so a caller can branch on it without matching on text and without the message becoming part of your API. It is also what `DDDToolkit.Analyzers` reads to report two rules on one entity sharing a code. |
| [`Check(CustomRoleAggregate<TRoleId>)`](#checkcustomroleaggregatetroleid-1) | Runs the rule. |

#### `Check(CustomRoleAggregate<TRoleId>)`

```csharp
public InvariantFailure? Check(CustomRoleAggregate<TRoleId> entity)
```

Runs the rule. Returns `null` when it holds, and otherwise what is wrong in the domain's own words, free to name the values that broke it. A string converts to `InvariantFailure`, so a rule with only a message returns the message; one that names values adds them with `InvariantFailure.With` so the message can be translated.

Not an `InvariantViolation` so that holding is free: this runs for every changed entity on every save, and the happy path returns `null` and allocates nothing. The caller pairs the failure with `IInvariant.Code`, so the code lives in one place.

**Parameters**

- `entity`: The object to check. Never `null`.
