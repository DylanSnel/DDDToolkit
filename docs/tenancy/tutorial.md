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

> **Going further.** A class of your own with rules: [How your classes add behaviour](your-module.md#how-your-classes-add-behaviour).
> How an id makes a new one: [How a new id is made](your-module.md#how-a-new-id-is-made).

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
> [The context and the registration](your-module.md#the-context-and-the-registration).

## Step 4: the module's registration

**Where we are.** Classes and a context, and nothing that registers them.

**Why now.** The host needs one call to add the module, and every request needs its check before its handler.

**What we want.** `AddTenancyModule(connectionString)`: everything the module needs, registered from its entry
project.

### The idea: the check before every handler

- Every request of the module implements one interface, `ITenancyRequest`, marked `[AccessRequests]`.
- The generator writes a pipeline behavior for it: before a handler runs, it checks what the request requires of
  its caller (an attribute on the request, from step 6 on).
- A request that states no requirement does not compile: a forgotten requirement never opens a door.

### What you write

**`Harbor.Tenancy.Application/Access/ITenancyRequest.cs`**

```csharp {5-6}
using DDDToolkit.Abstractions.Attributes;

namespace Harbor.Tenancy.Application.Access;

[AccessRequests]
public partial interface ITenancyRequest : IRequireAccess;
```

**`Harbor.Tenancy.Application/GlobalUsings.cs`**: what every request file of the module uses.

```csharp
global using DDDToolkit.Access;
global using DDDToolkit.Supporting.Tenancy.Access;
global using Harbor.Tenancy.Application.Access;
global using Harbor.Tenancy.Contracts.ValueObjects;
global using Mediator;
```

**`Harbor.Tenancy.Application/TenancyApplication.cs`**

```csharp {8}
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tenancy.Application;

public static class TenancyApplication
{
    public static IServiceCollection AddTenancyApplication(this IServiceCollection services)
        => services.AddTenancyAccessBehavior();
}
```

**`Harbor.Tenancy.Infrastructure/TenancyInfrastructure.cs`**

```csharp {15-16,18-20,22-24}
using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.Postgres;
using Harbor.Tenancy.Application.Access;
using Harbor.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tenancy.Infrastructure;

public static class TenancyInfrastructure
{
    public static IServiceCollection AddTenancyInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDDDToolkitEntityFramework(options => options.UseOutbox<TenancyContext>(outbox =>
            outbox.AddTenancyDomainEvents()));

        services.AddDbContext<TenancyContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseDDDToolkit(provider));

        services.AddTenancy<TenancyContext>();
        services.AddTenancyPostgres();
        services.AddTenancyAccess<ITenancyRequest, TenancyContext>();
        return services;
    }
}
```

**`Harbor.Tenancy.Api/TenancyModule.cs`**: the only thing the host calls.

```csharp {11-12}
using Harbor.Tenancy.Application;
using Harbor.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tenancy.Api;

// The module's entry: the host calls these, and nothing else of the module.
public static class TenancyModule
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services, string connectionString)
        => services.AddTenancyInfrastructure(connectionString).AddTenancyApplication();

    // The routes; each later step adds its own.
    public static IEndpointRouteBuilder MapTenancyModule(this IEndpointRouteBuilder routes)
        => routes;
}
```

### What it does

- `AddTenancyAccessBehavior()`: generated for `ITenancyRequest`; runs the check before each handler.
- `UseOutbox` + `AddTenancyDomainEvents()`: Tenancy's events are saved with the change, in one transaction.
- `UseDDDToolkit(provider)`: every lock the host registered, on this context: the caller's database role on each
  connection (RLS), and Tenancy's save check (no write into another tenant).
- `AddTenancy<TenancyContext>()`: the use cases, closed over your classes, storing in this context.
- `AddTenancyPostgres()`: the tenant of the request on every connection, for the RLS policies.
- `AddTenancyAccess<ITenancyRequest, TenancyContext>()`: the checks the behavior asks, reading this context.

## Step 5: the host

**Where we are.** A module with one entry, and nothing that runs it.

**Why now.** Every later step sends a request; something has to receive it.

**What we want.** An API that knows who calls, in which tenant, answers a refusal with the right status, and checks
the database before it starts.

### The idea: the caller, and the seat in the tenant

- **Caller**: who sends the request, from the sign-in. Until the sign-in step, a development header stands in:
  `X-Dev-User: <user id>`.
- **Tenant**: the request names it, here with the header `Tenant: harbor`.
- **Seat**: Tenancy looks up the caller's seat in that tenant. The rest of the request runs as that seat: the checks,
  the use cases, and the database role (RLS).
- **Refusal**: a rule that says no throws a `RefusalException` with a code (`tenancy.not-permitted`). The host turns
  it into a status: 404, 403, 409 or 400.

### What you write

**`src/Harbor.Host/Harbor.Host.csproj`**

```xml {9,11,16-17}
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="..\Modules\Tenancy\Harbor.Tenancy.Api\Harbor.Tenancy.Api.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <!-- Every build writes the SQL file of each migration that has none, into supabase/migrations. -->
    <SupabaseMigrationsExport>Write</SupabaseMigrationsExport>
    <!-- The role the API logs in as: the build writes its migration, without a login or a password. -->
    <SupabaseLoginRole>harbor_api</SupabaseLoginRole>
  </PropertyGroup>

  <ItemGroup>
    <!-- The mediator is generated here, with every handler of the modules this project references. -->
    <PackageReference Include="Mediator.Abstractions" />
    <PackageReference Include="Mediator.SourceGenerator" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

**`src/Harbor.Host/Program.cs`**

```csharp {3,12-15,18,22}
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Startup;
using Harbor.Host;
using Harbor.Host.Requests;
using Harbor.Tenancy.Api;

var builder = WebApplication.CreateBuilder(args);
var database = builder.Configuration.GetConnectionString("Supabase")
    ?? throw new InvalidOperationException("ConnectionStrings:Supabase is not set.");

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddSupabaseRowLevelSecurity();
builder.Services.AddTenancyModule(database);
builder.Services.AddTenancyPermissionsOfModules();
builder.Services.AddSupabaseMigrations();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RefusalsAsProblems>();
builder.Services.RunStartupChecks();

var app = builder.Build();
app.UseExceptionHandler();
app.UseCallerAndTenant();
app.MapTenancyModule();
app.Run();
```

**`src/Harbor.Host/Requests/RequestPipeline.cs`**

```csharp {14-15,17-19}
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;

namespace Harbor.Host.Requests;

public static class RequestPipeline
{
    // The rest of the request runs as its caller, and as that caller's seat in the tenant the header names.
    public static IApplicationBuilder UseCallerAndTenant(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            // A stand-in for sign-in, for development: a header says who calls.
            var caller = Guid.TryParse(context.Request.Headers["X-Dev-User"], out var user) ? Caller.User(user) : Caller.Anonymous;
            using (Callers.Begin(caller))
            {
                var seat = await context.RequestServices.GetRequiredService<ITenantSelection>()
                    .ResolveAsync(caller, context.Request.Headers["Tenant"], context.RequestAborted);
                using (TenancyCallers.Begin(seat))
                {
                    await next(context);
                }
            }
        });
}
```

**`src/Harbor.Host/Requests/RefusalsAsProblems.cs`**

```csharp {16-23}
using DDDToolkit.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace Harbor.Host.Requests;

// A refusal is an answer, not a crash: its kind is the status, its code is what a client reads.
public sealed class RefusalsAsProblems : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not RefusalException refusal)
        {
            return false;
        }

        context.Response.StatusCode = refusal.Kind switch
        {
            RefusalKind.NotFound => StatusCodes.Status404NotFound,
            RefusalKind.NotPermitted => StatusCodes.Status403Forbidden,
            RefusalKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        await context.Response.WriteAsJsonAsync(new { code = refusal.Code, detail = refusal.Message }, cancellationToken);
        return true;
    }
}
```

### What it does

- `AddSupabaseRowLevelSecurity()`: every query runs as the caller's database role, the one the policies are for.
- `using Harbor.Host;`: the calls the toolkit generates into the host live in the host's own namespace.
- `AddTenancyPermissionsOfModules()`: generated into the host: the permissions of every module it references.
- `AddSupabaseMigrations()` and `RunStartupChecks()`: before the API serves anything, it checks that the database has
  every migration and policy, and that the login role may become the callers' roles. A failed check names the fix.
- `Callers.Begin` / `TenancyCallers.Begin`: who calls, and as which seat, for the rest of the request.
- `SupabaseLoginRole`: the API logs in as `harbor_api`, a role that owns nothing, so RLS applies to it.

### Run it

1. Start Supabase locally: `npx supabase init` once, then `npx supabase start`.
2. Build the host: `dotnet build`. The export writes `supabase/migrations`: the Tenancy migration, the access file
   (functions and policies) and the login role.
3. Apply them: `npx supabase migration up --local`.
4. Let the login role log in, locally only:

   ```powershell
   docker exec supabase_db_harbor psql -U postgres -c "ALTER ROLE harbor_api WITH LOGIN PASSWORD 'local-only'"
   ```

   `harbor` is the `project_id` in `supabase/config.toml`.

5. Run the host with the connection string:

   ```powershell
   $env:ConnectionStrings__Supabase = "Host=127.0.0.1;Port=54322;Database=postgres;Username=harbor_api;Password=local-only"
   dotnet run --project src/Harbor.Host
   ```

The start-up checks pass, and the API runs, with no routes yet.

## Step 6: provisioning a tenant

**Where we are.** A running API without routes, and no tenant.

**Why now.** Every other request works inside a tenant, so the first one creates one.

**What we want.** `POST /dev/tenants`: a tenant, its root unit, and Ada as its first seat, with the administrators'
starter role at the root.

### The idea: who may ask, and what the work runs as

- **Who may send it**: an attribute on the request. `[SignedIn]`: anyone signed in, with or without a seat.
- **What it runs as**: creating a tenant is no seat's work, since no seat exists yet. It is **system work**, and the
  handler says so explicitly, for that one operation: `using (TenancyUseCases.BeginSystem())`.
- Tenancy's provisioning refuses anything but system work, so a forgotten `BeginSystem` fails loudly.

### What you write

**`Harbor.Tenancy.Application/Tenants/ProvisionTenant/ProvisionTenant.cs`**

```csharp {5,14,16-18}
using DDDToolkit.Supporting.Tenancy;

namespace Harbor.Tenancy.Application.Tenants.ProvisionTenant;

[SignedIn]
public sealed record ProvisionTenant(string Slug, string Name, Guid Admin)
    : ICommand<TenancyUseCases.ProvisionedTenant>, ITenancyRequest;

public sealed class ProvisionTenantHandler(TenancyUseCases.TenantCommands tenants)
    : ICommandHandler<ProvisionTenant, TenancyUseCases.ProvisionedTenant>
{
    public async ValueTask<TenancyUseCases.ProvisionedTenant> Handle(ProvisionTenant command, CancellationToken cancellationToken)
    {
        using (TenancyUseCases.BeginSystem())
        {
            return await tenants.ProvisionAsync(
                new TenancyUseCases.TenantToProvision(command.Slug, command.Name, TenantShape.Hierarchical, command.Name, command.Admin,
                    ConfigureFirstSeat: seat => seat.ChangeJobTitle("Harbor master")),
                cancellationToken);
        }
    }
}
```

**`Harbor.Tenancy.Api/Development/DevelopmentEndpoints.cs`**: a route for development only.

```csharp {13-17}
using Harbor.Tenancy.Application.Tenants.ProvisionTenant;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Harbor.Tenancy.Api.Development;

internal static class DevelopmentEndpoints
{
    public static IEndpointRouteBuilder MapDevelopmentEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/dev/tenants", async (NewTenant body, ISender sender, CancellationToken cancellationToken) =>
        {
            var made = await sender.Send(new ProvisionTenant(body.Slug, body.Name, body.Admin), cancellationToken);
            return Results.Ok(new { tenant = made.Tenant, root = made.RootUnit, adminSeat = made.AdminSeat });
        });

        return routes;
    }

    public sealed record NewTenant(string Slug, string Name, Guid Admin);
}
```

**`Harbor.Tenancy.Api/TenancyModule.cs`**: one more entry, for the host to call in development only.

```csharp {1,19-20}
using Harbor.Tenancy.Api.Development;
using Harbor.Tenancy.Application;
using Harbor.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tenancy.Api;

// The module's entry: the host calls these, and nothing else of the module.
public static class TenancyModule
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services, string connectionString)
        => services.AddTenancyInfrastructure(connectionString).AddTenancyApplication();

    // The routes; each later step adds its own.
    public static IEndpointRouteBuilder MapTenancyModule(this IEndpointRouteBuilder routes)
        => routes;

    public static IEndpointRouteBuilder MapTenancyDevelopment(this IEndpointRouteBuilder routes)
        => routes.MapDevelopmentEndpoints();
}
```

**`src/Harbor.Host/Program.cs`**: the development route, in Development only.

```csharp {24-27}
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Startup;
using Harbor.Host;
using Harbor.Host.Requests;
using Harbor.Tenancy.Api;

var builder = WebApplication.CreateBuilder(args);
var database = builder.Configuration.GetConnectionString("Supabase")
    ?? throw new InvalidOperationException("ConnectionStrings:Supabase is not set.");

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddSupabaseRowLevelSecurity();
builder.Services.AddTenancyModule(database);
builder.Services.AddTenancyPermissionsOfModules();
builder.Services.AddSupabaseMigrations();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RefusalsAsProblems>();
builder.Services.RunStartupChecks();

var app = builder.Build();
app.UseExceptionHandler();
app.UseCallerAndTenant();
app.MapTenancyModule();
if (app.Environment.IsDevelopment())
{
    app.MapTenancyDevelopment();
}

app.Run();
```

### What it does

- `[SignedIn]`: the check lets any signed-in caller through, and refuses an anonymous one.
- `BeginSystem()`: system work until the `using` ends; after it, the caller is who it was.
- `ProvisionAsync`: the tenant, its organization with the root unit "Harbor Works", every starter role, and Ada's
  seat at the root with the administrators' role, in one save.
- `ConfigureFirstSeat`: a field of your own on the first seat, set in the same save.

### Try it

```http
POST /dev/tenants
X-Dev-User: d0000000-0000-4000-8000-000000000001
Content-Type: application/json

{ "slug": "harbor", "name": "Harbor Works", "admin": "d0000000-0000-4000-8000-000000000001" }
```

`200`, with the ids of the tenant, its root unit and Ada's seat. Keep the root's id for step 8.

## Step 7: who am I

**Where we are.** Harbor Works exists, with Ada as its administrator.

**Why now.** Before Ada does anything, she should see what she is: it shows what the later steps change.

**What we want.** `GET /me`: Ada's seat in the tenant the request names, where it is placed, its roles, and every
permission it holds with where it reaches.

### The idea: the overview, and your own fields

- `WhoAmIAsync()` answers the calling seat's **overview**: your `Seat` class whole, its units and roles by name, and
  each permission with the units it reaches.
- What leaves the API is yours: a record of your own, selected from the overview.
- `[InTenant]`: any seat in the tenant may ask; a caller without one gets 403.

### What you write

**`Harbor.Tenancy.Application/Seats/WhoAmI/WhoAmI.cs`**

```csharp {3,10,21}
namespace Harbor.Tenancy.Application.Seats.WhoAmI;

[InTenant]
public sealed record WhoAmI : IQuery<MyOverview>, ITenancyRequest;

public sealed class WhoAmIHandler(TenancyUseCases.TenancyDirectory directory) : IQueryHandler<WhoAmI, MyOverview>
{
    public async ValueTask<MyOverview> Handle(WhoAmI query, CancellationToken cancellationToken)
    {
        var overview = await directory.WhoAmIAsync(cancellationToken);
        return new MyOverview(
            overview.Tenant.Slug,
            overview.Seat.Id,
            overview.Seat.JobTitle,
            [
                .. overview.Seat.Placements.Select(placement => new MyPlacement(
                    overview.UnitOf(placement.UnitId).Path,
                    placement.IsPrimary,
                    [.. placement.Grants.Select(grant => new MyGrant(overview.RoleOf(grant.RoleId)?.Name ?? "", grant.AppliesAt(overview.AsOf)))])),
            ],
            [.. overview.Permissions.Select(permission => new MyPermission(permission.Permission, permission.AtTenant, [.. permission.Reaches.Select(unit => unit.Path)]))]);
    }
}

public sealed record MyOverview(string Tenant, SeatId Seat, string? JobTitle, IReadOnlyList<MyPlacement> Placements, IReadOnlyList<MyPermission> Permissions);

public sealed record MyPlacement(string Unit, bool Primary, IReadOnlyList<MyGrant> Grants);

public sealed record MyGrant(string Role, bool AppliesNow);

public sealed record MyPermission(string Permission, bool AtTenant, IReadOnlyList<string> Reaches);
```

**`Harbor.Tenancy.Api/Seats/Rest/SeatsEndpoints.cs`**: a route only sends the request; the check is on its way.

```csharp {13-14}
using Harbor.Tenancy.Application.Seats.WhoAmI;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Harbor.Tenancy.Api.Seats.Rest;

internal static class SeatsEndpoints
{
    public static IEndpointRouteBuilder MapSeatsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/me", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok(await sender.Send(new WhoAmI(), cancellationToken)));

        return routes;
    }
}
```

**`Harbor.Tenancy.Api/TenancyModule.cs`**: the module maps it.

```csharp {2,17}
using Harbor.Tenancy.Api.Development;
using Harbor.Tenancy.Api.Seats.Rest;
using Harbor.Tenancy.Application;
using Harbor.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tenancy.Api;

// The module's entry: the host calls these, and nothing else of the module.
public static class TenancyModule
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services, string connectionString)
        => services.AddTenancyInfrastructure(connectionString).AddTenancyApplication();

    public static IEndpointRouteBuilder MapTenancyModule(this IEndpointRouteBuilder routes)
        => routes.MapSeatsEndpoints();

    public static IEndpointRouteBuilder MapTenancyDevelopment(this IEndpointRouteBuilder routes)
        => routes.MapDevelopmentEndpoints();
}
```

### What it does

- `overview.UnitOf(...)`, `overview.RoleOf(...)`: the names of the seat's own units and roles, read in the same query.
- `grant.AppliesAt(overview.AsOf)`: whether a grant applies at the moment the overview holds for.
- `permission.AtTenant`: held at the root, so for the whole tenant.

### Try it

```http
GET /me
X-Dev-User: d0000000-0000-4000-8000-000000000001
Tenant: harbor
```

Ada's seat: placed at `Harbor Works`, with the role Administrator, and every permission `atTenant: true`, reaching
`Harbor Works`. Without the `Tenant` header the request is refused: it needs a seat, and without a tenant there is none.

## Step 8: a unit and a seat

**Where we are.** A tenant with its root, and Ada.

**Why now.** Roles are given at units, to seats. Both have to exist before step 9 gives one.

**What we want.** North below Harbor Works, Rhea's seat placed at North, and the list of units a seat reads.

### The idea: a permission, and where it must be held

- `[RequiresPermission(...)]`: the caller must hold that permission.
- **Where**: without a marker, for the whole tenant. `[AtUnit]` on a parameter: at that unit, or above it.
- Tenancy's use case checks again where it acts, and adds its own rules: the door is the first check, not the only one.
- A rule only Harbor has, "no two units of one name below one parent", is Harbor's: a refusal of its own, in the
  handler.

### What you write

**`Harbor.Tenancy.Application/Units/AddUnit/AddUnit.cs`**

```csharp {6-7,14-22,24}
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Harbor.Tenancy.Application.Units.AddUnit;

[RequiresPermission(TenancyPermissions.UnitsManage)]
public sealed record AddUnit([AtUnit] OrganizationUnitId Parent, string Name) : ICommand<OrganizationUnitId>, ITenancyRequest;

public sealed class AddUnitHandler(TenancyUseCases.OrganizationCommands organization, TenancyUseCases.TenancyDirectory directory)
    : ICommandHandler<AddUnit, OrganizationUnitId>
{
    public async ValueTask<OrganizationUnitId> Handle(AddUnit command, CancellationToken cancellationToken)
    {
        // Harbor's own rule: no two active units of one name below one parent.
        var name = command.Name.Trim();
        var units = await directory.ListUnitsAsync(cancellationToken);
        if (units.Any(found => found.Unit.ParentId == command.Parent
            && found.Unit.Status == UnitStatus.Active
            && string.Equals(found.Unit.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            throw UnitRefusals.NameTakenBelow(name);
        }

        return await organization.AddUnitAsync(command.Parent, command.Name, cancellationToken);
    }
}
```

**`Harbor.Tenancy.Application/Units/UnitRefusals.cs`**: Harbor's refusal, with a code a client reads.

```csharp {7,9-11}
using DDDToolkit.Exceptions;

namespace Harbor.Tenancy.Application.Units;

public static class UnitRefusals
{
    public const string NameTaken = "units.name-taken";

    internal static RefusalException NameTakenBelow(string name)
        => new(NameTaken, RefusalKind.Conflict, $"The unit has a unit named {name} below it already.",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Name"] = name });
}
```

**`Harbor.Tenancy.Application/Units/ListUnits/ListUnits.cs`**

```csharp {5,11-12}
using DDDToolkit.Supporting.Tenancy;

namespace Harbor.Tenancy.Application.Units.ListUnits;

[InTenant]
public sealed record ListUnits : IQuery<IReadOnlyList<UnitListing>>, ITenancyRequest;

public sealed class ListUnitsHandler(TenancyUseCases.TenancyDirectory directory) : IQueryHandler<ListUnits, IReadOnlyList<UnitListing>>
{
    public async ValueTask<IReadOnlyList<UnitListing>> Handle(ListUnits query, CancellationToken cancellationToken)
        => [.. (await directory.ListUnitsAsync(cancellationToken))
            .Select(found => new UnitListing(found.Unit.Id, found.Unit.ParentId, found.Unit.Name, found.Unit.Status, found.Path, found.Depth))];
}

public sealed record UnitListing(OrganizationUnitId Id, OrganizationUnitId? ParentId, string Name, UnitStatus Status, string Path, int Depth);
```

**`Harbor.Tenancy.Application/Seats/AddSeat/AddSeat.cs`**

```csharp {5,12-13}
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Harbor.Tenancy.Application.Seats.AddSeat;

[RequiresPermission(TenancyPermissions.SeatsManage)]
public sealed record AddSeat(Guid Identity, OrganizationUnitId Unit) : ICommand<SeatId>, ITenancyRequest;

public sealed class AddSeatHandler(TenancyUseCases.SeatCommands seats) : ICommandHandler<AddSeat, SeatId>
{
    public async ValueTask<SeatId> Handle(AddSeat command, CancellationToken cancellationToken)
    {
        var seat = await seats.AddSeatAsync(command.Identity, cancellationToken);
        await seats.PlaceAsync(seat, command.Unit, primary: true, cancellationToken);
        return seat;
    }
}
```

**`Harbor.Tenancy.Api/Units/Rest/UnitsEndpoints.cs`**: each route only sends its request.

```csharp {15-16,18-19}
using Harbor.Tenancy.Application.Units.AddUnit;
using Harbor.Tenancy.Application.Units.ListUnits;
using Harbor.Tenancy.Contracts.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Harbor.Tenancy.Api.Units.Rest;

internal static class UnitsEndpoints
{
    public static IEndpointRouteBuilder MapUnitsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/units", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok(await sender.Send(new ListUnits(), cancellationToken)));

        routes.MapPost("/units", async (NewUnit body, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(new { unit = await sender.Send(new AddUnit(body.Parent, body.Name), cancellationToken) }));

        return routes;
    }

    public sealed record NewUnit(OrganizationUnitId Parent, string Name);
}
```

**`Harbor.Tenancy.Api/Seats/Rest/SeatsEndpoints.cs`**: `POST /seats` beside `GET /me`.

```csharp {1,3,17-18,23}
using Harbor.Tenancy.Application.Seats.AddSeat;
using Harbor.Tenancy.Application.Seats.WhoAmI;
using Harbor.Tenancy.Contracts.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Harbor.Tenancy.Api.Seats.Rest;

internal static class SeatsEndpoints
{
    public static IEndpointRouteBuilder MapSeatsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/me", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok(await sender.Send(new WhoAmI(), cancellationToken)));
        routes.MapPost("/seats", async (NewSeat body, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(new { seat = await sender.Send(new AddSeat(body.Identity, body.Unit), cancellationToken) }));

        return routes;
    }

    public sealed record NewSeat(Guid Identity, OrganizationUnitId Unit);
}
```

**`Harbor.Tenancy.Api/TenancyModule.cs`**: the module maps both.

```csharp {3,18}
using Harbor.Tenancy.Api.Development;
using Harbor.Tenancy.Api.Seats.Rest;
using Harbor.Tenancy.Api.Units.Rest;
using Harbor.Tenancy.Application;
using Harbor.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tenancy.Api;

// The module's entry: the host calls these, and nothing else of the module.
public static class TenancyModule
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services, string connectionString)
        => services.AddTenancyInfrastructure(connectionString).AddTenancyApplication();

    public static IEndpointRouteBuilder MapTenancyModule(this IEndpointRouteBuilder routes)
        => routes.MapUnitsEndpoints().MapSeatsEndpoints();

    public static IEndpointRouteBuilder MapTenancyDevelopment(this IEndpointRouteBuilder routes)
        => routes.MapDevelopmentEndpoints();
}
```

### What it does

- `[AtUnit] Parent`: Ada needs `tenancy.units.manage` at the parent or above. She holds it at the root, so anywhere.
- `AddUnitAsync`: the unit, and its place in the tree (`OrganizationUnitPaths`), in one save.
- `ListUnitsAsync`: the units this seat reads: where it is placed, and everything below.
- `AddSeatAsync` + `PlaceAsync`: a seat for a verified identity, placed at a unit. Adding needs `tenancy.seats.manage`
  for the whole tenant.

### Try it

As Ada (`X-Dev-User` Ada's id, `Tenant: harbor`):

1. `POST /units` with `{ "parent": "<root id>", "name": "North" }`: `200`, North's id.
2. The same again: `409`, `units.name-taken`. Harbor's rule.
3. `POST /seats` with `{ "identity": "d0000000-0000-4000-8000-000000000002", "unit": "<North's id>" }`: Rhea's seat.
4. `GET /units`: Harbor Works, and `Harbor Works / North` below it.

## Step 9: a role at a unit

**Where we are.** Rhea has a seat at North, and no role: she may do nothing yet.

**Why now.** This is the point of Tenancy: a permission given at one place, reaching below it and nowhere else.

**What we want.** Ada makes a role "Area manager" with `tenancy.units.manage`, and gives it to Rhea at North. Rhea
may then add units below North, and not below the root.

### The idea: a role of the tenant's own, granted at a unit

- **Making a role** is the tenant's own work, if your application offers it: here a request that requires
  `tenancy.roles.manage` for the whole tenant.
- **Granting** gives a role to a seat at a unit. It requires `tenancy.grants.manage` at that unit.
- **Reach**: a grant at North counts at North and below ([overview](overview.md#reach-a-permission-counts-below-where-it-is-given)).
- **Containment**: a seat gives a permission that manages access only where it holds it itself, and never to itself
  ([what your application decides](overview.md#what-your-application-decides)).

### What you write

**`Harbor.Tenancy.Application/Roles/CreateRole/CreateRole.cs`**

```csharp {5-6,10-11}
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Harbor.Tenancy.Application.Roles.CreateRole;

[RequiresPermission(TenancyPermissions.RolesManage)]
public sealed record CreateRole(string Name, string Description, IReadOnlyList<string> Permissions) : ICommand<RoleId>, ITenancyRequest;

public sealed class CreateRoleHandler(TenancyUseCases.RoleCommands roles) : ICommandHandler<CreateRole, RoleId>
{
    public async ValueTask<RoleId> Handle(CreateRole command, CancellationToken cancellationToken)
        => await roles.CreateAsync(command.Name, command.Description, command.Permissions, cancellationToken);
}
```

**`Harbor.Tenancy.Application/Grants/GrantRole/GrantRole.cs`**

```csharp {5-6,12}
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Harbor.Tenancy.Application.Grants.GrantRole;

[RequiresPermission(TenancyPermissions.GrantsManage)]
public sealed record GrantRole(SeatId Seat, [AtUnit] OrganizationUnitId Unit, RoleId Role) : ICommand, ITenancyRequest;

public sealed class GrantRoleHandler(TenancyUseCases.SeatCommands seats) : ICommandHandler<GrantRole>
{
    public async ValueTask<Unit> Handle(GrantRole command, CancellationToken cancellationToken)
    {
        await seats.GrantAsync(command.Seat, command.Unit, command.Role, until: null, reason: null, cancellationToken);
        return Unit.Value;
    }
}
```

**`Harbor.Tenancy.Api/Roles/Rest/RolesEndpoints.cs`**

```csharp {13-14}
using Harbor.Tenancy.Application.Roles.CreateRole;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Harbor.Tenancy.Api.Roles.Rest;

internal static class RolesEndpoints
{
    public static IEndpointRouteBuilder MapRolesEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/roles", async (NewRole body, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(new { role = await sender.Send(new CreateRole(body.Name, body.Description, body.Permissions), cancellationToken) }));

        return routes;
    }

    public sealed record NewRole(string Name, string Description, string[] Permissions);
}
```

**`Harbor.Tenancy.Api/Seats/Rest/SeatsEndpoints.cs`**: `POST /grants` beside the others.

```csharp {1,20-24,31}
using Harbor.Tenancy.Application.Grants.GrantRole;
using Harbor.Tenancy.Application.Seats.AddSeat;
using Harbor.Tenancy.Application.Seats.WhoAmI;
using Harbor.Tenancy.Contracts.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Harbor.Tenancy.Api.Seats.Rest;

internal static class SeatsEndpoints
{
    public static IEndpointRouteBuilder MapSeatsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/me", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok(await sender.Send(new WhoAmI(), cancellationToken)));
        routes.MapPost("/seats", async (NewSeat body, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(new { seat = await sender.Send(new AddSeat(body.Identity, body.Unit), cancellationToken) }));
        routes.MapPost("/grants", async (NewGrant body, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new GrantRole(body.Seat, body.Unit, body.Role), cancellationToken);
            return Results.NoContent();
        });

        return routes;
    }

    public sealed record NewSeat(Guid Identity, OrganizationUnitId Unit);

    public sealed record NewGrant(SeatId Seat, OrganizationUnitId Unit, RoleId Role);
}
```

**`Harbor.Tenancy.Api/TenancyModule.cs`**: the roles' routes too.

```csharp {2,19}
using Harbor.Tenancy.Api.Development;
using Harbor.Tenancy.Api.Roles.Rest;
using Harbor.Tenancy.Api.Seats.Rest;
using Harbor.Tenancy.Api.Units.Rest;
using Harbor.Tenancy.Application;
using Harbor.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tenancy.Api;

// The module's entry: the host calls these, and nothing else of the module.
public static class TenancyModule
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services, string connectionString)
        => services.AddTenancyInfrastructure(connectionString).AddTenancyApplication();

    public static IEndpointRouteBuilder MapTenancyModule(this IEndpointRouteBuilder routes)
        => routes.MapUnitsEndpoints().MapSeatsEndpoints().MapRolesEndpoints();

    public static IEndpointRouteBuilder MapTenancyDevelopment(this IEndpointRouteBuilder routes)
        => routes.MapDevelopmentEndpoints();
}
```

### What it does

- `CreateAsync`: refuses a permission the catalogue does not know (`tenancy.unknown-permission`) and a name the tenant
  uses already (`tenancy.role-name-taken`).
- `GrantAsync(..., until: null, ...)`: from now, for good. `until` would end it at a moment.
- The check at the door asks `tenancy.grants.manage` at the unit; `GrantAsync` adds containment and "never to
  yourself".

### Try it

1. As Ada: `POST /roles` with `{ "name": "Area manager", "description": "Runs an area", "permissions":
   ["tenancy.units.manage"] }`.
2. As Ada: `POST /grants` with Rhea's seat, North, and that role.
3. As Rhea (`X-Dev-User` Rhea's id): `POST /units` below North, "North Coast": `200`.
4. As Rhea: `POST /units` below the root: `403`, `tenancy.not-permitted`. Her grant reaches North and below only.
5. As Rhea: `GET /me`: `tenancy.units.manage`, `atTenant: false`, reaching `Harbor Works / North` and
   `Harbor Works / North / North Coast`.

## What you built

| Step | What now works |
|---|---|
| 1-3 | The module's projects, your classes, the tables |
| 4-5 | The module registered, a host that knows the caller and the seat, the database checked at start-up |
| 6 | A tenant, created as system work |
| 7 | `GET /me`: what a seat is and holds |
| 8 | Units and seats, behind permissions held at a unit |
| 9 | A role of the tenant's own, granted at a unit, reaching below it |

Next: access to one thing, such as a board with its own members: the [Membership tutorial](../membership/tutorial.md)
adds it to Harbor.
