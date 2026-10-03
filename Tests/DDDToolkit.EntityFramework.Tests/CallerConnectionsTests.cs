using System.Data.Common;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseRowLevelSecurityDatabase;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Connections for SQL sent outside Entity Framework, which carry the caller as a context's connections do:
/// set with the interceptor's own statement, refused by its own rules, and known to a context that is handed
/// one.
/// </summary>
[Collection(SettingsPerTransaction.Name)]
public sealed class CallerConnectionsTests(PoolerFixture database) : IAsyncLifetime
{
    private const string SetsTheRole = "set_config('role'";

    /// <summary>Who a statement runs as and what it sees: the role, the user, the tenant, the statement timeout and every visible note.</summary>
    private const string Who =
        "SELECT current_user || '|' || coalesce(auth.uid()::text, 'none') || '|' || " +
        $"coalesce(pg_catalog.current_setting('{TenantSetting.Tenant}', true), '') || '|' || pg_catalog.current_setting('statement_timeout') || '|' || " +
        """coalesce((SELECT string_agg("Text", ',' ORDER BY "Text") FROM notes."Notes"), '')""";

    private readonly CallerOfTheTest _caller = new();
    private readonly TenantSetting _tenant = new() { Current = "north" };
    private readonly SentStatements _sent = new();
    private readonly string _pool = "caller-" + Guid.NewGuid().ToString("N")[..12];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (database.Available)
        {
            await database.ClearAsync();
            await database.WriteAsync(Alice, "Alice's");
            await database.WriteAsync(Bob, "Bob's");
        }
    }

    public ValueTask DisposeAsync()
    {
        _sent.Dispose();

        if (database.Available)
        {
            // The plain client's connection to the pooler, which no data source of this test owns.
            using var plain = new NpgsqlConnection(database.Pooled(_pool + "-plain"));
            NpgsqlConnection.ClearPool(plain);
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Sql_on_a_caller_connection_runs_as_the_caller_with_its_settings()
    {
        database.Require();
        await using var dataSource = Direct(RowLevelSecurityScope.Connection);
        var connections = new CallerConnections(dataSource, Interceptor(RowLevelSecurityScope.Connection));

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        int backend;
        await using (var connection = await connections.OpenAsync(Cancellation))
        {
            backend = ((NpgsqlConnection)connection).ProcessID;
            (await ScalarAsync(connection, Who)).Should().Be($"authenticated|{Alice}|north|300ms|Alice's", "raw SQL on it is held to the caller's rows, settings and timeout");
            _sent.Count(SetsTheRole).Should().Be(1, "one statement, as a context sends when it opens a connection");
        }

        // Another caller, another connection; the pool's one backend, which kept nothing of the first.
        _caller.Current = Caller.Anonymous;
        _tenant.Current = null;
        await using (var connection = await connections.OpenAsync(Cancellation))
        {
            ((NpgsqlConnection)connection).ProcessID.Should().Be(backend);
            (await ScalarAsync(connection, Who)).Should().Be("anon|none||0|");
        }

        await using var plain = await dataSource.OpenConnectionAsync(Cancellation);
        (await BackendState.OnAsync(plain, Cancellation)).Should().Be(BackendState.Clean(backend), "the pool's reset takes the caller off when the connection is disposed");
    }

    [Theory]
    [InlineData(RowLevelSecurityScope.Connection)]
    [InlineData(RowLevelSecurityScope.Transaction)]
    public async Task A_caller_transaction_sets_the_caller_locally_and_leaves_the_backend_clean(RowLevelSecurityScope scope)
    {
        database.Require();

        // Through the pooler where the settings last a transaction, whose one server connection every client shares.
        var perTransaction = scope == RowLevelSecurityScope.Transaction;
        await using var dataSource = perTransaction ? _sent.DataSource(database.Pooled(_pool)) : Direct(scope);
        var connections = new CallerConnections(dataSource, Interceptor(scope));

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        BackendState state;
        await using (var transaction = await connections.BeginTransactionAsync(Cancellation))
        await using (var connection = transaction.Connection!)
        {
            state = await BackendState.OnAsync(connection, Cancellation, transaction);
            state.Should().Be(new BackendState(state.Backend, "authenticated", LoginRole, ClaimsOf(Alice), "", "north"));
            (await ScalarAsync(connection, Who, transaction)).Should().Be($"authenticated|{Alice}|north|300ms|Alice's");

            await using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = """INSERT INTO notes."Notes" ("Id", "Text") VALUES (gen_random_uuid(), 'Alice''s too')""";
            (await write.ExecuteNonQueryAsync(Cancellation)).Should().Be(1);

            await transaction.CommitAsync(Cancellation);

            if (!perTransaction)
            {
                (await ScalarAsync(connection, "SELECT current_user::text")).Should().Be(LoginRole, "the caller was set for the transaction alone, in either scope");
            }
        }

        // The next client on the same backend finds nothing: a plain one through the pooler, or the pool's next renter.
        if (perTransaction)
        {
            (await BackendState.OfAPlainClientAsync(database.Pooled(_pool + "-plain"), Cancellation)).Should().Be(BackendState.Clean(state.Backend));
        }
        else
        {
            await using var plain = await dataSource.OpenConnectionAsync(Cancellation);
            (await BackendState.OnAsync(plain, Cancellation)).Should().Be(BackendState.Clean(state.Backend));
        }

        (await database.AsOwnerAsync("""SELECT string_agg("Owner"::text, ',') FROM notes."Notes" WHERE "Text" = 'Alice''s too'""")).Should().Be(Alice.ToString(), "the insert ran as Alice, whose id the database filled in");
    }

    [Fact]
    public async Task OpenAsync_is_refused_in_transaction_scope()
    {
        database.Require();
        await using var dataSource = Direct(RowLevelSecurityScope.Transaction);
        var connections = new CallerConnections(dataSource, Interceptor(RowLevelSecurityScope.Transaction));
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));

        var open = async () => await connections.OpenAsync(Cancellation);

        (await open.Should().ThrowAsync<InvalidOperationException>("nothing may live on a session where a pooler hands it to the next client"))
            .WithMessage("The caller's settings last one transaction here (RowLevelSecurityScope.Transaction), so a connection cannot carry a caller for its session*Use BeginTransactionAsync, which sets the caller for one transaction.");
        _sent.Sent.Should().BeEmpty("nothing was opened");
    }

    [Fact]
    public async Task A_data_source_with_no_reset_on_close_is_refused_in_connection_scope()
    {
        database.Require();
        await using var dataSource = _sent.DataSource(database.Direct(_pool));
        var connections = new CallerConnections(dataSource, Interceptor(RowLevelSecurityScope.Connection));
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));

        var open = async () => await connections.OpenAsync(Cancellation);
        var begin = async () => await connections.BeginTransactionAsync(Cancellation);

        (await open.Should().ThrowAsync<InvalidOperationException>()).WithMessage("Row level security sets the caller's role and claims on the connection when a context opens it. 'No Reset On Close'*");
        (await begin.Should().ThrowAsync<InvalidOperationException>("the interceptor's refusals are the scope's, whichever way the caller would be set")).WithMessage("*'No Reset On Close'*");
        _sent.Sent.Should().BeEmpty("refused before anything connected");
    }

    [Fact]
    public async Task Nobody_calling_is_NoCallerException_with_explicit_callers()
    {
        database.Require();
        await using var dataSource = Direct(RowLevelSecurityScope.Connection);
        var required = new CallerOptions { RequireExplicitCallers = true };
        var connections = new CallerConnections(dataSource, PoolerFixture.Interceptor(new AmbientCallerAccessor(required), RowLevelSecurityScope.Connection, [_tenant], required));

        var open = async () => await connections.OpenAsync(Cancellation);
        var begin = async () => await connections.BeginTransactionAsync(Cancellation);

        await open.Should().ThrowAsync<NoCallerException>();
        await begin.Should().ThrowAsync<NoCallerException>();
        _sent.Sent.Should().BeEmpty("nothing connects for nobody");

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Bob))))
        {
            await using var connection = await connections.OpenAsync(Cancellation);
            (await ScalarAsync(connection, Who)).Should().StartWith($"authenticated|{Bob}|north|");
        }
    }

    [Fact]
    public async Task A_connection_the_caller_could_not_be_set_on_is_not_handed_out()
    {
        database.Require();
        await database.AsOwnerAsync(
            "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'beyond_the_grants') THEN CREATE ROLE beyond_the_grants NOLOGIN NOINHERIT; END IF; END $$; SELECT 1");

        await using var dataSource = Direct(RowLevelSecurityScope.Connection);
        var beyond = new CallerConnections(dataSource, PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Connection, [_tenant], configure: options => options.SystemInRole = "beyond_the_grants"));

        _caller.Current = Caller.SystemIn("tenancy");
        var open = async () => await beyond.OpenAsync(Cancellation);
        var begin = async () => await beyond.BeginTransactionAsync(Cancellation);

        (await open.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await begin.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        // The pool's one connection came back each time, so the next caller gets it, and gets it clean.
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        var connections = new CallerConnections(dataSource, Interceptor(RowLevelSecurityScope.Connection));
        await using var connection = await connections.OpenAsync(Cancellation);
        (await ScalarAsync(connection, Who)).Should().Be($"authenticated|{Alice}|north|300ms|Alice's");
    }

    [Fact]
    public async Task A_context_given_a_caller_connection_knows_its_caller()
    {
        database.Require();
        await using var dataSource = Direct(RowLevelSecurityScope.Connection);
        var connections = new CallerConnections(dataSource, Interceptor(RowLevelSecurityScope.Connection));
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));

        await using var connection = await connections.OpenAsync(Cancellation);
        _sent.Count(SetsTheRole).Should().Be(1);

        // A context with an interceptor of its own on that connection: it finds the caller there and sends nothing.
        await using var context = new NotesContext(new DbContextOptionsBuilder<NotesContext>().UseNpgsql(connection).AddInterceptors(Interceptor(RowLevelSecurityScope.Connection)).Options);
        (await context.Notes.Select(note => note.Text).ToListAsync(Cancellation)).Should().Equal("Alice's");
        _sent.Count(SetsTheRole).Should().Be(1, "what the caller connection set is remembered where the interceptor remembers it");

        // And it follows a caller that changes afterwards, as on any connection it holds.
        _caller.Current = Callers.FromClaims(ClaimsOf(Bob));
        (await context.Notes.Select(note => note.Text).ToListAsync(Cancellation)).Should().Equal("Bob's");
        _sent.Count(SetsTheRole).Should().Be(2);
    }

    [Theory]
    [InlineData(RowLevelSecurityScope.Connection)]
    [InlineData(RowLevelSecurityScope.Transaction)]
    public async Task A_context_given_a_caller_transaction_knows_its_caller(RowLevelSecurityScope scope)
    {
        database.Require();
        await using var dataSource = Direct(scope);
        var required = new CallerOptions { RequireExplicitCallers = true };
        PostgresRowLevelSecurityInterceptor Strict() => PoolerFixture.Interceptor(new AmbientCallerAccessor(required), scope, [_tenant], required, configure: Timed);
        var connections = new CallerConnections(dataSource, Strict());

        using var alice = Callers.Begin(Callers.FromClaims(ClaimsOf(Alice)));
        await using var transaction = await connections.BeginTransactionAsync(Cancellation);
        await using var connection = transaction.Connection!;
        _sent.Count(SetsTheRole).Should().Be(1);

        await using var context = new NotesContext(new DbContextOptionsBuilder<NotesContext>().UseNpgsql(connection).AddInterceptors(Strict()).Options);
        await context.Database.UseTransactionAsync(transaction, Cancellation);
        (await context.Notes.Select(note => note.Text).ToListAsync(Cancellation)).Should().Equal("Alice's");
        _sent.Count(SetsTheRole).Should().Be(1, "the transaction carries the caller already, in either scope, and the context asked the server instead of setting it again");

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Bob))))
        {
            var asBob = () => context.Notes.Select(note => note.Text).ToListAsync(Cancellation);
            (await asBob.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"The caller changed from authenticated {Alice} to authenticated {Bob} while a transaction was open on this connection.*");
        }
    }

    [Fact]
    public async Task AddCallerConnections_registers_them_over_the_data_source_it_is_given()
    {
        database.Require();
        await using var dataSource = Direct(RowLevelSecurityScope.Connection);

        await using var services = new ServiceCollection()
            .AddPostgresRowLevelSecurity<CallerOfTheTest>(Timed)
            .AddRowLevelSecuritySettings<TenantSetting>()
            .AddCallerConnections(_ => throw new InvalidOperationException("Replaced by the registration after it."))
            .AddCallerConnections(_ => dataSource)
            .BuildServiceProvider();

        ((CallerOfTheTest)services.GetRequiredService<ICallerAccessor>()).Current = Callers.FromClaims(ClaimsOf(Bob));
        var connections = services.GetRequiredService<CallerConnections>();
        connections.Should().BeSameAs(services.GetRequiredService<CallerConnections>(), "one for the application, as the interceptor is");

        await using (var connection = await connections.OpenAsync(Cancellation))
        {
            (await ScalarAsync(connection, Who)).Should().Be($"authenticated|{Bob}||300ms|Bob's", "it asks the registered accessor, and takes the registered roles, settings and timeouts");
        }

        // Without row level security there is no interceptor to set a caller with.
        await using var bare = new ServiceCollection().AddCallerConnections(_ => dataSource).BuildServiceProvider();
        var resolve = () => bare.GetRequiredService<CallerConnections>();
        resolve.Should().Throw<InvalidOperationException>().WithMessage("Row level security is not registered. Call services.AddPostgresRowLevelSecurity() first.");

        var none = () => new ServiceCollection().AddCallerConnections(null!);
        none.Should().Throw<ArgumentNullException>();
    }

    /// <summary>A data source of one connection straight to Postgres, whose statements are kept: reset on close where the settings last a session.</summary>
    private NpgsqlDataSource Direct(RowLevelSecurityScope scope)
        => _sent.DataSource(new NpgsqlConnectionStringBuilder(database.Direct(_pool)) { NoResetOnClose = scope == RowLevelSecurityScope.Transaction }.ConnectionString);

    private PostgresRowLevelSecurityInterceptor Interceptor(RowLevelSecurityScope scope)
        => PoolerFixture.Interceptor(_caller, scope, [_tenant], configure: Timed);

    /// <summary>A user's statements get 300 milliseconds; the other kinds get the login role's own.</summary>
    private static void Timed(PostgresRowLevelSecurityOptions options) => options.StatementTimeouts[CallerKind.User] = TimeSpan.FromMilliseconds(300);

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql, DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return (string?)await command.ExecuteScalarAsync(Cancellation);
    }
}
