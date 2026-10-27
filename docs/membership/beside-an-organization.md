---
title: "Membership: beside an organization"
sidebar_label: Beside an organization
---

# Membership: beside an organization

Who a resource's members are, where their roles come from, and reach from above, beside Tenancy or an organization
of your own. Read it when your resources belong to an organization's people.

## Beside an organization

The rules say three things apart from each other, and any choice of one goes with any of the others:

| The rules say | Left out | Said, and what you answer |
|---|---|---|
| **Who a member is** | follows from the member id: the user id for one over a `Guid`, the seat for [Tenancy's](#with-tenancy) | `members: MemberSource.Resolved(...)`, an id you resolve for the caller: `ICallerMember<TResourceId, TMemberId>` |
| **Where the roles come from** | always said | `Roles.Fixed` or [`Roles.Custom`](permissions-and-roles.md#roles-a-customer-makes), nothing to answer; or `Roles.External(...)`, rows you keep: `IRolesWithPermission<TResourceId, TRoleId>`, `IMemberRoles<TResourceId, TRoleId>` |
| **Whether something above reaches it** | nothing: members and owner alone | `reachFromAbove: ...`, an `EveryMember` permission, `[At]` on where the resource hangs: `IPlacesReached<TResourceId, TPlaceId>` |

**Reach from above** lets whoever runs a part of the organization act on what hangs there without being put on each
resource, and keeps a resource whose owner left somebody's to hand on
([in Harbor](overview.md#reach-from-above-a-permission-held-where-a-board-hangs),
[tutorial step 8](tutorial.md#step-8-reach-from-above)). Its rules:

- **Seen from above by the permission every member holds.** Who holds it where the resource hangs, or above, sees the
  resource as its members do, and holds there what else it holds above, but for `FromAbove = false`. Rules that reach
  from above without an `EveryMember` permission are refused when made.
- **A reach says where one permission is held, seen or not.** The questions answer only for what the caller sees; in
  your own statement, put the reach that sees first: `db.Boards.Within(seen).Within(act)`.
- **`InRoles = false` does not cut what is held above**, so an administrator at the root also says who is on a
  resource, and hands it on.
- **Members come first**: `Via` is `Members` for a caller that holds the permission both ways.
- **`[At]`** marks the one property where the aggregate root hangs, never nullable
  ([DDD00097](../diagnostics.md#ddd00097)); reach from above a class without one is [DDD00098](../diagnostics.md#ddd00098).
- **Held from above for as long as it is seen**: only the organization knows until when, so `MemberHold.Until` is
  `null` for a caller that sees the resource from above; for one that sees it as a member alone, the membership's end.
- **Still one statement**: what you answer is a query over rows your context maps
  ([on the context it runs on](checks-and-saves.md#inside-your-own-statements)).

## With Tenancy

Neither package references the other. In a project that references both, Membership's generator writes, for each
member class whose members are seats, the class that answers what the rules ask of Tenancy, and
`Add{Resource}MembershipWithTenancy`. The tutorial uses it throughout: the member class over `SeatId`
([step 1](tutorial.md#step-1-the-board-its-members-and-its-roles)), the context ([step 3](tutorial.md#step-3-where-it-is-stored)),
the registration ([step 5](tutorial.md#step-5-the-modules-registration-and-the-host)), reach from above
([step 8](tutorial.md#step-8-reach-from-above)). The [Tenancy sample](../tenancy/the-sample.md#who-may-do-what-in-the-sample)
does it at scale: a project's crew, with project roles each tenant makes from three starter roles.

### What the written class answers

- **A member is the caller's seat**, as Tenancy found it. A caller without an active seat in the tenant is refused
  before anything is read, with Tenancy's code: `tenancy.not-seated`, `tenancy.seat-suspended`,
  `tenancy.tenant-required`. A suspended seat reaches nothing, whatever rows name it.
- **The application's own work is nobody's member**: it holds everything where the rules let its scope through, so
  Tenancy's own work holds nothing on a board ([System work](checks-and-saves.md#system-work)).
- **A new member's seat is active in the caller's tenant**, or `MemberAdmission` refuses, `boards.member-not-active`.
  Tenancy answers from the seats the caller reads: all of the tenant's, unless a
  [read rule on your seat class](../tenancy/postgres.md#who-reads-the-seats-a-default-you-may-replace) narrows them.
- **A permission held at the resource's unit or above reaches it**, `Via` `Above`. Only a permission of Tenancy's
  catalogue is held there, and a module's `[Permissions]` classes are in it
  ([A module states its permissions once](../tenancy/roles-and-permissions.md#a-module-states-its-permissions-once)).
  Asking about one marked `FromAbove = false` is no mistake: nobody holds it from above.
- **A role given for a while** reaches until it ends; the hold does not name that moment, so a rule that needs it asks
  Tenancy.
- **The tenant's roles**, where the member class holds them: which in use give a permission, and whether a role is one.
  An archived role gives nothing where it is still held.
- **Rules that take a member from the token are refused** at start-up: a seat's id is over the same value as a user's,
  so `MemberSource.CallerId` would read a user for a seat.
- **A port you registered before stays.**

### The resource's roles, or the tenant's

The organization's roles live in Tenancy, given at a unit and counting below it; a resource's own in Membership,
counting on that resource alone. A member class that holds roles by Tenancy's role id holds the tenant's instead:

| | The resource's own roles | The tenant's roles on the member list |
|---|---|---|
| The member class | `[Member<BoardMemberId, SeatId, BoardRoleId, Board>]` | `[Member<LabTechnicianId, SeatId, RoleId, Lab>]` |
| The rules say | `Roles.Custom(...)` | `Roles.External(TenancyFunctions.RolesWithPermission)` |
| A role of the organization | reaches the resource from above | reaches it from above, and gives its permissions where given on the member list, cut to what a role may give |

### Where it is written

- **In the project that gets `Add{Resource}Membership`** (the member class's, or its module's that holds the context),
  when it references Tenancy's Entity Framework package. Without Tenancy nothing is written.
- **Only for seats**: the generator reads Tenancy's ids from your classes declared with its templates, in the project
  or one it references. A member id that is not your `[SeatAggregate<SeatId>]`'s gets nothing.
- **A module that knows the organization only by its ids**, as Boards does, cannot be told which are Tenancy's, nor
  the seat's from another. Each of its member classes gets the registration, which takes the tenant's, unit's and
  role's ids (the role's left out where members hold the tenant's roles); call it only for a resource whose members
  are seats:

  ```csharp
  services.AddBoardMembershipWithTenancy<BoardsContext, TenantId, OrganizationUnitId, RoleId>(BoardMembership.Rules);
  ```

### What stays yours

- **The rules**: a resource of a tenant may take its members elsewhere, keep roles any of the three ways, or have no
  reach from above. Tenancy's `CallerSeat` and `UnitsWhereIHold` functions are found without being named; the tenant's
  roles are named, `Roles.External(TenancyFunctions.RolesWithPermission)`.
- **The context**, which maps Tenancy's rows beside the resource:

  | Database | Map with |
  |---|---|
  | Postgres with `AddTenancyPostgres()`, which keeps the rights in the database | `AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>("tenancy")` ([Modules read through functions](../tenancy/postgres.md#modules-read-through-functions)). `AddTenancyReadModel` is refused there. |
  | one that does not keep them, such as SQLite | `AddTenancyReadModel<...>(...)`: views over Tenancy's tables |

- **Where a resource hangs**: `[At]` on its unit.
- **The tenant on your rows**: Membership knows none. `ScopeToTenant` keeps the resource and your role class to their
  tenant; starter roles are made when you set a tenant up.
- **On Postgres**, the export writes Tenancy's contribution (from your `[TenancyCatalogue]`) and each resource's
  together, so a resource's function finds Tenancy's in whatever schema; a name nothing defines is refused. What a
  seat reads inside its tenant is your [row access rule](postgres.md#on-postgres-the-second-lock).

## An organization of your own

You answer the ports the rules need, in one class handed to the registration, which registers each port it implements
for that resource. A port registered before stays; one that nobody answers is refused at start-up, naming what to
register. A crate in a depot, with all three said:

```csharp
// A member is a porter of the depot, and a role is one of the depot's
[Member<CratePorterId, PorterId, DepotRoleId, Crate>]
public sealed partial class CratePorter;

[AggregateRoot<CrateId>]
public sealed partial class Crate
{
    [At] public BayId BayId { get; private set; }               // where the crate stands: what reaches it from above
    [Owner] public PorterId OwnerId { get; private set; }
    public partial IReadOnlyList<CratePorter> Porters { get; }
}

[Permissions]
public static class CratePermissions
{
    [On<Crate>(EveryMember = true, Description = "See a crate")] public const string See = "crates.see";
    [On<Crate>(Description = "Pack a crate")] public const string Pack = "crates.pack";
    [On<Crate>(Owner = false, InRoles = false, Description = "Move a crate")] public const string Move = "crates.move";   // held from above alone
    [On<Crate>(Description = "Scrap a crate")] public const string Scrap = "crates.scrap";
}

// The depot's functions in the database, each named once, by its logical name
public static class DepotFunctions
{
    public static DatabaseFunction CallerPorter { get; } = new("depot", "caller_porter");
    public static DatabaseFunction RolesWithPermission { get; } = new("depot", "roles_with_permission");
    public static DatabaseFunction BaysWhereIHold { get; } = new("depot", "bays_where_i_hold");
}

public static MembershipRules Rules { get; } = MembershipRules.For<Crate>(
    Roles.External(DepotFunctions.RolesWithPermission, otherPermissions: OtherPermissions.AllBut("depot.manage")),   // the depot's roles; not its own permission
    members: MemberSource.Resolved(DepotFunctions.CallerPorter),
    reachFromAbove: DepotFunctions.BaysWhereIHold);

// OnModelCreating: the members; where a crate stands is its [At] property
modelBuilder.Entity<Crate>().HasMembers(crate => crate.Porters);

// Services: the resource, with the class that answers for the depot
services.AddCrateMembership<DepotContext, CratesInTheDepot>(CrateMembership.Rules);
```

The class that answers for the depot:

```csharp
public sealed class CratesInTheDepot(DepotDesk desk, DepotContext db)
    : ICallerMember<CrateId, PorterId>, IMemberRoles<CrateId, DepotRoleId>,
      IRolesWithPermission<CrateId, DepotRoleId>, IPlacesReached<CrateId, BayId>
{
    // Who the caller is as a member, or nobody: from what you know of the caller already
    public PorterId? Find(Caller caller) => desk.ActivePorterOf(caller);

    // The roles that give a permission: a query, which goes into the statement that asks
    public IQueryable<DepotRoleId> RolesWith(DbContext context, Caller caller, string permission)
        => context.Set<DepotRolePermission>().Where(given => given.Permission == permission).Select(given => given.RoleId);

    // Where the caller holds a permission: each place it is held at, and every place below
    public IQueryable<BayId> PlacesReached(DbContext context, Caller caller, string permission)
    {
        var user = caller.UserId;
        return from porter in context.Set<Porter>()
               where porter.UserId == user && porter.Active     // a porter the depot let go holds nothing
               join hold in context.Set<BayHold>() on porter.Id equals hold.PorterId
               where hold.Permission == permission
               join path in context.Set<BayPath>() on hold.BayId equals path.AboveId
               select path.BayId;
    }

    // Which roles there are: what the admission asks before a role is given
    public async ValueTask<bool> ExistsAsync(DepotRoleId role, CancellationToken cancellationToken)
        => await db.Roles.AnyAsync(kept => kept.Id == role && kept.InUse, cancellationToken);
}
```

- **A member is who you say.** `Find` is asked about every signed-in caller and answers nobody for one that does not
  count, whatever rows name it. It reads nothing: look the caller up where the request begins. `Require` refuses a
  caller outright, before anything is read; by default it refuses nobody.
- **Above is asked about everybody who signed in**, `Find`'s nobodies too, so say there as well whether the caller
  still counts: answered from the user id alone, somebody you let go keeps what they held.
- **A role gives a permission of the crate a role may give** (`InRoles`), or one `otherPermissions` lets through:
  `AllBut("depot.manage")` lets every other module's but the depot's own; left out, none. Nothing from above is cut.

### On Postgres

The five functions ask yours, each by the `DatabaseFunction` the rules name (`owner/name`): one without parameters
that answers the caller's member id or `NULL`, and two that take a permission and answer the role ids and the places.
Define them in [a contribution of your own](../row-level-security.md#policies-a-package-ships), listed by the project
that runs the export with `[assembly: UseRowAccessContribution(typeof(DepotRowAccess))]`. Shortened to the first
function:

```csharp
public sealed class DepotRowAccess : IRowAccessContribution
{
    public string Owner => "depot";

    public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
    {
        if (context.Model.FindEntityType(typeof(Porter)) is not { } porters)
        {
            return null;                                        // this context does not map the depot
        }

        var id = RowAccessModel.Column(porters, nameof(Porter.Id));
        var user = RowAccessModel.Column(porters, nameof(Porter.UserId));
        var active = RowAccessModel.Column(porters, nameof(Porter.Active));
        return new(
            Functions:
            [
                // depot/caller_porter: runs as its owner, and is granted to nobody
                new ContributedFunction(DepotFunctions.CallerPorter.Name, "", "uuid",
                    $"SELECT p.{id} FROM {RowAccessModel.Table(porters)} p WHERE p.{user} = {{caller:uid}} AND p.{active}",
                    SecurityDefiner: true),
                // depot/roles_with_permission and depot/bays_where_i_hold: each takes the permission as text, and answers a set
            ],
            Policies: [],
            Statements: []);
    }
}
```

- **Named once**, as a `DatabaseFunction` the rules and the contribution both read. The export refuses a name nothing
  defines; the start-up check refuses functions written from rules that name others.
- **Grant them to nobody**: the five run as their owner, so no caller needs yours, or can ask who it is or which roles
  give what.
- **Answer for the caller by a condition of your own**, its id or a claim, never by a policy: the five ask yours as
  their owner, past your tables' policies, so "the caller's rows" would be everybody's. The two that take a
  permission are never asked without one.
- **A database that answers nothing itself names no function**: `MemberSource.Resolved()`, `Roles.External()`, and
  `reachFromAbove: true`, whose place function follows from its type (only a package that keeps the places, such as
  Tenancy, answers it).
