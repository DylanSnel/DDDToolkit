---
title: "Membership on Postgres"
sidebar_label: On Postgres
---

# Membership on Postgres

Read this page when the database checks every row as the caller, with row level security (RLS). It covers the
functions your row access rules call, the lock on the member rows and the owner column, and the start-up check. The
tutorial sets this up in [steps 3 to 5](tutorial.md#step-3-where-it-is-stored).

## The five functions

`DTS.Membership.Postgres` writes five functions per resource, from the same rules that the access
manager and `WhereIHold` read in C#. For `documents`:

| Function | Returns | Your rules call it through |
|---|---|---|
| `documents_as_member` | the ids of the resources the caller is a member of now | |
| `documents_as_member_with` | for a permission, the ids of the resources where the caller holds a role now that gives the permission | |
| `documents_i_see` | the ids of the resources the caller sees: as a member, as the owner from the moment the row names the caller, or from above, from each place reached | `ResourceAccessSet.Seen` |
| `documents_where_i_hold` | for a permission, the ids of the resources the caller holds it on: as a member, as the owner, or from above, from each place reached | `ResourceAccessSet.HeldOn` |
| `documents_fellow_members` | the member ids of the current members of every resource the caller is a member of now, the caller included | `ResourceAccessSet.FellowMembers` |

- **Your [row access rules](../row-level-security.md#row-access-rules-written-in-c) decide what a caller reads and
  changes of a resource.** They call the last three functions by id. The member tables follow the resource's rules,
  like the entity tables of any aggregate.
- **A context needs no extra option.** A context wired with `UseDDDToolkit` runs as its caller once RLS is
  registered ([`UseDDDToolkit`](../entity-framework.md#usedddtoolkit)). The only Membership option a context can take
  is [the expert hold](checks-and-saves.md#the-expert-hold).

### The mark and the questions

The tutorial writes the mark ([step 3](tutorial.md#step-3-where-it-is-stored)), the row access rules and the lock
([step 4](tutorial.md#step-4-the-databases-own-rules)), and calls `AddMembershipPostgres()`
([step 5](tutorial.md#step-5-the-modules-registration-and-the-host)). A rule asks the last three functions by the
resource's id. A third question returns the people a caller works with. It also names the member id type:

```csharp
// The people the caller shares a document with, by their member id
public static bool Allows(Person person, IRowUser user) => user.FellowMembers<DocumentId, UserId>().Contains(person.Id);
```

- **The mark tells the build which rules belong to the resource.** Put `[MembershipRules<TMember>]` on a static
  `MembershipRules` property or field. The build needs the mark because it cannot read the value a registration is
  called with. The project that runs the export references the package, directly or through the module. The export
  writes one contribution per mark ([Policies a package ships](../row-level-security.md#policies-a-package-ships)):

  | The marks | Result |
  |---|---|
  | two for one member class, or one on a member of another type | [DDD00071](../diagnostics.md#ddd00071). The build stops. |
  | none | [DDD00054](../diagnostics.md#ddd00054), a warning. None of the package's SQL is written. |
  | some resources unmarked | The build passes. The start-up check finds them. |
  | a mark in a library, on a member that is not public | [DDD00070](../diagnostics.md#ddd00070) |

- **The access file names `MembershipRowAccessContribution<DocumentShare>` and the package's assembly without a
  version.** A new release of your application leaves the access file unchanged.
- **A question names no function.** The generator reads the id and the set. The export writes the function that this
  contribution says returns the set. A rule that uses a set that no function returns is refused, with the rule and
  the resource named.
- **The question's id** is declared, or it is the id the toolkit writes for `[AggregateRoot<Guid>]` in the declaring
  project. A module publishes the id in its contracts. Rules of other modules ask by it
  ([A resource's access, asked by its id](../row-level-security.md#asking-about-a-resource-by-its-id)).
- **`FellowMembers` reads the member rows alone.** An owner without a current member row finds nobody by owning. A
  read rule on another table can call it. Tenancy calls it itself for a seat outside the organization, through its
  function `fellow_seats()`, which the export writes from the marks the join generates
  ([What a seat outside the organization reads](../tenancy/postgres.md#what-a-seat-outside-the-organization-reads)).
- **The functions' names follow from the resource's [name](resources-and-members.md#names).** None of your code calls
  them by name. A renamed class gets new functions, and the old functions are dropped, unless `[Members]` keeps the
  name.

### The kind filter in the functions

Every member row stores the kind of the member's seat and whether it is inside the organization
([The member's kind](resources-and-members.md#the-members-kind)). The functions check it against the permission's
`Kinds`, from the same rules the C# check reads
([Kinds of seat and role sets](permissions-and-roles.md#kinds-of-seat-and-role-sets)):

- **`documents_as_member_with`** answers a resource only where the kind on the caller's member row may hold the
  permission.
- **`documents_where_i_hold`** reads the member rows itself for the permission every member holds, with the same
  condition. Its owner branch answers only permissions whose `Kinds` holds the kinds inside the organization. Its
  from-above branch answers only permissions whose `Kinds` holds the caller's own kind
  ([The caller's kind under reach from above](#the-callers-kind-under-reach-from-above)).
- **`documents_i_see`**, where the rules name a permission every member holds, answers a member only when its kind may
  hold that permission. It writes the owner branch only when that permission's `Kinds` holds the kinds inside the
  organization. Its from-above branch answers a caller only when the caller's own kind may hold that permission.
- **`documents_as_member` and `documents_fellow_members`** answer membership alone, with no filter.

The condition on a member row `m`, for rules where `documents.view` also lists `guest`:

**`*_access.documents.ddd.sql`**

```sql
(m."Kind" IN ('guest') OR m."InOrganization")   -- in documents_where_i_hold, for $1 = 'documents.view'
```

**A permission of another resource.** A role on a folder may give `documents.view`, which the document declares
([A permission of another resource](permissions-and-roles.md#a-permission-of-another-resource)). The export lists the
document's kinds for it in the folder's functions, in the same pairs as the folder's own permissions. Where
`documents.view` lists `guest` on the document, `folders_as_member_with` holds this pair:

**`*_access.folders.ddd.sql`, in `folders_as_member_with`**

```sql
EXISTS (SELECT 1 FROM (VALUES ('folders.view', 'guest'), ('documents.view', 'guest')) AS listed(permission, kind)
        WHERE listed.permission = $1 AND listed.kind = m."Kind")
```

- **The export lists a permission of another resource** where a role of the folder may give it and its `Kinds` differ
  from the kinds inside the organization. Every permission the functions do not list counts as inside the
  organization only, as in C#.
- **The fingerprint in the first line of each function covers those kinds as well.** A change of the document's
  `Kinds` changes the folder's fingerprint, so the start-up check refuses a database written before it.

A row written past the application cannot claim another kind, because the kind guard holds the row to its seat
([The kind guard](#the-kind-guard)). The functions are written in form 10. A database written before is refused by the
start-up check until its access files are written again.

### The caller's kind under reach from above

A caller reached from above has no member row on the resource. The from-above branches read the caller's kind from the
source of the members' kinds instead, the function `[MemberKindSource]` names:

- **With Tenancy**, the join marks `seat_to_admit(seat)`. Each from-above branch asks it once for the caller's seat,
  `current_seat()`, and compares its `Kind` and `InOrganization` as it compares a member row. A caller without a seat
  holds nothing from above.
- **Without a source**, every caller is `member`, inside the organization. The branch leaves out each permission whose
  `Kinds` does not hold that kind. A resource whose permissions all hold it gets the text it always had.

The from-above branch of a board at its unit, for rules where `boards.view` also lists `guest`:

**`*_access.boards.ddd.sql`, in `boards_where_i_hold`**

```sql
SELECT r."Id" FROM "boards"."Boards" r WHERE $1 IS NOT NULL
  AND EXISTS (SELECT 1 FROM "tenancy".seat_to_admit((SELECT "tenancy".current_seat())) c
              WHERE (EXISTS (SELECT 1 FROM (VALUES ('boards.view', 'guest')) AS listed(permission, kind)
                             WHERE listed.permission = $1 AND listed.kind = c."Kind") OR c."InOrganization"))
  AND r."UnitId" IN (SELECT reached.place FROM "tenancy".units_where_i_hold_permission($1) AS reached(place))
```

The condition reads no column of the board, so Postgres evaluates it once for the statement, as it does
`$1 IS NOT NULL`. C# checks the same kind with `MembershipRules.MayHold`
([Kinds of seat and role sets](permissions-and-roles.md#kinds-of-seat-and-role-sets)).

### Reach from several places

A resource that hangs at places of several kinds is reached from above from each place the export finds a function for
([A resource at several places](beside-an-organization.md#a-resource-at-several-places)). The export finds the
function by the place's kind:

1. the function a contribution answers for the kind, such as Tenancy's `units_where_i_hold_permission` for its unit id;
2. otherwise the function the application marks for the kind with `[PlacesWhereIHold]`;
3. otherwise no function, and the place is not reached from above.

`documents_i_see` and `documents_where_i_hold` get one `SELECT` for each place reached, joined with `UNION` as the other
ways of holding are. Each `SELECT` keeps the gates of a single place: a permission, not one marked `FromAbove = false`,
and one the caller's kind may hold. A board at a unit and a berth gets two, shown here without the caller's kind:

**`*_access.boards.ddd.sql`, in `boards_where_i_hold`**

```sql
SELECT r."Id" FROM "boards"."Boards" r WHERE $1 IS NOT NULL
  AND r."BerthId" IN (SELECT reached.place FROM "harbor".berths_where_i_hold($1) AS reached(place))
UNION
SELECT r."Id" FROM "boards"."Boards" r WHERE $1 IS NOT NULL
  AND r."UnitId" IN (SELECT reached.place FROM "tenancy".units_where_i_hold_permission($1) AS reached(place))
```

- **An empty place compares as `NULL IN (...)`**, which is not true. No extra condition is written.
- **A resource with one place gets the text it always had.**
- **The other three functions read no place.** Neither do the lock, the policy on roles of a set, and the kind guard.
- **The export refuses** rules that reach from above when no place has a function, two marks for one kind of the
  resource's places, and a mark for a kind that a contribution answers too.

### Who may execute the functions

Each function runs as its owner, with an empty search path. Only the roles your rules on the resource are for may
execute it:

| Your rules on the resource are for | The functions may be executed by |
|---|---|
| signed-in users, `RowAccessRoles.User` | `authenticated` |
| a role of your own as well, `To = [RowAccessRoles.User, "staff"]` | `authenticated` and `staff` |
| `anon` among other roles | the other roles only. A caller who did not sign in holds nothing. |
| any of these, while the rules let a scope through (`systemScopes`) | also `ddd_system_in`, the [scoped system role](../row-level-security.md#the-scoped-system-role). The functions return every resource for it. |

A rule for another role grants that role the functions with the next access file. Each function lists its roles in a
`-- Executed by:` line. The functions compare periods with the database's clock.

### The start-up check

`AddMembershipPostgres()` brings the start-up check `membership.functions-in-place`, and nothing else brings it. A host
that leaves the call out skips the check and logs nothing about it. The check runs with the other
[start-up checks](../startup-checks.md), after the migration checks. To run it by hand, call
[`MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync`](reference/postgres.md#ensurefunctionsareinplaceasynciserviceprovider-cancellationtoken).

The check stops the host from starting when any of these is true:

- No resource with members is registered.
- A resource's context is not registered, does not map the members, maps no `[At]` place for rules that reach from
  above, or maps no role class for roles a customer makes.
- A function of a registered resource is missing, does not run as its owner, has no empty search path, or returns no
  set.
- A function was written from other rules than the resource is registered with, or by another version of the
  package. The check reads the line of the body that names the rules, so it does not see a body rewritten below that
  line.
- A function was written for other places than C# reaches the resource from. That line names each place reached from
  above, by its name and its kind, and no function. C# reaches a resource from each place whose kind has an
  `IPlacesReached` registered before the resource. The export reaches it from each place it finds a function for. So a
  database that reaches labs from their building, beside a host that registers no answer for buildings, is refused.
- A function may be executed by every role, by a role its `-- Executed by:` line does not name, or not by a role the
  line names.
- A lock policy is missing for a role, or the owner trigger is missing or disabled.
- The role table of roles a customer makes has no RLS, and a role that may call the functions may write it.

The check logs a warning for a resource whose rules mark no `NamesOwner` permission.

### Who writes the member rows

The database cannot tell which command wrote a row. A caller whom your rule lets change a resource could write its
member rows and its owner column with a statement of its own. That caller could give itself a role, or take the
resource. The permissions the member service requires (`documents.share` on
[the document](resources-and-members.md#a-document-and-the-people-it-is-shared-with)) are marked, and the package
writes a lock from the same marks:

| The permissions mark | The database then has |
|---|---|
| `ManagesMembers` | A restrictive policy on both member tables. A row changes only on a resource where the caller holds that permission, or the permission marked `NamesOwner`, because naming an owner writes member rows too. |
| no `ManagesMembers` | The same policy. The rows of a set that no permission manages change only for system work in a scope the rules name. Without such a scope, nobody writes them. |
| `ManagesMembers`, where a [customer makes the roles](permissions-and-roles.md#roles-a-customer-makes) | A second policy, on the roles a member holds. A role that is added or changed must be one the caller sees in your role table, read as the caller. |
| `NamesOwner` | A trigger. A statement that leaves a row with another owner is refused, unless the caller held that permission before the statement. A new row had no owner, so your create rule decides whom it names. |
| either mark, with `reachFromAbove` | Nothing extra about where a resource hangs. The lock checks what the caller holds there, from each place reached, when the statement runs. |

- **The member service and the lock check the same permission.** The service refuses a caller without it before
  anything is written. The lock refuses a statement that went round the service.
- **A change the lock would refuse to the caller itself is saved as the resource's own work.** Leaving a document is
  an example. The lock refuses the member a write to its own row, because the member does not hold `documents.share`.
  So the member service saves the leave as system work in the resource's scope, begun for the member, when the request
  in hand was checked for this document ([The caller's own place](resources-and-members.md#the-callers-own-place)).
  With Tenancy, that work names the member's seat, so the history names who left.
- **Three columns need a rule of yours:**
  - *The owner of a new resource.* Your create rule says whom a caller may name as owner. That is the caller itself,
    unless the caller may name owners (tutorial step 4, and the sample's `SeatsOpenProjectsWhereTheyMay`).
  - *Where a resource hangs*, for a resource [reached from above](beside-an-organization.md#reach-from-above). A
    caller who may change that column could move the resource to a place where the caller holds the locking
    permission. Fix the column with `[At(Fixed = true)]` ([step 8](tutorial.md#step-8-reach-from-above)) or guard it
    with a trigger (the sample's `UnitChangesWithItsPermissions`).
  - *Who is put on a list.* A row's member cannot change after insert, because the column is left out of `UPDATE`.
    Your rules decide who may be added (the sample's `CrewSeatsOfTheProjectsTenant`).
- **Other columns follow your rule for changing the resource.** That rule lets every permission that changes the row
  through, because adding a member bumps the version. To require a stricter permission for one column, write a
  [column rule](../row-level-security.md#column-rules) that calls the functions, as the sample's
  `NameAndPlanChangeWithTheEditPermission` does. The owner column stays under the lock.
- **The lock policies are restrictive.** They narrow what your rules allow and grant nothing on their own. They apply
  to every database role that may call the functions. Work in a scope the rules do not name writes no member row and
  names no owner. System work and the table owner are not checked.
- **Without `ManagesMembers`**, only system work changes the members, in the member service and in the database.
  Opening a resource writes the owner's member row, so a caller who opens such a resource as itself is refused. Open
  it as system work, or mark a permission `ManagesMembers`.
- **Without `NamesOwner`**, the owner column follows your rule for changing the resource, and the start-up check
  warns for each resource. A caller who makes itself owner by a statement of its own then holds the other
  permissions by owning.
- **A refusal is `access.refused`, a 403.** The trigger refuses the way the toolkit's guards do: SQL state `42501`,
  the trigger's name as the constraint (`documents_owner_stays`), and the toolkit's hint
  ([When the database refuses](../row-level-security.md#when-the-database-refuses)).
- **The database checks each statement when it runs.**
  - Opening a resource writes the owner's member row in the same save. The owner sees the resource as soon as the resource
    row names the owner. So a rule over `user.Sees<DocumentId>()` lets the owner write the member rows of a resource it
    has just opened. Where callers open resources as themselves, let the owner hold the locking permission.
  - An owner who holds the permissions only by owning loses them once the owner column changes. The rest of that
    hand-over is then no longer the old owner's to write. So the member service saves an owner's own hand-over as
    the resource's own work, once the command's check passed. A caller who holds the permission through a role or
    from above saves the hand-over as itself.
- **The lock is written only for a resource with row access rules.** Without rules, no role may call the functions
  and nobody reads a member row.

**The lock per role set.** Rules with several [role sets](permissions-and-roles.md#kinds-of-seat-and-role-sets) lock
each member row with the permission of its set. The set follows from the row's kind, so the policy reads it from the
row:

**`*_access.documents.ddd.sql`**

```sql
CASE WHEN "InOrganization" THEN "DocumentId" IN (SELECT held.id FROM documents_where_i_hold('documents.share') AS held(id))
     WHEN "Kind" IN ('guest') THEN "DocumentId" IN (SELECT held.id FROM documents_where_i_hold('documents.guests.share') AS held(id))
     ELSE false END
```

- On the table of the roles a member holds, each case reads the kind from the member row of that hold.
- **Members hold roles of their set.** A restrictive policy on that table, for `INSERT` and `UPDATE`, takes only a role
  of the member's set. For roles a customer makes, the role row's `Set` must be the set of the member row's kind. For
  fixed roles, the role must be one the rules declare in that set. The policy is written only for rules with several
  sets.
- Rules with one set write the lock as before.
- **`documents_where_i_hold` filters by the caller's own row.** A guest passes the lock for a guest's row only where
  `documents.guests.share` lists `guest` and a role of the guest gives it
  ([A kind outside the organization that manages members](permissions-and-roles.md#a-kind-outside-the-organization-that-manages-members)).

### The kind guard

The member row carries a copy of its seat's kind. Two restrictive policies keep a row written past the application
from claiming another kind:

| Policy | On | Checks |
|---|---|---|
| "Members carry their seat's kind" | `INSERT` into the member table | The row's `Kind` and `InOrganization` equal the seat's, read through Tenancy's `seat_to_admit(seat)`. |
| "The owner is inside the organization" | `INSERT` and `UPDATE` of the resource table | The owner column names a seat inside the organization, read through `seat_to_admit(seat)`. |

- The export writes the guard only where a `[MemberKindSource]` mark names the member class. The join with Tenancy
  generates that mark ([Kinds of seat and fellow members](beside-an-organization.md#kinds-of-seat-and-fellow-members)).
  Without Tenancy no row can claim to be outside, so nothing is written.
- The resource's access file runs after Tenancy's, which makes `seat_to_admit(seat)`.
- `seat_to_admit(seat)` runs as its owner. It answers one seat of the caller's tenant by its id, whatever seats the
  caller reads. So the guard lets a member row through for a seat the caller does not read, as the member service does.
- To a signed-in user it answers an active seat alone. A seat that is not active, a seat of another tenant and an id no
  seat has give no row alike, and the guard refuses the member row.
- To scoped system work it answers a seat of the work's tenant whatever its status, so the application's own work
  writes a member row for a suspended seat
  ([What a seat outside the organization reads](../tenancy/postgres.md#what-a-seat-outside-the-organization-reads)).
- The guard is part of the functions' fingerprint, so the start-up check finds a database written without it.

### Roles a customer makes, in the database

The functions run as their owner and find a role by its id, past your query filter. So a role of one customer on
another customer's resource behaves differently in two places. In your application it gives nothing. In the database
functions it gives its permissions. Each writer is checked as follows:

- **Your application** gives roles through the member service, which checks the role under your filter.
- **A caller who writes rows directly** is checked by the lock, where a permission is marked `ManagesMembers`. A role
  the caller gives must be one it sees in your role table. So your read rule on that table says which customer a
  role belongs to, from the token or from a function of yours. Your rules also keep the resources a caller may change
  inside the same customer. Callers that change members need to read the roles they give and the roles on the rows
  they change, archived roles too.
- **System work** and the table owner are not checked. A row written before the lock existed is not
  checked again. A query over your scope columns finds such rows.

The package writes no policy for the role table. Give the table row access rules
([step 4](tutorial.md#step-4-the-databases-own-rules) writes two). The start-up check refuses a role table without
RLS that a role allowed to call the functions may write. The functions' fingerprint includes whether a customer makes
the roles and what a role may give. So the check refuses functions written before either of those changed. A new
starter role changes no function.
