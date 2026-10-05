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

- Declare every aggregate the package asks for, even the ones you add nothing to:
  `[RoleAggregate<RoleId>] public sealed partial class ShopRole;`. A missing one, or a class that does
  not fit the parent's constraints, is DDD00042 to DDD00050 and DDD00053 ([diagnostics.md](diagnostics.md)).
- The package's rules run first and always. There is nothing to override: no virtual members, no hooks.
  Add fields, entities, invariants, and handlers of the package's domain events.
- The package's registrations are generated closed over your classes, internal to the project that
  declares them, or to the project of the same `[assembly: Module]` that holds the context:
  `modelBuilder.AddTenancy(database: Database)` and `services.AddTenancy<TContext>(...)`. Do not write the
  generic form by hand, and call them from that project, not from an API project above it.

## Adopting Tenancy

1. Four ids in the contracts project (`TenantId`, `SeatId`, `OrganizationUnitId`, `RoleId`), and a class
   for each aggregate: `[TenantAggregate<TenantId>]`, `[OrganizationAggregate<TenantId>]`,
   `[OrganizationUnit<OrganizationUnitId>]`, `[SeatAggregate<SeatId>]`, `[RoleAggregate<RoleId>]`.
2. A plain context in the module, and migrations of your own. The package has none.

   ```csharp
   protected override void OnModelCreating(ModelBuilder modelBuilder)
   {
       modelBuilder.HasDefaultSchema("tenancy");
       modelBuilder.AddTenancy(database: Database);          // with Database: the two unique indexes too
       modelBuilder.AddDomainEventOutbox(Database, schema: "tenancy");
   }
   ```

3. Register it, with how ids are made, and the catalogue when the application has one. `UseTenancy` goes after
   `UseDDDToolkit`, on this context and on every context that keeps rows to a tenant.

   ```csharp
   services
       .AddTenancy<ShopTenancyContext>(options =>
       {
           options.Catalogue = ShopCatalogue.Application;   // optional: packs, keys of its own, keys that manage access
           options.NewSeatId = SeatId.CreateSequential;     // and NewTenantId, NewUnitId, NewRoleId
       })
       .AddDbContext<ShopTenancyContext>((serviceProvider, options) => options
           .UseNpgsql(connectionString)
           .UseDDDToolkit(serviceProvider)
           .UseTenancy(serviceProvider));
   ```

4. Name the use cases through an alias. They are nested in one generic class, `TenancyUseCases<...>`,
   closed over your classes; `AddTenancy` registers them as services. A global alias holds in the project
   that declares it, so every project that names a use case declares it.

   ```csharp
   // GlobalUsings.cs: full names, because an alias reads its target as if there were no other using
   global using ShopTenancy = DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<
       Shop.Tenancy.ShopTenant, Shop.Tenancy.Contracts.TenantId, Shop.Tenancy.ShopOrganization,
       Shop.Tenancy.ShopUnit, Shop.Tenancy.Contracts.OrganizationUnitId, Shop.Tenancy.ShopSeat,
       Shop.Tenancy.Contracts.SeatId, Shop.Tenancy.ShopRole, Shop.Tenancy.Contracts.RoleId>;

   // ShopTenancy.TenantCommands, OrganizationCommands, SeatCommands, RoleCommands, TenancyDirectory
   public sealed class FirstTenant(ShopTenancy.TenantCommands tenants)
   {
       public async Task SetUpAsync(Guid identity, CancellationToken cancellationToken)
       {
           using (TenancyWork.BeginSystem<TenantId, SeatId>())   // provisioning is system work outside any tenant
           {
               await tenants.ProvisionAsync(
                   new ShopTenancy.TenantToProvision(
                       "harbor", "Harbor Works", TenantShape.Hierarchical, "Harbor Works", identity, "Ada"),
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
       var seat = await selection.ResolveAsync(caller, context.Request.Headers["Tenant"], context.RequestAborted);
       using (TenancyCallers.Begin(seat))                    // TenantSelection<TenantId, SeatId>
       {
           await next(context);
       }
   }
   ```

6. A module's own tables: `modelBuilder.Entity<Project>().ScopeToTenant(project => project.TenantId)`; the
   start-up check `AddTenancy` brings holds every context to `UseTenancy`. A module's keys:
   `services.AddTenancyPermissions(ProjectCatalogue.Permissions)`.
7. Provisioning, seeding and jobs are system work, begun on purpose:
   `using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant, actingSeat)) { ... }`. A request is never
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
  no name. What a seat, a unit or a role is called is asked of `TenancyDirectory` by id
  (`SeatsByIdAsync`, `UnitsByIdAsync`, `RolesByIdAsync`) by whoever shows it.
- **Who may give a role.** `tenancy.grants.manage` at the unit gives any role that manages no access. A
  role that manages access is given only by a seat that holds each of its keys that do, there and for at
  least as long, and never to itself. A tenant always keeps an administrator. Mark your own keys that give
  power over other people's access (`ManagesAccess: true`, or `AccessManagingKeys` in the catalogue), and
  pin the set with a test: a forgotten mark fails open.
- **A refusal is a `RefusalException` with a code**: `tenancy.not-permitted`, `tenancy.not-seated`,
  `tenancy.grant-exceeds-own`, `tenancy.last-admin`. Branch on `Code`, never on the message. A refusal
  about one input names it in its `Field` argument.
- **Operators** are the application's own staff: a token role listed in `TenancyOptions.OperatorTokenRoles`.
  They hold no seat and only read. What one asks for is carried out by system work that names them,
  `TenancyWork.BeginOperatorIn(tenant, identity)`.
- **Who acted** is a seat, an operator, the system or a token, on every event of Tenancy's (`By`) and on a
  row that `RecordsWhoChanged()`. Never put whoever work acts for into the toolkit's `Caller`.
- **Invitations** are optional (`AddTenancyInvitations`). The token is a bearer credential: show it once,
  store its digest, keep it out of logs and query strings. A seat is linked to a verified identity, never
  found by an e-mail address. The account a person signs in with is asked for through `IIdentityAccounts`,
  after the invitation is issued; to have the provider's mail carry the token, pass the accept page's
  address with the token after the `#` as the redirect.
- **Keep access out of the domain project.** `ScopeToTenant`, `RecordsWhoChanged` and `[RowAccess]` rules go
  in the infrastructure project, keys and the access check in the application project.

On Postgres, `services.AddTenancyPostgres()` with a class derived from `TenancyRowAccessContribution`
puts the same rules in row level security under the application's checks, and brings the `TenancyPostgresChecks`
as start-up checks, which `services.RunStartupChecks()` runs with the others. A module's rule asks through `TenancyRowAccess.UnitsWhereIHold<TUnitId>(key)`, which is
set-shaped: once per statement, never per row. See `tenancy.md`, "On Postgres: the second lock".

## A module on Tenancy, as the sample lays it out

`Examples/Tenancy` in the repository is the worked example. Its layout, which
[persistence-and-modules.md](persistence-and-modules.md) shows as a tree:

- **A project per layer**, each with the module's `[assembly: Module]`: Contracts, Domain, Application,
  Infrastructure, Api. The host references the Api project alone, and calls `Add{Module}Module` and
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
  Membership's `MemberAccess.On(key, resource)`. None leaves the decision to a package: a request handed to
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
