using System.Net;
using System.Net.Sockets;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// One Postgres and one PgBouncer in front of it for every test of settings that last one transaction, which
/// run one class after the other: they count server connections, and read what the one before left behind.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SettingsPerTransaction : ICollectionFixture<PoolerFixture>
{
    public const string Name = "Settings per transaction";
}

/// <summary>
/// A Postgres set up the way <see cref="SupabaseRowLevelSecurityDatabase"/> is, with the toolkit's own schema
/// and its <c>ddd.use_caller</c> procedure added, and PgBouncer in transaction mode in front of it, on one
/// network. The pooler keeps <b>one</b> server connection for the application's login role, so every client
/// that connects through it as that role is handed the same backend, one transaction at a time: whatever one
/// client leaves on the session, the next one reads.
/// </summary>
public sealed class PoolerFixture : IAsyncLifetime
{
    /// <summary>
    /// PgBouncer 1.26.0, by the digest of the image: a tag can be moved, and what these tests prove is about
    /// one pooler's behavior.
    /// </summary>
    public const string PoolerImage = "edoburu/pgbouncer@sha256:9c78945868a6a142c7fc40ccd843bbe5a606df163c7ffce4de70e0d628d696a2";

    public const string LoginRole = SupabaseRowLevelSecurityDatabase.LoginRole;

    private const string DatabaseHost = "postgres";
    private const string DatabaseName = "ddd";
    private const string Superuser = "postgres";
    private const int PostgresPort = 5432;

    private INetwork? _network;
    private PostgreSqlContainer? _postgres;
    private IContainer? _pooler;
    private string? _whyNot;

    /// <summary>Whether both containers started; a test skips or fails through <see cref="Require"/> when they did not.</summary>
    public bool Available => _pooler is not null;

    /// <summary>The superuser's connection string, straight to Postgres: the tables' owner, which row level security does not apply to.</summary>
    public string OwnerConnectionString => Postgres.GetConnectionString();

    private PostgreSqlContainer Postgres => _postgres ?? throw new InvalidOperationException("The database did not start.");

    private IContainer Pooler => _pooler ?? throw new InvalidOperationException("The pooler did not start.");

    public async ValueTask InitializeAsync()
    {
        var cancellation = TestContext.Current.CancellationToken;
        try
        {
            _network = new NetworkBuilder().Build();
            await _network.CreateAsync(cancellation);

            _postgres = new PostgreSqlBuilder(PgmqDatabase.SupabaseImage)
                .WithDatabase(DatabaseName)
                .WithUsername(Superuser)
                .WithPassword(Superuser)
                .WithNetwork(_network)
                .WithNetworkAliases(DatabaseHost)
                .Build();
            await _postgres.StartAsync(cancellation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _whyNot = exception.Message.Trim();
            return;
        }

        await using (var owner = new NpgsqlConnection(Unpooled(OwnerConnectionString)))
        {
            await owner.OpenAsync(cancellation);
            await SupabaseRowLevelSecurityDatabase.SetUpAsync(owner, cancellation);

            // The toolkit's schema, with the procedure that sets a caller for one transaction, which the role the
            // application logs in as may call and nobody else.
            await using var setup = new NpgsqlCommand(PostgresRowAccess.SetupScript(loginRole: LoginRole), owner);
            await setup.ExecuteNonQueryAsync(cancellation);
        }

        // One server connection for the login role: with a second one, a test that reads what the last client
        // left behind could be handed a backend the last client never touched, and pass for nothing.
        var pooler = new ContainerBuilder(PoolerImage)
            .WithNetwork(_network)
            .WithEnvironment(new Dictionary<string, string>
            {
                ["DB_HOST"] = DatabaseHost,
                ["DB_PORT"] = PostgresPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["DB_NAME"] = DatabaseName,
                ["DB_USER"] = LoginRole,
                ["DB_PASSWORD"] = LoginRole,
                ["AUTH_TYPE"] = "scram-sha-256",
                ["POOL_MODE"] = "transaction",
                ["DEFAULT_POOL_SIZE"] = "1",
                ["MAX_CLIENT_CONN"] = "100",
                ["IGNORE_STARTUP_PARAMETERS"] = "extra_float_digits",
                ["MAX_PREPARED_STATEMENTS"] = "0",
            })
            .WithPortBinding(PostgresPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("process up"))
            .Build();

        await pooler.StartAsync(cancellation);
        _pooler = pooler;
    }

    public async ValueTask DisposeAsync()
    {
        // Idle connections of these tests would otherwise outlive the servers they point at.
        NpgsqlConnection.ClearAllPools();

        if (_pooler is not null)
        {
            await _pooler.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        if (_network is not null)
        {
            await _network.DisposeAsync();
        }
    }

    /// <summary>Skips the calling test without Docker, or fails it where containers are required.</summary>
    public void Require()
        => RequiredContainers.EnforceOrSkip(
            Available,
            RequiredContainers.Required,
            "PostgreSQL behind PgBouncer",
            $"No Docker here, so '{PgmqDatabase.SupabaseImage}' and '{PoolerImage}' could not be started ({_whyNot ?? "the pooler did not come up"}). Settings per transaction are not covered on this machine.");

    /// <summary>
    /// The application's connection string straight to Postgres, as the login role, with a pool of
    /// <paramref name="maximum"/> connections that are not reset when they go back: with one, whoever opens a
    /// connection next gets the same backend, as it was left.
    /// </summary>
    /// <param name="name">The application name, which gives a test a pool of its own: one test's leftovers are not another's.</param>
    /// <param name="maximum">How many connections the pool holds.</param>
    public string Direct(string name, int maximum = 1)
        => new NpgsqlConnectionStringBuilder(OwnerConnectionString)
        {
            Username = LoginRole,
            Password = LoginRole,
            ApplicationName = name,
            MinPoolSize = 0,
            MaxPoolSize = maximum,
            NoResetOnClose = true,
        }.ConnectionString;

    /// <summary>
    /// The application's connection string through the pooler, as the login role, the way a transaction pooler
    /// is used: no reset on close, and no prepared statements.
    /// </summary>
    /// <param name="name">The application name, which gives a test a pool of client connections of its own.</param>
    /// <param name="port">Another port to connect to, a relay in front of the pooler for one; the pooler's own when left out.</param>
    public string Pooled(string name, int? port = null)
        => new NpgsqlConnectionStringBuilder
        {
            Host = Pooler.Hostname,
            Port = port ?? PoolerPort,
            Database = DatabaseName,
            Username = LoginRole,
            Password = LoginRole,
            ApplicationName = name,
            MinPoolSize = 0,
            MaxPoolSize = 20,
            NoResetOnClose = true,
            MaxAutoPrepare = 0,
        }.ConnectionString;

    /// <summary>The port the pooler listens on, on this machine.</summary>
    public int PoolerPort => Pooler.GetMappedPublicPort(PostgresPort);

    /// <summary>The host the pooler listens on, on this machine.</summary>
    public string PoolerHost => Pooler.Hostname;

    /// <summary>Removes every note, as the owner.</summary>
    public async Task ClearAsync()
    {
        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""TRUNCATE notes."Notes" """, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>One value the owner reads, straight from Postgres: what the server itself knows, past every policy and pooler.</summary>
    public async Task<string?> AsOwnerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A note of <paramref name="user"/>'s, written by the owner, so a test starts from rows it did not need a caller to write.</summary>
    public async Task<Guid> WriteAsync(Guid user, string text)
    {
        var id = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""INSERT INTO notes."Notes" ("Id", "Text", "Owner") VALUES ($1, $2, $3)""", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = id });
        command.Parameters.Add(new NpgsqlParameter { Value = text });
        command.Parameters.Add(new NpgsqlParameter { Value = user });
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>
    /// An interceptor in <paramref name="scope"/> that asks <paramref name="callers"/> who is calling, with the
    /// modules' <paramref name="settings"/>.
    /// </summary>
    public static PostgresRowLevelSecurityInterceptor Interceptor(
        ICallerAccessor callers,
        RowLevelSecurityScope scope,
        IEnumerable<IRowLevelSecuritySettings>? settings = null,
        CallerOptions? callerOptions = null,
        Microsoft.Extensions.Logging.ILogger<PostgresRowLevelSecurityInterceptor>? logger = null,
        Action<PostgresRowLevelSecurityOptions>? configure = null)
    {
        var options = new PostgresRowLevelSecurityOptions { Scope = scope };
        configure?.Invoke(options);
        return new PostgresRowLevelSecurityInterceptor(callers, options, settings ?? [], callerOptions, logger);
    }

    /// <summary>A context on <paramref name="connectionString"/> with <paramref name="interceptor"/>.</summary>
    public static NotesContext Context(string connectionString, PostgresRowLevelSecurityInterceptor interceptor)
        => new(new DbContextOptionsBuilder<NotesContext>().UseNpgsql(connectionString).AddInterceptors(interceptor).Options);

    private static string Unpooled(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
}

/// <summary>
/// What a backend is running as, read in one statement, so the answer is about one transaction: the backend's
/// id, the current and the session user, the claims, the user id where Supabase's <c>auth.uid()</c> looks first,
/// and the tenant setting of <see cref="TenantSetting"/>.
/// </summary>
public sealed record BackendState(int Backend, string CurrentUser, string SessionUser, string Claims, string ClaimSub, string Tenant)
{
    /// <summary>The statement, of one row with <c>|</c> between the values; nothing in it comes from outside.</summary>
    public const string Sql =
        "SELECT pg_catalog.pg_backend_pid()::text || '|' || current_user || '|' || session_user || '|' || " +
        "coalesce(pg_catalog.current_setting('request.jwt.claims', true), '') || '|' || " +
        "coalesce(pg_catalog.current_setting('request.jwt.claim.sub', true), '') || '|' || " +
        $"coalesce(pg_catalog.current_setting('{TenantSetting.Tenant}', true), '')";

    /// <summary>What a backend nobody set anything on answers: the login role, and nothing else.</summary>
    public static BackendState Clean(int backend) => new(backend, PoolerFixture.LoginRole, PoolerFixture.LoginRole, "", "", "");

    public static BackendState Parse(string value)
    {
        var parts = value.Split('|');
        return new BackendState(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), parts[1], parts[2], parts[3], parts[4], parts[5]);
    }

    /// <summary>The state a plain client reads on <paramref name="connectionString"/>: no interceptor, no settings, a connection of its own.</summary>
    public static async Task<BackendState> OfAPlainClientAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Sql, connection);
        return Parse((string)(await command.ExecuteScalarAsync(cancellationToken))!);
    }

    /// <summary>The state a plain command on <paramref name="connection"/>, which is open, runs in.</summary>
    public static async Task<BackendState> OnAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken, System.Data.Common.DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Sql;
        command.Transaction = transaction;
        return Parse((string)(await command.ExecuteScalarAsync(cancellationToken))!);
    }

    /// <summary>The state a statement of <paramref name="context"/> runs in, read through Entity Framework and so through the interceptor.</summary>
    public static async Task<BackendState> OfAsync(DbContext context, CancellationToken cancellationToken)
        => Parse(await context.Database.SqlQueryRaw<string>(Sql + " AS \"Value\"").SingleAsync(cancellationToken));
}

/// <summary>
/// A relay between a client and a server that a test can cut, both ways at once and without a goodbye, the
/// way a client's machine losing its network looks to the server.
/// </summary>
public sealed class CuttableRelay : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopped = new();
    private readonly List<Socket> _sockets = [];
    private readonly Task _accepting;

    public CuttableRelay(string host, int port)
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _accepting = AcceptAsync(host, port);
    }

    /// <summary>The port clients connect to, on this machine.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Cuts every connection through the relay, with a reset instead of a close, and takes no new one.</summary>
    public void Cut()
    {
        _stopped.Cancel();
        _listener.Stop();

        lock (_sockets)
        {
            foreach (var socket in _sockets)
            {
                // No lingering: the peer sees the connection reset, not ended.
                socket.LingerState = new LingerOption(enable: true, seconds: 0);
                socket.Dispose();
            }

            _sockets.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Cut();
        try
        {
            await _accepting;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Stopped, as asked.
        }

        _stopped.Dispose();
    }

    private async Task AcceptAsync(string host, int port)
    {
        while (!_stopped.IsCancellationRequested)
        {
            var client = await _listener.AcceptSocketAsync(_stopped.Token);
            var server = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            client.NoDelay = true;

            lock (_sockets)
            {
                _sockets.Add(client);
                _sockets.Add(server);
            }

            await server.ConnectAsync(host, port, _stopped.Token);
            _ = PumpAsync(client, server);
            _ = PumpAsync(server, client);
        }
    }

    private async Task PumpAsync(Socket from, Socket to)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                var read = await from.ReceiveAsync(buffer, _stopped.Token);
                if (read == 0)
                {
                    to.Shutdown(SocketShutdown.Send);
                    return;
                }

                var sent = 0;
                while (sent < read)
                {
                    sent += await to.SendAsync(buffer.AsMemory(sent, read - sent), _stopped.Token);
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Cut, or the other side went first.
        }
    }
}
