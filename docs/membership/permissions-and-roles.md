---
title: "Membership: permissions and roles"
sidebar_label: Permissions and roles
---

# Membership: permissions and roles

Read this page when you declare permissions or write the commands that give roles. It covers the settings of a
permission, who holds a permission, who may give a role, and roles a customer makes.
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
| `LocksMembers` | It locks nothing. | Set `true` on the permission your commands require to change the members. On Postgres, a policy then lets only callers with this permission write the member rows ([Who writes the member rows](postgres.md#who-writes-the-member-rows)). One permission at most. |
| `LocksOwner` | It locks nothing. | Set `true` on the permission your command requires to hand the resource on. On Postgres, a trigger then lets only callers with this permission change the owner column. One permission at most. |
| `FromAbove` | The permission reaches from above, where the rules allow reach from above. | Set `false` when holding it where the resource hangs should give nothing on the resource ([Reach from above](beside-an-organization.md#reach-from-above)). When the rules reach from above, the `EveryMember` permission must keep `FromAbove = true`. |

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
| `[On<T>]` on a type that is not a resource with members, or two permissions marked `EveryMember`, `LocksMembers` or `LocksOwner` | [DDD00093](../diagnostics.md#ddd00093) |
| Rules for a resource with no permission declared on it | [DDD00094](../diagnostics.md#ddd00094), where the rules are made |
| A locking permission that nobody could hold: not the owner, not every member, no role and nothing above | An `ArgumentException` when the rules are made |

## Who holds a permission

The five ways a caller holds a permission are listed in the
[overview](overview.md#who-holds-a-permission-on-a-board). These rules follow from them:

- **The owner holds no role.** The owner holds the permissions marked for the owner. So `[OrOwn]` beside
  `[OnResource]` adds nothing for a permission the owner holds ([DDD00096](../diagnostics.md#ddd00096)).
- **A role gives the permissions it lists only where a role may give them.** Those are the permissions marked
  `InRoles`, and permissions of other modules that `otherPermissions` lets through.
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

The package has no permission of its own for adding a member, giving or taking a role, or naming another owner. It
also has no rule about who gives what to whom. Each of your commands requires the permission you choose.

- **Usually the command requires a permission on the resource.**
  [`ShareDocument`](resources-and-members.md#a-document-and-the-people-it-is-shared-with) requires
  `documents.share`. A caller with that permission shares with anyone, in any role, for any period. The owner holds
  it by owning. A member holds it when a role gives it.
- **Write any further rule in the handler**, over what `IMemberQuestions` returns in one statement. This handler
  refuses to put staff on a folder for longer than the caller holds `folders.staff`:

```csharp
[RequiresPermission("folders.staff")]
public sealed record AdmitStaff([OnResource] FolderId Folder, StaffCode Staff, NamedRole Role, DateTimeOffset? Until, [ExpectedVersion] long? ExpectedVersion = null) : IFilingRequest;

// In the handler:
var hold = await questions.RequireAsync(command.Folder, "folders.staff", cancellationToken);   // IMemberQuestions<FolderId>
if (hold.Until is { } mine && (command.Until is not { } theirs || theirs > mine))
{
    throw new RefusalException("folders.longer-than-held", RefusalKind.NotPermitted,
        "Nobody is put on a folder for longer than you hold the permission to put them there.");
}

await admission.RequireMemberAsync(command.Staff, cancellationToken);   // MemberAdmission<FolderId, StaffCode, NamedRole>
await admission.RequireRoleAsync(command.Role, cancellationToken);
var now = clock.GetUtcNow();
folder.Admit(command.Staff, command.Role, MemberPeriod.Between(now, command.Until), now, by);
```

- **[`MemberHold.Until`](reference/use-cases.md#until)** is the first moment the caller no longer holds the
  permission, as things stand now. It is `null` for the owner, for the application's own work, and when the
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

## Roles a customer makes

| The rules say | A member holds a role by | Who makes the roles | What a role gives is read from |
|---|---|---|---|
| `Roles.Fixed([...])` | its name, a `NamedRole` | you, in code | the rules |
| `Roles.Custom(...)` | the id of your role class | a customer, while the application runs | the role's row, in your context |
| `Roles.External(...)` | the id in the store that keeps it | whoever keeps the roles | what you [return](beside-an-organization.md#three-settings-of-the-rules) |

A custom role is given on one resource and counts there alone. The tutorial writes the role class
([step 1](tutorial.md#step-1-the-board-its-members-and-its-roles)), its mapping and unique indexes
([step 3](tutorial.md#step-3-where-it-is-stored)) and the starter roles ([step 7](tutorial.md#step-7-members)).

### Starter roles

- **Starter roles are the roles a scope begins with.** You name them in `Roles.Custom(starters: [...])`. A scope is a
  tenant, a customer of yours, or the whole application. `StarterRoles.Missing(rules, existing)` returns the starter
  roles a scope does not have yet, as drafts for your constructor.
- **Each starter role is made once.** `StarterRoles.Missing` recognises a role that was made from a starter role by
  its `MadeFrom` value. A renamed or archived role keeps that value, so it is not made again. The unique index over
  your scope column and `MadeFrom` stops two requests at the same moment from both making it.
- **No starter role is reserved for the owner.** The owner holds no role. A customer may rename, archive and change
  every starter role. A scope with no roles still has resources and owners.

### Changing a role

A role has three operations. Each returns what happened, so you can raise your own events:

```csharp
bool renamed = role.Rename("Lead editor", "Edits and plans a board.", BoardMembership.Rules);              // pass the name and the description as they should be
RolePermissionsSet set = role.SetPermissions([BoardPermissions.View, BoardPermissions.Edit], BoardMembership.Rules);   // set.Added, set.Removed
role.Archive(BoardMembership.Rules);
```

- **Each operation takes the rules.** A role row does not store them. The rules say what a role may give, and which
  codes it refuses with.
- **A role gives only permissions a role may give** (`InRoles`). Other permissions are refused with
  `boards.permission-not-for-members`, which names them. The rules are checked again whenever a role is read. So a
  permission later marked `InRoles = false` is given by no role, whatever the row holds.
- **Permissions of another module**, such as an inspection's permission on a project, need `otherPermissions`, as in
  `Roles.Custom(starters: [...], otherPermissions: OtherPermissions.AllBut(...))`. This lets them through, except
  the permissions named, the permissions the resource's own classes declare at another level, and the permissions
  marked `InRoles = false` ([`OtherPermissions`](reference/rules.md#otherpermissions)). `OtherPermissions.All` names
  no permission to leave out.
- **A name is trimmed and has 1 to 120 characters.** A description has at most 1000. Both are refused with
  `boards.role-name-invalid`, which names the field. `Rename` takes both values as they should be, so `null` clears
  the description.
- **A unique name is your index** over your columns and `Name`. `RefusesAs` gives it your code, for example
  `HasIndex(row => new { row.TenantId, row.Name }).IsUnique().RefusesAs("boards.role-name-taken", "Another role is called {Name} already.")`.
- **An archived role** gives nothing from then on, and nobody can give it again. It can no longer be changed
  (`boards.role-archived`). It stays on the members that hold it, so what they held keeps a name. Roles are never
  deleted.
- **`MadeFrom` cannot change after insert** (`IsFixedAfterInsert`). A save that changes it is refused, and the
  exported privileges leave the column out of `UPDATE`.
- **Who may make, change or archive a role** is a requirement on your command, the same as for
  [giving one](#who-may-give-a-role).

### Which customer a role belongs to

- **The customer column is yours.** The package has no customer or tenant column. Add the column, set it in your
  constructor, and keep customers apart with a query filter (with Tenancy, `ScopeToTenant`) or a
  [row access rule](../row-level-security.md#row-access-rules-written-in-c).
- **The admission reads through the request's context**, so your filter applies. Another customer's role does not
  exist there. Your filter also applies where a role's permissions are read
  ([Access in your own queries](checks-and-saves.md#access-in-your-own-queries)).
- **Without customers**, there is one set of roles. The two unique indexes then cover `Name` alone and `MadeFrom`
  alone.
- **To find roles another way**, register your own `IMemberRoles<TResourceId, TRoleId>` before the resource.
- **The roles are read where they are stored.** A subquery over your role table runs inside the statement that reads
  the members. On Postgres the functions join the role table, and you register nothing
  ([in the database](postgres.md#roles-a-customer-makes-in-the-database)).
- **A second resource** declares its own role class, rules and mapping. The roles of one resource are no roles of the
  other.
