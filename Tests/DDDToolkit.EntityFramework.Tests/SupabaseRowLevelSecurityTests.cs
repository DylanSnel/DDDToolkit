using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseRowLevelSecurityDatabase;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Row level security applied to a context's own queries, against a real Postgres set up with Supabase's
/// roles and auth functions. The policy under test is the usual one: a note is its owner's.
/// </summary>
public sealed class SupabaseRowLevelSecurityTests(SupabaseRowLevelSecurityDatabase database)
    : IClassFixture<SupabaseRowLevelSecurityDatabase>, IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly CallerOfTheTest _caller = new();

    public async ValueTask InitializeAsync()
    {
        if (database.Available)
        {
            await database.ClearAsync();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_user_sees_only_their_own_rows_and_the_database_fills_in_whose_they_are()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");
        await WriteAsAsync(Bob, "Bob's");

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = database.CreateContext(_caller);
        var notes = await context.Notes.ToListAsync(Cancellation);

        notes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Text = "Alice's", Owner = (Guid?)Alice });
    }

    [Fact]
    public async Task A_user_cannot_write_a_row_for_somebody_else()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = database.CreateContext(_caller);
        context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Bob's, says Alice", Owner = Bob });

        var save = () => context.SaveChangesAsync(Cancellation);

        (await save.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, "the policy's WITH CHECK refuses the row");
    }

    [Fact]
    public async Task A_caller_without_a_user_sees_nothing_and_postgres_knows_them_as_anon()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");

        _caller.Current = Caller.Anonymous;
        await using var context = database.CreateContext(_caller);

        (await context.Notes.CountAsync(Cancellation)).Should().Be(0, "the policy only lets authenticated users in");
        (await ScalarAsync(context, "SELECT current_user")).Should().Be("anon");
        (await ScalarAsync(context, "SELECT auth.role()")).Should().Be("anon", "PostgREST gives a request without a user the claims {\"role\":\"anon\"}");
        (await ScalarAsync(context, "SELECT coalesce(auth.uid()::text, 'none')")).Should().Be("none");
    }

    [Fact]
    public async Task The_claims_reach_postgres_exactly_as_they_were_signed()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice, email: "alice@example.com"));
        await using var context = database.CreateContext(_caller);

        (await ScalarAsync(context, "SELECT auth.uid()::text")).Should().Be(Alice.ToString());
        (await ScalarAsync(context, "SELECT auth.jwt() ->> 'email'")).Should().Be("alice@example.com");
        (await ScalarAsync(context, "SELECT auth.jwt() -> 'app_metadata' -> 'teams' ->> 0")).Should().Be("north", "nested claims stay nested");
        (await ScalarAsync(context, "SELECT current_user")).Should().Be("authenticated");
    }

    [Fact]
    public async Task Background_work_runs_as_the_system_role_when_one_is_set()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");
        await WriteAsAsync(Bob, "Bob's");

        _caller.Current = Caller.System;
        await using var context = database.CreateContext(_caller, new PostgresRowLevelSecurityOptions { SystemRole = SupabaseRowLevelSecurity.ServiceRole });

        (await context.Notes.CountAsync(Cancellation)).Should().Be(2, "service_role bypasses row level security, as a secret key does");
        (await ScalarAsync(context, "SELECT current_user")).Should().Be("service_role");
    }

    [Fact]
    public async Task Background_work_without_a_system_role_runs_as_the_login_role()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");
        _caller.Current = Caller.System;

        // Logged in as a role that may only switch roles, that fails closed.
        await using (var application = database.CreateContext(_caller))
        {
            (await ScalarAsync(application, "SELECT current_user")).Should().Be(LoginRole);

            var read = () => application.Notes.CountAsync(Cancellation);
            (await read.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        // Logged in as the owner, as an application on Supabase that logs in as postgres is, it sees everything.
        await using (var owner = database.CreateContext(_caller, connectionString: database.OwnerConnectionString))
        {
            (await owner.Notes.CountAsync(Cancellation)).Should().Be(1);
        }
    }

    [Fact]
    public async Task A_pooled_connection_forgets_the_last_caller_before_anyone_else_uses_it()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");
        await WriteAsAsync(Bob, "Bob's");

        // One connection in the pool, so every open below gets the same one.
        await using var pool = new NpgsqlDataSourceBuilder(
            new NpgsqlConnectionStringBuilder(database.ApplicationConnectionString) { MaxPoolSize = 1 }.ConnectionString).Build();

        await using var context = new NotesContext(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql(pool)
            .AddInterceptors(new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions()))
            .Options);

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        (await context.Notes.Select(note => note.Text).ToListAsync(Cancellation)).Should().Equal("Alice's");
        var backend = await ScalarAsync(context, "SELECT pg_backend_pid()::text");

        _caller.Current = Callers.FromClaims(ClaimsOf(Bob));
        (await context.Notes.Select(note => note.Text).ToListAsync(Cancellation)).Should().Equal("Bob's");

        // Code that does not go through the interceptor gets the very same server connection, and none of it.
        await using var plain = await pool.OpenConnectionAsync(Cancellation);
        await using var command = new NpgsqlCommand("SELECT pg_backend_pid()::text || '|' || current_user || '|' || coalesce(current_setting('request.jwt.claims', true), '')", plain);
        (await command.ExecuteScalarAsync(Cancellation)).Should().Be($"{backend}|{LoginRole}|");
    }

    [Theory]
    [InlineData("Host=db.example.com;Database=postgres;No Reset On Close=true", "No Reset On Close")]
    [InlineData("Host=db.example.com;Database=postgres;Multiplexing=true", "Multiplexing")]
    [InlineData("Host=aws-0-eu-west-1.pooler.supabase.com;Port=6543;Database=postgres", "6543")]
    [InlineData("Host=aws-0-eu-west-1.pooler.supabase.com:6543;Database=postgres", "6543")]
    public async Task Connection_strings_that_would_hand_a_callers_role_to_someone_else_are_refused_before_connecting(string connectionString, string named)
    {
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = new NotesContext(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions()))
            .Options);

        var open = () => context.Database.OpenConnectionAsync(Cancellation);

        (await open.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"*{named}*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[\"a\"]")]
    public void Claims_are_a_json_object(string claims)
    {
        var act = () => Callers.FromClaims(claims);

        act.Should().Throw<ArgumentException>();
    }

    private async Task WriteAsAsync(Guid user, string text)
    {
        var caller = new CallerOfTheTest { Current = Callers.FromClaims(ClaimsOf(user)) };
        await using var context = database.CreateContext(caller);
        context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = text });
        await context.SaveChangesAsync(Cancellation);
    }

    /// <summary>One value, from SQL the tests themselves write; nothing here comes from outside.</summary>
    private static async Task<string?> ScalarAsync(DbContext context, string sql)
    {
#pragma warning disable EF1003
        return await context.Database.SqlQueryRaw<string>(sql + " AS \"Value\"").SingleAsync(Cancellation);
#pragma warning restore EF1003
    }
}
