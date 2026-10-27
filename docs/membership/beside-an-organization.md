---
title: "Membership: beside an organization"
sidebar_label: Beside an organization
---

# Membership: beside an organization

Read this page when your resources belong to the people of an organization. It covers who a resource's members are,
where their roles come from, and reach from above, beside Tenancy or beside an organization of your own.

## Three settings of the rules

The rules declare three things. Each setting is independent, so any choice for one goes with any choice for the
others:

| The rules say | When left out | When set, and what you implement |
|---|---|---|
| **Who a member is** | It follows from the member id: the user id for an id over a `Guid`, the seat for [Tenancy's seat id](#with-tenancy). | `members: MemberSource.Resolved(...)` takes an id you resolve for the caller. You implement `ICallerMember<TResourceId, TMemberId>`. |
| **Where the roles come from** | The rules always declare it. | `Roles.Fixed` or [`Roles.Custom`](permissions-and-roles.md#roles-a-customer-makes) need nothing from you. `Roles.External(...)` takes roles from rows you keep. You implement `IRolesWithPermission<TResourceId, TRoleId>` and `IMemberRoles<TResourceId, TRoleId>`. |
| **Whether something above reaches the resource** | Nothing above reaches it. Only the members and the owner hold permissions on it. | `reachFromAbove: ...`, a permission marked `EveryMember`, and `[At]` on the property that says where the resource hangs. You implement `IPlacesReached<TResourceId, TPlaceId>`. |

## Reach from above

Reach from above lets the people who run a part of the organization act on what hangs there, without being added to
each resource. It also keeps a resource reachable after its owner has left, so somebody can still hand the resource on
([in Harbor](overview.md#reach-from-above-a-permission-held-at-the-boards-unit),
[tutorial step 8](tutorial.md#step-8-reach-from-above)). These rules apply:

- **The permission every member holds is the one that sees from above.** A caller who holds that permission where
  the resource hangs, or above, sees the resource the way its members do. That caller also holds, on the resource,
  every other permission it holds above, except the permissions marked `FromAbove = false`. Rules that reach from
  above without an `EveryMember` permission are refused when they are made. So are rules whose `EveryMember`
  permission is marked `FromAbove = false`.
- **`WhereIHold` and a reach cover one permission, and ignore whether the caller sees the resource.** A caller who
  holds `boards.edit` above a board, without the permission that sees, is inside `db.Boards.WhereIHold(BoardPermissions.Edit)`
  and still does not see the board. The access manager's `HoldAsync` and `PermissionsOnAsync`, and the pairs of
  `WhereIHold([...])`, return results only for resources the caller sees. In your own query, put the permission that
  sees first: `db.Boards.WhereIHold(BoardPermissions.View).WhereIHold(BoardPermissions.Edit, db)`.
- **`InRoles = false` does not limit what is held above.** So an administrator at the root can also decide who is on
  a resource, and can hand it on.
- **Members come first.** For a caller who holds the permission both as a member and from above, `Via` is
  `Members`.
- **`[At]`** marks the one property that says where the aggregate root hangs. The marker comes from the package
  `DDDToolkit.Supporting.Membership.Abstractions`, which the Membership package references. Its namespace is
  `DDDToolkit.Supporting.Membership`. The property is never nullable ([DDD00097](../diagnostics.md#ddd00097)). Reach
  from above on a class without `[At]` is [DDD00098](../diagnostics.md#ddd00098).
- **A permission held from above lasts as long as the caller sees the resource.** The organization keeps the end of
  that permission, outside Membership. So `MemberHold.Until` is `null` for a caller who sees the resource from
  above. For a caller who sees the resource only as a member, `Until` is the end of the membership.
- **Reach from above still runs in one statement.** What you return is a query over rows your context maps
  ([on the context the query runs on](checks-and-saves.md#the-context-a-question-runs-on)).

## With Tenancy

The two packages do not reference each other. In a project that references both, Membership's generator writes two
things for each member class whose members are seats: a class that returns what the rules and the managers need from
Tenancy, and the registration `Add{Resource}MembershipWithTenancy`. The tutorial uses both throughout:

- the member class over `SeatId` ([step 1](tutorial.md#step-1-the-board-its-members-and-its-roles));
- the context ([step 3](tutorial.md#step-3-where-it-is-stored));
- the registration ([step 5](tutorial.md#step-5-the-modules-registration-and-the-host));
- reach from above ([step 8](tutorial.md#step-8-reach-from-above));
- the access manager over seats ([step 9](tutorial.md#step-9-what-the-caller-may-do-on-a-board)).

The [Tenancy sample](../tenancy/the-sample.md#who-may-do-what-in-the-sample) uses them at scale: a project's crew, with
project roles that each tenant makes from three starter roles.

### What the generated class does

- **A member is the caller's seat**, as Tenancy found it. A caller without an active seat in the tenant is refused
  before anything is read, with Tenancy's code: `tenancy.not-seated`, `tenancy.seat-suspended` or
  `tenancy.tenant-required`. This applies to lists (`[SeenWith]`) as well. A suspended seat reaches nothing, whatever
  rows name it.
- **System work is nobody's member.** It holds every permission where the rules let its scope
  through. So Tenancy's own work, in the `tenancy` scope, holds nothing on a board
  ([System work](checks-and-saves.md#system-work)).
- **A new member's seat must be active in the caller's tenant.** Otherwise the member manager refuses with
  `boards.member-not-active`. The class reads the seats the caller can read. That is every seat of the tenant, unless
  a [read rule on your seat class](../tenancy/postgres.md#default-read-policies-for-seats-and-invitations) narrows them.
- **A permission held at the resource's unit or above reaches the resource**, with `Via` set to `Above`. Only a
  permission in Tenancy's catalogue can be held there. A module's `[Permissions]` classes are part of the catalogue
  ([Declaring a module's permissions](../tenancy/roles-and-permissions.md#declaring-a-modules-permissions)).
  Asking about a permission marked `FromAbove = false` is not an error. Nobody holds such a permission from above.
- **A role given for a limited time** reaches the resource until the role ends. The hold does not name that moment,
  so a rule that needs that moment reads it from Tenancy.
- **The tenant's roles.** When the member class holds the tenant's roles, the class returns which roles in use give
  a permission, and whether a role exists. An archived role gives nothing, even where it is still held.
- **The permission that changes the custom roles** (`Roles.Custom(managedWith: ...)`) is held for the whole tenant
  or not at all. The role manager refuses a caller without it with `tenancy.not-permitted`, and a caller without an
  active seat with Tenancy's own code. A permission that is not in Tenancy's catalogue is held by nobody.
- **A change to the caller's own place** is saved as system work in the caller's tenant, in the resource's scope, for
  the caller's seat ([The caller's own place](resources-and-members.md#the-callers-own-place)). The history names the
  seat.
- **Rules that take the member from the token are refused** when you register: `MemberSource.CallerId` and
  `MemberSource.Claim(...)`. A seat id is over the same type of value as a user id, so the registration would read a
  user id as a seat id.
- **A port you registered earlier is kept.**

### Resource roles and tenant roles

The organization's roles live in Tenancy. They are given at a unit and count below it. A resource's own roles live in
Membership and count on that resource alone. A member class that holds roles by Tenancy's role id holds the tenant's
roles instead:

| | Resource roles | Tenant roles on the member list |
|---|---|---|
| The member class | `[Member<BoardMemberId, SeatId, BoardRoleId, Board>]` | `[Member<LabTechnicianId, SeatId, RoleId, Lab>]` |
| The rules say | `Roles.Custom(...)` | `Roles.External(TenancyFunctions.RolesWithPermission)` |
| A role of the organization | reaches the resource from above | reaches the resource from above, and also gives its permissions where it is given on the member list, limited to what a role may give |

### Where the class is generated

- **In the project that gets `Add{Resource}Membership`.** That is the member class's project, or the project of its
  module that holds the context. The project must reference Tenancy's Entity Framework package. Without Tenancy,
  nothing is generated.
- **Only for seats.** The generator reads Tenancy's ids from your classes declared with Tenancy's templates, in the
  project or in a project it references. A member id that is not the id of your `[SeatAggregate<SeatId>]` class gets
  nothing.
- **A module that refers to the organization only by its ids**, as Boards does, sees none of Tenancy's classes. One
  line in the module names Tenancy's ids, `[assembly: TenancyIds<TenantId, SeatId, OrganizationUnitId, RoleId>]`
  ([tutorial step 2](tutorial.md#step-2-the-permissions-and-the-rules)). The generator then writes the registration
  only for a member class whose member id is the seat id, and the registration names only the context:

  ```csharp
  services.AddBoardMembershipWithTenancy<BoardsContext>(BoardMembership.Rules);
  ```

- **Without that line**, the generator cannot tell which ids are Tenancy's, or a seat id from another id. So each
  member class of the module gets the registration, and the registration takes the tenant, unit and role id types.
  The role id type is left out where members hold the tenant's roles. Call the registration only for a resource whose
  members are seats:

  ```csharp
  services.AddBoardMembershipWithTenancy<BoardsContext, TenantId, OrganizationUnitId, RoleId>(BoardMembership.Rules);
  ```

### What you still write

- **The rules.** A resource of a tenant may take its members from elsewhere, keep roles any of the three ways, or
  have no reach from above. Tenancy's `CallerSeat` and `UnitsWhereIHold` functions are found without being named. The
  tenant's roles are named: `Roles.External(TenancyFunctions.RolesWithPermission)`.
- **The context**, which maps Tenancy's rows beside the resource:

  | Database | Map with |
  |---|---|
  | Postgres with `AddTenancyPostgres()`, which keeps the rights in the database | `modelBuilder.AddTenancyReadFunctions("tenancy")`, which the generator writes from the `TenancyIds` line. Without the line, the call names the four ids: `AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>("tenancy")` ([Read functions for modules](../tenancy/postgres.md#read-functions-for-modules)). `AddTenancyReadModel` is refused there. |
  | a database that does not keep the rights, such as SQLite | `AddTenancyReadModel<...>(...)`, which maps views over Tenancy's tables |

- **Where a resource hangs.** Put `[At]` on its unit property.
- **The tenant on your rows.** Membership stores no tenant. Mark the tenant property of the resource and of your role
  class `[OfTenant]`, from `Supporting.Tenancy.Abstractions`. Tenancy's convention keeps both classes inside their
  tenant, and Membership's mapping gives the role class its two unique indexes per tenant
  ([Which customer a role belongs to](permissions-and-roles.md#which-customer-a-role-belongs-to)). You make the
  starter roles with the role manager when you set up a tenant.
- **Handlers that need the caller's tenant or seat** take Tenancy's access manager, `TenancyAccessManager`, as the
  tutorial's `OpenBoard` does. It also begins system work in the module's scope
  ([System work](checks-and-saves.md#system-work)).
- **On Postgres**, the export writes Tenancy's contribution (from your `[TenancyCatalogue]`) and each resource's
  contribution together. So a resource's function finds Tenancy's functions in any schema. A function name that
  nothing defines is refused. What a seat reads inside its tenant is your
  [row access rule](postgres.md#the-five-functions).

## An organization of your own

You implement the ports the rules need, in one class that you pass to the registration. The registration registers
each port the class implements, for that resource. A port registered earlier is kept. A port that nothing implements
is refused at start-up, with a message that names what to register. This example is a crate in a depot, with all
three settings used:

```csharp
// A member is a porter of the depot, and a role is one of the depot's
[Member<CratePorterId, PorterId, DepotRoleId, Crate>]
public sealed partial class CratePorter;

[AggregateRoot<CrateId>]
public sealed partial class Crate
{
    [At] public BayId BayId { get; private set; }               // the bay the crate stands in
    [Owner] public PorterId OwnerId { get; private set; }
    public partial IReadOnlyList<CratePorter> Porters { get; }
}

[Permissions]
public static class CratePermissions
{
    [On<Crate>(EveryMember = true, Description = "See a crate")] public const string See = "crates.see";
    [On<Crate>(Description = "Pack a crate")] public const string Pack = "crates.pack";
    [On<Crate>(Owner = false, InRoles = false, Description = "Move a crate")] public const string Move = "crates.move";   // held only from above
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
    Roles.External(DepotFunctions.RolesWithPermission, otherPermissions: OtherPermissions.AllBut("depot.manage")),   // the depot's roles, without the depot's own permission
    members: MemberSource.Resolved(DepotFunctions.CallerPorter),
    reachFromAbove: DepotFunctions.BaysWhereIHold);

// The context's AddDDDToolkitConventions() maps the porters. Where a crate stands comes from its [At] property.

// Services: the resource, with the class that implements the depot's ports
services.AddCrateMembership<DepotContext, CratesInTheDepot>(CrateMembership.Rules);
```

The class that implements the ports for the depot:

```csharp
public sealed class CratesInTheDepot(DepotDesk desk, DepotContext db)
    : ICallerMember<CrateId, PorterId>, IMemberRoles<CrateId, DepotRoleId>,
      IRolesWithPermission<CrateId, DepotRoleId>, IPlacesReached<CrateId, BayId>
{
    // The caller's member id, or null. Uses only what is already known of the caller.
    public PorterId? Find(CurrentUserSnapshot user) => desk.ActivePorterOf(user);

    // The roles that give a permission, as a query that runs inside the statement that asks
    public IQueryable<DepotRoleId> RolesWith(DbContext context, CurrentUserSnapshot user, string permission)
        => context.Set<DepotRolePermission>().Where(given => given.Permission == permission).Select(given => given.RoleId);

    // The places where the caller holds a permission, and every place below them
    public IQueryable<BayId> PlacesReached(DbContext context, CurrentUserSnapshot user, string permission)
    {
        var id = user.Id;
        return from porter in context.Set<Porter>()
               where porter.UserId == id && porter.Active       // a porter the depot let go holds nothing
               join hold in context.Set<BayHold>() on porter.Id equals hold.PorterId
               where hold.Permission == permission
               join path in context.Set<BayPath>() on hold.BayId equals path.AboveId
               select path.BayId;
    }

    // Whether a role exists. The member manager calls this before a role is given.
    public async ValueTask<bool> ExistsAsync(DepotRoleId role, CancellationToken cancellationToken)
        => await db.Roles.AnyAsync(kept => kept.Id == role && kept.InUse, cancellationToken);
}
```

- **You decide who a member is.** The package calls `Find` for every signed-in caller. Return `null` for a caller who
  does not count, whatever rows name that caller. `Find` reads nothing from the database. Look the caller up once,
  where the request begins. `Require` refuses a caller outright, before anything is read. By default `Require`
  refuses nobody.
- **The package calls `PlacesReached` for every signed-in caller**, including the callers `Find` returns `null` for.
  So check there too whether the caller still counts. If you answer from the user id alone, somebody you let go keeps
  what they held.
- **A role gives a permission of the crate only where a role may give it** (`InRoles`), or where `otherPermissions`
  lets it through. `AllBut("depot.manage")` lets through the permissions of every other module, except the depot's
  own permission. Without `otherPermissions`, a role gives no permission of another module. Nothing held from above is
  limited this way.

### Your functions on Postgres

The five functions call yours, each by the `DatabaseFunction` the rules name (`owner/name`). You write three
functions:

- one without parameters that returns the caller's member id, or `NULL`;
- one that takes a permission and returns the role ids;
- one that takes a permission and returns the places.

Define them in [a contribution of your own](../row-level-security.md#policies-a-package-ships). The project that runs
the export lists it with `[assembly: UseRowAccessContribution(typeof(DepotRowAccess))]`. The example is shortened to
the first function:

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
                // depot/roles_with_permission and depot/bays_where_i_hold: each takes the permission as text, and returns a set
            ],
            Policies: [],
            Statements: []);
    }
}
```

- **Name each function once**, as a `DatabaseFunction` that the rules and the contribution both read. The export
  refuses a name that nothing defines. The start-up check refuses functions written from rules that name other
  functions.
- **Grant your functions to nobody.** The five functions run as their owner, so no caller needs yours. Without a
  grant, no caller can ask who it is or which roles give what.
- **Select the caller's rows with a condition of your own**, such as the caller's id or a claim. Do not rely on a
  policy. The five functions call yours as their owner, past your tables' policies, so a policy that limits rows to
  the caller has no effect there. The two functions that take a permission are never called without one.
- **On a database without Membership's functions, the rules name no function.** Write `MemberSource.Resolved()`,
  `Roles.External()` and `reachFromAbove: true`. With `true` on Postgres, the place function follows from the type
  of the place. Only a package that keeps the places, such as Tenancy, provides that function.
