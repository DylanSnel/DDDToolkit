---
title: "Membership: permissions and roles"
sidebar_label: Permissions and roles
---

# Membership: permissions and roles

Read this page when you declare permissions or write the commands that give roles. It covers the settings of a
permission, who holds a permission, who may give a role, and roles a customer makes and changes through the role
manager.
[Tutorial step 2](tutorial.md#step-2-the-permissions-and-the-rules) introduces the levels and a first set of
permissions.

## The settings of a permission

A permission is a public constant in a static `[Permissions]` class. The attribute on the constant is its **level**,
which says where the permission is asked:

- `[On<Document>]` asks it on one document.
- `[AtUnit]` and `[AtTenant]` ask it at a unit or for the tenant. They come with Tenancy.

One class may mix levels. On a resource, `[On<T>]` also says how the permission is held:

| Setting | Default | Change it |
|---|---|---|
| `Owner` | The owner holds the permission by owning the resource. | Set `false` when the owner should hold it only the way anybody else does, through a role or from above. Use it for a permission such as naming another owner. |
| `EveryMember` | Being a member gives nothing. | Set `true` to let every member hold it, whatever their roles. This is usually the permission that sees the resource. One permission at most. |
| `InRoles` | A role may give it. | Set `false` when only the owner and reach from above may hold it. Use it for a permission a customer must never hand out. A fixed role that lists it is refused when the rules are made. |
| `ManagesMembers` | It manages nothing. | Set `true` on the permission that adds and removes members and gives and takes their roles. The [member manager](resources-and-members.md#the-member-manager) requires it. On Postgres, a policy also lets only callers with this permission write the member rows ([Who writes the member rows](postgres.md#who-writes-the-member-rows)). One permission at most. |
| `NamesOwner` | It names nobody. | Set `true` on the permission that hands the resource to another owner. The member manager requires it. On Postgres, a trigger also lets only callers with this permission change the owner column. One permission at most. |
| `FromAbove` | The permission reaches from above, where the rules allow reach from above. | Set `false` when holding it where the resource hangs should give nothing on the resource ([Reach from above](beside-an-organization.md#reach-from-above)). When the rules reach from above, the `EveryMember` permission must keep `FromAbove = true`. |
| `Kinds` | Only kinds of seat inside the organization may hold it. | List the kinds that may hold it, by their value or as `SeatKinds.InOrganization` and `SeatKinds.OutsideOrganization` for every kind on that side. The list is complete, so list `SeatKinds.InOrganization` too where the kinds inside should keep it ([Kinds of seat and role sets](#kinds-of-seat-and-role-sets)). `Kinds` exists on `[On<T>]` only. |

Every level also takes these settings:

- **`Description`** says what the permission lets somebody do. An administrator reads this English text where roles
  are made. Without it, the constant's `/// <summary>` is used, with or without a documentation file. A permission
  with neither is [DDD00099](../diagnostics.md#ddd00099), a warning. A retired permission needs neither.
  Translations go in a resx under the permission's name ([Localization](../localization.md)).
- **`Implies`** lists the permissions that come with this one. A role with only this permission also does what the
  implied permissions allow. It works one step deep. An implied permission implies nothing further.
- **`Retired = true`** means that nobody holds the permission and no role gets it again. Roles that list it keep
  it, so no stored right breaks. Retire a permission instead of deleting it.

| Mistake | Reported as |
|---|---|
| A `[Permissions]` class that is not static, a constant without a level, or a permission declared twice | [DDD00092](../diagnostics.md#ddd00092) |
| `[On<T>]` on a type that is not a resource with members, or two permissions marked `EveryMember`, `ManagesMembers` or `NamesOwner` | [DDD00093](../diagnostics.md#ddd00093) |
| Rules for a resource with no permission declared on it | [DDD00094](../diagnostics.md#ddd00094), where the rules are made |
| A permission marked `ManagesMembers` or `NamesOwner` that nobody could hold: not the owner, not every member, no role and nothing above | An `ArgumentException` when the rules are made |
| `Kinds` that is empty, lists a blank entry or one entry twice, or lists a value that is no kind of seat | [DDD11001](../diagnostics.md#ddd11001) |
| Role sets that do not hold together, such as a kind in two sets or a role that gives what its set's kinds may not hold | An `ArgumentException` when the rules are made ([Kinds of seat and role sets](#kinds-of-seat-and-role-sets)) |

## Who holds a permission

The five ways a caller holds a permission are listed in the
[overview](overview.md#who-holds-a-permission-on-a-board). These rules follow from them:

- **The owner holds no role.** The owner holds the permissions marked for the owner. So `[OrOwn]` beside
  `[OnResource]` adds nothing for a permission the owner holds ([DDD00096](../diagnostics.md#ddd00096)).
- **A role gives the permissions it lists only where a role may give them.** Those are the permissions marked
  `InRoles`, and permissions of other modules that `otherPermissions` lets through.
- **A member holds a permission only when its kind may hold it.** The kind on the member's row is checked against the
  permission's `Kinds`, whatever the member's roles give. Reach from above checks the caller's own kind against the
  same list. The owner counts as inside the organization ([Kinds of seat and role sets](#kinds-of-seat-and-role-sets)).
- **Refusals come in a fixed order:**
  1. the caller's own refusal, when the caller is nobody;
  2. `not-found` for a resource the caller does not see, the same as for a resource that does not exist;
  3. `not-permitted`, naming the permission;
  4. a version conflict, 409, last.
- **Who the caller is as a member follows from the member id.** For an id over a `Guid`, the member is the user id
  of the token. For Tenancy's seat id, the member is the caller's seat. For other ids, the rules name the source.
  `members: MemberSource.Claim("app_metadata.staff")` reads a claim of the token, for an id over a text. A claim
  under `user_metadata` is refused, because users write that part of the token themselves.
  `MemberSource.Resolved(...)` takes an id [you resolve](beside-an-organization.md#an-organization-of-your-own).
- **A caller who did not sign in is nobody's member** and holds nothing from above. Nothing is read for that caller.
  A token without the member id or the claim reaches no resource either, unless a permission held above reaches it.

## Who may give a role

The member manager requires one permission for adding a member, giving or taking a role, and taking a member off:
the one you mark `ManagesMembers`. Naming another owner requires the one you mark `NamesOwner`. The package has no
rule about who gives what to whom.

- **The request usually requires the same permission on the resource.**
  [`ShareDocument`](resources-and-members.md#a-document-and-the-people-it-is-shared-with) requires
  `documents.share`, the permission marked `ManagesMembers`. The manager then uses the check's answer. A caller with
  that permission shares with anyone, in any role, for any period. The owner holds it by owning. A member holds it
  when a role gives it.
- **Write any further rule in the handler**, over what the resource's access manager returns in one statement, and
  call the manager after it. The handler takes `MembershipAccessManager<Folder>` and `FolderMemberManager`. This
  handler refuses to put staff on a folder for longer than the caller holds `folders.staff`:

```csharp
public sealed record AdmitStaff([OnResource("folders.staff")] FolderId Folder, StaffCode Staff, NamedRole Role, DateTimeOffset? Until, [ExpectedVersion] long? ExpectedVersion = null) : IFilingRequest;

// In the handler. folderAccess is the MembershipAccessManager<Folder>, and staff the FolderMemberManager, the handler takes.
var hold = await folderAccess.RequireAsync(command.Folder, "folders.staff", cancellationToken);
if (hold.Until is { } mine && (command.Until is not { } theirs || theirs > mine))
{
    throw new RefusalException("folders.longer-than-held", RefusalKind.NotPermitted,
        "Nobody is put on a folder for longer than you hold the permission to put them there.");
}

return await staff.AddAsync(command.Folder, command.Staff, command.Role, command.Until, cancellationToken, command.ExpectedVersion);
```

- **[`MemberHold.Until`](reference/managers.md#until)** is the first moment the caller no longer holds the
  permission, as things stand now. It is `null` for the owner, for system work, and when the
  permission is not held.
  - Through membership alone, `Until` is the end of the membership.
  - Through a role, `Until` is the end of the last role that gives the permission now, or the end of the membership
    if that comes sooner. A role that only starts later does not extend it.
  - From above, the caller holds the permission
    [for as long as the caller sees the resource](beside-an-organization.md#reach-from-above).
- **`MemberHold.Via`** says how the caller holds the permission (`Members`, `Above` or `System`), for a rule that
  depends on it.
- **The package adds no rule of this kind.** Neither "no longer than your own hold" nor "never to yourself" is built
  in.
- **The permission's `Kinds` say which kinds manage members.** A permission marked `ManagesMembers`, or named as a
  set's `membersManagedWith`, is held by the kinds its `Kinds` lists, as every permission is. Left out, the list holds
  the kinds inside the organization alone, so only seats inside the organization manage members
  ([A kind outside the organization that manages members](#a-kind-outside-the-organization-that-manages-members)).
- **With several role sets, each set has its own permission.** Adding a member, and giving or taking its roles,
  requires the permission of the set the member's kind is in ([Kinds of seat and role sets](#kinds-of-seat-and-role-sets)).

## Kinds of seat and role sets

With Tenancy, every seat has a kind, inside the organization or outside it
([Kinds of seat](../tenancy/your-module.md#kinds-of-seat)). Without Tenancy, every member is `member`, inside the
organization. Membership uses the kind in two ways:

- **A permission lists the kinds that may hold it.** A member whose kind is not in the list does not hold the
  permission, whatever its roles give.
- **A role set groups the roles for some kinds.** A member is in the one set its kind is in, and gets only roles of
  that set. Each set has its own permission that manages its members.

Harbor Works has employees, and invites Mira, a client's site manager, as a `guest`. On the board Crane schedule at
North, Rhea is the owner and Leo an editor. Mira joins as a visitor in a second role set, Guests. Ada holds her role at
the root, so she reaches the board from above.

| | view | edit | members.manage | guests.manage |
|---|---|---|---|---|
| Rhea (owner) | yes | yes | yes | yes |
| Leo (`editor`) | yes | yes | no | no |
| Ada (from above) | yes | yes | yes | yes |
| Mira (`visitor`) | yes | no | no | no |
| Mira, with `editor` written by SQL past the application | yes | no | no | no |

The last row is the point of the list on a permission. `boards.edit` lists no kinds, so only kinds inside the
organization hold it, and Mira's member row says `guest`.

**`Harbor.Boards.Application/Access/BoardPermissions.cs`**

```csharp
[Permissions]
public static class BoardPermissions
{
    [On<Board>(EveryMember = true, Kinds = [SeatKinds.InOrganization, HarborSeatKinds.Guest], Description = "See a board and who is on it")]
    public const string View = "boards.view";

    [On<Board>(Description = "Change a board")]                                     // Kinds left out: inside the organization only
    public const string Edit = "boards.edit";

    [On<Board>(InRoles = false, ManagesMembers = true, Description = "Say who is on a board, and in which role")]
    public const string ManageMembers = "boards.members.manage";

    [On<Board>(InRoles = false, Description = "Say which guests are on a board")]
    public const string ManageGuests = "boards.guests.manage";

    [AtTenant(Description = "Make and change the roles of boards")]
    public const string ManageRoles = "boards.roles.manage";
}
```

**`Harbor.Boards.Application/Access/BoardMembership.cs`**

```csharp
public static class BoardMembership
{
    public static MembershipRules Rules { get; } = MembershipRules.For<Board>(
        Roles.Custom(
            sets:
            [
                RoleSet.Named("Team", kinds: [SeatKinds.InOrganization],
                    starters: [new("editor", [BoardPermissions.View, BoardPermissions.Edit])]),
                RoleSet.Named("Guests", kinds: [HarborSeatKinds.Guest],
                    starters: [new("visitor", [BoardPermissions.View])],
                    membersManagedWith: BoardPermissions.ManageGuests),
            ],
            managedWith: BoardPermissions.ManageRoles));
}
```

What the code does:

- **`Kinds`** on `View` lets guests hold it beside the kinds inside the organization. `Edit` lists no kinds, so a
  guest never holds it. `SeatKinds.InOrganization` and `SeatKinds.OutsideOrganization` stand for every kind on that
  side. Membership knows these two values and `member`, and nothing else of Tenancy.
- **`RoleSet.Named`** declares a set with a name, its kinds and its roles: `starters` under `Roles.Custom`, `roles`
  under `Roles.Fixed(sets: [...])`. Rules that declare no sets have one set, for every kind inside the organization.
- **The first set** is managed with the permission marked `ManagesMembers`. Every other set names its own permission
  with `membersManagedWith`. Without a `ManagesMembers` permission, only system work changes the first set's members,
  in the member manager and on Postgres.
- **`AddAsync`** reads the member's kind first, finds its set, and requires that set's permission. A role given must be
  in that set. A kind in no set is refused with `boards.kind-in-no-set`.

Each way of holding a permission is filtered by the kind:

| Way | Filtered by |
|---|---|
| Every member (`EveryMember`), and a role | The kind on the caller's member row: the permission's `Kinds` must hold the kind, or its side |
| Owner | The permission's `Kinds` must hold `SeatKinds.InOrganization`. An owner is always a seat inside the organization. |
| From above | The caller's own kind: the permission's `Kinds` must hold the kind, or its side. A caller without a kind holds nothing from above. |
| System work the rules name | Not filtered |

- **The caller's kind comes from `ICurrentMember<TResourceId, TMemberId>.KindOf`.** The join with Tenancy answers the
  kind of the caller's seat, as tenant selection read it, so the check sends no extra statement. On Postgres the
  functions ask `seat_to_admit` for the caller's seat, once for each statement
  ([The caller's kind under reach from above](postgres.md#the-callers-kind-under-reach-from-above)). Without a package
  that keeps seats, every caller is `member`, inside the organization. An application that resolves its own members
  and keeps kinds implements `KindOf` beside `Find`.
- **Tenancy places only seats inside the organization at a unit.** A place of your own may answer a seat outside the
  organization. Such a seat holds a permission on what hangs there only when the permission's `Kinds` lists its kind
  or its side.

- **Seeing follows the filter.** A member sees the board only when its kind may hold the permission every member holds.
  Rules without such a permission keep seeing for every member.
- **Being a member is not filtered.** `as_member` and the fellow members answer every member, whatever its kind.
- **A permission of another resource** that a role gives through `otherPermissions` has the `Kinds` that the resource
  that declares it lists. A permission that no resource with members declares counts as inside the organization only
  ([A permission of another resource](#a-permission-of-another-resource)).

**Rules the sets follow.** The rules check these when they are made, and throw an `ArgumentException` that names the
set, the permission or the entry:

- A set name converts as a `[Members]` name does, has at most 64 characters, and is used once.
- A set lists at least one kind, each once. An entry is `SeatKinds.InOrganization`, `SeatKinds.OutsideOrganization`
  or a value of lower case letters, digits and dashes that starts with a letter.
- A kind is in one set at most. The same entry in two sets is refused.
- A role name is used once across all sets.
- A role in a set gives only permissions that every kind of the set may hold. The check compares the entries as they
  are written, so a set of `[employee]` with a permission of `[SeatKinds.InOrganization]` is refused. List the group in
  the set instead.

**The start-up check.** The rules name kinds by value without knowing them, and cannot tell which side a kind is on.
With Tenancy, the generated join adds the start-up check `membership.kinds-fit-the-catalogue.<name>`, such as
`membership.kinds-fit-the-catalogue.boards`. It calls `rules.ProblemsWith(kinds)` with the catalogue's kinds of seat,
and stops the host for each problem it finds:

- a permission or a set that names a kind the catalogue does not declare;
- a kind in two sets, through the group of its side in one set and by its value in the other.

### A permission of another resource

A role may give a permission that another resource declares, through `otherPermissions`
([Changing a role](#changing-a-role)). The rules take the `Kinds` of such a permission from the resource that declares
it. Harbor's cards hang at their board, and a card is reached from its board from above
([A resource at several places](beside-an-organization.md#a-resource-at-several-places)). Mira's role on the board,
`visitor`, also gives `cards.view`. She then sees every card of the board without a member row on any card.

**`Harbor.Cards.Application/Access/CardPermissions.cs`**

```csharp
[Permissions]
public static class CardPermissions
{
    [On<Card>(EveryMember = true, Kinds = [SeatKinds.InOrganization, HarborSeatKinds.Guest], Description = "See a card")]
    public const string View = "cards.view";

    [On<Card>(Description = "Move a card")]                                       // Kinds left out: inside the organization only
    public const string Move = "cards.move";
}
```

**`Harbor.Boards.Application/Access/BoardMembership.cs`**

```csharp
public static class BoardMembership
{
    public static MembershipRules Rules { get; } = MembershipRules.For<Board>(
        Roles.Custom(
            sets:
            [
                RoleSet.Named("Team", kinds: [SeatKinds.InOrganization],
                    starters: [new("editor", [BoardPermissions.View, BoardPermissions.Edit])]),
                RoleSet.Named("Guests", kinds: [HarborSeatKinds.Guest],
                    starters: [new("visitor", [BoardPermissions.View, CardPermissions.View])],
                    membersManagedWith: BoardPermissions.ManageGuests),
            ],
            otherPermissions: OtherPermissions.All,
            managedWith: BoardPermissions.ManageRoles));
}
```

What the code does:

- **The board's rules read the card's list.** `MembershipRules.KindsOf("cards.view")` on the board answers
  `[SeatKinds.InOrganization, "guest"]`, the list on `[On<Card>]`. Mira's member row on the board says `guest`, so her
  role gives her `cards.view` on the board. Every card of the board is then reached from above. Reach from above
  checks her own kind on the card against the same list.
- **A permission whose `Kinds` leave the kind out stays refused.** `cards.move` lists no kinds, so only kinds inside
  the organization hold it, on the card and on the board. A starter role of the guests' set that lists it stops the
  host at start-up. A role a customer makes with it is refused with `boards.permission-not-for-set`. A role row
  written past the application gives Mira nothing of it, in C# and on Postgres.
- **The kinds are read when a question asks for them.** The cards' module references the boards' module for the
  `BoardId`, and the boards' module does not reference the cards'. So the board's rules can be made before the cards'
  module declares `cards.view`. The rules check a starter role's own permissions when they are made. They check its
  permissions of another resource in the start-up check `membership.permissions-of-other-resources`, which the
  registration of every resource adds once, and which the host runs with `services.RunStartupChecks()`.
- **A permission that no resource with members declares** counts as inside the organization only. So does every
  permission the rules do not declare where the roles take no permissions of other modules.
- **Two resources may declare one permission with different `Kinds`.** The board's rules then have two lists for one
  permission. The start-up check, the export and `KindsOf` refuse it with both lists in the message. Give both
  declarations the same `Kinds`, or keep the permission from the board's roles with `OtherPermissions.AllBut(...)`.
- **On Postgres**, the export writes the card's list into the board's functions, beside the board's own lists
  ([The kind filter in the functions](postgres.md#the-kind-filter-in-the-functions)).

### A kind outside the organization that manages members

A kind outside the organization manages members when the permission that manages them lists that kind. The package
has no setting for it. You open it with three things you write anyway:

- **The permission lists the kind.** The managing permission's `Kinds` names the kind beside
  `SeatKinds.InOrganization`. The owner holds it only while the list holds that group. Reach from above gives it to a
  caller whose own kind is on the list.
- **A role of the kind's set gives the permission.** A role holds only permissions every kind of its set may hold, so
  the rules refuse the role while the list leaves the kind out.
- **Your handler makes the change.** Its request requires the permission with `[OnResource]`, and the handler calls
  the member manager, which requires the same permission. The tutorial's `AddGuestCommand` is such a request
  ([Step 5](people-from-outside.md#step-5-mira-on-quay-plan)).

On Harbor's boards, a guest who holds `guest-host` adds other guests:

```csharp
[On<Board>(Kinds = [SeatKinds.InOrganization, HarborSeatKinds.Guest], Description = "Say which guests are on a board")]
public const string ManageGuests = "boards.guests.manage";

RoleSet.Named("Guests", kinds: [HarborSeatKinds.Guest],
    starters: [new("visitor", [BoardPermissions.View]), new("guest-host", [BoardPermissions.View, BoardPermissions.ManageGuests])],
    membersManagedWith: BoardPermissions.ManageGuests)
```

- **A guest manages the Guests set alone.** `boards.members.manage` lists no kinds, so a guest never adds a member of
  Team.
- **Without `HarborSeatKinds.Guest` in the list, nothing changes.** The rules refuse `guest-host`, and no role gives a
  guest the permission.
- **A guest adds a seat it does not read.** While `NarrowOutsideSeats` is on, a guest reads its own seats and the
  seats of its fellow members alone
  ([What a seat outside the organization reads](../tenancy/postgres.md#what-a-seat-outside-the-organization-reads)).
  The member manager reads the new member's one seat by its id, past that. It asks whether the seat is an active seat
  of the caller's tenant, and its kind, and nothing else.
- **On Postgres the kind guard asks the same of the database.** `seat_to_admit(seat)` answers the one seat of the
  caller's tenant, as its owner, so the guard lets a guest's member row through for a seat the guest does not read.
- **What the guest reads does not change.** Once the new member is on the board, it is a fellow member, so the guest
  reads its seat from then on.
- **A guest brings in a new person through your handler.** It issues the invitation as system work, after your own
  check ([An invitation your own code issues](../tenancy/invitations.md#an-invitation-your-own-code-issues)).

## Roles a customer makes

| The rules say | A member holds a role by | Who makes the roles | What a role gives is read from |
|---|---|---|---|
| `Roles.Fixed([...])` | its name, a `NamedRole` | you, in code | the rules |
| `Roles.Custom(...)` | the id of your role class | a customer, while the application runs | the role's row, in your context |
| `Roles.External(...)` | the id in the store that keeps it | whoever keeps the roles | what you [return](beside-an-organization.md#three-settings-of-the-rules) |

A custom role is given on one resource and counts there alone. The tutorial writes the role class
([step 1](tutorial.md#step-1-the-board-its-members-and-its-roles)), leaves its mapping and unique indexes to the
conventions ([step 3](tutorial.md#step-3-where-it-is-stored)), and gets the starter roles from what the generator writes
for `AddBoardMembershipWithTenancy` ([step 7](tutorial.md#step-7-members)).

### Starter roles

- **Starter roles are the roles a scope begins with.** You name them in `Roles.Custom(starters: [...])`. A scope is a
  tenant, a customer of yours, or the whole application.
- **Each starter role is made once.** The role manager recognises a role that was made from a starter role by its
  `FromStarterRole` value. A renamed or archived role keeps that value, so it is not made again. A unique index over the
  scope column and `FromStarterRole` stops two requests at the same moment from both making it
  ([Which customer a role belongs to](#which-customer-a-role-belongs-to)).
- **No starter role is reserved for the owner.** The owner holds no role. A customer may rename, archive and change
  every starter role. A scope with no roles still has resources and owners.
- **`FromStarterRole` and `PermissionsFromStarterRole` are `[Bookkeeping]`.** A GraphQL type that binds by convention
  leaves them out ([Personal data and bookkeeping](../graphql.md#personal-data-and-bookkeeping-are-left-out)).
- **A starter role belongs to the set it is declared in.** The role row made from it stores the set in `Set`, which is
  `null` for the first set and never changes. `Set` is `[Bookkeeping]` too. A starter role that the rules move to another
  set no longer matches its rows. `FollowStarterRolesAsync` lists such a row in `WithoutTheirStarterRole` and leaves it.
- **A role a customer makes names its set** in `CustomRoleDraft.Set`, by the set's name. An unknown set is refused with
  `boards.set-unknown`. A permission that a kind of the set may not hold is refused with `boards.permission-not-for-set`.
  One `managedWith` permission changes the roles of every set.

### Starter roles with Tenancy

In an application joined to Tenancy, the toolkit knows when a tenant exists. So you write no request for the starter
roles. `BoardRole` and its constructor stay as they are, and so do `builder.Services.SyncStarterRoles()` and
`services.AddBoardMembershipWithTenancy<BoardsContext>(BoardMembership.Rules)`. A new migration adds the column
`PermissionsFromStarterRole`.

| What changes | Effect on the tenant's role |
|---|---|
| A new tenant is provisioned | It has `editor` right after `ProvisionAsync`. |
| A host starts, and a tenant lacks a starter role | The role is made. |
| The starter role gains a permission | The role gains it. |
| The starter role loses a permission | The role loses it. |
| The tenant took a permission out, and the starter role still holds it | It stays out. |
| The tenant added a permission itself | It stays. |
| A role with no record yet, made before this column existed | It gets what it lacks and loses nothing. |
| The tenant archived the role | It follows nothing, and is not made again. |
| The tenant renamed the role | It follows. |
| The rules no longer declare the starter role | The role keeps its permissions, and the report counts it. |

The rules are the same as for a role of Tenancy
([How a role follows its starter role](../tenancy/roles-and-permissions.md#how-a-role-follows-its-starter-role)). A role
stores what the starter role gave it in `PermissionsFromStarterRole`, and `FollowStarterRole(rules)` compares that list with
the rules:

- A permission the rules no longer let a role give (`InRoles`) stays on the row. `Gives` answers `false` for it, because
  `Gives` asks the rules each time. If the rules allow it again, the role gives it again. `SetPermissions` refuses that
  permission, so a customer that sets the role's permissions leaves it out.
- A permission whose starter role lost it, and that the customer took out earlier, comes back if the starter role gains it
  again.
- `OnPermissionsSet(set)` runs when a permission came in or went out. A change of the stored list alone runs no hook.

**How the role is made.** The generator writes a class for a member whose role id belongs to a class declared with
`[CustomRole<TRoleId, TResource>]`, and the registration adds it.

- It calls the role class's public constructor, found by its parameter types in any order: the role id, the tenant id,
  `CustomRoleDraft`, and optionally `MembershipRules`. It makes the id with `BoardRoleId.Create()`.
- The optional parameter `makeRole` of the registration takes over, for names, descriptions or ids of your own:

  ```csharp
  services.AddBoardMembershipWithTenancy<BoardsContext>(BoardMembership.Rules, makeRole: (provider, tenant, draft) =>
      new BoardRole(BoardRoleId.Create(), tenant, draft with { Name = "Editor", Description = "Edits a board" }, BoardMembership.Rules));
  ```

- Where no constructor fits, or the role id has no `Create()`, `makeRole` is a required parameter, so the compiler reports
  it at the call. A module without the `TenancyIds` line names the tenant id at the call, so the generator cannot match the
  constructor, and `makeRole` is required there too.
- A member that holds the tenant's own roles, or named roles, gets none of this. Rules with no starter roles do nothing.
- The role manager reads the roles through the role class's own filter. So a role class kept per tenant marks its tenant
  `[OfTenant]`. Without the marker, the roles of every tenant count as the current tenant's.

**When it runs.** Tenancy runs it through
[`ITenantStarterRoles`](../tenancy/roles-and-permissions.md#starter-roles-of-another-module), as system work in the tenant,
in a scope of its own.

- It saves in another transaction than the tenant. A failure after the tenant's save does not fail `ProvisionAsync`. It is
  logged as an error, and the next `SyncStarterRoles()` makes the role.
- Making and following are one save per tenant. A starter role whose name the tenant already gave another role fails that
  save, because of the unique index on the name. The report names the tenant at each start, until the tenant renames that
  role.
- On Postgres, Tenancy's policy "System work in its tenant" lets this work read and write the role table. Your rules on the
  table are for signed-in callers.
- A seat with the permission that manages the roles can write `PermissionsFromStarterRole` with its own SQL statement, since
  the export grants the column with the others. It gains nothing that way, because that seat can set the role's permissions
  already.

### Starter roles without Tenancy

The toolkit cannot know when one of your scopes begins, so your code says it.

- `CreateStarterRolesAsync(make)` makes the starter roles a scope does not have yet. It passes your function a draft for
  each, and saves the roles your constructor makes from them. It requires the permission the rules name in `managedWith`.
- `FollowStarterRolesAsync(make)` does that and makes the rest follow, in one save, and saves only when something changed.
  Only system work runs it. A seat gets `access.system-only`. It answers `Made`, `Changed` and `WithoutTheirStarterRole`.
  Both take `scope`, for a role class kept per a scope of your own:

  ```csharp
  using (access.BeginSystem("gardens"))
  {
      var followed = await roles.FollowStarterRolesAsync(
          draft => new PlotRole(PlotRoleId.Create(), garden, draft), cancellationToken, scope: role => role.GardenId == garden);
  }
  ```

### Changing a role

The role manager has three changes. Each loads the role, checks the caller, changes the role, saves it and returns
it:

```csharp
// roles is the BoardRoleManager the handler takes
var renamed = await roles.RenameAsync(command.Role, "Lead editor", "Edits and plans a board.", cancellationToken);   // the name and the description as they should be
var narrowed = await roles.SetPermissionsAsync(command.Role, [BoardPermissions.View, BoardPermissions.Edit], cancellationToken);
var archived = await roles.ArchiveAsync(command.Role, cancellationToken);
```

- **Who may change a role** is the permission the rules name in `managedWith`
  ([The role manager](resources-and-members.md#the-role-manager)). Without it, only system work changes roles.
- **For your own events, implement the role's hooks**: `OnRenamed()`, `OnPermissionsSet(set)` with `set.Added` and
  `set.Removed`, and `OnArchived()`. Each runs only when the role changed.
- **The role checks every change against the rules.** A role row does not store them. The rules say what a role may
  give, and which codes it refuses with.
- **A role gives only permissions a role may give** (`InRoles`). Other permissions are refused with
  `boards.permission-not-for-members`, which names them. The rules are checked again whenever a role is read. So a
  permission later marked `InRoles = false` is given by no role, whatever the row holds.
- **Permissions of another module**, such as an inspection's permission on a project, need `otherPermissions`, as in
  `Roles.Custom(starters: [...], otherPermissions: OtherPermissions.AllBut(...))`. This lets them through, except
  the permissions named, the permissions the resource's own classes declare at another level, and the permissions
  marked `InRoles = false` ([`OtherPermissions`](reference/rules.md#otherpermissions)). `OtherPermissions.All` names
  no permission to leave out. The kinds that may hold such a permission come from the resource that declares it
  ([A permission of another resource](#a-permission-of-another-resource)).
- **A name is trimmed and has 1 to 120 characters.** A description has at most 1000. Both are refused with
  `boards.role-name-invalid`, which names the field. `RenameAsync` takes both values as they should be, so `null`
  clears the description.
- **A name is unique through a unique index** over the scope column and `Name`
  ([Which customer a role belongs to](#which-customer-a-role-belongs-to)). `RefusesAs` on that index gives a second
  role with the same name your refusal code, for example
  `role.HasIndex(row => new { row.TenantId, row.Name }).RefusesAs("boards.role-name-taken", "Another role is called {Name} already.")`.
- **An archived role** gives nothing from then on, and nobody can give it again. It can no longer be changed
  (`boards.role-archived`). It stays on the members that hold it, so what they held keeps a name. Roles are never
  deleted.
- **`FromStarterRole` cannot change after insert** (`IsFixedAfterInsert`). A save that changes it is refused, and the
  exported privileges leave the column out of `UPDATE`.
- **A role that is not there for the caller** is refused with `boards.role-not-found`, the same as a role that does
  not exist.

### Which customer a role belongs to

- **The customer column is yours.** The package has no customer or tenant column. Add the column and set it in your
  constructor.
- **With Tenancy, mark the column `[OfTenant]`**, as the tutorial's `BoardRole` does. The marker comes from
  `Supporting.Tenancy.Abstractions`. Tenancy's convention then adds the tenant filter and the save check. Membership's
  mapping adds two unique indexes: the tenant and `Name`, and the tenant and `FromStarterRole`. Entity Framework names them
  `IX_BoardRoles_TenantId_Name` and `IX_BoardRoles_TenantId_FromStarterRole`. No Membership package references Tenancy. The
  mapping finds the marker by its type name.
- **A customer of another kind**, such as a garden, gets no index from the package. Keep customers apart with a query
  filter or a [row access rule](../row-level-security.md#row-access-rules-written-in-c), and declare the two indexes
  in your context. The convention still maps the role:

  ```csharp
  modelBuilder.Entity<PlotRole>(role =>
  {
      role.HasIndex(row => new { row.GardenId, row.Name }).IsUnique().RefusesAs("plots.role-name-taken", "Another role is called {Name} already.");
      role.HasIndex(row => new { row.GardenId, row.FromStarterRole }).IsUnique();
  });
  ```

- **The managers read through the request's context**, so your filter applies. Another customer's role does not
  exist there. Your filter also applies where a role's permissions are read
  ([Access in your own queries](checks-and-saves.md#access-in-your-own-queries)).
- **Without customers**, there is one set of roles. Declare the two unique indexes over `Name` alone and `FromStarterRole`
  alone.
- **To find roles another way**, register your own `IMemberRoles<TResourceId, TRoleId>` before the resource.
- **The roles are read where they are stored.** A subquery over your role table runs inside the statement that reads
  the members. On Postgres the functions join the role table, and you register nothing
  ([in the database](postgres.md#roles-a-customer-makes-in-the-database)).
- **A second resource** declares its own role class, rules and mapping. The roles of one resource are no roles of the
  other.
