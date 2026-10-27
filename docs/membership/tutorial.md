---
title: "Membership: the tutorial"
sidebar_label: Tutorial
---

# Membership: the tutorial

This tutorial adds a Boards module to the Harbor application, one step at a time. Each step says where we are, why
this step comes now and what it achieves. Then it explains the idea, shows every file whole with the new lines
highlighted, and says what those lines do.

Read the [overview](overview.md) first: it says what a resource, a member, an owner, a role and reach from above are,
and the steps use those words from the start.

## Where we are: the Harbor app from the Tenancy tutorial

The [Tenancy tutorial](../tenancy/tutorial.md) built Harbor. It has:

- the tenant Harbor Works, with the units Harbor Works (the root), North, and North Coast below North;
- Ada, Administrator at the root, and Rhea, Area manager at North. Leo has no seat yet;
- a host on Supabase that runs every request as its caller.

**What we build.** A module called Boards. A board belongs to the tenant, hangs at a unit, has an owner and members,
and its members hold roles the tenant keeps. When it is done, Rhea opens a board at North and puts Leo on it as an
editor; Leo edits that board and nothing else; Ada, at the root, reaches every board.

The module gets the same five projects as Tenancy, one per layer:

| Project | What it holds | Membership's package in it |
|---|---|---|
| `Harbor.Boards.Contracts` | What other modules may use: a board's id, and the questions a row rule asks about boards. | none, only the toolkit |
| `Harbor.Boards.Domain` | The classes: a board, its member, a board role. | `Supporting.Membership` |
| `Harbor.Boards.Application` | The permissions, the rules, the requests and their handlers. | none, it gets the domain's |
| `Harbor.Boards.Infrastructure` | The context, its migrations, and the database's own rules. | `Supporting.Membership.Postgres` |
| `Harbor.Boards.Api` | The routes, and the one call the host makes to add the module. | none |

```text
src/
├── Harbor.Host/
└── Modules/
    ├── Tenancy/
    └── Boards/
        ├── Harbor.Boards.Contracts/
        ├── Harbor.Boards.Domain/
        ├── Harbor.Boards.Application/
        ├── Harbor.Boards.Infrastructure/
        └── Harbor.Boards.Api/
```

## Step 1: the board, its members and its roles

**Where we are.** Harbor runs with one module, Tenancy. Nothing knows a board.

**Why now.** Everything later reads what the domain declares: the permissions name the board, the context maps it,
the requests load it.

**What we want.** A board, its member class and its role class, with their ids, in two new projects that build with
0 warnings.

### The idea: a board, its members and its owner

- **Resource**: the board. An ordinary aggregate root of yours: the package puts no parent class and no attribute on
  it.
- **Member**: a seat on the board, from a moment, until a moment or for good. You declare the member class in one
  line, `[Member<...>]`, and the package's **template** gives it the rest: who, from when, until when, who added it,
  the roles it holds. Its four types are its own row's id, who a member is, what a role is known by, and the board.
- **Why a member is a seat**, Tenancy's `SeatId`, and not a user:
  - a board belongs to one tenant, and so does a seat;
  - a suspended seat is on no board any more;
  - adding a member asks Tenancy whether the seat is active in this tenant;
  - who "I" am on a board is the caller's seat. Nothing to write for it.
- **Owner**: one property of the board, marked `[Owner]`. A fact of the board: whose it is. The owner is always a
  member, cannot be taken off, and holds no role: it holds what the permissions give the owner (step 2).
- **Member list**: the rules about the members, such as "on the board once" and "the owner stays". The generator
  writes it onto your board as a private property, `Members`, over your collection `People` and the owner. Your
  methods change the members only through it. `Members.Open(now)` puts the owner on the board.
- **Custom role**: a role the tenant keeps as a row and may rename and change. Its template keeps the name, the
  permissions and where it was made from. Whose role it is, `TenantId`, is yours: the package knows no tenant.
- **The rules are handed in**: a role's permissions are checked against the rules (step 2), which live in the
  application layer. The domain says what a board is, not who may do what with it.
- **The name**: `boards`, the class's name made plural. A refusal reads `boards.already-member`, a function in the
  database `boards_i_see`. `[Members("...")]` on the class chooses another.

### What you write

**The package versions**, at the root. **Why:** the three Membership packages, and Tenancy's Entity Framework
package, which Boards' context uses to read Tenancy's rows (step 3). Every version stays in one file.

**`Directory.Packages.props`**

```xml {10-13}
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Temp.DDDToolkit" Version="3.2.0-preview.5" />
    <PackageVersion Include="Temp.DDDToolkit.Supporting.Tenancy" Version="3.2.0-preview.5" />
    <PackageVersion Include="Temp.DDDToolkit.Supporting.Tenancy.Postgres" Version="3.2.0-preview.5" />
    <PackageVersion Include="Temp.DDDToolkit.Supporting.Tenancy.EntityFramework" Version="3.2.0-preview.5" />
    <PackageVersion Include="Temp.DDDToolkit.Supporting.Membership" Version="3.2.0-preview.5" />
    <PackageVersion Include="Temp.DDDToolkit.Supporting.Membership.EntityFramework" Version="3.2.0-preview.5" />
    <PackageVersion Include="Temp.DDDToolkit.Supporting.Membership.Postgres" Version="3.2.0-preview.5" />
    <PackageVersion Include="Temp.DDDToolkit.EntityFramework.Supabase" Version="3.2.0-preview.5" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.12" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12" />
    <PackageVersion Include="Mediator.Abstractions" Version="3.0.2" />
    <PackageVersion Include="Mediator.SourceGenerator" Version="3.0.2" />
  </ItemGroup>
</Project>
```

**The contracts project.** **Why:** another module may store a board's id, and step 4 adds the questions other
modules' row rules may ask about boards. Only the toolkit, as in Tenancy's.

**`src/Modules/Boards/Harbor.Boards.Contracts/Harbor.Boards.Contracts.csproj`**

```xml {5}
<Project Sdk="Microsoft.NET.Sdk">

  <!-- What the other modules may use of Boards: a board's id, and the questions about boards a row access rule asks. -->
  <ItemGroup>
    <PackageReference Include="Temp.DDDToolkit" />
  </ItemGroup>

</Project>
```

**The domain project.** **Why:** the member and role templates, and the generator that writes the member list. Of
Tenancy it references the contracts only: a board stores Tenancy's ids, and may not change a seat.

**`src/Modules/Boards/Harbor.Boards.Domain/Harbor.Boards.Domain.csproj`**

```xml {5,11}
<Project Sdk="Microsoft.NET.Sdk">

  <!-- The board classes: a board, the seats on it, and the roles a tenant keeps for boards. -->
  <ItemGroup>
    <PackageReference Include="Temp.DDDToolkit.Supporting.Membership" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Harbor.Boards.Contracts\Harbor.Boards.Contracts.csproj" />
    <!-- A board belongs to a tenant, hangs at a unit, and its members are seats: Tenancy's ids, from its contracts. -->
    <ProjectReference Include="..\..\Tenancy\Harbor.Tenancy.Contracts\Harbor.Tenancy.Contracts.csproj" />
  </ItemGroup>

</Project>
```

**The three ids.** **Why:** a member row and a role row are rows with an id, and `[EntityId<Guid>]` writes `Create()`,
with which the member list makes a new row's id in code. The board's id is published, so it is in the contracts; the
other two stay in the domain.

**`Harbor.Boards.Contracts/ValueObjects/BoardId.cs`**

```csharp {5-6}
using DDDToolkit.Abstractions.Attributes;

namespace Harbor.Boards.Contracts.ValueObjects;

[EntityId<Guid>]
public readonly partial record struct BoardId;
```

**`Harbor.Boards.Domain/Aggregates/Boards/ValueObjects/BoardMemberId.cs`**

```csharp {5-6}
using DDDToolkit.Abstractions.Attributes;

namespace Harbor.Boards.Domain.Aggregates.Boards.ValueObjects;

[EntityId<Guid>]
public readonly partial record struct BoardMemberId;
```

**`Harbor.Boards.Domain/Aggregates/BoardRoles/ValueObjects/BoardRoleId.cs`**

```csharp {5-6}
using DDDToolkit.Abstractions.Attributes;

namespace Harbor.Boards.Domain.Aggregates.BoardRoles.ValueObjects;

[EntityId<Guid>]
public readonly partial record struct BoardRoleId;
```

**The member class.** **Why:** it is your class and your table; the one line says what a member is, and the template
does the rest.

**`Harbor.Boards.Domain/Aggregates/Boards/Entities/BoardMember.cs`**

```csharp {9-10}
using DDDToolkit.Supporting.Membership;
using Harbor.Boards.Domain.Aggregates.BoardRoles.ValueObjects;
using Harbor.Boards.Domain.Aggregates.Boards.ValueObjects;
using Harbor.Tenancy.Contracts.ValueObjects;

namespace Harbor.Boards.Domain.Aggregates.Boards.Entities;

// Its row's id, who a member is, what a role is known by, and the board it is a member of.
[Member<BoardMemberId, SeatId, BoardRoleId, Board>]
public sealed partial class BoardMember;
```

**The board.** **Why:** the members live inside it, loaded and saved with it under its one version. It reads no clock,
so the constructor takes `now`.

**`Harbor.Boards.Domain/Aggregates/Boards/Board.cs`**

```csharp {17,26-27,29}
using DDDToolkit.Abstractions.Attributes;
using Harbor.Boards.Contracts.ValueObjects;
using Harbor.Boards.Domain.Aggregates.Boards.Entities;
using Harbor.Tenancy.Contracts.ValueObjects;

namespace Harbor.Boards.Domain.Aggregates.Boards;

[AggregateRoot<BoardId>]
public sealed partial class Board
{
    public Board(BoardId id, TenantId tenantId, OrganizationUnitId unitId, string name, SeatId owner, DateTimeOffset now) : base(id)
    {
        TenantId = tenantId;
        UnitId = unitId;
        Name = name;
        OwnerSeatId = owner;
        Members.Open(now);
    }

    public TenantId TenantId { get; private set; }

    public OrganizationUnitId UnitId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    [Owner]
    public SeatId OwnerSeatId { get; private set; }

    public partial IReadOnlyList<BoardMember> People { get; }
}
```

**The role class.** **Why:** a role is a row of the tenant's, so it carries `TenantId`; the rules are handed in, so a
role cannot give a permission they rule out.

**`Harbor.Boards.Domain/Aggregates/BoardRoles/BoardRole.cs`**

```csharp {9,12-13,15}
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.Access;
using Harbor.Boards.Domain.Aggregates.BoardRoles.ValueObjects;
using Harbor.Boards.Domain.Aggregates.Boards;
using Harbor.Tenancy.Contracts.ValueObjects;

namespace Harbor.Boards.Domain.Aggregates.BoardRoles;

[CustomRole<BoardRoleId, Board>]
public sealed partial class BoardRole
{
    public BoardRole(BoardRoleId id, TenantId tenantId, CustomRoleDraft draft, MembershipRules rules) : base(id, draft, rules)
        => TenantId = tenantId;

    public TenantId TenantId { get; private set; }
}
```

Add the two projects to the solution, and build:

```powershell
dotnet sln Harbor.slnx add --solution-folder src/Modules/Boards src/Modules/Boards/Harbor.Boards.Contracts/Harbor.Boards.Contracts.csproj src/Modules/Boards/Harbor.Boards.Domain/Harbor.Boards.Domain.csproj
dotnet build Harbor.slnx
```

### What it does

- `[Member<...>]`: the generator writes the other half of `BoardMember`, derived from the member template.
- On `Board` it writes `Members`, over `People` and the property marked `[Owner]`, and `Codes`, the refusals under
  `boards.`.
- `Members.Open(now)`: the owner is on the board from now, for good, with no role.
- `[CustomRole<...>]`: `BoardRole` derives from the role template, which checks a new role's name and permissions
  against the rules handed in.

Look in the domain project's **Dependencies → Analyzers** for `Board.Members.g.cs`: one line, the member list.

> **Going further.** What is written, and when it is not: [The member list is written for you](resources-and-members.md#the-member-list-is-written-for-you).

## Step 2: the permissions and the rules

**Where we are.** The classes exist. Nothing says what may be done with a board.

**Why now.** The rules read the permissions, and everything after reads the rules: the context (step 3), the database's
functions (step 4) and every request (step 6).

**What we want.** Five permissions, each declared once with where it is asked, and the rules in one declaration:
roles the tenant keeps, starting from one called `editor`.

### The idea: a permission has a level

- **Permission**: a string a request asks for and a role gives, as in Tenancy.
- **Level**: where it is asked. Three:

  | Level | Attribute | In Harbor |
  |---|---|---|
  | on one board | `[On<Board>]` | `boards.view`, `boards.edit`, `boards.members.manage` |
  | at a unit | `[AtUnit]` | `boards.create`: there is no board yet, so it is asked where the board goes |
  | for the whole tenant | `[AtTenant]` | `boards.roles.manage`: the tenant's roles of boards |

- **On a board**, each permission also says how it is held there:

  | Setting | Unless said | In Harbor |
  |---|---|---|
  | `Owner` | the owner holds it, by owning | all three |
  | `EveryMember` | being a member gives nothing by itself | `boards.view`: being on a board is seeing it |
  | `InRoles` | a role may give it | `boards.members.manage` says `false`: who is on a board, the owner says, not a role |

- **Rules**: one declaration per kind of resource, `MembershipRules.For<Board>(...)`. Made through the board's type,
  they read its name, its codes and its permissions. What is left to say is where the roles come from.
- **Where roles come from**, three ways:

  | Kind | Who makes them | In code |
  |---|---|---|
  | Fixed | you, in code, the same for every tenant | `Roles.Fixed([...])` |
  | Custom | the tenant, as rows, at run time | `Roles.Custom(starters: [...])`, with a role class |
  | External | something else that keeps roles, such as Tenancy | `Roles.External(...)` |

  Harbor's are custom. A **starter role** is one every tenant starts with, and then changes as its own: here
  `editor`, with `boards.view` and `boards.edit`.
- **Policy, not domain**: the permissions and the rules live in the application project. The domain says what a board
  is; who may do what is the application's.
- **Tenancy's catalogue**: Tenancy's generator lists every permission of a `[Permissions]` class, so a role of the
  organization can hold `boards.create` at North. The host collects them in step 5.

### What you write

**The application project.** **Why:** the requests come here, and Tenancy's core package brings the two levels Tenancy
knows, `[AtUnit]` and `[AtTenant]`, and its answers about the caller (step 6).

**`src/Modules/Boards/Harbor.Boards.Application/Harbor.Boards.Application.csproj`**

```xml {6-7}
<Project Sdk="Microsoft.NET.Sdk">

  <!-- The requests of Boards and their handlers, a folder per feature, and who holds which permission on a board. -->
  <ItemGroup>
    <ProjectReference Include="..\Harbor.Boards.Domain\Harbor.Boards.Domain.csproj" />
    <!-- Tenancy's words for a permission held in the organization, [AtUnit] and [AtTenant], and its answers about the caller. -->
    <PackageReference Include="Temp.DDDToolkit.Supporting.Tenancy" />
    <PackageReference Include="Mediator.Abstractions" />
  </ItemGroup>

</Project>
```

**The permissions.** **Why:** each is named once: a request requires the constant, the rules read it, the database's
functions are written from it. The description is what a screen that makes roles shows.

**`Harbor.Boards.Application/Access/BoardPermissions.cs`**

```csharp {8,11,14,17,20,23}
using DDDToolkit.Access;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using Harbor.Boards.Domain.Aggregates.Boards;

namespace Harbor.Boards.Application.Access;

[Permissions]
public static class BoardPermissions
{
    [On<Board>(EveryMember = true, Description = "See a board and who is on it")]
    public const string View = "boards.view";

    [On<Board>(Description = "Change a board")]
    public const string Edit = "boards.edit";

    [On<Board>(InRoles = false, Description = "Say who is on a board, and in which role")]
    public const string ManageMembers = "boards.members.manage";

    [AtUnit(Description = "Open a board at a unit")]
    public const string Create = "boards.create";

    [AtTenant(Description = "Set up the roles of boards")]
    public const string ManageRoles = "boards.roles.manage";
}
```

**The rules.** **Why:** one value that the check in C# and the database's functions both read, so they cannot
disagree.

**`Harbor.Boards.Application/Access/BoardMembership.cs`**

```csharp {8-9}
using DDDToolkit.Supporting.Membership.Access;
using Harbor.Boards.Domain.Aggregates.Boards;

namespace Harbor.Boards.Application.Access;

public static class BoardMembership
{
    public static MembershipRules Rules { get; } = MembershipRules.For<Board>(
        Roles.Custom(starters: [new("editor", [BoardPermissions.View, BoardPermissions.Edit])]));
}
```

```powershell
dotnet sln Harbor.slnx add --solution-folder src/Modules/Boards src/Modules/Boards/Harbor.Boards.Application/Harbor.Boards.Application.csproj
dotnet build Harbor.slnx
```

### What it does

- `[Permissions]`: every public constant is a permission; the attribute on it is its level.
- `MembershipRules.For<Board>`: the owner holds the three permissions on a board, every member holds `boards.view`,
  and a role may give `boards.view` and `boards.edit`.
- The rules are checked when they are made: a starter role with `boards.members.manage` would be refused, since no
  role may give it.

> **Going further.** Every setting of a permission, `Implies` and `Retired` among them:
> [The settings of a permission](permissions-and-roles.md#the-settings-of-a-permission).

## Step 3: where it is stored

**Where we are.** The classes and the rules exist, in memory only.

**Why now.** Before a request opens a board, there has to be somewhere to keep it. The database's functions of step 4
are written from this model too.

**What we want.** A context for Boards with its tables in the schema `boards`, the first migration, and the rules
marked for the export.

### The idea: the members are mapped by one call, in your context

- Boards has a context of its own, as Tenancy has, in its own schema.
- `HasMembers(row => row.People)`: both member tables, the members and the roles each holds, owned by the board and
  saved with it. The owner is the property marked `[Owner]`.
- `IsCustomRole()`: the package's part of a role row: its name, description, permissions, status, and the starter role
  it was made from (`MadeFrom`).
- **Whose a role is, is yours**: two unique indexes of yours, a role's name once per tenant, and each starter role once
  per tenant.
- `ScopeToTenant`: Tenancy's tenant filter and save check, on the board and on the role, as on every row of a tenant.
- `AddTenancyReadFunctions`: Tenancy's rows, the rights and the units, mapped through Tenancy's functions. So "does
  Rhea hold `boards.create` at North" is part of this context's own query, and no table of Tenancy's is read. Boards
  sees Tenancy's ids, not its classes, so it names the four ids.
- **The mark**, `[MembershipRules<BoardMember>]`: the build cannot read the value a registration is called with, so the
  mark says which rules are the board's. The export writes the board's functions in the database from them.

### What you write

**The infrastructure project.** **Why:** Membership's Postgres package brings its Entity Framework package, and with
Tenancy's Entity Framework package beside it, the generator writes what joins the two (step 5).

**`src/Modules/Boards/Harbor.Boards.Infrastructure/Harbor.Boards.Infrastructure.csproj`**

```xml {14-15}
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Where Boards is stored: its context, its migrations, and what the database checks of boards. -->
  <ItemGroup>
    <ProjectReference Include="..\Harbor.Boards.Application\Harbor.Boards.Application.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <!-- dotnet ef runs this project as its own startup project. -->
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Temp.DDDToolkit.Supporting.Membership.Postgres" />
    <PackageReference Include="Temp.DDDToolkit.Supporting.Tenancy.EntityFramework" />
    <PackageReference Include="Temp.DDDToolkit.EntityFramework.Supabase" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

**The context.** **Why:** the model is your context's to build: which collection is the member list, what the tables
are called, and whose a role is.

**`Harbor.Boards.Infrastructure/Persistence/BoardsContext.cs`**

```csharp {27,31-32,37-40,48}
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Harbor.Boards.Domain.Aggregates.BoardRoles;
using Harbor.Boards.Domain.Aggregates.Boards;
using Harbor.Boards.Infrastructure.Converters;
using Harbor.Tenancy.Contracts.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Boards.Infrastructure.Persistence;

[SupabaseMigrations]
public sealed class BoardsContext(DbContextOptions<BoardsContext> options) : DbContext(options)
{
    public const string Schema = "boards";

    public DbSet<Board> Boards => Set<Board>();

    public DbSet<BoardRole> BoardRoles => Set<BoardRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // Tenancy's rows, read through its functions, so a question about the organization is part of this context's queries.
        modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>("tenancy");

        modelBuilder.Entity<Board>(board =>
        {
            board.ScopeToTenant(row => row.TenantId);
            board.HasMembers(row => row.People);
        });

        modelBuilder.Entity<BoardRole>(role =>
        {
            role.IsCustomRole();
            role.ScopeToTenant(row => row.TenantId);
            role.HasIndex(row => new { row.TenantId, row.Name }).IsUnique();
            role.HasIndex(row => new { row.TenantId, row.MadeFrom }).IsUnique();
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
        configurationBuilder.AddBoardsConverters();
    }
}
```

**The rules, marked.** **Why:** the mark is what the export reads; without it the build warns, DDD00054, and writes
no function for boards.

**`Harbor.Boards.Application/Access/BoardMembership.cs`**

```csharp {3,9-10}
using DDDToolkit.Supporting.Membership.Access;
using Harbor.Boards.Domain.Aggregates.Boards;
using Harbor.Boards.Domain.Aggregates.Boards.Entities;

namespace Harbor.Boards.Application.Access;

public static class BoardMembership
{
    // The export writes the boards' functions in the database from these rules.
    [MembershipRules<BoardMember>]
    public static MembershipRules Rules { get; } = MembershipRules.For<Board>(
        Roles.Custom(starters: [new("editor", [BoardPermissions.View, BoardPermissions.Edit])]));
}
```

```powershell
dotnet sln Harbor.slnx add --solution-folder src/Modules/Boards src/Modules/Boards/Harbor.Boards.Infrastructure/Harbor.Boards.Infrastructure.csproj
dotnet build Harbor.slnx
dotnet ef migrations add Boards --project src/Modules/Boards/Harbor.Boards.Infrastructure --startup-project src/Modules/Boards/Harbor.Boards.Infrastructure --output-dir Persistence/Migrations
```

### What it does

- `[SupabaseMigrations]`, `StoreDateTimeOffsetsAsUtc`: as in Tenancy's context. Periods are compared in SQL, so they
  are stored as UTC.
- `AddBoardsConverters()`: generated for the module. It stores Boards' ids and Tenancy's published ids as their `Guid`.
- The migration creates the schema `boards` with four tables: `Boards`, `BoardPeople` (the members, named after the
  board and its collection), `BoardMemberRoles` and `BoardRoles`. No table of Tenancy's.

> **Going further.** Your own table and column names: [Several kinds of resource](resources-and-members.md#several-kinds-of-resource).

## Step 4: the database's own rules

**Where we are.** Boards has tables, and nothing says who may read or write them.

**Why now.** Harbor runs every query as its caller, under row level security (RLS). A table with no rule of yours is
closed: Tenancy's policies on it only narrow, to the caller's tenant, so no seat reads a single board. The export of
step 5 writes these rules as policies.

**What we want.** Who reads, opens and changes a board; who reads and writes a board role; and a lock on the members
and the owner.

### The idea: a rule in C#, a policy in the database

- **Row access rule**: a static class with `[RowAccess<T>(...)]` and one method, `Allows(row, caller)`. Your
  application never runs it: the export translates it into a policy, which Postgres applies to every query.
- **What a policy asks about boards**: the functions Membership writes from the rules, by a board's id. A rule names
  them through a **contract**, one line each, in the contracts project so other modules' rules may ask too:
  - `ResourceAccessSet.Seen`: the boards the caller sees;
  - `ResourceAccessSet.HeldOn`: the boards the caller holds a given permission on.
- **What a policy asks about the organization**: Tenancy's questions, such as `TenancyRowAccess.UnitsWhereIHold(...)`
  and `CallerSeatId()`.
- **A row knows no command.** The rule for changing a board lets through anyone who may edit it or manage its members
  (adding a member raises the board's version). The check before the handler tells the two apart.
- **The lock.** The member tables follow the board's rules, so Leo, who may edit a board, could write its member rows
  by a statement of his own, or make himself its owner. `LocksMembers` and `LocksOwner` name the permission your
  commands require for those; the package writes a policy on the member tables and a trigger on the owner column from
  it.

Harbor's rules:

| Table | Operation | Allowed when |
|---|---|---|
| `Boards` | read | the caller sees the board |
| `Boards` | create | the caller holds `boards.create` at the board's unit, and owns it |
| `Boards` | change | the caller holds `boards.edit` or `boards.members.manage` on it |
| `BoardRoles` | read | the role is of the caller's tenant |
| `BoardRoles` | create, change | the caller holds `boards.roles.manage` for the whole tenant |

### What you write

**The two contracts.** **Why:** a rule asks about boards by their id, and never by a function's name: the export
finds the function.

**`Harbor.Boards.Contracts/RowAccess/BoardsISee.cs`**

```csharp {7-8}
using DDDToolkit.Abstractions.Attributes;
using Harbor.Boards.Contracts.ValueObjects;

namespace Harbor.Boards.Contracts.RowAccess;

// The boards the caller sees, by their id: a question a row access rule asks of the database.
[ResourceAccessContract<BoardId>(ResourceAccessSet.Seen)]
public static partial class BoardsISee;
```

**`Harbor.Boards.Contracts/RowAccess/BoardsWhereIHold.cs`**

```csharp {7-8}
using DDDToolkit.Abstractions.Attributes;
using Harbor.Boards.Contracts.ValueObjects;

namespace Harbor.Boards.Contracts.RowAccess;

// The boards the caller holds a permission on, by their id.
[ResourceAccessContract<BoardId>(ResourceAccessSet.HeldOn)]
public static partial class BoardsWhereIHold;
```

**The rules on boards.** **Why:** what a seat reads and changes of a board is your application's to say, and the
package writes no policy of its own on your table.

**`Harbor.Boards.Infrastructure/Access/BoardRowRules.cs`**

```csharp {12,15,19,23-24,28,32-33}
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.Access;
using Harbor.Boards.Application.Access;
using Harbor.Boards.Contracts.RowAccess;
using Harbor.Boards.Domain.Aggregates.Boards;
using Harbor.Tenancy.Contracts.ValueObjects;

namespace Harbor.Boards.Infrastructure.Access;

// A seat reads the boards it sees.
[RowAccess<Board>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class SeatsReadTheBoardsTheySee
{
    public static bool Allows(Board board, Caller caller) => BoardsISee.Ids().Contains(board.Id);
}

// A seat opens a board where it holds boards.create, and as its own.
[RowAccess<Board>(RowOperations.Create, To = [RowAccessRoles.User])]
public static partial class SeatsOpenBoardsWhereTheyMay
{
    public static bool Allows(Board board, Caller caller)
        => TenancyRowAccess.UnitsWhereIHold<OrganizationUnitId>(BoardPermissions.Create).Contains(board.UnitId)
            && board.OwnerSeatId == TenancyRowAccess.CallerSeatId<SeatId>();
}

// A seat changes a board it may edit, or whose members it may manage: adding a member raises the board's version.
[RowAccess<Board>(RowOperations.Change, To = [RowAccessRoles.User])]
public static partial class SeatsChangeTheBoardsTheyWorkOn
{
    public static bool Allows(Board board, Caller caller)
        => BoardsWhereIHold.Ids(BoardPermissions.Edit).Contains(board.Id)
            || BoardsWhereIHold.Ids(BoardPermissions.ManageMembers).Contains(board.Id);
}
```

**The rules on board roles.** **Why:** a role's row decides what everybody who holds it may do, so who writes one is
worth a rule in the database too.

**`Harbor.Boards.Infrastructure/Access/BoardRoleRowRules.cs`**

```csharp {11,14,18,21}
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.Access;
using Harbor.Boards.Application.Access;
using Harbor.Boards.Domain.Aggregates.BoardRoles;
using Harbor.Tenancy.Contracts.ValueObjects;

namespace Harbor.Boards.Infrastructure.Access;

// A seat reads the board roles of its tenant.
[RowAccess<BoardRole>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class SeatsReadTheirTenantsBoardRoles
{
    public static bool Allows(BoardRole role, Caller caller) => role.TenantId == TenancyRowAccess.CallerTenant<TenantId>();
}

// A role's row decides what everybody who holds it may do: only who manages the roles of boards writes one.
[RowAccess<BoardRole>(RowOperations.Create | RowOperations.Change, To = [RowAccessRoles.User])]
public static partial class RoleManagersWriteBoardRoles
{
    public static bool Allows(BoardRole role, Caller caller) => TenancyRowAccess.HoldsAtTenant(BoardPermissions.ManageRoles);
}
```

**The lock**, on the permission that says who is on a board. **Why:** it is the one your commands that change the
members require (step 7), and naming an owner would need it too.

**`Harbor.Boards.Application/Access/BoardPermissions.cs`**

```csharp {17}
using DDDToolkit.Access;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using Harbor.Boards.Domain.Aggregates.Boards;

namespace Harbor.Boards.Application.Access;

[Permissions]
public static class BoardPermissions
{
    [On<Board>(EveryMember = true, Description = "See a board and who is on it")]
    public const string View = "boards.view";

    [On<Board>(Description = "Change a board")]
    public const string Edit = "boards.edit";

    [On<Board>(InRoles = false, LocksMembers = true, LocksOwner = true, Description = "Say who is on a board, and in which role")]
    public const string ManageMembers = "boards.members.manage";

    [AtUnit(Description = "Open a board at a unit")]
    public const string Create = "boards.create";

    [AtTenant(Description = "Set up the roles of boards")]
    public const string ManageRoles = "boards.roles.manage";
}
```

Build again: `dotnet build Harbor.slnx`. The policies reach the database with the export, in step 5.

### What it does

- `To = [RowAccessRoles.User]`: the rule is for signed-in callers, the database role `authenticated`.
- `BoardsISee.Ids().Contains(board.Id)`: becomes a subquery of the policy, asking the board's function once per
  statement.
- The create rule holds the database to what the command does: the owner is the caller's own seat.
- `LocksMembers`: a member row is added, changed or removed only on a board the caller holds `boards.members.manage`
  on. `LocksOwner`: the owner column changes only for such a caller.
- Leave the locks out, and the start-up check says so as a warning: the member rows then follow whatever your rule
  for changing a board allows.

> **Going further.** Everything the lock holds and leaves to you: [Who writes the member rows](postgres.md#who-writes-the-member-rows).

## Step 5: the module's registration and the host

**Where we are.** The module builds. Nothing registers it, and the host does not know it.

**Why now.** The requests of step 6 need their check before each handler, and the database needs the new tables,
functions and policies.

**What we want.** `AddBoardsModule(connectionString)`, called by the host, and the database brought up to date.

### The idea: two packages check Boards' requests

- **One interface per module**, as in Tenancy: every request of Boards implements `IBoardsRequest`, marked
  `[AccessRequests]`, and the generator writes the behavior that runs the checks before each handler.
- **Two checks decide the cases**, each registered for `IBoardsRequest`:
  - Tenancy's: in the tenant, a permission at a unit, a permission for the whole tenant;
  - Membership's: a permission on one board, and the boards a list shows.

  A requirement that no registered check decides lets nobody through.
- **The join with Tenancy is written for you.** The project references both packages, so the generator writes
  `AddBoardMembershipWithTenancy`: a member is the caller's seat, and a new member's seat must be active in the tenant.
  Boards sees Tenancy's ids, not its classes, so it names three of them; the seat's follows from the member class.
- **Ada's role lacks the new permissions.** Her Administrator role was copied from Tenancy's default administrators'
  pack when Harbor Works was provisioned, before boards existed. That pack holds every permission there is, so
  `SyncRolePacks()` in the host gives her role the board permissions when the host starts, and undoes nothing the
  tenant changed itself.

### What you write

**The module's request interface.** **Why:** what tells Boards' requests from another module's, so each module's
checks decide only its own.

**`Harbor.Boards.Application/Access/IBoardsRequest.cs`**

```csharp {5-6}
using DDDToolkit.Abstractions.Attributes;

namespace Harbor.Boards.Application.Access;

[AccessRequests]
public partial interface IBoardsRequest : IRequireAccess;
```

**`Harbor.Boards.Application/GlobalUsings.cs`**: what every request file of the module uses.

```csharp
global using DDDToolkit.Access;
global using DDDToolkit.Supporting.Membership.Access;
global using DDDToolkit.Supporting.Tenancy.Access;
global using Harbor.Boards.Application.Access;
global using Harbor.Boards.Contracts.ValueObjects;
global using Harbor.Boards.Domain.Aggregates.BoardRoles;
global using Harbor.Boards.Domain.Aggregates.BoardRoles.ValueObjects;
global using Harbor.Boards.Domain.Aggregates.Boards;
global using Harbor.Tenancy.Contracts.ValueObjects;
global using Mediator;
```

**The application's registration.** **Why:** the behavior is generated beside `IBoardsRequest`, in this project.

**`Harbor.Boards.Application/BoardsApplication.cs`**

```csharp {8}
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Boards.Application;

public static class BoardsApplication
{
    public static IServiceCollection AddBoardsApplication(this IServiceCollection services)
        => services.AddBoardsAccessBehavior();
}
```

**The infrastructure's registration.** **Why:** the context and both packages' checks run over the same database, so
they are registered where the context is.

**`Harbor.Boards.Infrastructure/BoardsInfrastructure.cs`**

```csharp {23,26-28}
using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Harbor.Boards.Application.Access;
using Harbor.Boards.Infrastructure.Persistence;
using Harbor.Tenancy.Contracts.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Boards.Infrastructure;

public static class BoardsInfrastructure
{
    public static IServiceCollection AddBoardsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDDDToolkitEntityFramework();
        services.AddDbContext<BoardsContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseDDDToolkit(provider));

        // Tenancy's checks for Boards' requests: in the tenant, a permission at a unit or for the whole tenant.
        services.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, IBoardsRequest, BoardsContext>();

        // The boards with their members, joined to Tenancy, and Membership's check for a permission on a board.
        services.AddBoardMembershipWithTenancy<BoardsContext, TenantId, OrganizationUnitId, RoleId>(BoardMembership.Rules);
        services.AddBoardMemberAccess<IBoardsRequest>();
        services.AddMembershipPostgres();
        return services;
    }
}
```

**The API project and the module's entry.** **Why:** the host references this project and calls these two methods,
and nothing else of Boards.

**`src/Modules/Boards/Harbor.Boards.Api/Harbor.Boards.Api.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- The module's entry: its registration and its routes. The host references this project and nothing else of the module. -->
  <ItemGroup>
    <ProjectReference Include="..\Harbor.Boards.Application\Harbor.Boards.Application.csproj" />
    <ProjectReference Include="..\Harbor.Boards.Infrastructure\Harbor.Boards.Infrastructure.csproj" />
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

</Project>
```

**`Harbor.Boards.Api/BoardsModule.cs`**

```csharp {11-12}
using Harbor.Boards.Application;
using Harbor.Boards.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Boards.Api;

// The module's entry: the host calls these, and nothing else of the module.
public static class BoardsModule
{
    public static IServiceCollection AddBoardsModule(this IServiceCollection services, string connectionString)
        => services.AddBoardsInfrastructure(connectionString).AddBoardsApplication();

    // The routes; each later step adds its own.
    public static IEndpointRouteBuilder MapBoardsModule(this IEndpointRouteBuilder routes)
        => routes;
}
```

**The host.** **Why:** it references the module's entry, and its mediator is generated with Boards' handlers as well.

**`src/Harbor.Host/Harbor.Host.csproj`**

```xml {5}
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="..\Modules\Tenancy\Harbor.Tenancy.Api\Harbor.Tenancy.Api.csproj" />
    <ProjectReference Include="..\Modules\Boards\Harbor.Boards.Api\Harbor.Boards.Api.csproj" />
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

```csharp {3-4,16,18,28}
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Startup;
using DDDToolkit.Supporting.Tenancy;
using Harbor.Boards.Api;
using Harbor.Host;
using Harbor.Host.Requests;
using Harbor.Tenancy.Api;

var builder = WebApplication.CreateBuilder(args);
var database = builder.Configuration.GetConnectionString("Supabase")
    ?? throw new InvalidOperationException("ConnectionStrings:Supabase is not set.");

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddSupabaseRowLevelSecurity();
builder.Services.AddTenancyModule(database);
builder.Services.AddBoardsModule(database);
builder.Services.AddTenancyPermissionsOfModules();
builder.Services.SyncRolePacks();
builder.Services.AddSupabaseMigrations();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RefusalsAsProblems>();
builder.Services.RunStartupChecks();

var app = builder.Build();
app.UseExceptionHandler();
app.UseCallerAndTenant();
app.MapTenancyModule();
app.MapBoardsModule();
if (app.Environment.IsDevelopment())
{
    app.MapTenancyDevelopment();
}

app.Run();
```

```powershell
dotnet sln Harbor.slnx add --solution-folder src/Modules/Boards src/Modules/Boards/Harbor.Boards.Api/Harbor.Boards.Api.csproj
```

### What it does

- `AddTenancyAccess<..., IBoardsRequest, BoardsContext>()`: Tenancy's checks for Boards' requests, asked over this
  context's read functions.
- `AddBoardMembershipWithTenancy`: the boards' questions over this context, and the join with Tenancy.
- `AddBoardMemberAccess<IBoardsRequest>()`: Membership's check, for Boards' requests.
- `AddMembershipPostgres()`: a start-up check that the database's functions and lock were written from the rules the
  host runs with.
- `AddTenancyPermissionsOfModules()`, unchanged: it now lists Boards' five permissions as well.

### Run it

1. Build the host: `dotnet build`. The export writes three files into `supabase/migrations`:
   - `<v>_Boards.boards.ddd.sql`: the migration of step 3;
   - `<v>_access.tenancy.ddd.sql`: Tenancy's access file again, since its catalogue gained Boards' permissions;
   - `<v>_access.boards.ddd.sql`: five functions for boards, the policies of step 4, and the lock.
2. Apply them: `npx supabase migration up --local`.
3. Run the host as in the Tenancy tutorial. The start-up checks pass, `membership.functions-in-place` among them, and
   the log says:

   ```text
   The role packs are synced in 1 tenants: 1 roles followed their packs, ...
   ```

   That is Ada's Administrator, which now holds the board permissions.

## Step 6: open a board, see it, list them

**Where we are.** The module is registered and has no routes.

**Why now.** Members need a board to be on.

**What we want.** Rhea opens a board at North, sees it, and finds it in her list. Ada, who is not on it, does not see
it yet.

### The idea: three requirements, each on its request

- Each request says what it requires, and the check before the handler holds it to that:

  | Request | Requires | Checked by |
  |---|---|---|
  | `OpenBoard` | `[RequiresPermission(boards.create)]`, with `[AtUnit]` on its unit | Tenancy: at that unit or above. There is no board yet to be a member of. |
  | `SeeBoard` | `[RequiresPermission(boards.view)]`, with `[OnResource]` on its board | Membership: on that board |
  | `ListBoards` | `[SeenWith<BoardId>(boards.view)]` | Membership: refuses nobody, and the list holds what the caller sees |

- **A board the caller does not see does not exist for it**: `404`, `boards.not-found`, the same answer as for an id
  that is no board. Nobody learns a board is there.
- **Reach**: `IMemberQuestions<BoardId>.Reach(permission)` is the set of boards the caller holds a permission on, not
  yet read. A query puts it in with `Within(reach)`, so a list is one statement, never ids fetched first.
- **Repository**: the handlers load and save boards through `IBoardRepository`, an interface in the application
  project, implemented in the infrastructure project. A reach goes into it, since `Within` is an Entity Framework
  query.
- **The owner is the caller**: the handler asks Tenancy's answers for the caller's tenant and seat, `RequireTenant()`.
  The create rule of step 4 holds the database to the same.

### What you write

**The repository.** **Why:** the application project knows no Entity Framework; what it needs of storage, it says in
an interface.

**`Harbor.Boards.Application/Boards/IBoardRepository.cs`**

```csharp {10}
namespace Harbor.Boards.Application.Boards;

// Where the handlers load and save boards. The infrastructure project implements it.
public interface IBoardRepository
{
    void Add(Board board);

    Task<Board?> FindAsync(BoardId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Board>> ListAsync(MemberReach<BoardId> reach, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
```

**`Harbor.Boards.Infrastructure/Persistence/BoardRepository.cs`**

```csharp {18}
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Membership.EntityFramework;
using Harbor.Boards.Application.Boards;
using Harbor.Boards.Contracts.ValueObjects;
using Harbor.Boards.Domain.Aggregates.Boards;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Boards.Infrastructure.Persistence;

internal sealed class BoardRepository(BoardsContext db) : IBoardRepository
{
    public void Add(Board board) => db.Boards.Add(board);

    public Task<Board?> FindAsync(BoardId id, CancellationToken cancellationToken)
        => db.Boards.SingleOrDefaultAsync(board => board.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Board>> ListAsync(MemberReach<BoardId> reach, CancellationToken cancellationToken)
        => await db.Boards.Within(reach).OrderBy(board => board.Name).ToListAsync(cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
```

**`Harbor.Boards.Infrastructure/BoardsInfrastructure.cs`**: the repository registered.

```csharp {6,31}
using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Harbor.Boards.Application.Access;
using Harbor.Boards.Application.Boards;
using Harbor.Boards.Infrastructure.Persistence;
using Harbor.Tenancy.Contracts.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Boards.Infrastructure;

public static class BoardsInfrastructure
{
    public static IServiceCollection AddBoardsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDDDToolkitEntityFramework();
        services.AddDbContext<BoardsContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseDDDToolkit(provider));

        // Tenancy's checks for Boards' requests: in the tenant, a permission at a unit or for the whole tenant.
        services.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, IBoardsRequest, BoardsContext>();

        // The boards with their members, joined to Tenancy, and Membership's check for a permission on a board.
        services.AddBoardMembershipWithTenancy<BoardsContext, TenantId, OrganizationUnitId, RoleId>(BoardMembership.Rules);
        services.AddBoardMemberAccess<IBoardsRequest>();
        services.AddMembershipPostgres();

        services.AddScoped<IBoardRepository, BoardRepository>();
        return services;
    }
}
```

**Opening a board.** **Why:** a permission at the unit, since the board does not exist yet; the caller becomes its
owner.

**`Harbor.Boards.Application/Boards/OpenBoard/OpenBoard.cs`**

```csharp {3-4,14-15,17}
namespace Harbor.Boards.Application.Boards.OpenBoard;

[RequiresPermission(BoardPermissions.Create)]
public sealed record OpenBoard([AtUnit] OrganizationUnitId Unit, string Name) : ICommand<BoardId>, IBoardsRequest;

public sealed class OpenBoardHandler(
    IBoardRepository boards,
    ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId> tenancy,
    TimeProvider clock) : ICommandHandler<OpenBoard, BoardId>
{
    public async ValueTask<BoardId> Handle(OpenBoard command, CancellationToken cancellationToken)
    {
        // The caller's tenant and seat: the board is the tenant's, and the caller owns it.
        var scope = tenancy.RequireTenant();
        var owner = scope.Seat ?? throw new InvalidOperationException("A board is opened by a seat.");

        var board = new Board(BoardId.Create(), scope.Tenant, command.Unit, command.Name, owner, clock.GetUtcNow());
        boards.Add(board);
        await boards.SaveAsync(cancellationToken);
        return board.Id;
    }
}
```

**Seeing one board.** **Why:** a permission on that board; the answer lists its members, the owner first.

**`Harbor.Boards.Application/Boards/SeeBoard/SeeBoard.cs`**

```csharp {6-7,14,17}
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.UseCases;

namespace Harbor.Boards.Application.Boards.SeeBoard;

[RequiresPermission(BoardPermissions.View)]
public sealed record SeeBoard([OnResource] BoardId Board) : IQuery<BoardDetails>, IBoardsRequest;

public sealed class SeeBoardHandler(IBoardRepository boards, TimeProvider clock) : IQueryHandler<SeeBoard, BoardDetails>
{
    public async ValueTask<BoardDetails> Handle(SeeBoard query, CancellationToken cancellationToken)
    {
        var board = await boards.FindAsync(query.Board, cancellationToken)
            ?? throw Board.Codes.Refuse(MembershipRefusals.NotFound);

        // The owner first, then by when each membership starts, each with its roles.
        var members = MemberOverviews.From(board.People, board.OwnerSeatId, clock.GetUtcNow());
        return new BoardDetails(board.Id, board.Name, board.UnitId, board.Version, members);
    }
}

public sealed record BoardDetails(BoardId Id, string Name, OrganizationUnitId Unit, long Version, IReadOnlyList<MemberOverview<SeatId, BoardRoleId>> Members);
```

**Listing the caller's boards.** **Why:** a list refuses nobody; the reach decides what is in it.

**`Harbor.Boards.Application/Boards/ListBoards/ListBoards.cs`**

```csharp {3-4,12-13}
namespace Harbor.Boards.Application.Boards.ListBoards;

[SeenWith<BoardId>(BoardPermissions.View)]
public sealed record ListBoards : IQuery<IReadOnlyList<BoardListing>>, IBoardsRequest;

public sealed class ListBoardsHandler(IMemberQuestions<BoardId> questions, IBoardRepository boards)
    : IQueryHandler<ListBoards, IReadOnlyList<BoardListing>>
{
    public async ValueTask<IReadOnlyList<BoardListing>> Handle(ListBoards query, CancellationToken cancellationToken)
    {
        // The boards the caller holds boards.view on, as part of the query: never the ids first.
        var seen = questions.Reach(BoardPermissions.View);
        var found = await boards.ListAsync(seen, cancellationToken);
        return [.. found.Select(board => new BoardListing(board.Id, board.Name, board.UnitId, board.OwnerSeatId))];
    }
}

public sealed record BoardListing(BoardId Id, string Name, OrganizationUnitId Unit, SeatId Owner);
```

**The routes.** **Why:** a route only sends its request; the check is on its way.

**`Harbor.Boards.Api/Boards/Rest/BoardsEndpoints.cs`**

```csharp {17-22}
using Harbor.Boards.Application.Boards.ListBoards;
using Harbor.Boards.Application.Boards.OpenBoard;
using Harbor.Boards.Application.Boards.SeeBoard;
using Harbor.Boards.Contracts.ValueObjects;
using Harbor.Tenancy.Contracts.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Harbor.Boards.Api.Boards.Rest;

internal static class BoardsEndpoints
{
    public static IEndpointRouteBuilder MapBoardsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/boards", async (NewBoard body, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(new { board = await sender.Send(new OpenBoard(body.Unit, body.Name), cancellationToken) }));
        routes.MapGet("/boards", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok(await sender.Send(new ListBoards(), cancellationToken)));
        routes.MapGet("/boards/{board}", async (BoardId board, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(await sender.Send(new SeeBoard(board), cancellationToken)));

        return routes;
    }

    public sealed record NewBoard(OrganizationUnitId Unit, string Name);
}
```

**`Harbor.Boards.Api/BoardsModule.cs`**: the module maps them.

```csharp {1,16}
using Harbor.Boards.Api.Boards.Rest;
using Harbor.Boards.Application;
using Harbor.Boards.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Boards.Api;

// The module's entry: the host calls these, and nothing else of the module.
public static class BoardsModule
{
    public static IServiceCollection AddBoardsModule(this IServiceCollection services, string connectionString)
        => services.AddBoardsInfrastructure(connectionString).AddBoardsApplication();

    public static IEndpointRouteBuilder MapBoardsModule(this IEndpointRouteBuilder routes)
        => routes.MapBoardsEndpoints();
}
```

### What it does

- `[AtUnit] Unit`: Rhea needs `boards.create` at that unit or above it, from a role of the organization.
- `[OnResource] Board`: the check reads that board, and refuses a caller who does not see it, or sees it without the
  permission, before the handler runs.
- `Board.Codes.Refuse(MembershipRefusals.NotFound)`: the board is gone between the check and the load, refused under
  the board's own code.
- `MemberOverviews.From(...)`: the members in the one order every answer lists them in. It reads nothing.
- `Within(reach)`: the boards Rhea holds `boards.view` on, as a subquery of this one statement.

### Try it

Rhea holds no `boards.create` yet. Ada makes a role of the organization for it and gives it to Rhea at North, with the
routes of the Tenancy tutorial:

```http
POST /roles
X-Dev-User: d0000000-0000-4000-8000-000000000001
Tenant: harbor
Content-Type: application/json

{ "name": "Board keeper", "description": "Opens boards", "permissions": ["boards.create", "boards.view"] }
```

1. As Ada: `POST /grants` with Rhea's seat, North, and that role: `204`.
2. As Rhea (`X-Dev-User` ending in `0002`): `POST /boards` with `{ "unit": "<North's id>", "name": "Crane schedule" }`:
   `200`, the board's id. Keep it.
3. As Rhea: `POST /boards` at the root: `403`, `tenancy.not-permitted`, "You lack the permission boards.create."
4. As Rhea: `GET /boards`: Crane schedule, with her seat as its owner.
5. As Rhea: `GET /boards/<id>`: `version` 1, one member, Rhea, `isOwner: true`, no roles.
6. As Ada: `GET /boards`: `[]`. `GET /boards/<id>`: `404`, `boards.not-found`. Ada holds `boards.view` at the root, and
   Rhea holds it at North: until step 8 a permission on a board is held on the board alone.

> **Going further.** A command that names the version its caller read, `[ExpectedVersion]`, and what holds a change
> between the check and the save: [From the check to the save](checks-and-saves.md#from-the-check-to-the-save).

## Step 7: members

**Where we are.** Rhea owns a board and is alone on it. Leo has no seat.

**Why now.** This is the point of Membership: access to one board through the people on it.

**What we want.** Harbor Works gets its starter role, `editor`. Rhea puts Leo on her board as an editor. Leo edits that
board, and may not say who else is on it.

### The idea: who may add a member, and what a role gives

- **Starter roles are made per tenant**, when your application says so. `StarterRoles.Missing(rules, roles)` gives the
  ones the tenant lacks; a role made from one before is not made again, even renamed. Here a request an administrator
  sends once, requiring `boards.roles.manage` for the tenant. A real application sends it while it sets a tenant up.
- **Who may add a member is yours.** The package has no permission of its own for it. Here: `boards.members.manage` on
  the board, which the owner holds by owning, and no role can give.
- **What the member list cannot know, the admission asks** (`MemberAdmission`), before anything changes:
  - `RequireMemberAsync`: the seat is an active seat of this tenant. Tenancy answers.
  - `RequireRoleAsync`: the role is one of this tenant's board roles, in use.
- **The member list then keeps its rules**: a seat already on the board is refused, `409`, `boards.already-member`.
- **A period**: `MemberPeriod.Open(now)` is from now, for good. `MemberPeriod.Between(now, until)` ends it.
- **What a role gives**: `editor` gives `boards.view` and `boards.edit` on each board it is held on, and nothing else.

### What you write

**The board's two methods.** **Why:** your own guard would go in front of the member list here, such as "a closed
board takes no member"; the member list keeps the rest.

**`Harbor.Boards.Domain/Aggregates/Boards/Board.cs`**

```csharp {2,4,33,35-37}
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Membership;
using Harbor.Boards.Contracts.ValueObjects;
using Harbor.Boards.Domain.Aggregates.BoardRoles.ValueObjects;
using Harbor.Boards.Domain.Aggregates.Boards.Entities;
using Harbor.Tenancy.Contracts.ValueObjects;

namespace Harbor.Boards.Domain.Aggregates.Boards;

[AggregateRoot<BoardId>]
public sealed partial class Board
{
    public Board(BoardId id, TenantId tenantId, OrganizationUnitId unitId, string name, SeatId owner, DateTimeOffset now) : base(id)
    {
        TenantId = tenantId;
        UnitId = unitId;
        Name = name;
        OwnerSeatId = owner;
        Members.Open(now);
    }

    public TenantId TenantId { get; private set; }

    public OrganizationUnitId UnitId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    [Owner]
    public SeatId OwnerSeatId { get; private set; }

    public partial IReadOnlyList<BoardMember> People { get; }

    public void Rename(string name) => Name = name;

    // A seat on the board, in a role, for a period: the member list refuses a seat that is on it already.
    public void AddMember(SeatId seat, BoardRoleId role, MemberPeriod period, DateTimeOffset now, SeatId? addedBy)
        => Members.Add(seat, role, period, now, addedBy);
}
```

**The roles' repository.** **Why:** the same reason as the boards': the application project says what it needs.

**`Harbor.Boards.Application/BoardRoles/IBoardRoleRepository.cs`**

```csharp
namespace Harbor.Boards.Application.BoardRoles;

// Where the handlers load and save the tenant's board roles.
public interface IBoardRoleRepository
{
    void Add(BoardRole role);

    Task<IReadOnlyList<BoardRole>> ListAsync(CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
```

**`Harbor.Boards.Infrastructure/Persistence/BoardRoleRepository.cs`**

```csharp {11-13}
using Harbor.Boards.Application.BoardRoles;
using Harbor.Boards.Domain.Aggregates.BoardRoles;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Boards.Infrastructure.Persistence;

internal sealed class BoardRoleRepository(BoardsContext db) : IBoardRoleRepository
{
    public void Add(BoardRole role) => db.BoardRoles.Add(role);

    // The tenant filter keeps it to the caller's tenant.
    public async Task<IReadOnlyList<BoardRole>> ListAsync(CancellationToken cancellationToken)
        => await db.BoardRoles.OrderBy(role => role.Name).ToListAsync(cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
```

**`Harbor.Boards.Infrastructure/BoardsInfrastructure.cs`**: the roles' repository registered.

```csharp {6,33}
using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Harbor.Boards.Application.Access;
using Harbor.Boards.Application.BoardRoles;
using Harbor.Boards.Application.Boards;
using Harbor.Boards.Infrastructure.Persistence;
using Harbor.Tenancy.Contracts.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Boards.Infrastructure;

public static class BoardsInfrastructure
{
    public static IServiceCollection AddBoardsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDDDToolkitEntityFramework();
        services.AddDbContext<BoardsContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseDDDToolkit(provider));

        // Tenancy's checks for Boards' requests: in the tenant, a permission at a unit or for the whole tenant.
        services.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, IBoardsRequest, BoardsContext>();

        // The boards with their members, joined to Tenancy, and Membership's check for a permission on a board.
        services.AddBoardMembershipWithTenancy<BoardsContext, TenantId, OrganizationUnitId, RoleId>(BoardMembership.Rules);
        services.AddBoardMemberAccess<IBoardsRequest>();
        services.AddMembershipPostgres();

        services.AddScoped<IBoardRepository, BoardRepository>();
        services.AddScoped<IBoardRoleRepository, BoardRoleRepository>();
        return services;
    }
}
```

**Setting up the starter roles.** **Why:** the roles are the tenant's rows, so somebody has to make them once per
tenant; the rules say which.

**`Harbor.Boards.Application/BoardRoles/SetUpBoardRoles/SetUpBoardRoles.cs`**

```csharp {5-6,16,19}
using DDDToolkit.Supporting.Membership.UseCases;

namespace Harbor.Boards.Application.BoardRoles.SetUpBoardRoles;

[RequiresPermission(BoardPermissions.ManageRoles)]
public sealed record SetUpBoardRoles : ICommand<int>, IBoardsRequest;

public sealed class SetUpBoardRolesHandler(
    IBoardRoleRepository roles,
    ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId> tenancy) : ICommandHandler<SetUpBoardRoles, int>
{
    public async ValueTask<int> Handle(SetUpBoardRoles command, CancellationToken cancellationToken)
    {
        // The starter roles this tenant does not have yet; a role made from one before is not made again.
        var tenant = tenancy.RequireTenant().Tenant;
        var missing = StarterRoles.Missing(BoardMembership.Rules, await roles.ListAsync(cancellationToken));
        foreach (var draft in missing)
        {
            roles.Add(new BoardRole(BoardRoleId.Create(), tenant, draft, BoardMembership.Rules));
        }

        await roles.SaveAsync(cancellationToken);
        return missing.Count;
    }
}
```

**Listing the roles.** **Why:** adding a member takes a role's id; any seat of the tenant may read them.

**`Harbor.Boards.Application/BoardRoles/ListBoardRoles/ListBoardRoles.cs`**

```csharp {3}
namespace Harbor.Boards.Application.BoardRoles.ListBoardRoles;

[InTenant]
public sealed record ListBoardRoles : IQuery<IReadOnlyList<BoardRoleListing>>, IBoardsRequest;

public sealed class ListBoardRolesHandler(IBoardRoleRepository roles) : IQueryHandler<ListBoardRoles, IReadOnlyList<BoardRoleListing>>
{
    public async ValueTask<IReadOnlyList<BoardRoleListing>> Handle(ListBoardRoles query, CancellationToken cancellationToken)
        => [.. (await roles.ListAsync(cancellationToken)).Select(role => new BoardRoleListing(role.Id, role.Name, role.Permissions))];
}

public sealed record BoardRoleListing(BoardRoleId Id, string Name, IReadOnlyList<string> Permissions);
```

**Adding a member.** **Why:** your rule for who may, on the request; what the member list cannot know, from the
admission; the change, through the board.

**`Harbor.Boards.Application/Boards/AddMember/AddMember.cs`**

```csharp {6-7,18-19,24}
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.UseCases;

namespace Harbor.Boards.Application.Boards.AddMember;

[RequiresPermission(BoardPermissions.ManageMembers)]
public sealed record AddMember([OnResource] BoardId Board, SeatId Seat, BoardRoleId Role) : ICommand, IBoardsRequest;

public sealed class AddMemberHandler(
    IBoardRepository boards,
    MemberAdmission<BoardId, SeatId, BoardRoleId> admission,
    ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId> tenancy,
    TimeProvider clock) : ICommandHandler<AddMember>
{
    public async ValueTask<Unit> Handle(AddMember command, CancellationToken cancellationToken)
    {
        // An active seat of this tenant, and a board role of this tenant in use.
        await admission.RequireMemberAsync(command.Seat, cancellationToken);
        await admission.RequireRoleAsync(command.Role, cancellationToken);

        var board = await boards.FindAsync(command.Board, cancellationToken)
            ?? throw Board.Codes.Refuse(MembershipRefusals.NotFound);
        var now = clock.GetUtcNow();
        board.AddMember(command.Seat, command.Role, MemberPeriod.Open(now), now, addedBy: tenancy.RequireTenant().Seat);
        await boards.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
```

**Renaming a board.** **Why:** a command that a role can allow: `boards.edit`, which `editor` gives.

**`Harbor.Boards.Application/Boards/RenameBoard/RenameBoard.cs`**

```csharp {5-6}
using DDDToolkit.Supporting.Membership;

namespace Harbor.Boards.Application.Boards.RenameBoard;

[RequiresPermission(BoardPermissions.Edit)]
public sealed record RenameBoard([OnResource] BoardId Board, string Name) : ICommand, IBoardsRequest;

public sealed class RenameBoardHandler(IBoardRepository boards) : ICommandHandler<RenameBoard>
{
    public async ValueTask<Unit> Handle(RenameBoard command, CancellationToken cancellationToken)
    {
        var board = await boards.FindAsync(command.Board, cancellationToken)
            ?? throw Board.Codes.Refuse(MembershipRefusals.NotFound);
        board.Rename(command.Name);
        await boards.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
```

**The routes.**

**`Harbor.Boards.Api/BoardRoles/Rest/BoardRolesEndpoints.cs`**

```csharp {14-17}
using Harbor.Boards.Application.BoardRoles.ListBoardRoles;
using Harbor.Boards.Application.BoardRoles.SetUpBoardRoles;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Harbor.Boards.Api.BoardRoles.Rest;

internal static class BoardRolesEndpoints
{
    public static IEndpointRouteBuilder MapBoardRolesEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/board-roles/starter", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok(new { made = await sender.Send(new SetUpBoardRoles(), cancellationToken) }));
        routes.MapGet("/board-roles", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok(await sender.Send(new ListBoardRoles(), cancellationToken)));

        return routes;
    }
}
```

**`Harbor.Boards.Api/Boards/Rest/BoardsEndpoints.cs`**: a new name and a new member, beside the others.

```csharp {1,4,7,26-35,42,44}
using Harbor.Boards.Application.Boards.AddMember;
using Harbor.Boards.Application.Boards.ListBoards;
using Harbor.Boards.Application.Boards.OpenBoard;
using Harbor.Boards.Application.Boards.RenameBoard;
using Harbor.Boards.Application.Boards.SeeBoard;
using Harbor.Boards.Contracts.ValueObjects;
using Harbor.Boards.Domain.Aggregates.BoardRoles.ValueObjects;
using Harbor.Tenancy.Contracts.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Harbor.Boards.Api.Boards.Rest;

internal static class BoardsEndpoints
{
    public static IEndpointRouteBuilder MapBoardsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/boards", async (NewBoard body, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(new { board = await sender.Send(new OpenBoard(body.Unit, body.Name), cancellationToken) }));
        routes.MapGet("/boards", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok(await sender.Send(new ListBoards(), cancellationToken)));
        routes.MapGet("/boards/{board}", async (BoardId board, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(await sender.Send(new SeeBoard(board), cancellationToken)));
        routes.MapPut("/boards/{board}/name", async (BoardId board, NewName body, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new RenameBoard(board, body.Name), cancellationToken);
            return Results.NoContent();
        });
        routes.MapPost("/boards/{board}/members", async (BoardId board, NewMember body, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new AddMember(board, body.Seat, body.Role), cancellationToken);
            return Results.NoContent();
        });

        return routes;
    }

    public sealed record NewBoard(OrganizationUnitId Unit, string Name);

    public sealed record NewName(string Name);

    public sealed record NewMember(SeatId Seat, BoardRoleId Role);
}
```

**`Harbor.Boards.Api/BoardsModule.cs`**: the roles' routes too.

```csharp {1,17}
using Harbor.Boards.Api.BoardRoles.Rest;
using Harbor.Boards.Api.Boards.Rest;
using Harbor.Boards.Application;
using Harbor.Boards.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Boards.Api;

// The module's entry: the host calls these, and nothing else of the module.
public static class BoardsModule
{
    public static IServiceCollection AddBoardsModule(this IServiceCollection services, string connectionString)
        => services.AddBoardsInfrastructure(connectionString).AddBoardsApplication();

    public static IEndpointRouteBuilder MapBoardsModule(this IEndpointRouteBuilder routes)
        => routes.MapBoardsEndpoints().MapBoardRolesEndpoints();
}
```

### What it does

- `StarterRoles.Missing(...)`: drafts for the starter roles the tenant lacks, told by `MadeFrom`, not by name.
- `[OnResource] Board` with `boards.members.manage`: the owner may; a member may not, whatever role it holds.
- `admission.RequireMemberAsync`, `RequireRoleAsync`: refused under the board's codes, `boards.member-not-active` and
  `boards.role-not-for-members`.
- `board.AddMember(...)`: the member list adds Leo, with the role for the same period, and refuses him a second time.
- `addedBy`: the caller's seat, kept on the member row.

### Try it

1. As Rhea: `POST /board-roles/starter`: `403`, `tenancy.not-permitted`: she lacks `boards.roles.manage`.
2. As Ada: `POST /board-roles/starter`: `{ "made": 1 }`. Again: `{ "made": 0 }`.
3. As Ada: `GET /board-roles`: `editor`, its id, `["boards.edit", "boards.view"]`. Keep the id.
4. As Ada: `POST /seats` with `{ "identity": "d0000000-0000-4000-8000-000000000003", "unit": "<North's id>" }`: Leo's
   seat.
5. As Rhea: `POST /boards/<id>/members` with `{ "seat": "<Leo's seat>", "role": "<editor's id>" }`: `204`.
6. As Leo: `GET /boards`: Crane schedule. `PUT /boards/<id>/name` with `{ "name": "Crane schedule, week 44" }`: `204`.
7. As Leo: `POST /boards/<id>/members` with Ada's seat: `403`, `boards.not-permitted`, "Doing this needs the permission
   boards.members.manage."
8. As Rhea: Leo again: `409`, `boards.already-member`.
9. As Rhea: `GET /boards/<id>`: `version` 3, Rhea first as the owner, then Leo with `editor`.

The lock of step 4, without the API. Open psql in the database, `docker exec -it supabase_db_harbor psql -U postgres`,
and act as Leo the way the API sets up each connection
([The caller on the connection](../how-a-request-is-checked.md#the-caller-on-the-connection)). The tenant id is the
`tenant` that `POST /dev/tenants` answered in the Tenancy tutorial.

```sql
begin;
set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"d0000000-0000-4000-8000-000000000003","role":"authenticated"}', true);
select set_config('tenancy.caller_tenant', '<tenant id>', true);

update boards."Boards" set "Name" = 'By hand' where "Id" = '<board id>';             -- UPDATE 1: he may edit it
update boards."Boards" set "OwnerSeatId" = '<Leo seat id>' where "Id" = '<board id>';  -- refused
rollback;
```

The second statement is refused by the lock's trigger, with the hint `ddd:access.refused`: Leo may change the board,
and not its owner. A member row he adds by hand is refused the same way, by the lock's policy on `BoardPeople`.

> **Going further.** A role the tenant makes itself, renames or archives: [Roles a customer makes](permissions-and-roles.md#roles-a-customer-makes).
> A rule of your own, such as "never for longer than you hold it yourself": [Who may give a role](permissions-and-roles.md#who-may-give-a-role-is-yours-to-decide).

## Step 8: reach from above

**Where we are.** Leo works on Crane schedule. Ada, administrator at the root, sees no board. Rhea's `boards.view` at
North gives her nothing on a board she does not own.

**Why now.** An organization's people act on what hangs in their part of it, without being put on each board. And a
board whose owner leaves is still somebody's to hand on.

**What we want.** Ada reaches every board; Rhea every board at North and below; Leo still only his own.

### The idea: a permission held where a board hangs counts on it

- `[At]` on the board's unit: a fact of the board, where it hangs.
- `reachFromAbove: true`: a permission held at that unit, or above it, counts on the board. Tenancy answers where the
  caller holds it, in C# and in the database's functions alike.
- **Seen from above by the permission every member holds**: `boards.view`. Rules that reach from above without an
  `EveryMember` permission are refused when they are made.
- **`InRoles = false` does not cut what is held above.** It keeps a permission out of the board's roles. Ada's
  Administrator holds `boards.members.manage` at the root, so she may say who is on any board.
- **Members come first**: a caller that is on the board and holds the permission above holds it as a member.
- **Where a board hangs now decides who reaches it**, so moving it would move it into somebody's reach. Harbor has no
  command that moves a board, so the unit is fixed once the board is there: `IsFixedAfterInsert`.

### What you write

**`Harbor.Boards.Domain/Aggregates/Boards/Board.cs`**

```csharp {24-25}
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Membership;
using Harbor.Boards.Contracts.ValueObjects;
using Harbor.Boards.Domain.Aggregates.BoardRoles.ValueObjects;
using Harbor.Boards.Domain.Aggregates.Boards.Entities;
using Harbor.Tenancy.Contracts.ValueObjects;

namespace Harbor.Boards.Domain.Aggregates.Boards;

[AggregateRoot<BoardId>]
public sealed partial class Board
{
    public Board(BoardId id, TenantId tenantId, OrganizationUnitId unitId, string name, SeatId owner, DateTimeOffset now) : base(id)
    {
        TenantId = tenantId;
        UnitId = unitId;
        Name = name;
        OwnerSeatId = owner;
        Members.Open(now);
    }

    public TenantId TenantId { get; private set; }

    // Where the board hangs: a permission held here, or above, reaches the board.
    [At]
    public OrganizationUnitId UnitId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    [Owner]
    public SeatId OwnerSeatId { get; private set; }

    public partial IReadOnlyList<BoardMember> People { get; }

    public void Rename(string name) => Name = name;

    // A seat on the board, in a role, for a period: the member list refuses a seat that is on it already.
    public void AddMember(SeatId seat, BoardRoleId role, MemberPeriod period, DateTimeOffset now, SeatId? addedBy)
        => Members.Add(seat, role, period, now, addedBy);
}
```

**`Harbor.Boards.Application/Access/BoardMembership.cs`**

```csharp {12-13}
using DDDToolkit.Supporting.Membership.Access;
using Harbor.Boards.Domain.Aggregates.Boards;
using Harbor.Boards.Domain.Aggregates.Boards.Entities;

namespace Harbor.Boards.Application.Access;

public static class BoardMembership
{
    // The export writes the boards' functions in the database from these rules.
    [MembershipRules<BoardMember>]
    public static MembershipRules Rules { get; } = MembershipRules.For<Board>(
        Roles.Custom(starters: [new("editor", [BoardPermissions.View, BoardPermissions.Edit])]),
        reachFromAbove: true);
}
```

**`Harbor.Boards.Infrastructure/Persistence/BoardsContext.cs`**

```csharp {34-35}
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Harbor.Boards.Domain.Aggregates.BoardRoles;
using Harbor.Boards.Domain.Aggregates.Boards;
using Harbor.Boards.Infrastructure.Converters;
using Harbor.Tenancy.Contracts.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Boards.Infrastructure.Persistence;

[SupabaseMigrations]
public sealed class BoardsContext(DbContextOptions<BoardsContext> options) : DbContext(options)
{
    public const string Schema = "boards";

    public DbSet<Board> Boards => Set<Board>();

    public DbSet<BoardRole> BoardRoles => Set<BoardRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // Tenancy's rows, read through its functions, so a question about the organization is part of this context's queries.
        modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>("tenancy");

        modelBuilder.Entity<Board>(board =>
        {
            board.ScopeToTenant(row => row.TenantId);
            board.HasMembers(row => row.People);

            // Where a board hangs decides who reaches it from above, so no statement moves it once it is there.
            board.Property(row => row.UnitId).IsFixedAfterInsert();
        });

        modelBuilder.Entity<BoardRole>(role =>
        {
            role.IsCustomRole();
            role.ScopeToTenant(row => row.TenantId);
            role.HasIndex(row => new { row.TenantId, row.Name }).IsUnique();
            role.HasIndex(row => new { row.TenantId, row.MadeFrom }).IsUnique();
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
        configurationBuilder.AddBoardsConverters();
    }
}
```

### What it does

- `[At]` and `reachFromAbove: true`: the board's functions ask Tenancy where the caller holds a permission, and count
  the boards that hang there or below.
- `IsFixedAfterInsert`: a save that changes a board's unit is refused, and the export leaves `UnitId` out of the
  columns a caller may update.
- The model of the tables did not change: no migration. The export writes a new `<v>_access.boards.ddd.sql`.

### Run it

Build the host, `npx supabase migration up --local`, and start the host again.

### Try it

1. As Ada: `GET /boards`: Crane schedule. `GET /boards/<id>`: `200`, with its members.
2. As Ada: `POST /boards` with `{ "unit": "<North Coast's id>", "name": "Quay plan" }`: Ada owns it.
3. As Rhea: `GET /boards`: Crane schedule and Quay plan. Her `boards.view` at North reaches North Coast.
4. As Leo: `GET /boards`: Crane schedule only. He holds nothing in the organization.
5. As Rhea: `PUT /boards/<Quay plan's id>/name`: `403`, `boards.not-permitted`, "Doing this needs the permission
   boards.edit." Board keeper gives `boards.create` and `boards.view`, and nothing more.
6. As Ada: `POST /boards/<Crane schedule's id>/members` with her own seat and `editor`: `204`. She is neither on the
   board nor its owner: `boards.members.manage` reaches it from the root.

> **Going further.** A command that moves a board, and the trigger that holds the unit to its permissions:
> [Who writes the member rows](postgres.md#who-writes-the-member-rows). Reach from above without Tenancy:
> [An organization of your own](beside-an-organization.md#an-organization-of-your-own).

## What you built

| Step | What now works |
|---|---|
| 1-3 | A board with members, an owner and the tenant's roles, and its tables |
| 4 | The database's own rules: who reads and changes a board, and the lock on its members |
| 5 | The module registered, checked by Tenancy and Membership; Ada's role brought up to date |
| 6 | Boards opened at a unit, seen and listed on a permission held on them |
| 7 | Starter roles, and members added by the owner, in a role that counts on that board alone |
| 8 | A permission held in the organization reaching every board below it |

Next: every detail of every part is in the topic pages, from [Resources and members](resources-and-members.md) on,
and every class and method in the [API reference](reference/resource-and-members.md).
