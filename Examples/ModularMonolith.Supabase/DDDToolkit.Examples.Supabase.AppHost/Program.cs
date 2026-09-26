using Microsoft.Extensions.Configuration;

// The modular monolith on Supabase's database. Two ways to give it one:
//
// - Nothing configured: a Postgres container, seeded the way Supabase seeds a project. Postgres runs every
//   .sql file in its init directory once, in name order, on a fresh database, and supabase/migrations is
//   exactly such a directory, named by timestamp. So the container starts with every module's schema,
//   applied from the very files `supabase db push` and branching apply, and the host's start-up check
//   finds nothing missing.
//
// - ConnectionStrings:Supabase set on this AppHost (dotnet user-secrets set ConnectionStrings:Supabase
//   "..."): a real Supabase project, or one of its branches. Nothing is started; the host is pointed at
//   it, and the migrations are whatever Supabase applied there.
//
// And two ways for the modules to talk, chosen with Messaging: in process by default, or through Supabase
// Queues with Messaging=pgmq (the "pgmq" launch profile, or `dotnet run -- --Messaging=pgmq`).
//
// Who is asking decides what the modules' queries see: the host runs each request's queries as the user
// whose Supabase Auth token it carries, and the policies in supabase/migrations do the rest. Against the
// container, tokens are signed the way the Supabase CLI's local stack signs them, with its well-known JWT
// secret. Against a project, set Supabase:Url on this AppHost as well, and the host checks tokens against
// the keys the project publishes.

var builder = DistributedApplication.CreateBuilder(args);

var monolith = builder.AddProject<Projects.DDDToolkit_Examples_Host>("monolith")
    .WithHttpHealthCheck("/health");

if (builder.Configuration["Messaging"] is { Length: > 0 } messaging)
{
    monolith.WithEnvironment("Messaging", messaging);
}

if (builder.Configuration.GetConnectionString("Supabase") is { Length: > 0 })
{
    monolith.WithReference(builder.AddConnectionString("Supabase"));

    if (builder.Configuration["Supabase:Url"] is { Length: > 0 } project)
    {
        monolith.WithEnvironment("Supabase__Url", project);
    }
}
else
{
    var migrations = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "supabase", "migrations"));

    // Postgres 17 with pgmq 1.5.1: the versions a Supabase project has, so that the migration turning
    // Queues on runs here as it runs there, and the queues behave as Supabase's do. Before the migrations,
    // database/ gives it what every Supabase project starts with: PostgREST's roles and the auth functions.
    var postgres = builder.AddPostgres("postgres")
        .WithImage("pgmq/pg17-pgmq", "v1.5.1")
        .WithImageRegistry("ghcr.io")
        .WithInitFiles(Path.Combine(builder.AppHostDirectory, "database"))
        .WithInitFiles(migrations);

    // Supabase's database is called postgres, and the migrations were written for it.
    var database = postgres.AddDatabase("supabase", databaseName: "postgres");

    monolith
        .WithReference(database, connectionName: "Supabase")
        .WaitFor(database)
        // The issuer and secret of `supabase start`, so a token from a local stack works here as well.
        .WithEnvironment("Supabase__Url", "http://127.0.0.1:54321")
        .WithEnvironment("Supabase__JwtSecret", "super-secret-jwt-token-with-at-least-32-characters-long");
}

builder.Build().Run();
