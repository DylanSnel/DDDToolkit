---
title: "Tenancy: your module"
sidebar_label: Your module
---

# Tenancy: your module

The module that holds Tenancy is the toolkit's first [supporting domain](../writing-a-supporting-domain.md). This
page covers the module's options, variants and edge cases: its packages, classes, ids, context and registration, and
how your code names the managers and the access manager. The [tutorial](tutorial.md) shows the basic path. The [overview](overview.md)
defines the words.

## The packages

| Package | What it contains | Where it goes |
|---|---|---|
| `DDDToolkit.Supporting.Tenancy` | The model, its rules, its managers and their generators. No database code. | The domain project, which declares your classes. Also a contracts project with `[assembly: GenerateTenancyIds]`. |
| `DDDToolkit.Supporting.Tenancy.Abstractions` | Tenancy's markers for domain classes: `[OfTenant]`, on the property that says which tenant a row belongs to ([Keeping tenants apart](keeping-tenants-apart.md)). No Entity Framework. `DDDToolkit.Supporting.Tenancy` references it. | The domain project of another module, which sees only Tenancy's ids |
| `DDDToolkit.Supporting.Tenancy.EntityFramework` | `AddTenancy()` for your context, the organization tree as a closure table, and Tenancy's queries as Entity Framework queries. It works with every provider. | The infrastructure project, which has the context |
| `DDDToolkit.Supporting.Tenancy.Postgres` | Tenancy's queries as SQL functions, and row level security (RLS) policies, written by the [export](../supabase.md). It references the Entity Framework package. | The infrastructure project, on Postgres |

The packages are published as `Temp.DDDToolkit.Supporting.Tenancy*`, like
[every 3.x package](../migrating-to-3.md#the-package-ids). Their version is the version of every other
`Temp.DDDToolkit.*` package. For now they are prereleases (`--prerelease`), and names can change until a release.

## The model

| | Contains | Rules the package enforces |
|---|---|---|
| **Tenant** | Its slug, status and shape | A tenant goes from provisioned to active. It can be suspended and reactivated. A closed tenant stays closed. The shape changes only from flat to hierarchical. |
| **Organization** | The tree of **OrganizationUnits** | One root, no cycles, a depth of at most 32. A unit is archived and never deleted. |
| **Seat** | A person in a tenant, with **Placements** at units and **RoleGrants** at each placement | One placement per unit, and one primary placement. Only active roles are granted. The seat's identity never changes. |
| **Role** | A name and its **Permissions** | Only permissions from the catalogue. The permissions they imply are added. |
| **Catalogue** | The permissions and the **starter roles** (`StarterRole`) | One administrators' starter role per shape. Tenancy adds its own when you declare none. A permission is retired and never deleted. Permissions that manage access are marked. |

- Tenant, Organization, Seat and Role are aggregates. They refer to each other by id.
- The catalogue is not an aggregate. It is data in code: Tenancy's permissions, your modules' permissions, and an
  optional part that your application adds
  ([What the catalogue is for](roles-and-permissions.md#what-the-catalogue-is-for)).

The managers enforce the rules that involve more than one aggregate. These managers first take the tenant's access
revision, so two concurrent changes cannot both commit
([Who may give a role](roles-and-permissions.md#who-may-give-a-role)):

- The last administrator of a tenant cannot be removed.
- While containment is on, only a seat that holds a role's access-managing permissions at the unit, for at least as
  long as the grant runs, can give or take away a role that manages access. A seat never gives itself a role that manages access.
- The containment rule also covers suspending, deactivating or reactivating a seat with a role that manages access.
  It also covers moving a unit, because a move changes which grants reach the unit.
- Any other role needs `tenancy.grants.manage` at the unit where it is given.

## Adopting Tenancy step by step

What a module needs, in order:

| What | Basic path | Options and variants |
|---|---|---|
| The packages | [Step 1](tutorial.md#step-1-the-modules-projects-and-their-packages) | [The packages](#the-packages) |
| Your classes and ids | [Step 2](tutorial.md#step-2-your-classes) | [Quick start](#quick-start), [your own classes](#your-own-classes), [new ids](#how-a-new-id-is-made) |
| The module's context, and your migration | [Step 3](tutorial.md#step-3-where-it-is-stored) | [The context and the registration](#the-context-and-the-registration) |
| The registration, and a catalogue when you need one | [Step 4](tutorial.md#step-4-the-modules-registration) | [The registration](#the-context-and-the-registration), [the catalogue](roles-and-permissions.md#declaring-a-modules-permissions) |
| The caller of each request, through `ITenantSelection` | [Step 5](tutorial.md#step-5-the-host) | [How the tenant reaches a policy](postgres.md#passing-the-tenant-to-postgres) |
| Provisioning a tenant as system work | [Step 6](tutorial.md#step-6-provisioning-a-tenant) | [System work](keeping-tenants-apart.md#system-work) |
| Your tables kept to a tenant, with `[OfTenant]` | | [Keeping tenants apart](keeping-tenants-apart.md) |
| Queries in your modules | [Steps 7-8](tutorial.md#step-7-who-am-i) | [Who may do what](who-may-do-what.md), [names](names-and-languages.md#names) |
| Requests through which people manage access | [Step 9](tutorial.md#step-9-a-role-at-a-unit) | [Who may give a role](roles-and-permissions.md#who-may-give-a-role) |
| Other features | | [Invitations](invitations.md), [languages](names-and-languages.md#languages), [operators](operators-and-history.md#operators), [who changed a row](operators-and-history.md#who-changed-a-row), [access history](operators-and-history.md#access-history), [starter roles you change later](roles-and-permissions.md#starter-roles-after-provisioning) |
| Row level security on Postgres | Steps 1-5 | [Setup steps](postgres.md#setup-steps) |

The [sample](the-sample.md#who-may-do-what-in-the-sample) is an application built this way.

## Quick start

Add one line to the project your classes belong to. The docs call this attribute the **switch**:

```csharp
[assembly: GenerateTenancyClasses]
```

The generator then writes every Tenancy class that the project does not declare, as the package ships it:

- `Tenant`, `Organization`, `OrganizationUnit`, `Role` and `Seat`, each declared with its template.
- `TenantId`, `OrganizationUnitId`, `RoleId` and `SeatId`, each an `[EntityId<Guid>]` without a prefix and
  published with `[ModuleContract]`. An organization uses its tenant's id, so there is no `OrganizationId`.
- Each type is public and in the project's root namespace. Its documentation comment says how to declare the type
  yourself.
- The invitation is not written. Declaring the invitation class turns [invitations](invitations.md) on.
- Each written class has every member of the package's class. For example, the role's `PermissionsFromStarterRole` is a
  column, and your migration maps it like any other column
  ([Starter roles after provisioning](roles-and-permissions.md#starter-roles-after-provisioning)).

The diagram shows how the generator decides, for each class and each id, what to write. It never replaces a type you
declare. Purple: the switch. Green: written by the generator. Grey: your type, used as you wrote it.

```mermaid
flowchart TB
    theSwitch["<b>[assembly: GenerateTenancyClasses]</b><br/><small>the switch</small>"]
    classQ{"Does this project, or a project<br/>of its module, declare the class?"}
    yours["<b>Your class</b><br/><small>used as you wrote it</small>"]
    written["<b>Class written</b><br/><small>Tenant, Organization, Role ...</small>"]
    idQ{"Does a type with<br/>the id's name exist?"}
    taken["<b>Your id</b><br/><small>used as you wrote it</small>"]
    writtenId["<b>Id written</b><br/><small>TenantId, RoleId ...</small>"]
    generators["<b>Every generator</b><br/><small>base class, converters,<br/>AddTenancy(), the managers</small>"]
    theSwitch --> classQ
    classQ -- "yes" --> yours
    classQ -- "no" --> written
    written --> idQ
    idQ -- "yes" --> taken
    idQ -- "no" --> writtenId
    yours --> generators
    taken --> generators
    writtenId --> generators
    classDef strong fill:#5b2ec933,stroke:#5b2ec9,stroke-width:2px
    classDef good fill:#1f8a5b26,stroke:#1f8a5b
    classDef muted fill:none,stroke:#9a9ab5,stroke-dasharray:4 3,color:#9a9ab5
    class theSwitch strong
    class written,writtenId good
    class yours,taken muted
```

- "A project of its module" means a project of the same module that this project references.
- A written class takes the id of a declared class that shares the id. Example: you declare `ShopTenant` over
  `ShopTenantId`, so the written organization is declared over `ShopTenantId` too.
- Every generator writes the same code for a written class as for a declared class. This includes a
  [Membership](../membership/overview.md) member list over a written `SeatId`. Projects above see written classes in
  the same way as declared ones.

<details>
<summary>Show the code: what the switch writes for a class and for an id</summary>

```csharp title="Role.TemplateDefault.g.cs, the comment shortened"
namespace Shop.Tenants;

/// <summary>
/// The application's <c>Role</c>, declared <c>[RoleAggregate&lt;RoleId&gt;]</c> and nothing more: the package's class as it
/// ships. Written by the generator, because this project says <c>[assembly: GenerateTenancyClasses]</c> and
/// neither it nor a project it references declares a class with <c>[RoleAggregate]</c>.
/// </summary>
[global::DDDToolkit.Supporting.Tenancy.RoleAggregateAttribute<global::Shop.Tenants.RoleId>]
public sealed partial class Role
{
}
```

```csharp title="RoleId.TemplateDefault.g.cs, the comment shortened"
namespace Shop.Tenants;

/// <summary>
/// The id of the class declared with <c>[RoleAggregate]</c>: written by the generator, because this
/// project says <c>[assembly: GenerateTenancyClasses]</c> and neither it nor a project it references declares <c>RoleId</c>.
/// </summary>
[global::DDDToolkit.Abstractions.Attributes.ModuleContract]
[global::DDDToolkit.Abstractions.Attributes.EntityId<global::System.Guid>]
public readonly partial record struct RoleId;
```

Other generators write the base class, the constructor, the rules and the id's members in separate files, as they do
for a declared class and id.

</details>

### Names the switch cannot use

When the switch cannot use a name, it reports [DDD00066](../diagnostics.md#ddd00066) once, on the switch line, and
does not write that class. This happens in these cases:

- A type with the class's name exists in the root namespace, in your project or in a referenced project.
- A namespace with the class's name exists, for example a folder `Organization/` directly under the project.
- A type with the id's name exists and is not an entity id. Mark it `[EntityId<T>]`.
- The generator already writes an id with that name for one of your `[AggregateRoot<Guid>]` or `[Entity<Guid>]`
  classes.
- Two types with the id's name exist.

A class that needs a class the switch left out is left out too, and neither class gets an id. Example: a seat needs
its role. `AddTenancy()` and the managers are then not written either, and they report no second error.

When one of your classes is in the way and should become the package's class, add the template to it:
`[RoleAggregate<RoleId>] public sealed partial class Role`.

### Classes seen only from the next project up

- Other generators in the switch's project do not see the written classes. So an `[ObjectType<Role>]` over a written
  `Role` does not compile in that project, and neither does a row access rule that names a written class. Put that
  code in a project above, or declare the class or id yourself in one line.
- A written id works as the key of a
  [resource access contract](../row-level-security.md#resource-access-contracts) in the same project.
- Code outside the root namespace imports the written types with `global using Shop.Tenants;`. A `using` in one file
  does not reach the files that the generators write for your classes.

### Modules split by layer

In a module split by layer, the ids go in the contracts project, which other modules reference. Put
`[assembly: GenerateTenancyIds]` in the contracts project and `[assembly: GenerateTenancyClasses]` in the domain
project. The domain project then uses the ids from the contracts project. `GenerateTenancyIds` requires the contracts
project to reference the Tenancy package. When the contracts project should not reference the package, declare the
four ids there yourself, one line each. The sample and the tutorial do this.

## Your own classes

Declare a class yourself when it needs fields, rules or behaviour. The switch writes the other classes.
[Tutorial step 2](tutorial.md#step-2-your-classes) declares one seat class beside the switch.

Without the switch, you declare all five classes and all four ids, with the names, key types and prefixes you choose.
An id over any type other than `Guid` must say how a new id is made ([How a new id is made](#how-a-new-id-is-made)):

```csharp
// Shop.Tenants.Contracts
[EntityId<long>]
public readonly partial record struct TenantId
{
    public static TenantId Create() => new(Snowflakes.Next());   // yours: a snowflake, or a number from a HiLo block
}

[EntityId<Guid>] public readonly partial record struct SeatId;
[EntityId<Guid>] public readonly partial record struct OrganizationUnitId;
[EntityId<Guid>] public readonly partial record struct RoleId;

// Shop.Tenants
[TenantAggregate<TenantId>]
public sealed partial class ShopTenant
{
    public bool IsDemo { get; private set; }
}

[OrganizationAggregate<TenantId>] public sealed partial class ShopOrganization;
[OrganizationUnit<OrganizationUnitId>] public sealed partial class ShopUnit;
[SeatAggregate<SeatId>] public sealed partial class ShopSeat;
[RoleAggregate<RoleId>] public sealed partial class ShopRole;
```

The package's rules run for your classes before your rules. Entity Framework maps your classes, written or declared
([Writing your own supporting domain](../writing-a-supporting-domain.md)).

## How a new id is made

Tenancy creates the id of every tenant, unit, seat, role and invitation in code, before the save. It calls the static
`Create()` of the id type, for example `TSeatId.Create()`. So the change and its events have the id from the start.
The database never creates an id. A manager method that receives an id, for an import or seed data, uses that id.

| The id | Its `Create()` |
|---|---|
| `[EntityId<Guid>]`, including the switch's ids | Written by the generator. It makes a time-ordered id, the same as `CreateSequential()`, which keeps a database index in order. A `Create()` you write takes precedence. |
| `[EntityId<long>]`, `<int>`, `<string>` | None. You declare it. For a `long`, use a snowflake or a number from a HiLo block. A 64-bit snowflake does not fit an `int`, so use the HiLo number there. |
| An id written by hand, without `[EntityId<T>]` | Nothing from the generator. Declare `Create()` and implement `ICreatableEntityId<TenantId>` yourself. |

- A class declared over an id without `Create()` is [DDD00067](../diagnostics.md#ddd00067) at build. Its code fix
  adds a `Create()` that throws until you write the body.
- The registration sets nothing for this. Entity Framework stores the id as a value that the application provides
  ([Ids the database never makes](../entity-framework.md#ids-made-in-code)).

## The context and the registration

The context is a plain `DbContext` of your module that calls `modelBuilder.AddTenancy(database: Database)`
([tutorial step 3](tutorial.md#step-3-where-it-is-stored)). The registration is `services.AddTenancy<TContext>()`
([step 4](tutorial.md#step-4-the-modules-registration)). The details the tutorial leaves out follow.

**The context**

- `Add{Module}Converters()` is named after your module, for example `AddTenantsConverters()` for a module named
  Tenants ([`Add{Module}Converters`](../entity-framework.md#adddddtoolkitconventions-and-addmoduleconverters)).
- **Migrations.** You create the migrations from your model, in the context's project. Their history table is in the
  context's schema. A design-time factory keeps the history there by calling `UseDDDToolkitDesignTime()` after its
  provider ([The migration history](../entity-framework.md#the-migration-history)). On Supabase,
  `[SupabaseMigrations]` writes the factory
  ([Exporting as part of the build](../supabase.md#exporting-as-part-of-the-build)).
- **Two rules in the database.** When `AddTenancy` receives `database`, it adds two filtered unique indexes. One
  allows one root per organization. The other allows one primary placement per seat. Postgres, SQLite and SQL Server
  get both indexes. Another provider, or a call without `database`, gets neither. The indexes refuse a write that
  bypasses the aggregates, such as SQL you write. [Row level security on Postgres](postgres.md) requires the root
  index ([The index on a tenant's root](postgres.md#the-index-on-a-tenants-root)).
- **Names.** The tables are `Tenants`, `Seats`, `SeatRoleGrants` and so on. The two indexes are named after their
  tables. Pass a `TenancyTableNames` to change these names. Every other name follows your context's naming
  ([Your own naming](postgres.md#naming-tables-and-columns) keeps a database in snake_case).

**The registration**

| Option, in `AddTenancy<TContext>(options => ...)` | Default | Set it when |
|---|---|---|
| `Catalogue` | None. The catalogue then holds Tenancy's permissions, your modules' permissions and the default administrators' role. | You declare starter roles, permissions that no module owns, or marks ([What the catalogue is for](roles-and-permissions.md#what-the-catalogue-is-for)) |
| `TenantSelection.SeatedTokenRoles` | `authenticated` | Your tokens give people with seats another role |
| `OperatorTokenRoles` | Empty | You have [operators](operators-and-history.md#operators) |

[`TenancyOptions`](reference/managers.md#tenancyoptionsttenantid-tseatid-tunitid-troleid) lists every option.

- `services.AddTenancy<TContext>()` and `modelBuilder.AddTenancy()` are generated, closed over your classes and ids
  ([A registration closed over your classes](../writing-a-supporting-domain.md#a-registration-closed-over-your-classes)).
- Both are internal to the project that declares your classes, or to a project of the same module that declares none,
  such as the infrastructure project
  ([In a module split by layer](../writing-a-supporting-domain.md#in-a-module-split-by-layer)). The module name comes
  from `DDD_Module` ([A module named by its folder](../modules.md#a-module-named-by-its-folder)).
- `AddTenancy` adds Tenancy's save check to every context that `UseDDDToolkit` wires
  ([Keeping tenants apart](keeping-tenants-apart.md)).

## The managers and the access manager

Your code changes and reads Tenancy's aggregates through **managers**, one per aggregate, as ASP.NET Core Identity
does with `UserManager` and `RoleManager`. Each manager holds the commands and the reads of its aggregate. The
package declares each one as a generic class, such as `SeatManager<TTenant, TTenantId, …>`. In the project that
declares your classes, the generator writes a class without type parameters, closed over your classes. Each class is
in the global namespace, whatever your module is called. Every project that references the declaring project takes
the class by that name:

| Class | What it holds | Registered by |
|---|---|---|
| `TenantManager` | Provisioning a tenant, changing its shape, suspending, reactivating and closing it. The operators' list of tenants. | `AddTenancy` |
| `OrganizationManager` | Renaming the organization. Adding, renaming, moving and archiving units. The units a caller reads. | `AddTenancy` |
| `SeatManager` | Adding a seat, placing and withdrawing it, granting and revoking roles, suspending, reactivating and deactivating it. The caller's overview and the seats a caller reads. | `AddTenancy` |
| `RoleManager` | Creating, renaming and archiving roles, changing their permissions, following the starter roles. The tenant's roles. | `AddTenancy` |
| `InvitationManager` | Issuing, cancelling and accepting invitations, and the open ones. It is written only where you declare an invitation class. | `AddTenancyInvitations` |
| `TenancyAccessManager` | Who acts (`CurrentUser`), in which tenant (`CurrentTenant`), and the `Begin…` methods that start system work ([System work](keeping-tenants-apart.md#system-work)). | `AddTenancy`, and `AddTenancyAccess` in another module |
| `TenantToProvision` | The record `ProvisionAsync` takes. Your code makes it with `new`. | (a record) |

```csharp
[RequiresPermission(TenancyPermissions.GrantsManage)]
public sealed record GrantRole(SeatId Seat, RoleId Role, [AtUnit] OrganizationUnitId Unit) : ICommand<Seat>, ITenancyRequest;

public sealed class GrantRoleHandler(SeatManager seats) : ICommandHandler<GrantRole, Seat>
{
    // The request carries ids, so the handler passes ids. GrantAsync returns the seat with its new grant.
    public async ValueTask<Seat> Handle(GrantRole command, CancellationToken cancellationToken)
        => await seats.GrantAsync(command.Seat, command.Role, command.Unit, until: null, reason: null, cancellationToken);
}
```

- **Objects or ids.** A method takes your objects or their ids: `seats.GrantAsync(leo, safetyOfficer, north, ...)`
  or `seats.GrantAsync(seatId, roleId, unitId, ...)`. The object overload reads only the object's id. The manager
  loads the aggregate and checks it the same way for both.
- **What a command returns.** A command returns the aggregate it made or changed. `AddAsync` returns the new `Seat`,
  `AddUnitAsync` the new unit, `CreateAsync` the new `Role`, `GrantAsync` the seat, and `ProvisionAsync` the new
  `Tenant`.
- **What a read returns.** A read returns your classes. A read that adds data your class does not have returns a
  record that holds your objects: `SeatManager.SeatOverview`, `OrganizationManager.UnitInTree` and
  `TenantManager.TenantListing`. `roles.ManagesAccess(role)` says whether a role holds a permission that manages
  access.
- **The checks.** Each manager checks the caller's permissions where it acts, after the access check before the
  handler ([Requests that call a manager](who-may-do-what.md#requests-that-call-a-manager)).
- [Tenants](reference/managers.md#tenants), [Organization](reference/managers.md#organization),
  [Seats](reference/managers.md#seats), [Roles](reference/managers.md#roles) and
  [Invitations](reference/managers.md#invitations) in the reference list every method.

The first manager method most applications call provisions a tenant
([tutorial step 6](tutorial.md#step-6-provisioning-a-tenant)).

C# finds a nested type through a derived class. So `SeatManager.SeatOverview` is the package's `SeatOverview`,
closed over your classes. Each name is a class because a `global using` alias works only in its own project. A class
reaches every project above, and the generators of those projects read it.

### Another name for a manager

Add one line per class to the project that declares the classes, beside `[assembly: Module]` or the switch. Every
project above uses the new name.

```csharp
using DDDToolkit.Abstractions.Attributes;

[assembly: TemplateFacadeName("SeatManager", "ShopSeatManager")]
```

- You need these lines when two modules declare Tenancy's classes ([below](#two-modules-with-tenancys-classes)).
  Otherwise a line is optional. **Template facade** is the toolkit's term for a class like `SeatManager`, which a
  package requests with `[assembly: TemplateFacade]`.
- The name must not be the name of a namespace or type of yours in the global namespace. For example, `Shop` is taken
  when your namespaces start with `Shop.`. A line that changes nothing is [DDD00076](../diagnostics.md#ddd00076).

### When a manager is not written

| Situation | Result |
|---|---|
| A template has no class or several classes, or a class does not meet the constraints of the managers | No manager is written. [DDD00065](../diagnostics.md#ddd00065) reports this as information on the first class, because projects above would see only CS0246. When a module's classes are split over two projects, the first project gets the message, and the second project, where the classes are complete, gets the managers. |
| A type, namespace or alias of yours with a manager's name exists in the global namespace | That manager is not written. Your type stays. |
| A type of yours with a manager's name exists in a namespace of the declaring project | That manager is not written. [DDD00065](../diagnostics.md#ddd00065) says so on your type: a class in the global namespace would win over yours in every file that imports your namespace. |
| A project above has an alias with that name | CS0576. Remove the alias. |
| HotChocolate's generator runs in the declaring project | HotChocolate's generator does not see the managers, because they are another generator's output. Its code over a manager's record, such as `SeatManager.SeatOverview`, does not compile. A module split by layer names those records in its API project, where the generators see them. A module of one project keeps an alias of the manager's name, such as `global using SeatManager = DDDToolkit.Supporting.Tenancy.UseCases.SeatManager<...>;`. Every generator reads the alias, and the class is then not written. |

### Two modules with Tenancy's classes

An application can have two modules that declare Tenancy's classes, for example one for customers' tenants and one
for partners' tenants. Each module's domain project then gets its own managers and its own access manager.

- The two sets of classes meet in every project that references both modules, such as the host and your tests. In
  those projects, naming `TenantManager` is CS0433. [DDD00075](../diagnostics.md#ddd00075) warns in each of those
  projects, on the project file. The warning contains the line that gives one class another name, and names the
  project where that module declares its classes.
- When one module's domain project references the other module's domain project, the warning appears on the first
  Tenancy class of the referencing project. In that project, C# binds the name to the project's own class (CS0436).
  No project above can name either class (CS0433).
- Give each class of one module another name, one line per class.

**`Shop.Customers.Domain/Module.cs`**

```csharp
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy;

[assembly: Module("Customers")]
[assembly: GenerateTenancyClasses]
[assembly: TemplateFacadeName("TenantManager", "CustomersTenantManager")]
[assembly: TemplateFacadeName("OrganizationManager", "CustomersOrganizationManager")]
[assembly: TemplateFacadeName("SeatManager", "CustomersSeatManager")]
[assembly: TemplateFacadeName("RoleManager", "CustomersRoleManager")]
[assembly: TemplateFacadeName("TenantToProvision", "CustomersTenantToProvision")]
[assembly: TemplateFacadeName("TenancyAccessManager", "CustomersAccessManager")]
```

**`Shop.Host/Provisioning.cs`**

```csharp
public sealed class Provisioning(CustomersTenantManager customers, TenantManager partners)
{
    // Customers' tenants through the new names, partners' tenants through the package's names.
}
```

The rest of these pages use the package's names, as the sample does.

## Calls closed over your ids

Tenancy's calls that are generic over your ids also come in a form closed over your ids. So you write your ids only
where you declare them. The last column says where each closed form is available:

| What | Generic over your ids | Closed over your ids | Where the closed form is available |
|---|---|---|---|
| Who acts, and system work | `TenancyCallers.Current<TenantId, SeatId>()`, `TenancyWork.BeginSystem<TenantId, SeatId>()` and the others | `TenancyAccessManager`: `CurrentUser`, `CurrentTenant`, `BeginSystem()`, `BeginSystemIn(tenant)`, `BeginTokenIn(...)`, `TenantsToSweepAsync(...)` | Every project that sees your classes: their module, the host, your tests. Also a project of another module with the `TenancyIds` line below. |
| Tenancy's queries over a context | `access.In(source, executor)`, over a read source that is not Entity Framework | `db.Tenancy()` | The project that holds the context, in your classes' module or in a module with the `TenancyIds` line |
| Registrations | `outbox.AddTenancyDomainEvents<TenantId, SeatId, OrganizationUnitId, RoleId>()` and the others | `outbox.AddTenancyDomainEvents()`, `log.AddTenancyEventLog()`, `modelBuilder.AddTenancyReadModel()` and the others, beside `AddTenancy` | Internal to the project that declares your classes, or to a project of their module that registers Tenancy. A test project that registers Tenancy itself writes the ids. |
| Tenant selection, for a host's middleware | `TenantSelection<TenantId, SeatId>` | `ITenantSelection`, which returns the caller without its ids | Every project |

- **`ITenantSelection`** is the closed selection, registered per scope, unless your host registered one before. It
  returns an `ITenancyCaller`, which `TenancyCallers.Begin` takes. So a host's middleware
  ([tutorial step 5](tutorial.md#step-5-the-host)) looks the same in every application.
- **Another module sees only your ids** (Billing, Projects). The toolkit never picks an id type as the tenant's id
  because of its name. The module says which of its ids are Tenancy's, with one line in its application project:

  ```csharp
  [assembly: TenancyIds<TenantId, SeatId, OrganizationUnitId, RoleId>]
  ```

  The generator then writes `TenancyAccessManager` into that project's root namespace. The module's project that
  holds its context gets `db.Tenancy()`, `services.AddTenancyAccess<TRequests, TContext>()` and
  `modelBuilder.AddTenancyReadFunctions(schema)`. None of them names an id. The managers are not written there: only
  the module that declares Tenancy's classes changes them. A class that the module declares with one of Tenancy's
  templates wins over the line.
- **Some types always carry the ids**: `ITenancyQueries<...>`, and records such as `SeatOfCaller<TenantId, SeatId>`.
  A module that sees only the ids can use these types. Name each one once per project with a `global using` alias.
- **Two calls take the ids in every project**, the declaring project included:
  - `TenantSelection<TenantId, SeatId>.SeatsOfAsync`, which returns a person's seats for a tenant picker;
  - `EfTenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>`, the rows of one context, for
    `access.In(source, executor)`. `db.Tenancy()` makes one for you.

<details>
<summary>Show the code: the same calls where your classes are seen, and in another module</summary>

```csharp
// In the infrastructure project of the module that declares the classes. Closed over the four ids.
options.UseOutbox<ShopTenancyContext>(outbox => outbox
    .AddTenancyDomainEvents()
    .AddTenancyInvitationEvents<InvitationId>()       // the invitation's id: an application may have none
    .KeepEventLog(log => log.AddTenancyEventLog()));

// In any project that sees your classes: the host, a test. access: TenancyAccessManager
using (access.BeginSystemIn(tenant, administrator))
{
    await seeder.SeedAsync(cancellationToken);
}

var tenant = access.CurrentTenant.Id;                 // null outside any tenant

// In another module: the line, once, in its application project
[assembly: TenancyIds<TenantId, SeatId, OrganizationUnitId, RoleId>]

// ... and in its infrastructure project, with no ids
services.AddTenancyAccess<IBillingRequest, BillingContext>();
modelBuilder.AddTenancyReadFunctions("tenancy");

// ... and in its handlers, through the module's own TenancyAccessManager
using (access.BeginSystemIn(tenant, scope: "billing"))
{
    await invoices.SaveOnlyAsync(invoice, cancellationToken);
}
```

</details>

## How your classes add behaviour

Your class is the package's class plus your additions. The package has no virtual members or hooks. A package rule
always runs, and your code adds to it. You can add four things:

- **Members.** Add fields and methods. Entity Framework stores them with the class. Placements and grants stay the
  package's, and only the package's managers change them.
- **Entities.** Map entities of yours with the class, as in any aggregate.
- **Rules.** A nested `IInvariant<T>` runs after the package's rules on every save. It refuses the save in the same
  way as the package's rules.
- **Handlers for the package's domain events.** Every change raises an event (`tenancy.seat-suspended` and the
  others). Each event records [who made the change](operators-and-history.md#access-history). Handle an event in
  process with `AlsoDispatchInProcess`, or [publish](#publishing-an-event) it. An event without a mapping is stored
  and reaches no other module.

The sample's unit adds a cost centre and a rule for its format.

**`OrganizationUnit.cs`**, in the domain project

```csharp
[OrganizationUnit<OrganizationUnitId>]
public sealed partial class OrganizationUnit
{
    public string? CostCentre { get; private set; }

    public void SetCostCentre(string? costCentre) => CostCentre = costCentre;

    // Runs after the package's rules about names, parents and archiving. It cannot replace them.
    public sealed partial class CostCentreFormat : IInvariant<OrganizationUnit>
    {
        public string Code => "tenants.unit.cost-centre";

        public InvariantFailure? Check(OrganizationUnit entity)
            => entity.CostCentre is null || Format().IsMatch(entity.CostCentre)
                ? null
                : "A cost centre is two capitals, a dash and three digits.";

        [GeneratedRegex(@"^[A-Z]{2}-\d{3}$")]
        private static partial Regex Format();
    }
}
```

### Setting your fields on a new tenant, root and seat

Your fields start at their default values. `TenantToProvision` has three callbacks that set them on the new tenant,
its root unit and its first seat:

```csharp
await tenants.ProvisionAsync(
    new TenantToProvision(
        "harbor", "Harbor Works", TenantShape.Hierarchical, "Harbor Works", identity,
        ConfigureTenant: tenant => tenant.MarkAsDemo(),
        ConfigureRoot: root => root.SetCostCentre("HW-001"),
        ConfigureFirstSeat: seat =>
        {
            seat.Rename("Ada");
            seat.ChangeJobTitle("Harbor master");
        }),
    cancellationToken);
```

- The callbacks run in that order. Each callback runs once its instance exists, and before the tenant is activated.
  At that point the root is in its organization, and the seat is placed at the root and holds the administrators'
  role.
- The provisioning save writes what the callbacks set. An event that your class raises in a callback is saved with
  the provisioning's events. When a callback throws, provisioning stops and nothing is saved.
- For later units, use the `configure` callback of `OrganizationManager.AddUnitAsync`
  ([The kind of a unit](#the-kind-of-a-unit)). For later seats, use the callbacks of `SeatManager.AddAsync` and of
  `InvitationManager.AcceptAsync`
  ([How a seat is shown](names-and-languages.md#how-a-seat-is-shown)).

### Publishing an event

To let another module receive one of Tenancy's events, map the event in the outbox of your Tenancy context, before
`AddTenancyDomainEvents`. `AddTenancyDomainEvents` keeps every event without a mapping off the sinks, so a mapping
after that call throws:

```csharp
services.AddDDDToolkitEntityFramework(options => options.UseOutbox<ShopTenancyContext>(outbox => outbox
    .PublishAs<SeatSuspended<TenantId, SeatId>, SeatSuspendedV1>(suspended => new SeatSuspendedV1(suspended.SeatId.Value))
    .AddTenancyDomainEvents()));
```

### The kind of a unit

Tenancy stores no kind for a unit. Region, area or site plays no part in access, so the package neither requires nor
checks a kind. Keep the kind as a field of your unit class, such as an enum. Set the field in the callback of the use
case that creates the unit.

The diagram shows what `AddUnitAsync` checks, in order, and where your callback runs. So you can see what is saved
when your callback throws. Purple: the call. Green: the unit is added. Red: refused, or not added.

```mermaid
flowchart TB
    addUnit["<b>AddUnitAsync</b><br/><small>parent, name, configure</small>"]
    permission{"tenancy.units.manage<br/>at the parent?"}
    tree{"Does the tree<br/>accept the unit?"}
    configure["<b>configure(unit)</b><br/><small>sets your fields</small>"]
    refused["<b>Refused</b><br/><small>nothing saved</small>"]
    notAdded["<b>Not added</b><br/><small>nothing saved</small>"]
    added["<b>Added</b><br/><small>one save: the unit and your fields</small>"]
    addUnit --> permission
    permission -- "no" --> refused
    permission -- "yes" --> tree
    tree -- "flat tenant, archived parent,<br/>too deep, invalid name" --> refused
    tree -- "yes" --> configure
    configure -- "throws" --> notAdded
    configure -- "returns" --> added
    classDef strong fill:#5b2ec933,stroke:#5b2ec9,stroke-width:2px
    classDef good fill:#1f8a5b26,stroke:#1f8a5b
    classDef bad fill:#c93c3c24,stroke:#c93c3c
    class addUnit strong
    class added good
    class refused,notAdded bad
```

- Your class's rules check the field in that save. When the callback throws, a later save in the same scope does not
  add the unit either. Set the root's kind through `ConfigureRoot`.
- **Showing the kind.** [`ListUnitsAsync`](reference/managers.md#listunitsasynccancellationtoken) returns the units
  the caller reads. Each unit is your unit class with all its fields, in a `UnitInTree` with its path and depth. Your
  field is on each unit.
- **By id.** [`UnitsByIdAsync`](reference/managers.md#unitsbyidasyncireadonlycollectiontunitid-cancellationtoken)
  returns a `NamedUnit` with the name, path and depth. A seat that may not read the rest of a unit still gets the
  unit's name this way. Use it when another module shows units by id.

<details>
<summary>Show the code: a unit's kind as the application's own enum</summary>

```csharp
public enum UnitKind { Company, Region, Area, Site }

[OrganizationUnit<OrganizationUnitId>]
public sealed partial class OrganizationUnit
{
    public UnitKind? Kind { get; private set; }

    public void SetKind(UnitKind? kind) => Kind = kind;
}

// The command's handler. The manager adds the unit, the callback sets the kind, and one save writes both.
// AddUnitAsync returns the new unit with its kind.
public async ValueTask<OrganizationUnit> Handle(AddOrganizationUnit command, CancellationToken cancellationToken)
    => await organization.AddUnitAsync(command.Parent, command.Name, cancellationToken, configure: unit => unit.SetKind(command.Kind));

// The query's handler. ListUnitsAsync returns the units the caller reads. Each UnitInTree holds your unit with its kind.
IReadOnlyList<OrganizationManager.UnitInTree> units = await organization.ListUnitsAsync(cancellationToken);

// Store the kind by its name, with one line in the context's ConfigureConventions
configurationBuilder.Properties<UnitKind>().HaveConversion<string>().HaveMaxLength(16);
```

</details>

The sample does all of this. It stores the kind by a lower-case key, with a converter it declares (`UnitKindKeyConverter`).
