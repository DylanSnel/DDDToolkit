using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A Postgres set up the way a Supabase project is for row level security, and no further: the three
/// roles PostgREST switches between, <c>auth.uid()</c>, <c>auth.jwt()</c> and <c>auth.role()</c> as
/// Supabase defines them, and one table of notes that a policy keeps to their owner. The application
/// logs in as <c>shop_app</c>, which may do nothing but switch to those roles.
/// </summary>
public sealed class SupabaseRowLevelSecurityDatabase : IAsyncLifetime
{
    public const string LoginRole = "shop_app";

    public static readonly Guid Alice = Guid.Parse("a11ce000-0000-4000-8000-000000000001");

    public static readonly Guid Bob = Guid.Parse("b0b00000-0000-4000-8000-000000000002");

    private const string Setup = """
        CREATE ROLE anon NOLOGIN NOINHERIT;
        CREATE ROLE authenticated NOLOGIN NOINHERIT;
        CREATE ROLE service_role NOLOGIN NOINHERIT BYPASSRLS;
        CREATE ROLE shop_app LOGIN NOINHERIT PASSWORD 'shop_app';
        GRANT anon, authenticated, service_role TO shop_app;

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
        GRANT USAGE ON SCHEMA auth TO anon, authenticated, service_role;

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
        GRANT USAGE ON SCHEMA notes TO anon, authenticated, service_role;
        GRANT SELECT, INSERT, UPDATE, DELETE ON notes."Notes" TO anon, authenticated, service_role;
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
        await using var command = new NpgsqlCommand(Setup, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

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
        => new(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql(connectionString ?? ApplicationConnectionString)
            .AddInterceptors(new PostgresRowLevelSecurityInterceptor(callers, options ?? new PostgresRowLevelSecurityOptions()))
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

public class PrivateNote
{
    public Guid Id { get; set; }

    public string Text { get; set; } = "";

    public Guid? Owner { get; set; }
}
