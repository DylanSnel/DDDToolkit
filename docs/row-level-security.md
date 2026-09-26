# Row level security

Postgres's row level security decides, for every query, which rows the caller gets to see and change,
from policies on the tables. It does not apply to the tables' owner, and an application usually logs in
as the owner, so the policies guard whatever else reaches the database and not the application.
`DDDToolkit.EntityFramework.Postgres` changes that: every connection a context opens runs as the caller,
so the policies decide what the application sees too. And the policies themselves can be written in C#,
next to the aggregate they guard, as rules the generator turns into SQL when the code compiles.

This page is about Postgres, any Postgres. [Supabase](supabase.md#row-level-security-for-your-own-queries)
has the same thing with Supabase's roles and `auth` functions, and its build writes the policies into
`supabase/migrations` for you.

## Install

```bash
dotnet add package DDDToolkit.EntityFramework.Postgres
```

## Running queries as the caller

```csharp
builder.Services.AddPostgresRowLevelSecurity();

builder.Services.AddDbContext<OrderingContext>((provider, options) => options
    .UseNpgsql(connectionString)
    .UsePostgresRowLevelSecurity(provider));
```

Every time the context opens a connection, the interceptor sets a role and the caller's token claims on
it, the way PostgREST does for each request:

```sql
SELECT set_config('role', 'authenticated', false), set_config('request.jwt.claims', '{"sub":"…"}', false)
```

| The caller | Runs as | `ddd.caller_id()` |
|---|---|---|
| A signed-in user | `authenticated`, with their token's claims | the user's id |
| A request without a user | `anon`, with the claims `{"role":"anon"}` | `null` |
| No request at all: an outbox poller, a hosted service | `SystemRole`, or the role the application logged in as | `null` |
| Code inside `using (Callers.Begin(caller))` | that caller, whatever the request says | the caller's |

The roles are PostgREST's by default; `AddPostgresRowLevelSecurity(options => ...)` changes them.

**Who is calling** is the toolkit's `Caller`, and an `ICallerAccessor` says which one it is at this
moment. A host registers the accessor that knows its callers: `AddSupabaseJwtBearer` from
`DDDToolkit.Auth.Supabase.AspNetCore` answers from the request, and `DDDToolkit.Auth.Supabase.AzureFunctions`
from the invocation. Without one, the answer is whatever `Callers.Begin` made current, or the system
outside any, which is what a worker service or a test wants. A caller comes from the claims of a
validated token, `Callers.FromClaims(claims)`, or is made in code, `Caller.User(id)`, in which case the
database sees its id and role and no other claims.

```csharp
// a job queued on a user's behalf, with the claims of the token that queued it
using (Callers.Begin(Callers.FromClaims(job.Claims)))
{
    await handler.HandleAsync(job, cancellationToken);
}
```

`Callers.Begin` follows `await` and `Task.Run` the way an `AsyncLocal` does, and a caller begun in code
wins over the request's. The same works the other way round, for a step a request takes on the
application's behalf: `Callers.Begin(Caller.System)`.

**Background work** runs as `SystemRole`, or, left unset, as the role the application logged in as,
which as the owner sees every row. For an application that should not be able to see everything by
accident, log in as a role of its own that may do nothing but switch roles, and set `SystemRole` to a
role with `BYPASSRLS`. A query outside a request that nobody meant to run as the system then fails
instead of seeing every row.

**How the settings travel.** They are set on the connection when a context opens it, because Entity
Framework opens a connection for each query outside a transaction, and cost one round trip each time.
Npgsql clears them with `DISCARD ALL` before the pooled connection is used again. Three ways of
connecting would let them reach somebody else, and the interceptor refuses them before connecting:
`No Reset On Close`, which skips that reset; `Multiplexing`, which shares a connection between callers at
once; and Supabase's transaction pooler on port 6543, which hands each transaction whichever server
connection is free. A connection you open yourself and hand to `UseNpgsql(connection)` gets no settings
at all.

## A Postgres of your own

A Supabase project has the roles and the functions a policy asks about the caller. A Postgres of your
own has neither until `PostgresRowAccess.SetupScript()` makes them: `anon` and `authenticated`, granted
to the role the application logs in as so it may switch to them, and `ddd.caller_id()`,
`ddd.caller_role()` and `ddd.caller_claims()`, which read the claims the interceptor set. It can run
again, so it belongs in an early migration:

```csharp
public partial class RowLevelSecurity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(PostgresRowAccess.SetupScript());
        migrationBuilder.Sql("""
            grant usage on schema ordering to anon, authenticated;
            grant select, insert, update, delete on all tables in schema ordering to anon, authenticated;
            alter default privileges in schema ordering grant select, insert, update, delete on tables to anon, authenticated;
            """);
    }
}
```

The grants give the two roles the tables; the policies then decide which rows. `ddd.caller_id()` is
`null` for a token whose `sub` is not a `uuid`, so a rule about a user matches nobody rather than failing
every query; `Caller.UserId` is `null` for it too.

## Row access rules written in C#

A rule is a static method on a class of its own, marked with the aggregate it guards and what it allows:

```csharp
[RowAccess<Order>(RowOperations.Read | RowOperations.Change)]
public static partial class ACustomerSeesTheirOrders
{
    public static bool Allows(Order order, Caller caller)
        => order.PlacedBy == null || order.PlacedBy?.Value == caller.UserId;
}
```

When the class compiles, the generator translates `Allows` into SQL and writes it into the class as
`RowAccessSql`. The rule stays an ordinary method as well, so a handler, a test or a screen asks the same
rule in C#: `ACustomerSeesTheirOrders.Allows(order, caller)`.

```mermaid
flowchart LR
    Rule["ACustomerSeesTheirOrders.Allows<br/>in C#"] -->|"the generator,<br/>when it compiles"| Template["RowAccessSql:<br/>SQL with the columns<br/>still to fill in"]
    Template --> Script["PostgresRowAccess.Script:<br/>columns from the<br/>Entity Framework model"]
    Script --> Policies["CREATE POLICY<br/>on the aggregate's tables"]
    Rule -->|"called directly"| Handler["a handler, a test"]
```

What the generator translates, and into what:

| In `Allows` | In the policy |
|---|---|
| `order.PlacedBy == null \|\| order.PlacedBy?.Value == caller.UserId` | `("PlacedBy" IS NULL) OR ("PlacedBy" IS NOT DISTINCT FROM ddd.caller_id())` |
| `order.Status != OrderStatus.Cancelled` | `"Status" IS DISTINCT FROM 2`, or `'Cancelled'` when the column stores it as text |
| `order.Total.Amount <= 500` | `coalesce("Total_Amount" <= 500, FALSE)` |
| `caller.IsSignedIn && !order.IsPublic` | `(ddd.caller_id() IS NOT NULL) AND (NOT "IsPublic")` |
| `caller.Claim("app_metadata.team") == order.Team` | `ddd.caller_claims() #>> '{app_metadata,team}' IS NOT DISTINCT FROM "Team"` |
| `caller.Role == "authenticated"` | `ddd.caller_role() IS NOT DISTINCT FROM 'authenticated'` |

`==` is C#'s equality, nulls included, so it becomes `IS NOT DISTINCT FROM`, and a comparison that SQL
would leave unknown is false, as it is in C#. The caller functions are wrapped in `(SELECT ...)` in the
policy itself, so Postgres asks them once per query rather than once per row. Column names come from the
Entity Framework model when the policies are written, so renaming a property or its column changes the
policy with it. A value object stored inline is its columns, and a typed id's `.Value` is the id's column.

What the database cannot check is a compile error on that expression: a method call such as
`order.Team.StartsWith("n")`, `DateTime.UtcNow`, anything about another aggregate. For those,
`Sql.Call<T>` and `Sql.Raw<T>` write SQL into the rule:

```csharp
public static bool Allows(Order order, Caller caller)
    => order.PlacedBy?.Value == caller.UserId
       || Sql.Call<bool>("support.is_agent", caller.UserId);
```

The arguments are translated like the rest; the function is yours to create in a migration. A rule that
uses either is the database's only: called in C#, it throws, because C# cannot run the SQL.

**Entities follow their aggregate.** A rule is on the root. The tables of the aggregate's entities, an
order's lines, get a policy of their own that asks the root's table, which answers under the rules, so an
aggregate is visible whole or not at all and Entity Framework never loads half of one.

**Roles.** A rule is for `anon` and `authenticated` unless `To` says otherwise:
`[RowAccess<Order>(RowOperations.Read, To = new[] { "authenticated" })]`. Several rules on one aggregate add up:
a row one of them allows is allowed.

[DDD00038](diagnostics.md#ddd00038) to [DDD00041](diagnostics.md#ddd00041) are the rules the generator
enforces: the class's shape, what it can translate, that the type is an aggregate root, and that a rule
asks an access function about the aggregate's entities.

### Asking the aggregate's entities: access functions

Whether the caller is one of a project's members is a question about the project's entities, and a rule
cannot ask it itself. The tables of the entities have policies that ask the aggregate's table whether
their row is visible, so a policy on the aggregate's table that read them would ask itself, and Postgres
stops the query with infinite recursion ([DDD00041](diagnostics.md#ddd00041)). Put the question in an
access function, written the way a rule is, and let the rules call it:

```csharp
[AccessFunction<Project>("projects.is_member")]
public static partial class ProjectMembership
{
    public static bool Allows(Project project, Caller caller)
        => project.Members.Any(member => member.UserId == caller.UserId);
}

[RowAccess<Project>(RowOperations.Read | RowOperations.Change)]
public static partial class MembersWorkOnTheirProjects
{
    public static bool Allows(Project project, Caller caller) => ProjectMembership.Allows(project, caller);
}
```

The function becomes one SQL function. It runs as its owner, `SECURITY DEFINER`, so it reads the members
without their policies, and with an empty search path, so nothing in the caller's session changes what it
reads. The rule asks it about the row:

```sql
CREATE OR REPLACE FUNCTION projects.is_member(uuid) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
    SELECT EXISTS (SELECT 1 FROM projects."Projects" root
                   WHERE root."Id" = $1 AND (EXISTS (SELECT 1 FROM projects."ProjectMember" e1
                                                     WHERE e1."ProjectId" = root."Id" AND ((e1."UserId" IS NOT DISTINCT FROM (SELECT ddd.caller_id()))))))
$function$;

CREATE POLICY "Members work on their projects (read)" ON projects."Projects" FOR SELECT TO anon, authenticated
    USING (projects.is_member("Id"));
```

**It is made once.** Only the context that maps `Project` writes the function, however many modules see
the class that declares it, and it writes it before the policies that call it. A later script replaces it
in place, so the policies of other modules that call it keep working, and drops the functions the context
no longer declares. Two access functions with one name are refused.

**Asked by a key.** The generator adds `Name` and an `Allows` that takes the aggregate's key, for a rule
that holds a project's id rather than the project: `ProjectMembership.Allows(task.ProjectId)` becomes
`projects.is_member("ProjectId")`. Only the database can answer that one; called in C#, it throws
`DatabaseOnlyException`. Where C# holds the project, `ProjectMembership.Allows(project, caller)` answers in
memory, because the members are loaded with the project.

**Other modules** see only the Projects module's contracts, which cannot hold the definition, since it
reads the module's own aggregate. So the contracts publish it by its key, and the definition takes its
name from there:

```csharp
// Projects.Contracts
[AccessFunctionContract<ProjectId>("projects.is_member")]
public static partial class ProjectMembers;

// Projects
[AccessFunction<Project>(ProjectMembers.Name)]
public static partial class ProjectMembership
{
    public static bool Allows(Project project, Caller caller)
        => project.Members.Any(member => member.UserId == caller.UserId);
}

// Tasks
[RowAccess<ProjectTask>(RowOperations.All)]
public static partial class ProjectMembersWorkOnItsTasks
{
    public static bool Allows(ProjectTask task, Caller caller) => ProjectMembers.Allows(task.ProjectId);
}
```

The function's name is written once, and the key is typed: a rule cannot pass a task's id where a
project's is asked. The Supabase build writes the access file of the module that owns a function before
the files of the modules that call it, and refuses a rule that asks a function no module defines.

`Sql.Call` stays for functions defined outside C#, in a migration of your own: an existing
`app.has_permission('project.update', "Id")`, say. The build does not look for those among the access
functions; they are yours.

`Any` is over the aggregate's own collections, with or without a condition; an entity's entities are not
reachable from it.

### Writing the policies

`PostgresRowAccess.Script(context, rules, accessFunctions)` returns the SQL for one context: it first drops
every policy an earlier script made on the context's tables, found by the comment each one carries, then
the access functions the context no longer declares, and then makes its access functions and the rules'
policies again. So the latest script says what the rules are now, a rule taken out disappears with it, and
a policy you wrote by hand is left alone.

```csharp
var sql = PostgresRowAccess.Script(context,
[
    RowAccessRule.For<Order>("A customer sees their orders", RowOperations.Read | RowOperations.Change, ACustomerSeesTheirOrders.RowAccessSql),
]);
```

Put the SQL it returns into a migration when the rules change, as text rather than as the call: a
migration renders nothing when it runs later, on a database that is only as far as that migration. That
is what the Supabase build does by itself, into `supabase/migrations`, from every rule of every module the
host references; see [Supabase](supabase.md#row-access-rules-in-the-build).

## Where to look next

- [Supabase](supabase.md#row-level-security-for-your-own-queries) for Supabase Auth's tokens, and the build
  that writes the policies.
- [Designing aggregates](aggregate-design.md), because a policy is about an aggregate, not a row.
- [Diagnostics](diagnostics.md#ddd00038) for the build errors about a rule.
