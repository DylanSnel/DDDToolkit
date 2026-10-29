# Supporting domains and Tenancy

A supporting domain is a package that ships aggregates an application extends: the package states the
rules every application shares, and the application declares the classes, with its own ids, fields and
rules. Tenancy is the toolkit's first: who a person is in a tenant, and what they may do where in its
organization. Full pages: `https://dylansnel.github.io/DDDToolkit/docs/tenancy/overview.md`, the topic pages
beside it under `docs/tenancy/` (listed on the overview and in `llms.txt`), and
`writing-a-supporting-domain.md`. The Tenancy packages are published as prereleases, under `Temp.` ids like
every 3.x package, and their names can change until a release that is not one; read the project's references
before writing against them.

Packages: `DDDToolkit.Supporting.Tenancy` (the model, its rules, a manager per aggregate, the access manager, no
database), `DDDToolkit.Supporting.Tenancy.Abstractions` (`[OfTenant]`, the marker of a row's tenant, for a module's
domain project that sees only Tenancy's ids; the first package references it),
`DDDToolkit.Supporting.Tenancy.EntityFramework` (`AddTenancy()`, the read model, the queries as `db.Tenancy()`, the
convention that maps `[OfTenant]`) and `DDDToolkit.Supporting.Tenancy.Postgres` (the same queries as SQL functions
and row level security).

## A class of a supporting domain

```csharp
[SeatAggregate<SeatId>]                     // the package's attribute, closed over your id
public sealed partial class ShopSeat
{
    public string? JobTitle { get; private set; }             // your own field, stored with the seat

    public sealed class JobTitleLength : IInvariant<ShopSeat>   // your own rule, after the package's
    {
        public string Code => "shop.seat.job-title";

        public InvariantFailure? Check(ShopSeat seat) => seat.JobTitle is { Length: > 80 } ? "A job title is at most 80 characters." : null;
    }
}
```

- Declare a class only where it adds something. `[assembly: GenerateTenancyClasses]` in the project the
  classes belong to has the generator write every class of Tenancy's the project leaves out (`Tenant`,
  `Organization`, `OrganizationUnit`, `Role`, `Seat`) and every id of theirs no project of the module declares
  (`TenantId`, `OrganizationUnitId`, `RoleId`, `SeatId`, `[EntityId<Guid>]`), public, in the root namespace. A
  class or an id you declare always wins. Never write an empty class by hand next to the switch, and never
  copy a written one into a file: declare it only to add to it, or, in a module of one project, where another
  generator of that project needs to see it (HotChocolate's `[ObjectType<Role>]`, a row access rule that names
  it), since none sees what the switch wrote; a project above sees it. Code outside the root namespace imports
  the written types with `global using <RootNamespace>;`, not a file's using. The invitation is never written; declare it
  to have invitations. A module split by layer says `[assembly: GenerateTenancyIds]` in its contracts project
  (which then references the package) or declares the four ids there, and `GenerateTenancyClasses` in its
  domain project. Without the switch, every class is declared. A missing one, or a class that does not fit the
  parent's constraints, is DDD00042 to DDD00050 and DDD00053, and what the switch cannot write is DDD00066
  ([diagnostics.md](diagnostics.md)).
- The package's rules run first and always. There is nothing to override: no virtual members, no hooks.
  Add fields, entities, invariants, and handlers of the package's domain events.
- The package's registrations are generated closed over your classes, internal to the project that
  declares them, or to the project of the same module that holds the context:
  `modelBuilder.AddTenancy(database: Database)` and `services.AddTenancy<TContext>(...)`. Do not write the
  generic form by hand, and call them from that project, not from an API project above it.

## Adopting Tenancy

1. `[assembly: GenerateTenancyClasses]`, and a class of your own only for an aggregate you add to, such as
   `[SeatAggregate<SeatId>]` with a job title. Or declare all four ids (`TenantId`, `SeatId`,
   `OrganizationUnitId`, `RoleId`) and a class for each aggregate yourself: `[TenantAggregate<TenantId>]`,
   `[OrganizationAggregate<TenantId>]`, `[OrganizationUnit<OrganizationUnitId>]`, `[SeatAggregate<SeatId>]`,
   `[RoleAggregate<RoleId>]`.
2. A plain context in the module, and migrations of your own. The package has none.

   ```csharp
   protected override void OnModelCreating(ModelBuilder modelBuilder)
   {
       modelBuilder.HasDefaultSchema("tenancy");
       modelBuilder.AddTenancy(database: Database);          // with Database: the two unique indexes too
       modelBuilder.AddDomainEventOutbox(Database, schema: "tenancy");
   }
   ```

3. Register it, with the catalogue when the application has one; every option has a default, and a new id is
   the id's own `TenantId.Create()` (an id over a `long` declares it, [DDD00067](diagnostics.md)). `AddTenancy` brings
   Tenancy's save check, and `UseDDDToolkit` puts it on this context and on every context that keeps rows to a
   tenant: nothing more to write. A context given the base alone, `UseDDDToolkitCore`, takes it with `UseTenancy`
   after it.

   ```csharp
   services
       .AddTenancy<ShopTenancyContext>(options =>
           options.Catalogue = ShopCatalogue.Application)   // optional: starter roles, permissions of its own, permissions that manage access
       .AddDbContext<ShopTenancyContext>((serviceProvider, options) => options
           .UseNpgsql(connectionString)
           .UseDDDToolkit(serviceProvider));
   ```

   A starter role of the catalogue is `new StarterRole(permission, name, description, permissions)`. `SeededFor: TenantShape.Hierarchical`
   gives a copy only to a tenant of that shape, provisioned so or changed to it; left out, every tenant gets one.
   It filters seeding and is no property of the role: a role has no shape. Declare no administrators' starter role
   and every tenant gets Tenancy's own; declare one (`Administers: true`) and declare one for every shape, a single
   one without `SeededFor` or one seeded for each shape.

4. Take the managers and the access manager by their own names, and write no alias. The toolkit's generator
   writes, into the project that declares your classes, a sealed class per manager closed over them:
   `TenantManager`, `OrganizationManager`, `SeatManager`, `RoleManager`, and `InvitationManager` where an
   invitation class is declared; `TenancyAccessManager` closed over the four ids; and the record
   `TenantToProvision`. Each has the package's class name without type parameters, whatever the module is called,
   in the global namespace, and every project above sees it. `AddTenancy` registers them per scope; the container
   makes them. A method takes your object or its id (`GrantAsync(seat, role, at, until, reason, ct)`), creating
   returns the new aggregate, changing returns the changed one, reads return your own classes
   (`seats.ByIdAsync(ids, ct)`, `roles.ListAsync(ct)`). A record nested in a manager is the package's own type:
   `SeatManager.SeatOverview`, `OrganizationManager.UnitInTree`.
   The stores the managers save through are interfaces (`ITenancyStore<...>`, `IInvitationStore<...>` in
   `DDDToolkit.Supporting.Tenancy.Managers`): the generator writes `global using TenancyStore = ...;`, and
   `InvitationStore` where an invitation class is declared, into the declaring project and every project above that
   sees its classes (where the toolkit's generator runs). A command that changes a field of the application's own on
   a Tenancy aggregate takes `TenancyStore`, loads, changes, `SaveAsync`; write no alias of the stores either.
   A hand-written `global using SeatManager = ...` above it is CS0576: delete it. Two modules that each declare
   Tenancy's classes get two classes of each name, and the host is warned once per name (DDD00075): copy the lines
   the messages write out into one module's domain project, beside `[assembly: Module]` or the switch,
   `[assembly: TemplateFacadeName("SeatManager", "CustomersSeatManager")]`, one per class (in
   `DDDToolkit.Abstractions.Attributes`; another name works too, but not one a namespace has), and take that
   module's managers by those names above. CS0246 for a name above: read DDD00065 in that project. Only a module of
   one project with HotChocolate types over the nested records keeps one alias of exactly the class's name there,
   since another generator does not see a generated class.
   `TenancyAccessManager` says who acts and begins the work that is not the caller's own. It derives from the core's
   `AccessManager`, so a Tenancy application takes this one: `access.CurrentUser`, the
   toolkit's `ICurrentUser` (`Id`, `IsAuthenticated`, `IsSystem`, `Role`), `access.CurrentTenant` (`Id`, `Seat`, `IsAvailable`,
   `Require()`), `access.BeginAs(...)`, `access.BeginSystemBypassingRowAccess()` and `access.BeginNone()` (the core's,
   which leave the tenant as it is), `access.BeginSystem()` (outside every tenant, in Tenancy's scope `tenancy`, for
   provisioning), `access.BeginSystem(scope)` (outside every tenant, in that scope),
   `access.BeginSystemIn(tenant, actingSeat, scope)`,
   `access.BeginTokenIn(tenant, seat, scope)` and `access.TenantsToSweepAsync(scope, ct)`. A module that sees only
   Tenancy's ids says which they are once, in its application project,
   `[assembly: TenancyIds<TenantId, SeatId, OrganizationUnitId, RoleId>]`, and gets `TenancyAccessManager` in its
   root namespace, `db.Tenancy()` and `services.AddTenancyAccess<TRequests, TContext>()` in its infrastructure
   project. Write no `TenancyWork.BeginSystem<TenantId, SeatId>()`.

   ```csharp
   public sealed class FirstTenant(TenantManager tenants, TenancyAccessManager access)
   {
       public async Task<Tenant> SetUpAsync(Guid identity, CancellationToken cancellationToken)
       {
           using (access.BeginSystem())                           // provisioning is system work outside any tenant
           {
               return await tenants.ProvisionAsync(               // returns the new Tenant
                   new TenantToProvision(
                       "harbor", "Harbor Works", TenantShape.Hierarchical, "Harbor Works", identity,
                       ConfigureFirstSeat: seat => seat.Rename("Ada")),   // a seat's name is a field of yours
                   cancellationToken);
           }
       }
   }
   ```

5. Per request, after authentication: the token's user, then that user's seat in the tenant the request
   names. The seat is found from the verified identity, never taken from what a client sends.

   ```csharp
   var access = context.RequestServices.GetRequiredService<AccessManager>();   // the core's, a singleton
   var requestUser = context.GetSupabaseUser();
   using (access.BeginAs(requestUser))
   {
       // ITenantSelection: the selection without its ids, so the middleware names none
       var seat = await selection.ResolveAsync(requestUser, context.Request.Headers["Tenant"], context.RequestAborted);
       using (TenancyUsers.Begin(seat))
       {
           await next(context);
       }
   }
   ```

6. A module's own tables: mark the aggregate root's tenant in the domain, `[OfTenant] public TenantId TenantId
   { get; private set; }` (`DDDToolkit.Supporting.Tenancy`, package `...Tenancy.Abstractions`), and call
   `configurationBuilder.AddDDDToolkitConventions()` in the context's `ConfigureConventions`: Tenancy's convention
   gives every marked class the tenant filter and the save check, with no line per class. One marked property per
   class, of the tenant id, never nullable, on the root of a hierarchy; a model that breaks this, or that marks a
   class without Tenancy's Entity Framework package referenced, is refused while it is built. A context that leaves
   the conventions out is refused at its first save. `modelBuilder.Entity<Project>().ScopeToTenant(project =>
   project.OwnerTenant)` is for a tenant held in another property, and wins over the marker. The model then requires
   the save check, so such a context without it is refused at its first save, and the start-up check `AddTenancy`
   brings holds every context that keeps rows to a tenant to it. A module's permissions,
   stated once, as the constants of a `[Permissions] public static class` with a level and a description each:
   `[AtTenant(Description = "See the orders")] public const string View = "orders.view";` (`[AtUnit]` for a
   permission asked at a unit, Membership's `[On<TResource>]` for one asked on a resource with members; the constant's
   `<summary>` is the description where no level says one, and neither is DDD00099). Tenancy's generator
   writes the module's `DeclaredPermissions.All` from it (marked `[TenancyPermissions]`, never by hand: DDD00095), and
   in the host, which references every module, `services.AddTenancyPermissionsOfModules()`, generated in every
   application (module or not) and every library that declares no module, in the namespace named after the
   project's assembly (a top-level `Program.cs` needs `using <HostAssembly>;`). The Postgres export finds the same
   lists itself, in the modules the exporting project references, and builds its catalogue from them and the
   `ApplicationCatalogue` marked `[TenancyCatalogue]` (written into that project's
   `DDDToolkit.RowAccessContributionsOfPackages.g.cs`). Do not also call `AddTenancyPermissions` with a module's
   list: the catalogue refuses it twice (DDD00092 is a `[Permissions]` class or constant of the wrong shape;
   DDD00070 a `[TenancyCatalogue]` or `[TenancyOperators]` that a library keeps internal).
7. Provisioning, seeding and jobs are system work, begun on purpose:
   `using (access.BeginSystemIn(tenant, actingSeat)) { ... }`. A job visits the tenants of
   `await access.TenantsToSweepAsync("inspections", ct)` and begins system work in each, in its own scope. A request
   is never system work; an operator's command runs as operator work, which the pipeline begins (`[Operator]`). With `services.RequireExplicitCurrentUser()` work that began no user fails instead of running
   as system work.

## The access model

- A **tenant** has an **organization**, a tree of units. A **seat** stands for one person in one tenant:
  it is placed in units, with **role** grants at each. A role holds permission **permissions** from the
  **catalogue**.
- A permission held at a unit holds for every unit below it. A grant counts from its start to its end, compared
  when the question is asked: nothing is written when one runs out.
- Tenancy's queries are about the organization and nothing else, `db.Tenancy()` over a context. The module that
  owns a resource decides who may open it, with what they return and what it knows itself (a project's crew, say).

  | Query | Returns |
  |---|---|
  | `UnitsWhereIHold(permission)` | the units where the calling seat holds the permission, and every unit below them |
  | `HoldsAtAsync(permission, unit)`, `HoldsAtTenantAsync(permission)` | whether it holds the permission there, or for the whole tenant |
  | `UnitsWhereIHold(permissions)` | every pair of a unit and a permission, for several permissions in one query |
  | `RolesWithPermission(permission)`, `RolesWithPermission(permissions)` | the active roles that grant a permission, or pairs of role and permission |

- **Ask inside the module's own statement.** The module's context maps Tenancy's read model
  (`AddTenancyReadModel`, or `AddTenancyReadFunctions` on Postgres), and
  `db.Tenancy().UnitsWhereIHold(permission)` is a query, not a list: use it in `Where(project =>
  held.Contains(project.UnitId))` and it becomes a subquery. Do not fetch the units first, and do not ask
  `HoldsAtAsync` per row. A read source that is not Entity Framework asks `access.In(source, executor)`.
- **Permission sets draw a screen; a command asks again.** A list may carry what the caller can do with each row.
  The manager still checks when it runs.
- **A module answers ids, never Tenancy's names.** The read model has ids, permissions, periods and statuses, and
  no name. What a unit or a role is called is asked of the managers by id (`OrganizationManager.UnitsByIdAsync`,
  `RoleManager.ByIdAsync`) by whoever shows it.
- **The managers' reads return the application's own classes, whole.** `seats.ListAsync(ct)` and
  `seats.ByIdAsync(ids, ct)` return the module's `Seat`s, `organization.ListUnitsAsync(ct)` its units as
  `OrganizationManager.UnitInTree` (the unit, its `Path`, its `Depth`), `roles.ListAsync(ct)` and
  `roles.ByIdAsync(ids, ct)` its `Role`s (`roles.ManagesAccess(role)` for whether one manages access), all untracked;
  `organization.UnitsByIdAsync(ids, ct)` names units alone, as `NamedUnit` (`Id`, `Name`, `Path`, `Depth`),
  through a function that runs as its owner on Postgres, so a seat kept out of the organization still reads what
  its project's unit is called. The operators' `tenants.ListAsync(after, size, ct)` returns a
  `TenantManager.TenantPage` of `TenantListing(Tenant, Name, ActiveSeats)`. Whoever wants fewer fields selects
  them. `seats.WhoAmIAsync(ct)` returns a `SeatManager.SeatOverview`: the `Seat` with its placements and grants,
  and only what is not on it (`Tenant`, `TenantName`, `Units` by path, `Roles` its grants name, `Permissions`,
  `AsOf`; `UnitOf(id)` and `RoleOf(id)` look them up). The tenant picker's
  `TenantSelection.SeatsOfAsync<Seat>(user.Snapshot, ct)` returns `SeatInTenant<Seat>` (the seat, `Slug`,
  `OrganizationName`, `TenantStatus`). A seat carries its identity: decide what leaves, in a route's select or in
  the GraphQL type over the class. `seats.OverviewAsync(seat, ct)` returns the same `SeatOverview` for any seat of
  the tenant and asks no permission: the request that sends it says who may ask (the sample's `SeatGrants`,
  `tenancy.seats.manage` for the whole tenant), and the read rules what comes back. A listed seat and an overview
  carry its placements and grants, and only Postgres's policies narrow another seat's grants, with the permissions worked
  out from the grants read; elsewhere the request is the only rule. A seat the caller does not read is
  `tenancy.seat-not-found`. `RoleOf(id)` is `null` for a role the application's own filter hides.
- **A seat has no name in Tenancy.** What a person is shown by is the application's: a field of its seat
  class (set in `ConfigureFirstSeat`, `seats.AddAsync(..., configure:)` and `invitations.AcceptAsync(..., configure:)`,
  renamed by a command of its own), the identity provider's name, or a profile of its own by `Identity`.
  Never add a name back to Tenancy.
- **Who may give a role.** `tenancy.grants.manage` at the unit gives any role that manages no access. A
  role that manages access is given only by a seat that holds each of its permissions that do, there and for at
  least as long, and never to itself. A tenant always keeps an administrator. Mark your own permissions that give
  power over other people's access (`ManagesAccess: true`, or `AccessManagingPermissions` in the catalogue), and
  pin the set with a test: a forgotten mark fails open.
- **Containment is a setting**, `ApplicationCatalogue.ContainAccessManagingPermissions`, on by default: off, a role that
  manages access goes as one that manages none, so a seat with a permission that manages access hands out every permission it
  reaches, in C# and in the exported SQL alike (a seat's grant to itself still ends when its grants permission does).
  Leave it on when the database can be reached without the handlers (Supabase's Data API). A handler that gives a
  role after a check of its own (a quiz) keeps it on and grants inside `access.BeginSystemIn(tenant)`, which
  containment never holds: the seat comes from `access.CurrentTenant.Require()` and the role from the application,
  never from the request. The setting is on the part marked `[TenancyCatalogue]`, so the host and the
  export read one value. On Postgres export and apply the access files after changing it:
  `tenancy.policies-in-place` refuses a database written the other way round.
- **Starter roles after provisioning.** A tenant's roles start as copies of the catalogue's starter roles, and
  are the tenant's own to rename and to give other permissions. `builder.Services.SyncStarterRoles()` in the host,
  beside `RunStartupChecks()` and not a check, since it changes roles, makes every role made from a starter role
  follow it once the host has started, tenant by tenant as system work: what the starter role gained is added,
  what it lost is taken out, what the tenant changed itself stays, and each role that changes raises `RoleFollowedItsStarterRole`,
  which the access history keeps. It needs the roles' nullable `PermissionsFromStarterRole` column: add a migration. A host
  that syncs from a deployment step runs `IStarterRoleSync` instead; one tenant is `RoleManager.FollowStarterRolesAsync`.
- **A refusal is a `RefusalException` with a code**: `tenancy.not-permitted`, `tenancy.not-seated`,
  `tenancy.grant-exceeds-own`, `tenancy.last-admin`. Branch on `Code`, never on the message. A refusal
  about one input names it in its `Field` argument. A class of codes makes its refusal with `Refuse`,
  `throw TenancyRefusals.Refuse(TenancyRefusals.SeatNotFound)`, and a module's own does the same,
  `throw ProjectRefusals.Refuse(ProjectRefusals.NotFound)`; a method that converts is `From`, one that looks up `For`.
- **Operators** are the application's own staff: a token role listed in `TenancyOptions.OperatorTokenRoles`.
  They hold no seat and no permission. A request for them carries `[Operator]`; every other caller is refused with
  `tenancy.operators-only`. On a query nothing begins: an operator reads across tenants through its token's database
  role. On a command the pipeline runs the handler as operator work, system work recorded as the operator: in the
  tenant of the command's `[ForTenant] TenantId Tenant` member (another tenant beside it is
  `tenancy.tenant-mismatch`), with `[Operator(Tenant = OperatorTenant.FromRequest)]` in the tenant the request
  names, with neither in no tenant (provisioning). `[Operator(Scope = "projects")]` for a module's own command. The
  handler writes no `using`, and does the operator's act and nothing more. Not beside `[InTenant]` or
  `[RequiresPermission]` (DDD00104); for an operator or a seat, `AccessRequirement.Any(TenancyAccess.Operator(),
  TenancyAccess.AtTenant(permission))`.
- **Who acted** is a seat, an operator, the system or a token, on every event of Tenancy's (`By`) and on a
  row that `RecordsWhoChanged()`. Never put whoever work acts for into the toolkit's `ICurrentUser`.
- **Invitations** are optional (`AddTenancyInvitations`). The token is a bearer credential: show it once,
  store its digest, keep it out of logs and query strings. A seat is linked to a verified identity, never
  found by an e-mail address. The account a person signs in with is asked for through `IIdentityAccounts`,
  after the invitation is issued; to have the provider's mail carry the token, pass the accept page's
  address with the token after the `#` as the redirect.
- **Keep access out of the domain project.** `ScopeToTenant`, `RecordsWhoChanged` and `[RowAccess]` rules go
  in the infrastructure project, permissions and the access check in the application project. Facts about the
  aggregate stay on it: `[OfTenant]`, Membership's `[At]` and the core's `[Owner]`.

On Postgres, `services.AddTenancyPostgres()`, with the catalogue marked `[TenancyCatalogue]` (and the operators' token
roles `[TenancyOperators]`), which the Supabase export writes Tenancy's policies from because the exporting project
references the package: no class and no `UseRowAccessContribution` line for it,
puts the same rules in row level security under the application's checks, and brings the `TenancyPostgresChecks`
as start-up checks, which `services.RunStartupChecks()` runs with the others. A module's rule asks through `TenancyRowAccess.UnitsWhereIHold<TUnitId>(permission)`, which is
set-shaped: once per statement, never per row. There a seat reads its own rights and grants, and another seat's
grants only at the units where it holds `tenancy.grants.manage`, `tenancy.seats.manage` or `tenancy.units.manage`,
or all of them with `tenancy.roles.manage` for the whole tenant; `db.Tenancy().SeatsHoldingPermissionAt` returns by the
same rule on every database. The database guards Tenancy's own columns of a seat (no seat changes its id, identity
or tenant; its status only as the managers do) and none of yours: a field you add to the seat class is as writable
as the row, by the seat itself and by a seat that manages seats or grants anywhere in the tenant, until a column
rule of yours holds it to your command's rule, beside your infrastructure:
`[RowAccess<Seat>(RowOperations.Change, To = [RowAccessRoles.User], Columns = [nameof(Seat.DisplayName)])]`.
Who reads the seats, and who besides the seats managers reads an invitation, are defaults: a read rule on your seat
or invitation class, `[RowAccess<Seat>(RowOperations.Read)]` (without `To` it is for signed-in users, as the
default is), takes the place of one, held to the tenant, beside what Tenancy's own work reads (a person's own seats;
every seat of the tenant to a seat that manages seats, grants or units anywhere, or roles for the whole tenant),
which no rule takes away. Write it with `TenancyRowAccess`'s functions (`CurrentSeatId`, `CurrentTenant`,
`UnitsWhereIHold`, `SeatsInMyUnits`: the seats placed at or below the caller's units; `CurrentSeat<Seat>().Kind`, a
field of the caller's own seat), or a module's `[ResourceAccessContract<ProjectId>(ResourceAccessSet.FellowMembers)]`
(declaring `static partial AccessSet<SeatId> Ids();`), the people on the caller's projects. It changes what a read of the
seats returns, a direct read, the managers' reads, `tenant_seats()` and `db.Tenancy().Seats()`, so Membership's
member manager then admits only seats the caller reads (`member-not-active` otherwise); never Tenancy's queries about
the caller and rights, the placements (every member still reads each seat's id and units), or who writes. A read
rule on any other class of Tenancy's is refused.
To take away what Tenancy allows, write a restrictive rule, `[RowAccess<Organization>(RowOperations.Read, Restrictive = true)]`:
AND-ed with Tenancy's policies, it narrows and never grants (`CurrentSeat<Seat>().Kind == SeatKind.Employee` keeps an
external seat out of the organization, its units and its tree). It goes on the organization, seat or role class;
a tenant or invitation is DDD00100. Tenancy's own work and its owner-run functions keep working.
See `tenancy/postgres.md` ("Row level security on Postgres"): "Default read policies for seats and invitations",
"Restrictive rules", "Who reads which grants" and "Who may change a seat's columns".

## A module on Tenancy, as the sample lays it out

`Examples/Tenancy` in the repository is the worked example. Its layout, which
[persistence-and-modules.md](persistence-and-modules.md) shows as a tree:

- **A project per layer**, every one declaring the module, which the sample does once for the folder with
  `DDD_Module` in `Modules/Directory.Build.props`: Contracts, Domain, Application, Infrastructure, Api. The host references the Api project alone, and calls `Add{Module}Module` and
  `Map{Module}Module`. Only Infrastructure names Entity Framework, only Api names ASP.NET Core.
- **Feature folders, with `Commands` and `Queries` inside.** `Crew/Commands/AddCrewMember.cs`,
  `Crew/Queries/AllCrewMembers.cs`. A use case is one file: the request, its handler, and what only it
  returns. Never a folder named after a kind of class at a project's root.
- **`Rest` and `GraphQL` per feature in the Api project**, under the application project's feature names:
  `Crew/Rest/CrewEndpoints.cs`, `Crew/GraphQL/CrewMutations.cs`. A route and a field only send the
  feature's command or query.
- **The namespace of a type is its folder**, in every project and in the tests.
- **A request says who may send it**, by implementing its module's request interface and carrying
  requirement attributes, which all hold where several are stacked. The interface is marked `[AccessRequests]` and declared `partial`, so the module's
  pipeline behavior is generated, which asks the module's checks before the handler runs, and `RequiredAccess` is
  written into it from the attributes. A module writes a check (`IAccessCheck`) only for cases of its own;
  `AddTenancyAccess<TRequests, TContext>()` adds the package's checks for Tenancy's cases (the operator cases among
  them), and `AddProjectMemberAccess<IProjectsRequest>()`, generated for the Membership package's member class, the
  check for a permission held on a project. The attributes: the toolkit's `[AllowAnonymous]` and `[SignedIn]`,
  Tenancy's `[InTenant]` and `[Operator]` (with `[ForTenant]` on the tenant a command acts in),
  `[RequiresPermission(permission)]` for the whole tenant, with Tenancy's `[AtUnit]` on the unit for a permission there, or with
  Membership's `[OnResource]` on the resource and `[ExpectedVersion]` on the version read, and Membership's
  `[SeenWith<TResourceId>(permission)]` for a query that shows the resources the permission is held on, and the core's `[OrOwn]`
  on a seat beside `[RequiresPermission]` for the caller's own seat or the permission (`RenameSeat`; not beside `[AtUnit]`), and
  `[OwnOnly]` with `[Own]` for the seat alone. Where nearly every request of a module requires a caller in the
  tenant, the interface says it once, `[AccessRequests, InTenant]` (the sample's Projects module): a request that
  states nothing takes it, on a generated line of its own, and one that states its own keeps only its own; a
  command that forgets its permission then requires a seat, so give every command its permission. A request answers `RequiredAccess` itself only for a case of the
  module's own, a requirement computed from its values (`SeatGrants`, `OpenProject`), or one that combines what no
  stack of attributes says: two permissions, `AccessRequirement.All(MemberAccess.On(...), new ProjectsRequirement.AtUnit(...))`
  (`MoveProjectToUnit`), or one caller or another, `AccessRequirement.Any(TenancyAccess.Operator(),
  TenancyAccess.AtTenant(permission))`, with a row rule for each way onto the module's own table. None leaves the decision to a package: a request handed to Tenancy's manager says what that
  manager asks first, and the manager keeps the rest. System work is no requirement: trusted code begins it around
  the work (`access.BeginSystemIn(tenant)`), a request it sends in a tenant is `[InTenant]`, and the
  handler refuses everyone else (`access.system-only`). A request that declares nothing is DDD00077.

  ```csharp
  [RequiresPermission(ProjectPermissionNames.Close)]                                           // the Membership package's case
  public sealed record CloseProject([OnResource] ProjectId Id, [ExpectedVersion] long? ExpectedVersion = null) : ICommand, IProjectsRequest;
  ```

- **A command saves through a store port, a query reads through a read port** on a context of its own
  from a pool, so the queries of one request can run side by side. A query answers data, never an entity.
- **Scenario tests run on the real database, from one fixture.** A scenario class takes the fixture
  (`SampleHosts`) and asks it for its host; it never makes one. The fixture starts Supabase's own images
  through Testcontainers, so the class carries the traits that keep it out of a build without Docker:

  ```csharp
  [Trait("Category", "Samples")]
  [Trait("Sample", "Tenancy.Supabase")]
  public sealed class ClosingAndReopeningScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
  {
      [Fact]
      public async Task Leo_closes_Pier_7_as_its_owner_and_reopens_it()
      {
          await using var host = await sample.StartAsync();      // a host of this test's own; SharedAsync() for a test that changes nothing
          /* the test */
      }
  }
  ```

  No scenario moves a clock on to see a period end: a database compares periods with its own. Such a test
  gives the period an end a few seconds ahead and waits for it.
- **Architecture tests hold the layout**: which project references which, every request in its feature's
  `Commands` or `Queries`, every namespace its folder, every request declaring its access. Add to them
  when the layout gains a rule.

`tenancy/sample/design-choices.md`, "Design choices and where to see them", lists each choice with its code,
something to try and its test.
