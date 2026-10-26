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

**Why a module.** A module has its own data and rules ([Modules](../modules.md)). Other modules use Tenancy only the
way it allows: they ask who may do what, and cannot change a seat or a role themselves.

**Why layers.** Each layer has one job, and uses only the layer below it:

- **domain**: the classes and their rules. No database, no HTTP, so you test the rules as plain C#.
- **application**: the requests and their handlers.
- **infrastructure**: the database. The only layer that knows it.
- **API**: the routes. The only project the host references.

**Why a contracts project.**

- Other modules store Tenancy's ids: a board keeps its `TenantId` and its owner's `SeatId`.
- Referencing Tenancy's domain project for them would also hand out `Seat` and `Role`, to change past Tenancy's rules.
- So the ids get a small project of their own, the only one other modules may reference. The analyzer warns when a
  module uses more ([Module contracts](../module-contracts.md)).

**Why Postgres, on Supabase.**

- Tenancy works on any Entity Framework database. Its checks run in C#, before your handler.
- On Postgres it adds **row level security (RLS)**: a policy on each table that Postgres applies to every query, as
  the caller. Even a query that skips your handlers gets only the caller's rows:

  ```sql
  -- Leo, a seat of Harbor Works, from any tool:
  select * from tenancy."Seats";
  -- only Harbor Works' seats come back
  ```

- Supabase is Postgres with sign-in (Supabase Auth) and a local stack, so this tutorial can show RLS end to end.
- On another database you leave out the Postgres package and the Supabase steps. The C# checks stay the same.

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

**`Directory.Build.props`**, at the root. **Why:** the toolkit must know each project's module, to check what one
module uses of another and to name generated code. The highlighted part derives it once, from the folder, so a new
module needs nothing but its folder.

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

**`Directory.Packages.props`**, at the root. **Why:** every version in one file, so the toolkit's packages cannot drift
apart. A prerelease version is written in full.

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

- `Supporting.Tenancy` (domain): the templates, their rules and the generator. No database.
- `Supporting.Tenancy.Postgres` (infrastructure): the Entity Framework mapping, which it brings along, plus the RLS
  policies. On another database, reference `Supporting.Tenancy.EntityFramework` instead.
- `EntityFramework.Supabase` (infrastructure): writes migrations and policies as SQL files for Supabase, and checks
  them at start-up.

Build the solution. It builds empty; step 2 adds the classes.

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

- Tenancy ships a **template** for each class: an abstract parent with all its rules, such as "the last
  administrator cannot be removed".
- The class your application uses is **yours**: in your domain project, derived from the template. So it can carry
  fields of your own, and your database stores it.
- You declare a class yourself, or let **the switch** write it. This one line has the generator write every class you
  did not declare:

  ```csharp
  [assembly: GenerateTenancyClasses]
  ```

- A class you declare always wins. Harbor declares only `Seat`, for a job title.
- An organization has no id of its own: it shares its tenant's.

### What you write

**The four ids**, in the contracts project, because other modules store them. **Why not a plain `Guid`:** a `SeatId`
passed as a `TenantId` is a compile error, not a bug. `[EntityId<Guid>]` writes the rest: equality, parsing,
`Create()`, storage.

**`Harbor.Tenancy.Contracts/ValueObjects/TenantId.cs`**

```csharp {5-6}
using DDDToolkit.Abstractions.Attributes;

namespace Harbor.Tenancy.Contracts.ValueObjects;

[EntityId<Guid>]
public readonly partial record struct TenantId;
```

`SeatId.cs`, `OrganizationUnitId.cs` and `RoleId.cs` are the same file with another name.

**The switch**, in the domain project, where the classes belong.

**`Harbor.Tenancy.Domain/Module.cs`**

```csharp {3}
using DDDToolkit.Supporting.Tenancy;

[assembly: GenerateTenancyClasses]
```

**The seat**, in the domain project. **Why declare it:** a field of your own (the job title) needs a class of your own.
The attribute names the template and the id.

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

- `[SeatAggregate<SeatId>]`: the generator writes the other half of the `partial` class, derived from the template.
  `Seat` has everything a seat has, plus `JobTitle`.
- The switch writes `Tenant`, `Organization`, `OrganizationUnit` and `Role`, and no `Seat`: you declared one.
- It takes the ids from the contracts project instead of writing its own.
- It also writes `TenancyUseCases`: Tenancy's use cases closed over your classes. Every later request uses it.

Build again, and look in the domain project's **Dependencies → Analyzers** to see what was written.

> **Going further.** A class of your own with rules: [How your classes add behaviour](../tenancy.md#how-your-classes-add-behaviour).
> How an id makes a new one: [How a new id is made](../tenancy.md#how-a-new-id-is-made).

## Step 3: where it is stored

**Where we are.** The classes exist, in memory only.

**Why now.** Before a request can create a tenant, there has to be somewhere to keep it.

**What we want.** A database context for the module, its tables in their own schema, `tenancy`, and the first
migration.

### The idea: Tenancy's tables are mapped by one call, in your context

- Tenancy has no database of its own. Its classes live in **your** context, beside the module's other data.
- One call, `modelBuilder.AddTenancy(...)`, maps them all: tables, keys, indexes and the organization's tree. The
  generator writes it for your classes, so it takes no type arguments.
- `[SupabaseMigrations]` on the context: the migrations become SQL files for Supabase, and the host checks at start-up
  that the database has them.

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

- `[SupabaseMigrations]`: the build writes the design-time factory `dotnet ef` needs. From step 4 the host exports
  each migration as a `.sql` file into `supabase/migrations`.
- `Schema = "tenancy"`: Tenancy's tables in a schema of their own, apart from other modules'.
- `AddTenancy(database: Database)`: maps the five classes. `database` says which database, for its own indexes.
- `AddDomainEventOutbox`: Tenancy raises a domain event per change; the outbox saves them in the same transaction.
- `AddDDDToolkitConventions`, `AddTenancyConverters`: Entity Framework does not know your id types; these store each
  as its `Guid`.
- `StoreDateTimeOffsetsAsUtc`: "which grants apply now" is asked in SQL. Moments stored as UTC compare on every
  database.

Make the first migration. The infrastructure project is its own startup project, through that factory:

```powershell
dotnet ef migrations add Tenancy --project src/Modules/Tenancy/Harbor.Tenancy.Infrastructure --startup-project src/Modules/Tenancy/Harbor.Tenancy.Infrastructure --output-dir Persistence/Migrations
```

It creates the schema `tenancy` with, among others:

- a table per class: `Tenants`, `Organizations`, `OrganizationUnits`, `Seats`, `Roles`;
- `SeatPlacements` and `SeatRoleGrants`;
- `OrganizationUnitPaths`: each unit with every unit above it, so "everything below North" is one query.

> **Going further.** Everything `AddTenancy` maps, and naming the tables yourself:
> [The context and the registration](../tenancy.md#the-context-and-the-registration).

## Next steps

The tutorial goes on in the same way:

4. **Registration and the host**: the module's registration, and the host that runs it.
5. **Provisioning a tenant**: the first request, as system work.
6. **Who am I**: what the caller is, in the tenant the request names.
7. **A unit and a seat**: requests that require a permission, at a unit.
8. **A role at a unit**: a role made by the tenant, given at a unit, and reach.
