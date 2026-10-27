---
title: "Membership on Postgres"
sidebar_label: On Postgres
---

# Membership on Postgres

The functions your row access rules ask, the lock on the member rows and the owner, and the start-up check. Read it
when the database checks every row as the caller, under row level security (RLS). The tutorial sets it up in
[steps 3 to 5](tutorial.md#step-3-where-it-is-stored).

## On Postgres: the second lock

`DDDToolkit.Supporting.Membership.Postgres` writes five functions per resource, from the same rules as the questions
in C#. For `documents`:

| Function | Answers the ids of | Your rules ask it through |
|---|---|---|
| `documents_as_member` | the resources the caller is a member of now | |
| `documents_as_member_with` | those where it holds a role now that gives a permission | |
| `documents_i_see` | those it sees: as a member, as the owner from the moment the row names it, from above | `ResourceAccessSet.Seen` |
| `documents_where_i_hold` | those it holds a permission on, as a member, as the owner or from above | `ResourceAccessSet.HeldOn` |
| `documents_fellow_members` | the members now of every resource the caller is a member of now, the caller among them | `ResourceAccessSet.FellowMembers` |

- **What a caller reads and changes of a resource is yours**, in
  [row access rules](../row-level-security.md#row-access-rules-written-in-c) that ask the last three by id. The member
  tables follow the resource's rules, as an aggregate's entity tables do.
- **Nothing goes on a context's options**: one wired with `UseDDDToolkit` runs as its caller once RLS is registered
  ([`UseDDDToolkit`](../entity-framework.md#usedddtoolkit)). The only line of Membership's a context can take is
  [the expert hold](checks-and-saves.md#the-expert-hold).

### What you write

The tutorial writes the mark and two contracts ([step 3](tutorial.md#step-3-where-it-is-stored)), the row access rules
and the lock ([step 4](tutorial.md#step-4-the-databases-own-rules)), and calls `AddMembershipPostgres()`
([step 5](tutorial.md#step-5-the-modules-registration-and-the-host)). The third contract, for the people a caller
works with, declares its `Ids` with the members' id:

```csharp
// The people the caller shares a document with, by the id they are known by, which the contract says
[ResourceAccessContract<DocumentId>(ResourceAccessSet.FellowMembers)]
public static partial class PeopleIShareWith
{
    public static partial AccessSet<UserId> Ids();
}
```

- **The mark**, `[MembershipRules<TMember>]` on a static `MembershipRules` property or field, tells the build which
  rules are the resource's: it cannot read the value a registration is called with. The project that runs the export
  references the package, directly or through the module, and writes one contribution per mark
  ([Policies a package ships](../row-level-security.md#policies-a-package-ships)):

  | The marks | Then |
  |---|---|
  | two for one member class, or one on a member of another type | [DDD00071](../diagnostics.md#ddd00071): the build stops |
  | none | [DDD00054](../diagnostics.md#ddd00054), a warning; none of the package's SQL is written |
  | some resources unmarked | the build passes; the start-up check finds them |
  | one a library keeps internal | [DDD00070](../diagnostics.md#ddd00070) |

- **The access file** names `MembershipRowAccessContribution<DocumentShare>` and the package's assembly without a
  version, so a release of your application leaves it as it is.
- **A contract names no function**: the generator reads the declaration, and the export writes the function this
  contribution answers the set with. A rule that asks a set nothing answers is refused, naming rule and resource.
- **The contract's id** is declared, or the one the toolkit writes for `[AggregateRoot<Guid>]` in the declaring
  project. Contracts are the owning module's; other modules' rules ask them
  ([A resource's access, asked by its id](../row-level-security.md#a-resources-access-asked-by-its-id)).
- **`FellowMembers`** finds nobody for an owner on no list. A read rule on another table asks it, such as one on
  [Tenancy's seats](../tenancy/postgres.md#rules-that-narrow-the-organization) that lets a seat read the people on its
  projects; it goes into Tenancy's access file, which runs first, and the export writes it all the same.
- **The functions' names** follow from the resource's [name](resources-and-members.md#names); nothing of yours asks
  them by name. A renamed class gets new functions and the old are dropped, unless `[Members]` keeps the name.

### Who may ask them

The functions run as their owner with an empty search path, executable only by the roles your rules on the resource
are for:

| Your rules on the resource are for | The functions may be executed by |
|---|---|
| signed-in users, `RowAccessRoles.User` | `authenticated` |
| a role of your own as well, `To = [RowAccessRoles.User, "staff"]` | `authenticated` and `staff` |
| `anon` among the others | the others alone: a caller who did not sign in holds nothing |
| any of these, while the rules let a scope through (`systemScopes`) | `ddd_system_in` as well, the [scoped system role](../row-level-security.md#the-scoped-system-role), answered every resource |

A rule for another role grants it the functions with the next access file, which lists the roles in each function's
`-- Executed by:` line. Periods are compared with the database's clock.

### The start-up check

`membership.functions-in-place` comes with `AddMembershipPostgres()`, and nothing else brings it: a host that leaves
the call out skips it without a word. It runs with the other [start-up checks](../startup-checks.md), after the
migrations'; by hand,
[`MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync`](reference/postgres.md#ensurefunctionsareinplaceasynciserviceprovider-cancellationtoken).
It refuses a database where a registered resource's function:

- is missing, does not run as its owner, has no empty search path, or answers no set;
- was written from other rules than the resource is registered with, or by another version of the package (it reads
  the line of the body that names the rules, so a body rewritten under it is not found);
- may be executed by every role, by one its `-- Executed by:` line does not name, or not by one it names.

It refuses a lock policy gone for a role or a disabled trigger too, and warns for a resource with no lock.

### Who writes the member rows

A row knows no command: whoever your rule lets change a resource could write its member rows and owner column by a
statement of its own, giving itself a role or the resource. So the permissions mark the ones your commands require
(`documents.share`, on [the document](resources-and-members.md#a-document-and-the-people-it-is-shared-with)), and the
package writes a lock from them:

| The permissions mark | The database then holds |
|---|---|
| `LocksMembers` | a restrictive policy on both member tables: a row changes only on a resource the caller holds that permission on, or the one that locks the owner, since naming an owner writes member rows too |
| `LocksMembers`, where a [customer makes the roles](permissions-and-roles.md#roles-a-customer-makes) | a second one on the roles a member holds: a role added or changed is one the caller sees in your role table, asked as the caller |
| `LocksOwner` | a trigger: a statement that leaves a row with another owner is refused, unless the caller held that permission before. A new row had no owner, so whom it names is your create rule's |
| either, with `reachFromAbove` | nothing about where a resource hangs: the lock asks what the caller holds there when the statement runs |

- **You still decide who may**: no code of the package gates a command by the marks.
- **What your command allows and the lock does not, the handler does as the application's own work, naming who
  acts.** Leaving: `documents.leave`, marked `EveryMember = true`, is required at the door, but the lock refuses the
  member its own row. So the handler removes it as system work begun for the member (with Tenancy,
  `TenancyUseCases.BeginSystemIn(tenant, actingSeat: caller, scope: "documents")`), and the history names who left.
  The sample's `OwnPlaceOnTheCrew` does this.
- **Three columns are yours to hold**:
  - *the owner of a new resource*: your create rule says whom a caller may name, itself unless it may name owners
    (tutorial step 4; the sample's `SeatsOpenProjectsWhereTheyMay`);
  - *where a resource hangs*, for one [reached from above](beside-an-organization.md#beside-an-organization): whoever
    may change it could move it to where it holds the locking permission. Fix the column (`IsFixedAfterInsert`,
    [step 8](tutorial.md#step-8-reach-from-above)) or hold it with a trigger (the sample's `UnitChangesWithItsPermissions`);
  - *who is put on a list*: a row's member is fixed once written (left out of `UPDATE`), but who is added is your
    rules' (the sample's `CrewSeatsOfTheProjectsTenant`).
- **Other columns follow your rule for changing the resource**, which lets every permission that changes the row
  through, since adding a member bumps the version. Hold a column to a stricter permission with a
  [column rule](../row-level-security.md#column-rules) that asks the functions, as the sample's
  `NameAndPlanChangeWithTheEditPermission` does. The owner column stays the lock's.
- **It narrows, and allows nothing**, for every database role that may ask the functions: work in a scope the rules
  do not name writes no member row and names no owner. The application itself and the table owner are not held.
- **Without either mark** the member rows follow your rule for changing the resource, and the start-up check warns per
  resource. Sound where that rule admits exactly who may change members and owner, or nobody reaches the database past
  your application. `LocksMembers` without `LocksOwner` leaves a way round: whoever makes itself owner holds the rest.
- **A refusal is `access.refused`**, a 403. The trigger refuses as the toolkit's guards do: `42501`, its name as the
  constraint (`documents_owner_stays`), the toolkit's hint
  ([When the database refuses](../row-level-security.md#when-the-database-refuses)).
- **Each statement is asked as it runs.**
  - Opening a resource writes the owner's member row in the same save. The owner sees the resource once its row names
    it, so a read rule over `DocumentsISee.Ids()` lets it; let the owner hold the locking permission where callers
    open resources as themselves.
  - An owner that held the permissions by owning alone loses them once the owner column changed, so the rest of its
    own hand-over is not its to write. Save it as the application's own work once your command's check passed
    (`RequestInHand.Current.Request` is the command), and as the caller otherwise. Who holds the permission through
    a role or from above hands on as itself.
- **Only on a resource the database checks**: without row access rules of yours on the resource, nobody would read a
  member row.

### Roles a customer makes, in the database

The functions run as their owner and find a role by its id, past your query filter: a role of one customer on another's
resource gives nothing where your application asks, and its permissions where the database is asked. What holds each
writer:

- **Your application** asks the admission, `RequireRoleAsync`, under your filter.
- **A caller writing rows itself** is held by the lock, where a permission is marked `LocksMembers`: a role it gives
  must be one it sees in your role table. So your read rule there says whose a role is, from the token or a function
  of yours; and your rules keep the resources a caller may change to the same customer. Callers that change members
  read the roles they give and those of the rows they change, archived ones too.
- **The application's own work** and the table owner are not held. A row written before the lock was there is not
  looked at again; a query over your scope columns finds one.

The role table gets no policy from the package: give it row access rules ([step 4](tutorial.md#step-4-the-databases-own-rules)
writes two). The start-up check refuses a role table without RLS that a role which may ask the functions may write.
The functions' fingerprint covers that a customer makes the roles and what a role may give, so the check refuses
functions written before either changed; another starter role changes no function.
