---
title: "Tenancy: the tutorial"
sidebar_label: Tutorial
---

# Tenancy: the tutorial

This tutorial builds a small API for harbor companies with Tenancy, one step at a time. Each step says where we
are, why this step comes now and what it achieves. Then it explains the idea, shows every file whole with the new
lines highlighted, and says what those lines do.

Read the [overview](overview.md) first: it says what a tenant, a unit, a seat, a role and a grant are, and the steps
use those words from the start.

The application is called **Harbor**. When it is done, a company signs up and becomes a tenant, its administrator
adds units such as North and North Coast, gives people a seat, and gives them roles at units.

## Step 1: the module's projects and their packages

**Where we are.** An empty solution with a host: an ASP.NET Core project that will run the application.

**Why now.** Tenancy becomes a module of your application, like any module of yours. Every later step writes into
the module's projects, so they come first.

**What we want.** A module called Tenancy, in five projects, each holding one layer and only the packages that layer
needs.

### The idea: a module in layers

**Why a module.** A **module** is a part of your application with its own data and its own rules
([Modules](../modules.md)). Tenancy is one, so that the rest of Harbor can only use it in the ways it allows: a
Boards module later asks Tenancy who may do what, and cannot change a seat or a role behind Tenancy's back.

**Why layers.** Each layer has one job and may only use the layer below it:

- the **domain** holds the classes and their rules, and knows nothing of databases or HTTP, so its rules are plain
  C# that you can test without either;
- the **application** holds the requests and their handlers: what a caller can ask, and what happens then;
- the **infrastructure** is the only place that knows the database;
- the **API** holds the routes, and is the only project the host references, so the host cannot reach past it.

**Why a contracts project.** Other modules will need Tenancy's ids. A board in a Boards module has to store which
tenant it belongs to and which seat owns it, so it needs `TenantId` and `SeatId`. If Boards referenced Tenancy's
domain project to get them, it could also reach `Seat` and `Role` and change them past Tenancy's rules, and Tenancy
could never change those classes again without breaking Boards. So the ids go in a small project of their own, the
**contracts** project: the one part of a module that other modules may reference. The toolkit's analyzer warns when a
module uses anything of another module outside its contracts ([Module contracts](../module-contracts.md)).

Harbor's Tenancy module therefore has five projects, one per layer:

| Project | What it holds | Tenancy's package in it |
|---|---|---|
| `Harbor.Tenancy.Contracts` | What other modules may use: the ids. | none, only the toolkit |
| `Harbor.Tenancy.Domain` | The classes: tenant, organization, unit, seat, role. | `Supporting.Tenancy` |
| `Harbor.Tenancy.Application` | The requests and their handlers. | none, it gets the domain's |
| `Harbor.Tenancy.Infrastructure` | Where it is stored: the database context and its migrations. | `Supporting.Tenancy.Postgres` |
| `Harbor.Tenancy.Api` | The routes, and the one call the host makes to add the module. | none |

```text
Harbor.slnx
Directory.Build.props
Directory.Packages.props
src/
├── Harbor.Host/
└── Modules/
    └── Tenancy/
        ├── Harbor.Tenancy.Contracts/
        ├── Harbor.Tenancy.Domain/
        ├── Harbor.Tenancy.Application/
        ├── Harbor.Tenancy.Infrastructure/
        └── Harbor.Tenancy.Api/
```

### What you write

**`Directory.Build.props`**, at the root. **Why:** the toolkit has to know which module each project belongs to,
because it checks what one module uses of another and names generated code after the module. Saying it here, once,
from the folder a project is in, means no project has to say it itself, and a new module needs nothing but its
folder. The highlighted part does that.

```xml {8-11,13-16}
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <!-- A project in src/Modules/<Module>/ belongs to that module: the folder's name is the module's name. -->
  <PropertyGroup Condition="$(MSBuildProjectDirectory.Replace('\', '/').Contains('/src/Modules/'))">
    <DDD_Module>$([System.IO.Path]::GetFileName($([System.IO.Path]::GetDirectoryName($(MSBuildProjectDirectory)))))</DDD_Module>
  </PropertyGroup>

  <!-- A module's project named *.Contracts is what the other modules may use of it. -->
  <PropertyGroup Condition="'$(DDD_Module)' != '' and $(MSBuildProjectName.EndsWith('.Contracts'))">
    <DDD_ModuleContracts>true</DDD_ModuleContracts>
  </PropertyGroup>
</Project>
```

- `DDD_Module` names the module. The toolkit writes `[assembly: Module("Tenancy")]` into each of the five projects
  from it, so no project needs a file to say so.
- `DDD_ModuleContracts` marks the contracts project as what other modules may use.

**`Directory.Packages.props`**, at the root. **Why:** the toolkit's packages must all be at the same version, and
with every version in this one file, a project names a package and never its version, so two projects cannot drift
apart. The toolkit is a prerelease, so its version is written in full.

```xml {7-10}
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Temp.DDDToolkit" Version="3.2.0-preview.5" />
    <PackageVersion Include="Temp.DDDToolkit.Supporting.Tenancy" Version="3.2.0-preview.5" />
    <PackageVersion Include="Temp.DDDToolkit.Supporting.Tenancy.Postgres" Version="3.2.0-preview.5" />
    <PackageVersion Include="Temp.DDDToolkit.EntityFramework.Supabase" Version="3.2.0-preview.5" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.12" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12" />
    <PackageVersion Include="Mediator.Abstractions" Version="3.0.2" />
    <PackageVersion Include="Mediator.SourceGenerator" Version="3.0.2" />
  </ItemGroup>
</Project>
```

**The five project files.** Each references only the layer below it, and has only the packages its layer needs:
that is what keeps the database out of the domain. The comment at the top of each says what the project holds.

**`src/Modules/Tenancy/Harbor.Tenancy.Contracts/Harbor.Tenancy.Contracts.csproj`**

```xml {5}
<Project Sdk="Microsoft.NET.Sdk">

  <!-- What the other modules may use of Tenancy: its ids. -->
  <ItemGroup>
    <PackageReference Include="Temp.DDDToolkit" />
  </ItemGroup>

</Project>
```

**`src/Modules/Tenancy/Harbor.Tenancy.Domain/Harbor.Tenancy.Domain.csproj`**

```xml {5}
<Project Sdk="Microsoft.NET.Sdk">

  <!-- The tenancy classes, and the rules the application adds to them. -->
  <ItemGroup>
    <PackageReference Include="Temp.DDDToolkit.Supporting.Tenancy" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Harbor.Tenancy.Contracts\Harbor.Tenancy.Contracts.csproj" />
  </ItemGroup>

</Project>
```

**`src/Modules/Tenancy/Harbor.Tenancy.Application/Harbor.Tenancy.Application.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- The requests and their handlers, a folder per feature. The mediator itself is generated into the host. -->
  <ItemGroup>
    <ProjectReference Include="..\Harbor.Tenancy.Domain\Harbor.Tenancy.Domain.csproj" />
    <PackageReference Include="Mediator.Abstractions" />
  </ItemGroup>

</Project>
```

**`src/Modules/Tenancy/Harbor.Tenancy.Infrastructure/Harbor.Tenancy.Infrastructure.csproj`**

```xml {14-15}
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Where Tenancy is stored: its context and its migrations. -->
  <ItemGroup>
    <ProjectReference Include="..\Harbor.Tenancy.Application\Harbor.Tenancy.Application.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <!-- dotnet ef runs this project as its own startup project. -->
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Temp.DDDToolkit.Supporting.Tenancy.Postgres" />
    <PackageReference Include="Temp.DDDToolkit.EntityFramework.Supabase" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

**`src/Modules/Tenancy/Harbor.Tenancy.Api/Harbor.Tenancy.Api.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- The module's entry: its registration and its routes. The host references this project and nothing else of the module. -->
  <ItemGroup>
    <ProjectReference Include="..\Harbor.Tenancy.Application\Harbor.Tenancy.Application.csproj" />
    <ProjectReference Include="..\Harbor.Tenancy.Infrastructure\Harbor.Tenancy.Infrastructure.csproj" />
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

</Project>
```

### What it does

- `Temp.DDDToolkit.Supporting.Tenancy` in the domain project brings Tenancy's classes and rules, and the generator
  that writes your classes from them (step 2). There is no database in it.
- `Temp.DDDToolkit.Supporting.Tenancy.Postgres` in the infrastructure project brings the Entity Framework mapping,
  through `Supporting.Tenancy.EntityFramework`, which it depends on, and the row level security for Postgres.
- `Temp.DDDToolkit.EntityFramework.Supabase` writes the migrations for Supabase, and checks them when the host starts
  (step 3).

Build the solution. It builds with nothing in it yet: the next step adds the classes.

```powershell
dotnet build Harbor.slnx
```

## Step 2: your classes

**Where we are.** The module has its five projects and its packages, and no code.

**Why now.** Everything Tenancy does works on its classes: a request creates a tenant, adds a unit, gives a seat a
role. The classes have to exist before anything can use them.

**What we want.** A tenant, an organization, a unit, a role and a seat in the domain project, with their four ids in
the contracts project. And the seat gets one field of Harbor's own: a job title.

### The idea: Tenancy's classes are templates, and yours are the real ones

Tenancy does not give you finished classes. It gives you **templates**: an abstract parent class for each, with all
of its rules, such as "a seat is placed at a unit of its own tenant" or "the last administrator cannot be removed".
The class your application uses is **yours**: it lives in your domain project, it is called what you call it, and it
derives from the template. So it can carry fields and methods of your own, and your database stores your class.

You can declare each class yourself, or let the generator write it. One line in the domain project, **the switch**,
has the generator write every class you did not declare:

```csharp
[assembly: GenerateTenancyClasses]
```

A class you declare yourself always wins over the one the switch would write. Harbor declares only the seat itself,
because it adds a job title, and lets the switch write the other four.

The ids go in the contracts project, because other modules store them: a board of another module keeps its tenant's
`TenantId` and its owner's `SeatId`. An organization has no id of its own: it shares its tenant's.

### What you write

**The four ids**, in the contracts project, because other modules store them (step 1). **Why an id type of its own
and not a plain `Guid`:** a `SeatId` cannot be passed where a `TenantId` is expected, so mixing the two up is a compile
error instead of a bug in production. Each is one line; `[EntityId<Guid>]` has the toolkit write the rest: equality,
parsing, `Create()` for a new one, and how it is stored.

**`Harbor.Tenancy.Contracts/ValueObjects/TenantId.cs`**

```csharp {5-6}
using DDDToolkit.Abstractions.Attributes;

namespace Harbor.Tenancy.Contracts.ValueObjects;

[EntityId<Guid>]
public readonly partial record struct TenantId;
```

`SeatId.cs`, `OrganizationUnitId.cs` and `RoleId.cs` are the same file with another name.

**The switch**, in the domain project, because the classes it writes belong there. A file of one line, in the
project's root folder.

**`Harbor.Tenancy.Domain/Module.cs`**

```csharp {3}
using DDDToolkit.Supporting.Tenancy;

[assembly: GenerateTenancyClasses]
```

**The seat**, in the domain project. **Why we declare it ourselves:** Harbor wants a job title on a seat, and a field
of your own needs a class of your own. The attribute says which template the class is made from, and with which id.

**`Harbor.Tenancy.Domain/Aggregates/Seats/Seat.cs`**

```csharp {6-7,9,11}
using DDDToolkit.Supporting.Tenancy;
using Harbor.Tenancy.Contracts.ValueObjects;

namespace Harbor.Tenancy.Domain.Aggregates.Seats;

[SeatAggregate<SeatId>]
public sealed partial class Seat
{
    public string? JobTitle { get; private set; }

    public void ChangeJobTitle(string? jobTitle) => JobTitle = jobTitle;
}
```

### What it does

- `[SeatAggregate<SeatId>]` makes `Seat` a seat: the generator writes the other part of this `partial` class, which
  derives from Tenancy's seat template. Your `Seat` has everything a seat has, its tenant, its identity, its
  placements and grants, plus `JobTitle`.
- The switch writes `Tenant`, `Organization`, `OrganizationUnit` and `Role` into the domain project, public, in its
  root namespace. It writes no `Seat`, because you declared one.
- It finds the ids in the contracts project, which the domain project references, and takes them instead of writing
  its own.
- It also writes `TenancyUseCases`: Tenancy's use cases closed over your classes, such as
  `TenancyUseCases.TenantCommands` to create a tenant. Every request of the coming steps names a use case through it.

Build again. To see what the generator wrote, open the domain project's **Dependencies → Analyzers** in your editor:
`Tenant`, `Organization`, `OrganizationUnit`, `Role`, the generated part of `Seat`, and `TenancyUseCases`.

> **Going further.** A class of your own with rules: [How your classes add behaviour](../tenancy.md#how-your-classes-add-behaviour).
> How an id makes a new one: [How a new id is made](../tenancy.md#how-a-new-id-is-made).

## Step 3: where it is stored

**Where we are.** The classes exist, in memory only.

**Why now.** Before a request can create a tenant, there has to be somewhere to keep it.

**What we want.** A database context for the module, its tables in their own schema, `tenancy`, and the first
migration.

### The idea: Tenancy's tables are mapped by one call, in your context

Tenancy brings no database of its own. Its classes are stored in **your** context, next to whatever else the module
stores. One call in `OnModelCreating`, `modelBuilder.AddTenancy(...)`, maps all of them: the tables, their keys and
indexes, and the organization's tree. The generator writes that call for your classes, so it needs no type
arguments.

The context is marked `[SupabaseMigrations]`. The build then writes the migrations as SQL files for Supabase, and the
host checks at start-up that the database has them all.

### What you write

**`Harbor.Tenancy.Infrastructure/Persistence/TenancyContext.cs`**

```csharp {11,15,19-21,26-28}
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Harbor.Tenancy.Infrastructure.Converters;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Tenancy.Infrastructure.Persistence;

// Its migrations are written for Supabase, and the host checks them at start-up.
[SupabaseMigrations]
public sealed class TenancyContext(DbContextOptions<TenancyContext> options) : DbContext(options)
{
    // The schema of this module's tables.
    public const string Schema = "tenancy";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddTenancy(database: Database);
        modelBuilder.AddDomainEventOutbox(Database, schema: Schema);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
        configurationBuilder.AddTenancyConverters();
    }
}
```

### What it does

- `[SupabaseMigrations]`: the build writes a design-time factory beside the context, which `dotnet ef` uses to make
  it. In step 4 the host's build exports each migration of a marked context as a `.sql` file into
  `supabase/migrations`, and checks at start-up that the database has them.
- `Schema = "tenancy"` and `HasDefaultSchema`: Tenancy's tables live in their own schema, apart from other modules'.
- `AddTenancy(database: Database)`: maps the five classes, generated for yours. `database` tells it which database
  it is on, so it can add the indexes that only some databases have.
- `AddDomainEventOutbox`: Tenancy raises a domain event for each change, such as a role given. The outbox stores them
  with the change that raised them, in the same save.
- `AddDDDToolkitConventions` and `AddTenancyConverters`: how the ids are stored. Entity Framework does not know your
  id types, so each needs a converter to and from its `Guid`; they are generated for the module's ids, and named after
  the module.
- `StoreDateTimeOffsetsAsUtc`: a grant starts and ends at a moment, and Tenancy asks "which grants apply now" inside
  the query. Stored as UTC, those moments compare in SQL on every database.

Now make the first migration. The infrastructure project is its own startup project, through the factory the build
wrote:

```powershell
dotnet ef migrations add Tenancy --project src/Modules/Tenancy/Harbor.Tenancy.Infrastructure --startup-project src/Modules/Tenancy/Harbor.Tenancy.Infrastructure --output-dir Persistence/Migrations
```

The migration creates the schema `tenancy` with, among others, a table for each class (`Tenants`, `Organizations`,
`OrganizationUnits`, `Seats`, `Roles`), one for the placements and one for the grants (`SeatPlacements`,
`SeatRoleGrants`), and `OrganizationUnitPaths`: every unit with every unit above it, so "everything below North" is
one query.

> **Going further.** Everything `AddTenancy` maps, and naming the tables yourself:
> [The context and the registration](../tenancy.md#the-context-and-the-registration).

## Next steps

The tutorial goes on in the same way:

4. **Registration and the host**: the module's registration, and the host that runs it.
5. **Provisioning a tenant**: the first request, as system work.
6. **Who am I**: what the caller is, in the tenant the request names.
7. **A unit and a seat**: requests that require a permission, at a unit.
8. **A role at a unit**: a role made by the tenant, given at a unit, and reach.
