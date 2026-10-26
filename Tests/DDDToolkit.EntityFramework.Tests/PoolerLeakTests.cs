using System.Collections.Concurrent;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseRowLevelSecurityDatabase;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Settings that last one transaction, through a real transaction pooler: PgBouncer with one server connection
/// for the application's login role, so every client of these tests is handed the same backend, one
/// transaction at a time. What one client leaves on that backend's session the next one reads, which is what
/// these tests look for and what the two controls show to be real.
/// </summary>
[Collection(SettingsPerTransaction.Name)]
public sealed class PoolerLeakTests(PoolerFixture database) : IAsyncLifetime
{
    /// <summary>Who a statement runs as and what it sees, in one row: the backend, the role, the user, the team and every visible note.</summary>
    private const string Who =
        "SELECT pg_catalog.pg_backend_pid()::text || '|' || current_user || '|' || coalesce(auth.uid()::text, 'none') || '|' || " +
        $"coalesce(pg_catalog.current_setting('{TeamOfTheCaller.Setting}', true), '') || '|' || " +
        """coalesce((SELECT string_agg("Text", ',' ORDER BY "Text") FROM notes."Notes"), '') AS "Value" """;

    private readonly CallerOfTheTest _caller = new();
    private readonly TenantSetting _tenant = new() { Current = "north" };
    private readonly string _pool = "pooler-" + Guid.NewGuid().ToString("N")[..12];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Through the pooler, with client connections of this test alone.</summary>
    private string Connection => database.Pooled(_pool);

    /// <summary>Through the pooler as well, from a client that shares nothing with <see cref="Connection"/> but the pooler's one server connection.</summary>
    private string PlainConnection => database.Pooled(_pool + "-plain");

    public async ValueTask InitializeAsync()
    {
        if (database.Available)
        {
            await database.ClearAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (database.Available)
        {
            foreach (var connectionString in new[] { Connection, PlainConnection })
            {
                using var connection = new NpgsqlConnection(connectionString);
                NpgsqlConnection.ClearPool(connection);
            }
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Two_callers_interleaved_through_one_server_connection_each_see_only_their_own()
    {
        database.Require();
        await database.WriteAsync(Alice, "Alice's");
        await database.WriteAsync(Bob, "Bob's");

        const int Flows = 16;
        const int Rounds = 25;
        var interceptor = PoolerFixture.Interceptor(new AmbientCallerAccessor(), RowLevelSecurityScope.Transaction, [new TeamOfTheCaller()]);
        var backends = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        var wrong = new ConcurrentBag<string>();

        await Task.WhenAll(Enumerable.Range(0, Flows).Select(flow => Task.Run(
            async () =>
            {
                for (var round = 0; round < Rounds; round++)
                {
                    // Alice and Bob in turn, each flow starting with the other one.
                    var (id, name, team) = (flow + round) % 2 == 0 ? (Alice, "Alice", "amber") : (Bob, "Bob", "teal");

                    using (Callers.Begin(PooledNotesHost.User(id, team)))
                    {
                        await using var context = PoolerFixture.Context(Connection, interceptor);

                        // One request in five saves, in a transaction of its own, so saves and queries interleave.
                        if (round % 5 == 4)
                        {
                            context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = $"{name}'s {flow}.{round}" });
                            await context.SaveChangesAsync(Cancellation);
                        }

                        // Read inside the statement it is about: afterwards the backend is somebody else's.
                        var answer = (await context.Database.SqlQueryRaw<string>(Who).SingleAsync(Cancellation)).Split('|');
                        backends.TryAdd(answer[0], true);

                        var notes = answer[4].Split(',', StringSplitOptions.RemoveEmptyEntries);
                        if (answer[1] != "authenticated" || answer[2] != id.ToString() || answer[3] != team
                            || notes.Length == 0 || notes.Any(note => !note.StartsWith(name + "'s", StringComparison.Ordinal)))
                        {
                            wrong.Add($"{name} got {string.Join('|', answer)}");
                        }
                    }
                }
            },
            Cancellation)));

        wrong.Should().BeEmpty($"each of the {Flows * Rounds} requests ran as its own caller and saw only that caller's notes");
        backends.Should().ContainSingle("the pooler handed every transaction the same server connection");
    }

    [Theory]
    [InlineData("a query")]
    [InlineData("a save")]
    [InlineData("a user transaction")]
    [InlineData("a failed statement")]
    [InlineData("a save the policy refuses")]
    [InlineData("a cancelled command")]
    [InlineData("a rolled-back transaction")]
    public async Task A_plain_client_between_them_sees_the_defaults(string after)
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();

        // The backend the caller's transactions run on, read inside one of them.
        var state = await BackendState.OfAsync(context, Cancellation);
        state.Should().Be(new BackendState(state.Backend, "authenticated", LoginRole, ClaimsOf(Alice), "", "north"));

        switch (after)
        {
            case "a query":
                (await context.Notes.CountAsync(Cancellation)).Should().Be(0);
                break;

            case "a save":
                context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Alice's" });
                (await context.SaveChangesAsync(Cancellation)).Should().Be(1);
                break;

            case "a user transaction":
                await using (var transaction = await context.Database.BeginTransactionAsync(Cancellation))
                {
                    (await BackendState.OfAsync(context, Cancellation)).Should().Be(state);
                    context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Alice's" });
                    await context.SaveChangesAsync(Cancellation);
                    await transaction.CommitAsync(Cancellation);
                }

                break;

            case "a failed statement":
                var divide = () => context.Database.SqlQueryRaw<string>("""SELECT (1 / (SELECT 0))::text AS "Value" """).SingleAsync(Cancellation);
                (await divide.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.DivisionByZero);
                break;

            case "a save the policy refuses":
                context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Bob's, says Alice", Owner = Bob });
                var save = () => context.SaveChangesAsync(Cancellation);
                await save.Should().ThrowAsync<DbUpdateException>();
                break;

            case "a cancelled command":
                using (var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Cancellation))
                {
                    cancelled.CancelAfter(TimeSpan.FromMilliseconds(300));
                    var sleep = () => context.Database.SqlQueryRaw<string>("""SELECT pg_sleep(30)::text AS "Value" """).SingleAsync(cancelled.Token);
                    await sleep.Should().ThrowAsync<OperationCanceledException>();
                }

                break;

            case "a rolled-back transaction":
                await using (var transaction = await context.Database.BeginTransactionAsync(Cancellation))
                {
                    context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Never kept" });
                    await context.SaveChangesAsync(Cancellation);
                    await transaction.RollbackAsync(Cancellation);
                }

                break;

            default:
                throw new InvalidOperationException($"This test has no case '{after}'.");
        }

        var plain = await BackendState.OfAPlainClientAsync(PlainConnection, Cancellation);

        // Without this the test proves nothing: a pooler that handed out another backend would show defaults
        // whatever the first one still carried.
        plain.Backend.Should().Be(state.Backend, $"after {after} the plain client has to be on the backend the caller ran on, or its answer says nothing about what the caller left");
        plain.Should().Be(BackendState.Clean(state.Backend), $"after {after} nothing of the caller is on the session");
    }

    [Fact]
    public async Task A_plain_client_after_a_dropped_client_sees_the_defaults_on_a_fresh_backend()
    {
        database.Require();

        // A client whose network goes away in the middle of a transaction, through a relay this test cuts.
        await using var relay = new CuttableRelay(database.PoolerHost, database.PoolerPort);
        var dropping = new NpgsqlConnectionStringBuilder(database.Pooled(_pool + "-dropped", relay.Port)) { Pooling = false }.ConnectionString;

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        var context = PoolerFixture.Context(dropping, PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Transaction, [_tenant]));
        BackendState state;
        try
        {
            await context.Database.BeginTransactionAsync(Cancellation);
            state = await BackendState.OfAsync(context, Cancellation);
            state.Should().Be(new BackendState(state.Backend, "authenticated", LoginRole, ClaimsOf(Alice), "", "north"));
            context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Never committed" });
            await context.SaveChangesAsync(Cancellation);

            relay.Cut();
        }
        finally
        {
            try
            {
                await context.DisposeAsync();
            }
            catch (Exception exception) when (exception is NpgsqlException or IOException or InvalidOperationException)
            {
                // The connection is gone, which is the point; there is nothing left to roll back over it.
            }
        }

        var plain = await BackendState.OfAPlainClientAsync(PlainConnection, Cancellation);

        // A pooler does not hand on a server connection whose client vanished inside a transaction: it closes it.
        // So here the plain client is on another backend, and what is checked is that the old one is gone.
        plain.Should().Be(BackendState.Clean(plain.Backend), "nothing of the caller is on the session the next client gets");
        plain.Backend.Should().NotBe(state.Backend, "the pooler closed the server connection the dropped client's transaction was open on");
        (await database.AsOwnerAsync($"SELECT count(*) FROM pg_catalog.pg_stat_activity WHERE pid = {state.Backend}")).Should().Be("0", "and Postgres rolled its transaction back with it");
        (await database.AsOwnerAsync("""SELECT count(*) FROM notes."Notes" """)).Should().Be("0");
    }

    [Fact]
    public async Task A_dirty_backend_cannot_change_who_a_transaction_runs_as()
    {
        database.Require();
        await database.WriteAsync(Alice, "Alice's");
        await database.WriteAsync(Bob, "Bob's");

        try
        {
            // What a client that sets things for its session leaves on the pooler's one server connection: Bob,
            // everywhere a policy or Supabase's functions could look.
            int backend;
            await using (var dirty = new NpgsqlConnection(PlainConnection))
            {
                await dirty.OpenAsync(Cancellation);
                await using var leave = new NpgsqlCommand(
                    "SELECT pg_catalog.pg_backend_pid() FROM (SELECT set_config('role', 'authenticated', false), set_config('request.jwt.claims', $1, false), " +
                    $"set_config('request.jwt.claim.sub', $2, false), set_config('{TenantSetting.Tenant}', 'left behind', false), set_config('{TeamOfTheCaller.Setting}', 'left behind', false)) set",
                    dirty);
                leave.Parameters.Add(new NpgsqlParameter { Value = ClaimsOf(Bob) });
                leave.Parameters.Add(new NpgsqlParameter { Value = Bob.ToString() });
                backend = (int)(await leave.ExecuteScalarAsync(Cancellation))!;
            }

            (await BackendState.OfAPlainClientAsync(PlainConnection, Cancellation)).Should().Be(
                new BackendState(backend, "authenticated", LoginRole, ClaimsOf(Bob), Bob.ToString(), "left behind"),
                "the control: the session is as the last client left it");

            var team = new TeamOfTheCaller();
            await using var context = PoolerFixture.Context(Connection, PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Transaction, [_tenant, team]));

            _caller.Current = PooledNotesHost.User(Alice, "amber");
            (await WhoAsync(context)).Should().Be($"{backend}|authenticated|{Alice}|amber|Alice's", "Alice's transaction runs as Alice, whatever the session carries");
            (await BackendState.OfAsync(context, Cancellation)).Should().Be(
                new BackendState(backend, "authenticated", LoginRole, _caller.Current.Claims!, "", "north"), "the setting Supabase's auth.uid() reads first is emptied, not Bob's");

            // A caller without a team or a tenant: both settings are emptied for the transaction, not left as they were.
            _caller.Current = Callers.FromClaims(ClaimsOf(Bob));
            _tenant.Current = null;
            (await WhoAsync(context)).Should().Be($"{backend}|authenticated|{Bob}||Bob's");
            (await BackendState.OfAsync(context, Cancellation)).Tenant.Should().Be("");

            _caller.Current = Caller.Anonymous;
            (await WhoAsync(context)).Should().Be($"{backend}|anon|none||", "nobody is signed in, though the session says Bob");

            // The system runs as the login role, which sees no table here: the role the session carries is not its.
            _caller.Current = Caller.System;
            var state = await BackendState.OfAsync(context, Cancellation);
            state.Should().Be(new BackendState(backend, LoginRole, LoginRole, "", "", ""));
        }
        finally
        {
            await ResetTheBackendAsync();
        }
    }

    [Fact]
    public async Task Session_settings_would_leak_through_the_pooler()
    {
        database.Require();

        try
        {
            // The control: what the scope is for. One client sets a setting for its session and goes away.
            int backend;
            await using (var first = new NpgsqlConnection(Connection))
            {
                await first.OpenAsync(Cancellation);
                await using var set = new NpgsqlCommand($"SELECT pg_catalog.pg_backend_pid() FROM pg_catalog.set_config('{TenantSetting.Tenant}', 'left behind', false)", first);
                backend = (int)(await set.ExecuteScalarAsync(Cancellation))!;
            }

            // Another client, with a connection to the pooler of its own, is handed the same backend as it was left.
            (await BackendState.OfAPlainClientAsync(PlainConnection, Cancellation)).Should().Be(
                BackendState.Clean(backend) with { Tenant = "left behind" },
                "a pooler in transaction mode resets nothing between two clients");
        }
        finally
        {
            await ResetTheBackendAsync();
        }
    }

    [Fact]
    public async Task Saves_through_the_pooler_keep_their_row_counts()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var first = Guarded();
        await using var second = Guarded();

        var id = Guid.CreateVersion7();
        first.Notes.Add(new PrivateNote { Id = id, Text = "One" });
        (await first.SaveChangesAsync(Cancellation)).Should().Be(1);

        first.Notes.AddRange(Enumerable.Range(0, 3).Select(index => new PrivateNote { Id = Guid.CreateVersion7(), Text = $"More {index}" }));
        (await first.SaveChangesAsync(Cancellation)).Should().Be(3);

        var mine = await first.Notes.SingleAsync(note => note.Id == id, Cancellation);
        var theirs = await second.Notes.SingleAsync(note => note.Id == id, Cancellation);
        mine.Text = "Mine";
        (await first.SaveChangesAsync(Cancellation)).Should().Be(1);

        theirs.Text = "Theirs";
        var stale = () => second.SaveChangesAsync(Cancellation);
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();

        first.Notes.Remove(mine);
        (await first.SaveChangesAsync(Cancellation)).Should().Be(1);
        (await first.Notes.ExecuteDeleteAsync(Cancellation)).Should().Be(3);

        var state = await BackendState.OfAsync(first, Cancellation);
        (await BackendState.OfAPlainClientAsync(PlainConnection, Cancellation)).Should().Be(BackendState.Clean(state.Backend));
    }

    [Fact]
    public async Task The_timeout_never_reaches_the_next_client_through_the_pooler()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = PoolerFixture.Context(
            Connection,
            PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Transaction, [_tenant], configure: options => options.StatementTimeouts[CallerKind.User] = TimeSpan.FromMilliseconds(200)));

        var backend = (await BackendState.OfAsync(context, Cancellation)).Backend;
        (await context.Database.SqlQueryRaw<string>("""SELECT pg_catalog.current_setting('statement_timeout') AS "Value" """).SingleAsync(Cancellation))
            .Should().Be("200ms", "the user's statements run under the user's timeout");

        var slow = () => context.Database.SqlQueryRaw<string>("""SELECT pg_sleep(2)::text AS "Value" """).SingleAsync(Cancellation);
        (await slow.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);

        // The next client on the same backend has the login role's own timeout, and may take its time.
        await using var plain = new NpgsqlConnection(PlainConnection);
        await plain.OpenAsync(Cancellation);
        await using var read = new NpgsqlCommand("SELECT pg_catalog.pg_backend_pid()::text || '|' || pg_catalog.current_setting('statement_timeout') FROM pg_sleep(0.5)", plain);
        (await read.ExecuteScalarAsync(Cancellation)).Should().Be($"{backend}|0");
    }

    private NotesContext Context()
        => PoolerFixture.Context(Connection, PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Transaction, [_tenant]));

    private GuardedNotesContext Guarded()
        => new(new DbContextOptionsBuilder<GuardedNotesContext>()
            .UseNpgsql(Connection)
            .AddInterceptors(PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Transaction, [_tenant]))
            .Options);

    private static Task<string> WhoAsync(NotesContext context)
        => context.Database.SqlQueryRaw<string>(Who).SingleAsync(Cancellation);

    /// <summary>
    /// Takes everything off the pooler's one server connection again, and proves it: what a control left there
    /// on purpose must not be what the next test finds.
    /// </summary>
    private async Task ResetTheBackendAsync()
    {
        await using (var connection = new NpgsqlConnection(PlainConnection))
        {
            await connection.OpenAsync(Cancellation);
            await using var reset = new NpgsqlCommand("DISCARD ALL", connection);
            await reset.ExecuteNonQueryAsync(Cancellation);
        }

        var state = await BackendState.OfAPlainClientAsync(PlainConnection, Cancellation);
        state.Should().Be(BackendState.Clean(state.Backend), "the backend is clean again for the tests that follow");
    }
}
