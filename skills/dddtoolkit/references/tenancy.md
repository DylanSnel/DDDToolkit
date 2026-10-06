# Supporting domains and Tenancy

A supporting domain is a package that ships aggregates an application extends: the package states the
rules every application shares, and the application declares the classes, with its own ids, fields and
rules. Tenancy is the toolkit's first: who a person is in a tenant, and what they may do where in its
organization. Full pages: `https://dylansnel.github.io/DDDToolkit/docs/tenancy.md` and
`writing-a-supporting-domain.md`. The Tenancy packages are published as prereleases, under `Temp.` ids like
every 3.x package, and their names can change until a release that is not one; read the project's references
before writing against them.

Packages: `DDDToolkit.Supporting.Tenancy` (the model, its rules, its use cases, no database),
`DDDToolkit.Supporting.Tenancy.EntityFramework` (`AddTenancy()`, the read model, the questions as queries)
and `DDDToolkit.Supporting.Tenancy.Postgres` (the same questions as SQL functions and row level security).

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
           options.Catalogue = ShopCatalogue.Application)   // optional: packs, keys of its own, keys that manage access
       .AddDbContext<ShopTenancyContext>((serviceProvider, options) => options
           .UseNpgsql(connectionString)
           .UseDDDToolkit(serviceProvider));
   ```

   A pack of the catalogue is `new RolePack(key, name, description, keys)`. `SeededFor: TenantShape.Hierarchical`
   gives a copy only to a tenant of that shape, provisioned so or changed to it; left out, every tenant gets one.
   It filters seeding and is no property of the role: a role has no shape. Declare no administrators' pack and
   every tenant gets Tenancy's own; declare one (`Administers: true`) and declare one for every shape, a single
   one without `SeededFor` or one seeded for each shape.

4. Name the use cases through `{Module}Tenancy`, and write no alias. They are nested in one generic class,
   `TenancyUseCases<...>`, and the toolkit's generator closes it over your classes in the project that declares
   them, as a class named after its module: the module Tenants, declared by its folder (`DDD_Module` in
   `Modules/Directory.Build.props`) or by `[assembly: Module("Tenants")]`, gives `TenantsTenancy`, which every project above sees. `TenantsTenancy.SeatCommands` is the package's own type, which `AddTenancy` registered.
   A hand-written `global using TenantsTenancy = ...` above it is CS0576: delete it. For another name (a module
   called Tenancy would get `TenancyTenancy`), add one line in the project that declares the classes:
   `[assembly: TemplateFacade(typeof(TenancyUseCases<,,,,,,,,>), "ShopTenancy")]`. CS0246 for the name above:
   read DDD00065 in that project. Only a module of one project with HotChocolate types over the records keeps
   one alias of exactly the class's name there, since another generator does not see a generated class.
   The same class closes over your ids what is called: system work, `TenantsTenancy.BeginSystem()`,
   `BeginSystemIn(tenant, actingSeat)`, `BeginOperator`, `BeginOperatorIn`, `BeginTokenIn`, and
   `TenantsTenancy.CurrentCaller()`. Write no `TenancyWork.BeginSystem<TenantId, SeatId>()` where the class is
   seen; a module that sees only the ids calls `TenancyWork`'s, which infers both from a tenant and a seat.

   ```csharp
   // TenantsTenancy.TenantCommands, OrganizationCommands, SeatCommands, RoleCommands, TenancyDirectory,
   // InvitationCommands<ShopInvitation, InvitationId>, and the records: TenantToProvision, SeatOverview, ...
   public sealed class FirstTenant(TenantsTenancy.TenantCommands tenants)
   {
       public async Task SetUpAsync(Guid identity, CancellationToken cancellationToken)
       {
           using (TenantsTenancy.BeginSystem())                  // provisioning is system work outside any tenant
           {
               await tenants.ProvisionAsync(
                   new TenantsTenancy.TenantToProvision(
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
   using (Callers.Begin(caller))
   {
       // ITenantSelection: the selection without its ids, so the middleware names none
       var seat = await selection.ResolveAsync(caller, context.Request.Headers["Tenant"], context.RequestAborted);
       using (TenancyCallers.Begin(seat))
       {
           await next(context);
       }
   }
   ```

6. A module's own tables: `modelBuilder.Entity<Project>().ScopeToTenant(project => project.TenantId)`; the
   model then requires the save check, so such a context without it is refused at its first save, and the
   start-up check `AddTenancy` brings holds every context that keeps rows to a tenant to it. A module's keys,
   stated once: `[TenancyPermissions] public static IReadOnlyList<Permission> Permissions { get; } = [...];` on
   the module's list, and in the host, which references every module, `services.AddTenancyPermissionsOfModules()`,
   generated by Tenancy's generator in every application (module or not) and every library that declares no
   module, in the namespace named after the project's assembly (a top-level `Program.cs` needs `using <HostAssembly>;`). The Postgres export
   finds the same marked lists itself, in the modules the exporting project references, and builds its catalogue
   from them and the `ApplicationCatalogue` marked `[TenancyCatalogue]` (written into that project's
   `DDDToolkit.RowAccessContributionsOfPackages.g.cs`). Do not also call `AddTenancyPermissions` with a marked
   list: the catalogue refuses it twice (DDD00063 is a marked list that is not public, static and a sequence of
   `Permission`; DDD00070 a `[TenancyCatalogue]` or `[TenancyOperators]` that a library keeps internal).
7. Provisioning, seeding and jobs are system work, begun on purpose:
   `using (TenantsTenancy.BeginSystemIn(tenant, actingSeat)) { ... }`. A request is never
   system work. With `services.RequireExplicitCallers()` work that named no caller fails instead of running
   as the application.

## The access model

- A **tenant** has an **organization**, a tree of units. A **seat** stands for one person in one tenant:
  it is placed in units, with **role** grants at each. A role holds permission **keys** from the
  **catalogue**.
- A key held at a unit holds for every unit below it. A grant counts from its start to its end, compared
  when the question is asked: nothing is written when one runs out.
- Tenancy answers questions about the organization and nothing else. The module that owns a resource
  decides who may open it, with those answers and what it knows itself (a project's crew, say).

  | Question | Answers |
  |---|---|
  | `UnitsWhereIHold(key)` | the units where the calling seat holds the key, and every unit below them |
  | `HoldsAtAsync(key, unit)`, `HoldsTenantWideAsync(key)` | whether it holds the key there, or for the whole tenant |
  | `WhereIHold(keys)` | every pair of a unit and a key, for several keys in one query |
  | `RolesWithKey(key)`, `RoleKeys(keys)` | the active roles that grant a key |

- **Ask inside the module's own statement.** The module's context maps Tenancy's read model
  (`AddTenancyReadModel`, or `AddTenancyReadFunctions` on Postgres), and
  `answers.Over(db).UnitsWhereIHold(key)` is a query, not a list: use it in `Where(project =>
  held.Contains(project.UnitId))` and it becomes a subquery. Do not fetch the units first, and do not ask
  `HoldsAtAsync` per row.
- **Key sets draw a screen; a command asks again.** A list may carry what the caller can do with each row.
  The use case still checks when it runs.
- **A module answers ids, never Tenancy's names.** The read model has ids, keys, periods and statuses, and
  no name. What a unit or a role is called is asked of `TenancyDirectory` by id (`UnitsByIdAsync`,
  `RolesByIdAsync`) by whoever shows it.
- **The directory answers the application's own classes, whole.** `ListSeatsAsync(ct)` and
  `SeatsByIdAsync(ids, ct)` answer the module's `Seat`s, `ListUnitsAsync(ct)` and `UnitsByIdAsync(ids, ct)` its
  units as `UnitInTree` (the unit, its `Path`, its `Depth`), all untracked: show them with a plain `Select`
  (`seats.Select(seat => new SeatListing(seat.Id, seat.DisplayName, seat.Status))`). `WhoAmIAsync(ct)` answers a
  `SeatOverview`: the `Seat` with its placements and grants, and only what is not on it (`Tenant`, `Units` by path,
  `Roles` its grants name, `Keys`, `AsOf`; `UnitOf(id)` and `RoleOf(id)` look them up). The tenant picker's
  `TenantSelection.SeatsOfAsync<Seat>(caller, ct)` answers `SeatInTenant<Seat>` (the seat, `Slug`,
  `OrganizationName`, `TenantStatus`). A seat carries its identity: select what leaves, and never return the
  class itself from a route or a GraphQL field. A listed seat carries its placements and grants too, and only
  Postgres's policies narrow another seat's grants: show those only from a question that asks a key (the sample's
  `SeatGrants`, `tenancy.seats.manage` for the whole tenant). `RoleOf(id)` is `null` for a role the application's
  own filter hides.
- **A seat has no name in Tenancy.** What a person is shown by is the application's: a field of its seat
  class (set in `ConfigureFirstSeat`, `AddSeatAsync(..., configure:)` and `AcceptAsync(..., configure:)`,
  renamed by a use case of its own), the identity provider's name, or a profile of its own by `Identity`.
  Never add a name back to Tenancy.
- **Who may give a role.** `tenancy.grants.manage` at the unit gives any role that manages no access. A
  role that manages access is given only by a seat that holds each of its keys that do, there and for at
  least as long, and never to itself. A tenant always keeps an administrator. Mark your own keys that give
  power over other people's access (`ManagesAccess: true`, or `AccessManagingKeys` in the catalogue), and
  pin the set with a test: a forgotten mark fails open.
- **Containment is a setting**, `ApplicationCatalogue.ContainAccessManagingKeys`, on by default: off, a role that
  manages access goes as one that manages none, so a seat with a key that manages access hands out every key it
  reaches, in C# and in the exported SQL alike (a seat's grant to itself still ends when its grants key does).
  Leave it on when the database can be reached without the handlers (Supabase's Data API). A handler that gives a
  role after a check of its own (a quiz) keeps it on and grants inside `TenancyWork.BeginSystemIn`, which
  containment never holds: the seat comes from the caller and the role from the application, never from the
  request. On Postgres export and apply the access files after changing it: `tenancy.policies-in-place` refuses a
  database written the other way round.
- **Packs after provisioning.** A tenant's roles start as copies of the catalogue's packs, and are the
  tenant's own to rename and re-key. `builder.Services.SyncRolePacks()` in the host, beside
  `RunStartupChecks()` and not a check, since it changes roles, makes every role made from a pack follow it
  once the host has started, tenant by tenant as system work: what the pack gained is added, what it lost is
  taken out, what the tenant changed itself stays, and each role that changes raises `RoleFollowedItsPack`,
  which the access history keeps. It needs the roles' nullable `KeysFromPack` column: add a migration. A host
  that syncs from a deployment step runs `IRolePackSync` instead; one tenant is `RoleCommands.FollowPacksAsync`.
- **A refusal is a `RefusalException` with a code**: `tenancy.not-permitted`, `tenancy.not-seated`,
  `tenancy.grant-exceeds-own`, `tenancy.last-admin`. Branch on `Code`, never on the message. A refusal
  about one input names it in its `Field` argument.
- **Operators** are the application's own staff: a token role listed in `TenancyOptions.OperatorTokenRoles`.
  They hold no seat and only read. What one asks for is carried out by system work that names them,
  `TenantsTenancy.BeginOperatorIn(tenant, identity)`.
- **Who acted** is a seat, an operator, the system or a token, on every event of Tenancy's (`By`) and on a
  row that `RecordsWhoChanged()`. Never put whoever work acts for into the toolkit's `Caller`.
- **Invitations** are optional (`AddTenancyInvitations`). The token is a bearer credential: show it once,
  store its digest, keep it out of logs and query strings. A seat is linked to a verified identity, never
  found by an e-mail address. The account a person signs in with is asked for through `IIdentityAccounts`,
  after the invitation is issued; to have the provider's mail carry the token, pass the accept page's
  address with the token after the `#` as the redirect.
- **Keep access out of the domain project.** `ScopeToTenant`, `RecordsWhoChanged` and `[RowAccess]` rules go
  in the infrastructure project, keys and the access check in the application project.

On Postgres, `services.AddTenancyPostgres()`, with the catalogue marked `[TenancyCatalogue]` (and the operators' token
roles `[TenancyOperators]`), which the Supabase export writes Tenancy's policies from because the exporting project
references the package: no class and no `UseRowAccessContribution` line for it,
puts the same rules in row level security under the application's checks, and brings the `TenancyPostgresChecks`
as start-up checks, which `services.RunStartupChecks()` runs with the others. A module's rule asks through `TenancyRowAccess.UnitsWhereIHold<TUnitId>(key)`, which is
set-shaped: once per statement, never per row. There a seat reads its own rights and grants, and another seat's
grants only at the units where it holds `tenancy.grants.manage`, `tenancy.seats.manage` or `tenancy.units.manage`,
or all of them with `tenancy.roles.manage` for the whole tenant; `ITenancyQuestions.SeatsHoldingAt` answers by the
same rule on every database. The database guards Tenancy's own columns of a seat (no seat changes its id, identity
or tenant; its status only as the use cases do) and none of yours: a field you add to the seat class is as writable
as the row, by the seat itself and by a seat that manages seats or grants anywhere in the tenant, until a column
rule of yours holds it to your command's rule, beside your infrastructure:
`[RowAccess<Seat>(RowOperations.Change, To = [RowAccessRoles.User], Columns = [nameof(Seat.DisplayName)])]`.
See `tenancy.md`, "On Postgres: the second lock", "Who reads which grants" and "What the database guards on a seat".

## A module on Tenancy, as the sample lays it out

`Examples/Tenancy` in the repository is the worked example. Its layout, which
[persistence-and-modules.md](persistence-and-modules.md) shows as a tree:

- **A project per layer**, every one declaring the module, which the sample does once for the folder with
  `DDD_Module` in `Modules/Directory.Build.props`: Contracts, Domain, Application, Infrastructure, Api. The host references the Api project alone, and calls `Add{Module}Module` and
  `Map{Module}Module`. Only Infrastructure names Entity Framework, only Api names ASP.NET Core.
- **Feature folders, with `Commands` and `Queries` inside.** `Crew/Commands/AddCrewMember.cs`,
  `Crew/Queries/AllCrewMembers.cs`. A use case is one file: the request, its handler, and what only it
  answers with. Never a folder named after a kind of class at a project's root.
- **`Rest` and `GraphQL` per feature in the Api project**, under the application project's feature names:
  `Crew/Rest/CrewEndpoints.cs`, `Crew/GraphQL/CrewMutations.cs`. A route and a field only send the
  feature's command or query.
- **The namespace of a type is its folder**, in every project and in the tests.
- **A request says what it requires of its caller**, by implementing its module's request interface. The
  interface is marked `[AccessRequests]`, so the module's pipeline behavior is generated: it asks the
  module's checks before the handler runs. A module writes a check (`IAccessCheck`) only for cases of its
  own; `AddTenancyAccess` adds the package's check for Tenancy's cases, and
  `AddProjectMemberAccess<IProjectsRequest>()`, generated for the Membership package's member class, the
  check for a key held on a project. Every request picks one requirement that says what it requires: the
  toolkit's `AccessRequirement.AllowAnonymous()`, `SignedIn()` or `RequiresSystemWork()`, Tenancy's
  `TenancyAccess.InTenant()`, `ForTheWholeTenant(key)`, `AtUnit(key, unit)` or `RequiresOperator()`, or
  Membership's `MemberAccess.On(key, resource)`, or `MemberAccess.SeenWith<TResourceId>(key)` for a query that
  shows the resources the key is held on. None leaves the decision to a package: a request handed to
  Tenancy's use case says what that use case asks first, and the use case keeps the rest. A test fails for a
  request that declares nothing.

  ```csharp
  public sealed record CloseProject(ProjectId Id, long? ExpectedVersion = null) : ICommand, IProjectsRequest
  {
      AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(ProjectKeys.Close, Id, ExpectedVersion);   // the Membership package's case
  }
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

`tenancy.md`, "Design choices and where to see them", lists each choice with its code, something to try and
its test.
