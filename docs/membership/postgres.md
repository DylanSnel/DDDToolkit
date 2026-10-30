---
title: "Membership on Postgres"
sidebar_label: On Postgres
---

# Membership on Postgres

Read this page when the database checks every row as the caller, with row level security (RLS). It covers the
functions your row access rules call, the lock on the member rows and the owner column, and the start-up check. The
tutorial sets this up in [steps 3 to 5](tutorial.md#step-3-where-it-is-stored).

## The five functions

`DDDToolkit.Supporting.Membership.Postgres` writes five functions per resource, from the same rules that the access
manager and `WhereIHold` read in C#. For `documents`:

| Function | Returns | Your rules call it through |
|---|---|---|
| `documents_as_member` | the ids of the resources the caller is a member of now | |
| `documents_as_member_with` | for a permission, the ids of the resources where the caller holds a role now that gives the permission | |
| `documents_i_see` | the ids of the resources the caller sees: as a member, as the owner from the moment the row names the caller, or from above | `ResourceAccessSet.Seen` |
| `documents_where_i_hold` | for a permission, the ids of the resources the caller holds it on: as a member, as the owner, or from above | `ResourceAccessSet.HeldOn` |
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
  read rule on another table can call it. One example is a read rule on
  [Tenancy's seats](../tenancy/postgres.md#restrictive-rules) that lets a seat read the people on its
  projects. That rule goes into Tenancy's access file, which runs before the Membership functions exist. The export
  writes the rule through a function of Tenancy's that calls the Membership function at run time.
- **The functions' names follow from the resource's [name](resources-and-members.md#names).** None of your code calls
  them by name. A renamed class gets new functions, and the old functions are dropped, unless `[Members]` keeps the
  name.

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
- A function may be executed by every role, by a role its `-- Executed by:` line does not name, or not by a role the
  line names.
- A lock policy is missing for a role, or the owner trigger is missing or disabled.
- The role table of roles a customer makes has no RLS, and a role that may call the functions may write it.

The check logs a warning for a resource whose rules mark no `ManagesMembers` permission, no `NamesOwner` permission,
or neither.

### Who writes the member rows

The database cannot tell which command wrote a row. A caller whom your rule lets change a resource could write its
member rows and its owner column with a statement of its own. That caller could give itself a role, or take the
resource. The permissions the member manager requires (`documents.share` on
[the document](resources-and-members.md#a-document-and-the-people-it-is-shared-with)) are marked, and the package
writes a lock from the same marks:

| The permissions mark | The database then has |
|---|---|
| `ManagesMembers` | A restrictive policy on both member tables. A row changes only on a resource where the caller holds that permission, or the permission marked `NamesOwner`, because naming an owner writes member rows too. |
| `ManagesMembers`, where a [customer makes the roles](permissions-and-roles.md#roles-a-customer-makes) | A second policy, on the roles a member holds. A role that is added or changed must be one the caller sees in your role table, read as the caller. |
| `NamesOwner` | A trigger. A statement that leaves a row with another owner is refused, unless the caller held that permission before the statement. A new row had no owner, so your create rule decides whom it names. |
| either mark, with `reachFromAbove` | Nothing extra about where a resource hangs. The lock checks what the caller holds there when the statement runs. |

- **The member manager and the lock check the same permission.** The manager refuses a caller without it before
  anything is written. The lock refuses a statement that went round the manager.
- **A change the lock would refuse to the caller itself is saved as the resource's own work.** Leaving a document is
  an example. The lock refuses the member a write to its own row, because the member does not hold `documents.share`.
  So the member manager saves the leave as system work in the resource's scope, begun for the member, when the request
  in hand was checked for this document ([The caller's own place](resources-and-members.md#the-callers-own-place)).
  With Tenancy, that work names the member's seat, so the history names who left.
- **Three columns need a rule of yours:**
  - *The owner of a new resource.* Your create rule says whom a caller may name as owner. That is the caller itself,
    unless the caller may name owners (tutorial step 4, and the sample's `SeatsOpenProjectsWhereTheyMay`).
  - *Where a resource hangs*, for a resource [reached from above](beside-an-organization.md#reach-from-above). A
    caller who may change that column could move the resource to a place where the caller holds the locking
    permission. Fix the column (`IsFixedAfterInsert`, [step 8](tutorial.md#step-8-reach-from-above)) or guard it with
    a trigger (the sample's `UnitChangesWithItsPermissions`).
  - *Who is put on a list.* A row's member cannot change after insert, because the column is left out of `UPDATE`.
    Your rules decide who may be added (the sample's `CrewSeatsOfTheProjectsTenant`).
- **Other columns follow your rule for changing the resource.** That rule lets every permission that changes the row
  through, because adding a member bumps the version. To require a stricter permission for one column, write a
  [column rule](../row-level-security.md#column-rules) that calls the functions, as the sample's
  `NameAndPlanChangeWithTheEditPermission` does. The owner column stays under the lock.
- **The lock policies are restrictive.** They narrow what your rules allow and grant nothing on their own. They apply
  to every database role that may call the functions. Work in a scope the rules do not name writes no member row and
  names no owner. System work and the table owner are not checked.
- **Without either mark**, the member manager changes the members only for system work. The member rows follow your
  rule for changing the resource, and the start-up check warns for each resource. `ManagesMembers` without
  `NamesOwner` leaves a gap. A caller who makes itself owner by a statement of its own then holds the other
  permissions by owning.
- **A refusal is `access.refused`, a 403.** The trigger refuses the way the toolkit's guards do: SQL state `42501`,
  the trigger's name as the constraint (`documents_owner_stays`), and the toolkit's hint
  ([When the database refuses](../row-level-security.md#when-the-database-refuses)).
- **The database checks each statement when it runs.**
  - Opening a resource writes the owner's member row in the same save. The owner sees the resource as soon as the resource
    row names the owner. So a rule over `user.Sees<DocumentId>()` lets the owner write the member rows of a resource it
    has just opened. Where callers open resources as themselves, let the owner hold the locking permission.
  - An owner who holds the permissions only by owning loses them once the owner column changes. The rest of that
    hand-over is then no longer the old owner's to write. So the member manager saves an owner's own hand-over as
    the resource's own work, once the command's check passed. A caller who holds the permission through a role or
    from above saves the hand-over as itself.
- **The lock is written only for a resource with row access rules.** Without rules, no role may call the functions
  and nobody reads a member row.

### Roles a customer makes, in the database

The functions run as their owner and find a role by its id, past your query filter. So a role of one customer on
another customer's resource behaves differently in two places. In your application it gives nothing. In the database
functions it gives its permissions. Each writer is checked as follows:

- **Your application** gives roles through the member manager, which checks the role under your filter.
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
