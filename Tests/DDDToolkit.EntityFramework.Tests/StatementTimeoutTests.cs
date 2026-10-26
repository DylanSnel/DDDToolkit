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
/// A statement timeout per kind of caller. Postgres applies the settings stored on a role when that role logs
/// in, not when a session switches to it, so without this a caller's statements run under the timeout of the
/// role the application logged in as. The login role of these tests has a timeout of its own, five seconds, so
/// "the login role's own" is something a test can tell from "none".
/// </summary>
[Collection(SettingsPerTransaction.Name)]
public sealed class StatementTimeoutTests(PoolerFixture database) : IAsyncLifetime
{
    private const string LoginTimeout = "5s";
    private const string Timeout = """SELECT pg_catalog.current_setting('statement_timeout') AS "Value" """;
    private const string Slow = """SELECT pg_sleep(2)::text AS "Value" """;

    private readonly CallerOfTheTest _caller = new();
    private readonly SentStatements _sent = new();
    private readonly string _pool = "timeout-" + Guid.NewGuid().ToString("N")[..12];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (database.Available)
        {
            await database.ClearAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        _sent.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_roles_stored_timeout_does_not_apply_after_set_role()
    {
        database.Require();

        // The control: what the option is for. The role itself is given a timeout far shorter than the statement.
        await database.AsOwnerAsync("ALTER ROLE authenticated SET statement_timeout = '200ms'; SELECT 1");
        try
        {
            await using var dataSource = DataSource(RowLevelSecurityScope.Connection);
            await using var connection = await dataSource.OpenConnectionAsync(Cancellation);
            await using var command = new NpgsqlCommand(
                "SELECT set_config('role', 'authenticated', false); SELECT current_user || '|' || pg_catalog.current_setting('statement_timeout') FROM pg_sleep(0.6)",
                connection);
            await using var reader = await command.ExecuteReaderAsync(Cancellation);
            await reader.NextResultAsync(Cancellation);
            await reader.ReadAsync(Cancellation);

            reader.GetString(0).Should().Be(
                $"authenticated|{LoginTimeout}",
                "switching to a role does not apply the settings stored on it: the statement ran three times as long as the role's timeout, under the login role's");
        }
        finally
        {
            await database.AsOwnerAsync("ALTER ROLE authenticated RESET statement_timeout; SELECT 1");
        }
    }

    [Fact]
    public async Task A_callers_statement_runs_under_its_kinds_timeout_and_is_cancelled_with_57014()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Connection);
        await using var context = Context(dataSource, RowLevelSecurityScope.Connection);

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        (await context.Database.SqlQueryRaw<string>(Timeout).SingleAsync(Cancellation)).Should().Be("200ms");

        var slow = () => context.Database.SqlQueryRaw<string>(Slow).SingleAsync(Cancellation);
        (await slow.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled, "57014: the statement was cancelled by its timeout");

        // Another kind, another timeout.
        _caller.Current = Caller.Anonymous;
        (await context.Database.SqlQueryRaw<string>(Timeout).SingleAsync(Cancellation)).Should().Be("100ms");
    }

    [Fact]
    public async Task A_kind_without_a_timeout_keeps_the_login_roles()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Connection);
        await using var context = Context(dataSource, RowLevelSecurityScope.Connection);

        _caller.Current = Caller.SystemIn("tenancy");
        (await context.Database.SqlQueryRaw<string>(Timeout).SingleAsync(Cancellation)).Should().Be(LoginTimeout, "no timeout is configured for a scoped system caller");

        // A user whose token's role is on no list, and who runs as an anonymous caller, gets that caller's.
        _caller.Current = Caller.User(Alice, role: "stranger");
        await using var lenient = Context(dataSource, RowLevelSecurityScope.Connection, options => options.UnknownTokenRole = UnknownTokenRole.Anonymous);
        (await lenient.Database.SqlQueryRaw<string>("""SELECT current_user || '|' || pg_catalog.current_setting('statement_timeout') AS "Value" """).SingleAsync(Cancellation))
            .Should().Be("anon|100ms");
    }

    [Fact]
    public async Task After_a_user_the_system_gets_the_login_default_back_on_the_same_connection()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Connection);
        await using var context = Context(dataSource, RowLevelSecurityScope.Connection);
        await context.Database.OpenConnectionAsync(Cancellation);
        var backend = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        (await context.Database.SqlQueryRaw<string>(Timeout).SingleAsync(Cancellation)).Should().Be("200ms");

        _caller.Current = Caller.System;
        (await context.Database.SqlQueryRaw<string>(Timeout).SingleAsync(Cancellation)).Should().Be(
            LoginTimeout, "a timeout never outlives the caller it was set for: the system is given the login role's own, not the user's and not none");
        ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID.Should().Be(backend, "on the very connection the user ran on");

        // And once the connection is the pool's again, the timeout is gone with everything else.
        await context.Database.CloseConnectionAsync();
        await using var plain = await dataSource.OpenConnectionAsync(Cancellation);
        await using var read = new NpgsqlCommand("SELECT pg_catalog.current_setting('statement_timeout')", plain);
        (await read.ExecuteScalarAsync(Cancellation)).Should().Be(LoginTimeout);
        plain.ProcessID.Should().Be(backend);
    }

    [Fact]
    public async Task The_timeout_travels_in_the_same_statement()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Connection);
        await using var context = Context(dataSource, RowLevelSecurityScope.Connection, settings: [new TenantSetting { Current = "north" }]);

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        (await context.Database.SqlQueryRaw<string>(Timeout).SingleAsync(Cancellation)).Should().Be("200ms");

        const string caller = "set_config('role'";
        _sent.Sent.Where(sent => sent.Contains("statement_timeout", StringComparison.Ordinal)).Should().HaveCount(2, "the statement that set the caller, and the query that asked");
        _sent.Sent.Where(sent => sent.Contains(caller, StringComparison.Ordinal)).Should().ContainSingle("one statement, one round trip, for the role, the claims, the settings and the timeout")
            .Which.Should().EndWith(
                ": SELECT set_config('role', $1, false), set_config('request.jwt.claims', $2, false), " +
                "set_config('request.jwt.claim.sub', '', false), set_config('request.jwt.claim.role', '', false), " +
                "set_config('request.jwt.claim.email', '', false), set_config('request.jwt.claim', '', false), " +
                "set_config($3, $4, false), set_config($5, $6, false), " +
                "set_config('statement_timeout', coalesce(nullif($7, ''), (SELECT reset_val FROM pg_catalog.pg_settings WHERE name = 'statement_timeout')), false)");

        // Without any timeout configured the statement is what it always was.
        await using var untimed = new NotesContext(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql(dataSource)
            .AddInterceptors(PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Connection))
            .Options);
        (await untimed.Database.SqlQueryRaw<string>(Timeout).SingleAsync(Cancellation)).Should().Be(LoginTimeout);
        _sent.Sent.Last(sent => sent.Contains(caller, StringComparison.Ordinal)).Should().EndWith("set_config('request.jwt.claim', '', false)");
    }

    [Fact]
    public async Task In_transaction_scope_the_timeout_is_local_to_the_transaction_and_cancels_with_57014()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Transaction);
        await using var context = Context(dataSource, RowLevelSecurityScope.Transaction);
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));

        // Outside a transaction, where the call in front of the command sets it.
        (await context.Database.SqlQueryRaw<string>(Timeout).SingleAsync(Cancellation)).Should().Be("200ms");
        var slow = () => context.Database.SqlQueryRaw<string>(Slow).SingleAsync(Cancellation);
        (await slow.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);
        await ShouldHaveTheLoginTimeoutAsync(dataSource);

        // Inside one, where its first statement does.
        await using (await context.Database.BeginTransactionAsync(Cancellation))
        {
            (await context.Database.SqlQueryRaw<string>(Timeout).SingleAsync(Cancellation)).Should().Be("200ms");
            (await slow.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);
        }

        await ShouldHaveTheLoginTimeoutAsync(dataSource);

        // A kind without one sets nothing: the transaction runs under what the session has.
        _caller.Current = Caller.SystemIn("tenancy");
        (await context.Database.SqlQueryRaw<string>(Timeout).SingleAsync(Cancellation)).Should().Be(LoginTimeout);

        async Task ShouldHaveTheLoginTimeoutAsync(NpgsqlDataSource source)
        {
            await using var plain = await source.OpenConnectionAsync(Cancellation);
            await using var read = new NpgsqlCommand("SELECT pg_catalog.current_setting('statement_timeout')", plain);
            (await read.ExecuteScalarAsync(Cancellation)).Should().Be(LoginTimeout, "the timeout ended with the transaction it was set for, on a connection that is never reset");
        }
    }

    public static TheoryData<TimeSpan> NotATimeout() =>
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(-1),
        TimeSpan.FromTicks(1),
        TimeSpan.FromMilliseconds((double)int.MaxValue + 1),
    ];

    [Theory]
    [MemberData(nameof(NotATimeout))]
    public void A_timeout_is_at_least_a_millisecond_and_no_more_than_postgres_counts(TimeSpan timeout)
    {
        var register = () => new ServiceCollection().AddPostgresRowLevelSecurity(options => options.StatementTimeouts[CallerKind.User] = timeout);
        register.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(PostgresRowLevelSecurityOptions.StatementTimeouts))
            .WithMessage("The statement timeout of a User caller is at least a millisecond and at most 2147483647 milliseconds.*");

        var build = () => new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions { StatementTimeouts = { [CallerKind.User] = timeout } });
        build.Should().Throw<ArgumentOutOfRangeException>("an interceptor built by hand checks its options as the registration does");

        var kind = () => new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions { StatementTimeouts = { [(CallerKind)99] = TimeSpan.FromSeconds(1) } });
        kind.Should().Throw<ArgumentOutOfRangeException>().WithMessage("A statement timeout is for a kind of caller that exists.*");
    }

    /// <summary>A data source of one connection as the login role, whose sessions start with a timeout of five seconds, and whose statements are kept.</summary>
    private NpgsqlDataSource DataSource(RowLevelSecurityScope scope)
        => _sent.DataSource(new NpgsqlConnectionStringBuilder(database.Direct(_pool))
        {
            NoResetOnClose = scope == RowLevelSecurityScope.Transaction,
            Options = "-c statement_timeout=5000",
        }.ConnectionString);

    /// <summary>A context whose users get 200 milliseconds and whose anonymous callers 100; the other kinds get none.</summary>
    private NotesContext Context(NpgsqlDataSource dataSource, RowLevelSecurityScope scope, Action<PostgresRowLevelSecurityOptions>? configure = null, IEnumerable<IRowLevelSecuritySettings>? settings = null)
        => new(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql(dataSource)
            .AddInterceptors(PoolerFixture.Interceptor(_caller, scope, settings, configure: options =>
            {
                options.StatementTimeouts[CallerKind.User] = TimeSpan.FromMilliseconds(200);
                options.StatementTimeouts[CallerKind.Anonymous] = TimeSpan.FromMilliseconds(100);
                configure?.Invoke(options);
            }))
            .Options);
}
