---
title: "Membership: permissions and roles"
sidebar_label: Permissions and roles
---

# Membership: permissions and roles

The settings of a permission, who holds one, who may give a role, and roles a customer makes. Read it when you
declare permissions or write the commands that give roles. Levels and a first set:
[tutorial step 2](tutorial.md#step-2-the-permissions-and-the-rules).

## The settings of a permission

A permission is a public constant of a static `[Permissions]` class; the attribute on it is its **level**:
`[On<Document>]` on one document, and with Tenancy `[AtUnit]` and `[AtTenant]`. One class may mix levels. On a
resource, `[On<T>]` also says how it is held:

| Setting | Default | Set it |
|---|---|---|
| `Owner` | the owner holds it by owning | `false`: the owner holds it only as anybody would, from above say; for one such as naming another owner |
| `EveryMember` | being a member gives nothing | `true`: every member holds it, whatever its roles; usually the one that sees. One at most |
| `InRoles` | a role may give it | `false`: only the owner and reach from above hold it; for one a customer must never hand out |
| `LocksMembers` | locks nothing | `true`: what your commands that change the members require; the database holds the member rows to it ([Who writes the member rows](postgres.md#who-writes-the-member-rows)). One at most |
| `LocksOwner` | locks nothing | `true`: what your command that hands the resource on requires; the database holds the owner column to it. One at most |
| `FromAbove` | reaches from above, where the rules allow | `false`: held where the resource hangs, it gives nothing on it ([Beside an organization](beside-an-organization.md#beside-an-organization)) |

Every level also takes:

- **`Description`**: what it lets somebody do, the English text an administrator reads where roles are made.
  Without it the constant's `/// <summary>` is used, documentation file or not; neither is
  [DDD00099](../diagnostics.md#ddd00099), a warning. Translations go in a resx under the permission's name
  ([Localization](../localization.md)).
- **`Implies`**: what holding it brings with it, so a role with only this one does what those allow too.
- **`Retired = true`**: nobody holds it, yet roles that list it keep it and no stored right breaks. A permission is
  retired, never deleted.

| Mistake | Reported |
|---|---|
| A `[Permissions]` class that is not static, a constant without a level, a permission declared twice | [DDD00092](../diagnostics.md#ddd00092) |
| `[On<T>]` on what is no resource with members; two permissions that say `EveryMember`, `LocksMembers` or `LocksOwner` | [DDD00093](../diagnostics.md#ddd00093) |
| Rules for a resource no permission is declared on | [DDD00094](../diagnostics.md#ddd00094), where they are made |

## Who holds a permission

The five ways are in the [overview](overview.md#who-holds-a-permission-on-a-board). What follows from them:

- **The owner holds no role**: it holds the permissions marked for the owner. So `[OrOwn]` beside `[OnResource]`,
  for a permission the owner holds, adds nothing ([DDD00096](../diagnostics.md#ddd00096)).
- **A role gives what it lists** only where a role may give it: `InRoles`, or another module's permission that
  `otherPermissions` lets through.
- **Refusals come in one order**: the caller's own when it is nobody; `not-found` for a resource it does not see,
  exactly as for one that does not exist; `not-permitted`, naming the permission; the version last.
- **Who the caller is as a member** follows from the member id: the token's user id for an id over a `Guid`, the seat
  for Tenancy's seat id. Otherwise the rules say it: `members: MemberSource.Claim("app_metadata.staff")`, or
  `MemberSource.Resolved(...)` for an id [you resolve](beside-an-organization.md#an-organization-of-your-own).
- **A caller who did not sign in** is nobody's member and holds nothing from above; nothing is read for it. Nor does a
  token without that id or claim reach a resource, unless something above does.

## Who may give a role is yours to decide

Adding a member, giving or taking a role and naming another owner have no permission of the package's, and no rule
about who gives what to whom: each of your commands requires what you choose.

- **Usually a permission on the resource**, as `documents.share` on
  [`ShareDocument`](resources-and-members.md#a-document-and-the-people-it-is-shared-with): whoever holds it shares
  with anyone, in any role, for any period. The owner holds it by owning; a member when a role gives it.
- **A further rule goes in the handler**, over what the questions answer in one statement. "Nobody gives a role for
  longer than they hold the permission themselves":

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

- **[`MemberHold.Until`](reference/use-cases.md#until)**: the first moment the caller no longer holds the permission,
  as things stand now. `null` for the owner and the application's own work. Through membership: its end. Through a
  role: the end of the last role that gives it, or of the membership if sooner. From above:
  [for as long as it is seen](beside-an-organization.md#beside-an-organization).
- **`MemberHold.Via`** says how the caller holds it, for a rule about that.
- **The package adds no rule**: not "no longer than your own hold", not "never to yourself".

## Roles a customer makes

| The rules say | A member holds a role by | Made by | What it gives is read from |
|---|---|---|---|
| `Roles.Fixed([...])` | its name, `NamedRole` | you, in code | the rules |
| `Roles.Custom(...)` | your role class's id | a customer, at run time | the role's row, in your context |
| `Roles.External(...)` | the id of what keeps it | whoever keeps it | what you [answer](beside-an-organization.md#beside-an-organization) |

A custom role is given on one resource and counts there alone. The tutorial writes the role class
([step 1](tutorial.md#step-1-the-board-its-members-and-its-roles)), its mapping and unique indexes
([step 3](tutorial.md#step-3-where-it-is-stored)) and the starter roles ([step 7](tutorial.md#step-7-members)).

### Starter roles

- **What a scope begins with**, named in `Roles.Custom(starters: [...])`: a tenant, a customer of yours, or the whole
  application. `StarterRoles.Missing(rules, existing)` gives those a scope lacks, for your constructor.
- **Made once**: told by `MadeFrom`, not by name or status, so one renamed or archived is not made again. The unique
  index over your scope column and `MadeFrom` keeps two requests at once apart.
- **None is the owner's**: a customer may rename, archive and change each, and a scope with no roles still has
  resources and owners.

### Changing a role

Three operations, each answering what happened so you raise your own events:

```csharp
bool renamed = role.Rename("Lead editor", "Edits and plans a board.", BoardMembership.Rules);              // both, as they are to be
RolePermissionsSet set = role.SetPermissions([BoardPermissions.View, BoardPermissions.Edit], BoardMembership.Rules);   // set.Added, set.Removed
role.Archive(BoardMembership.Rules);
```

- **Each takes the rules**: a row carries none, and they say what a role may give and its codes.
- **Only permissions a role may give** (`InRoles`), or `boards.permission-not-for-members`, naming them. Asked again
  where a role is read, so a permission later marked `InRoles = false` is given by no role, whatever its row holds.
- **Another module's permissions** (an inspection's, on a project): `Roles.Custom(starters: [...], otherPermissions:
  OtherPermissions.AllBut(...))` lets them through, except those named, those the resource's own classes declare at
  another level, and those marked `InRoles = false` ([`OtherPermissions`](reference/rules.md#otherpermissions)).
- **A name** is trimmed, 1 to 120 characters, a description at most 1000: `boards.role-name-invalid`, naming the field.
  `Rename` takes both as they are to be: `null` clears the description.
- **A unique name** is your index over your columns and `Name`, given your code with `RefusesAs`:
  `HasIndex(row => new { row.TenantId, row.Name }).IsUnique().RefusesAs("boards.role-name-taken", "Another role is called {Name} already.")`.
- **An archived role** gives nothing from then on, is not given again, changes no more (`boards.role-archived`), and
  stays on the members that hold it, so what they held keeps a name. Roles are never deleted.
- **`MadeFrom` is fixed once written** (`IsFixedAfterInsert`): a save that changes it is refused, and the exported
  privileges leave it out of `UPDATE`.
- **Who may make, change or archive a role**: a requirement on your command, as for
  [giving one](#who-may-give-a-role-is-yours-to-decide).

### Whose a role is

- **Yours**: the package knows no customer or tenant. Add the column, set it in your constructor, and keep customers
  apart by a query filter (with Tenancy, `ScopeToTenant`) or a
  [row access rule](../row-level-security.md#row-access-rules-written-in-c).
- **The admission reads through the request's context**, so your filter applies: another customer's role is no role
  there. So it does where a role's permissions are read ([Inside your own statements](checks-and-saves.md#inside-your-own-statements)).
- **Without customers**: one set of roles, the two unique indexes over `Name` and over `MadeFrom` alone.
- **Found another way**: register your own `IMemberRoles<TResourceId, TRoleId>` before the resource.
- **Read where it is kept**: a subquery over your role table, inside the statement that reads the members; on
  Postgres the functions join it, with nothing of yours registered
  ([in the database](postgres.md#roles-a-customer-makes-in-the-database)).
- **A second resource** declares a role class, rules and mapping of its own; neither's roles are the other's.
