using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A Postgres set up the way a Supabase project is for row level security, and no further: the three
/// roles PostgREST switches between, <c>auth.uid()</c>, <c>auth.jwt()</c>, <c>auth.role()</c> and
/// <c>auth.email()</c> as Supabase defines them, and one table of notes that a policy keeps to their owner. The application
/// logs in as <c>shop_app</c>, which may do nothing but switch to those roles and to the scoped system
/// role, <c>ddd_system_in</c>, which may read the notes but has no policy that lets it see one.
/// </summary>
public sealed class SupabaseRowLevelSecurityDatabase : IAsyncLifetime
{
    public const string LoginRole = "shop_app";

    public static readonly Guid Alice = Guid.Parse("a11ce000-0000-4000-8000-000000000001");

    public static readonly Guid Bob = Guid.Parse("b0b00000-0000-4000-8000-000000000002");

    /// <summary>The roles, which are the server's rather than a database's.</summary>
    private const string Roles = """
        CREATE ROLE anon NOLOGIN NOINHERIT;
        CREATE ROLE authenticated NOLOGIN NOINHERIT;
        CREATE ROLE service_role NOLOGIN NOINHERIT BYPASSRLS;
        CREATE ROLE ddd_system_in NOLOGIN NOINHERIT;
        CREATE ROLE shop_app LOGIN NOINHERIT PASSWORD 'shop_app';
        GRANT anon, authenticated, service_role, ddd_system_in TO shop_app;
        """;

    /// <summary>What each database has: Supabase's caller functions and the notes.</summary>
    private const string Setup = """
        CREATE SCHEMA auth;
        CREATE FUNCTION auth.uid() RETURNS uuid LANGUAGE sql STABLE AS $$
            SELECT coalesce(
                nullif(current_setting('request.jwt.claim.sub', true), ''),
                (nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'sub'))::uuid
        $$;
        CREATE FUNCTION auth.jwt() RETURNS jsonb LANGUAGE sql STABLE AS $$
            SELECT coalesce(
                nullif(current_setting('request.jwt.claim', true), ''),
                nullif(current_setting('request.jwt.claims', true), ''))::jsonb
        $$;
        CREATE FUNCTION auth.role() RETURNS text LANGUAGE sql STABLE AS $$
            SELECT coalesce(
                nullif(current_setting('request.jwt.claim.role', true), ''),
                (nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'role'))::text
        $$;
        CREATE FUNCTION auth.email() RETURNS text LANGUAGE sql STABLE AS $$
            SELECT coalesce(
                nullif(current_setting('request.jwt.claim.email', true), ''),
                (nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'email'))::text
        $$;
        GRANT USAGE ON SCHEMA auth TO anon, authenticated, service_role, ddd_system_in;

        CREATE SCHEMA notes;
        CREATE TABLE notes."Notes" (
            "Id" uuid PRIMARY KEY,
            "Text" text NOT NULL,
            "Owner" uuid DEFAULT auth.uid()
        );
        ALTER TABLE notes."Notes" ENABLE ROW LEVEL SECURITY;
        CREATE POLICY "A note is its owner's" ON notes."Notes" FOR ALL TO authenticated
            USING ("Owner" = (SELECT auth.uid()))
            WITH CHECK ("Owner" = (SELECT auth.uid()));
        GRANT USAGE ON SCHEMA notes TO anon, authenticated, service_role, ddd_system_in;
        GRANT SELECT, INSERT, UPDATE, DELETE ON notes."Notes" TO anon, authenticated, service_role, ddd_system_in;
        """;

    private PgmqDatabase? _database;

    /// <summary>Whether the container started; a test skips or fails through <see cref="Require"/> when it did not.</summary>
    public bool Available => _database is not null;

    /// <summary>The superuser's connection string: the tables' owner, which row level security does not apply to.</summary>
    public string OwnerConnectionString => Database.ConnectionString;

    /// <summary>The application's connection string, as <see cref="LoginRole"/>.</summary>
    public string ApplicationConnectionString => new NpgsqlConnectionStringBuilder(Database.ConnectionString)
    {
        Username = LoginRole,
        Password = LoginRole,
    }.ConnectionString;

    private PgmqDatabase Database => _database ?? throw new InvalidOperationException("The database did not start.");

    public async ValueTask InitializeAsync()
    {
        _database = await PgmqDatabase.StartAsync(TestContext.Current.CancellationToken);
        if (_database is null)
        {
            return;
        }

        await using var connection = await _database.OpenAsync(TestContext.Current.CancellationToken);
        await SetUpAsync(connection, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Makes a fresh server what this fixture is: the roles, Supabase's caller functions and the notes, over
    /// <paramref name="owner"/>, a superuser's connection to the database the tests use. For a fixture that
    /// starts its own container, behind a pooler for one.
    /// </summary>
    public static async Task SetUpAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Roles + Setup, owner);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Another database on the same server, set up as the first one is, as the owner's connection string: for
    /// a test that changes something about a database as a whole.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(string name)
    {
        await using (var connection = await Database.OpenAsync(TestContext.Current.CancellationToken))
        {
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(Database.ConnectionString) { Database = name }.ConnectionString;
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var setup = new NpgsqlCommand(Setup, connection);
            await setup.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        return connectionString;
    }

    /// <summary><paramref name="connectionString"/>, an owner's, as <see cref="LoginRole"/> instead.</summary>
    public static string AsApplication(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString) { Username = LoginRole, Password = LoginRole }.ConnectionString;

    /// <summary>Skips the calling test without Docker, or fails it where containers are required.</summary>
    public void Require()
        => RequiredContainers.EnforceOrSkip(
            Available,
            RequiredContainers.Required,
            "PostgreSQL",
            $"No Docker here, so '{PgmqDatabase.Image}' could not be started. Row level security for Supabase is not covered on this machine.");

    /// <summary>Removes every note, as the owner.</summary>
    public async Task ClearAsync()
    {
        await using var connection = await Database.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""TRUNCATE notes."Notes" """, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A context on <paramref name="connectionString"/>, the application's by default, running as whoever <paramref name="callers"/> names.</summary>
    public NotesContext CreateContext(ICallerAccessor callers, PostgresRowLevelSecurityOptions? options = null, string? connectionString = null)
        => CreateContext(new PostgresRowLevelSecurityInterceptor(callers, options ?? new PostgresRowLevelSecurityOptions()), connectionString);

    /// <summary>A context on <paramref name="connectionString"/>, the application's by default, with <paramref name="interceptor"/>.</summary>
    public NotesContext CreateContext(PostgresRowLevelSecurityInterceptor interceptor, string? connectionString = null)
        => new(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql(connectionString ?? ApplicationConnectionString)
            .AddInterceptors(interceptor)
            .Options);

    /// <summary>The claims Supabase Auth would sign for <paramref name="user"/>.</summary>
    public static string ClaimsOf(Guid user, string email = "someone@example.com")
        => $$$"""{"sub":"{{{user}}}","role":"authenticated","aud":"authenticated","email":"{{{email}}}","app_metadata":{"provider":"email","teams":["north"]}}""";

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }
}

/// <summary>Whoever the test says is calling.</summary>
public sealed class CallerOfTheTest : ICallerAccessor
{
    public Caller Current { get; set; } = Caller.System;
}

public class NotesContext(DbContextOptions<NotesContext> options) : DbContext(options)
{
    public DbSet<PrivateNote> Notes => Set<PrivateNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notes");

        // Owner is the database's to fill in, from the caller's token, when the application leaves it out.
        modelBuilder.Entity<PrivateNote>().Property(note => note.Owner).HasDefaultValueSql("auth.uid()");
    }
}

/// <summary>The same notes, with a note's text as its concurrency token: a save that finds another text than it read changes nothing.</summary>
public class GuardedNotesContext(DbContextOptions<GuardedNotesContext> options) : DbContext(options)
{
    public DbSet<PrivateNote> Notes => Set<PrivateNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notes");
        modelBuilder.Entity<PrivateNote>(note =>
        {
            note.Property(each => each.Owner).HasDefaultValueSql("auth.uid()");
            note.Property(each => each.Text).IsConcurrencyToken();
        });
    }
}

public class PrivateNote
{
    public Guid Id { get; set; }

    public string Text { get; set; } = "";

    public Guid? Owner { get; set; }
}
