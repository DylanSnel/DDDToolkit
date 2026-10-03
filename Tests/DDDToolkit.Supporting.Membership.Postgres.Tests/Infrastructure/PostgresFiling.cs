using DDDToolkit.EntityFramework.Postgres;
using Npgsql;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests.Infrastructure;

/// <summary>
/// The application over one Postgres database of the calling test's own, as a host registers it: both kinds of
/// resource through the TestHost, row level security with the folder's token role mapped, and its context
/// connecting as the login role. The data is the scenario the SQLite suite asks over, made for the moment the
/// test runs at, since the database's functions ask the database's clock.
/// </summary>
public sealed class PostgresFiling : IDisposable
{
    private PostgresFiling(FilingDatabase database, bool asCaller, Action<IServiceCollection>? configure, bool ownContexts)
    {
        Database = database;
        Services = new FilingServices(
            options => options.UseNpgsql(database.ConnectionString),
            TimeProvider.System,
            services =>
            {
                services.AddPostgresRowLevelSecurity(FilingPostgres.Roles);
                configure?.Invoke(services);
            },
            ownContexts,
            wiring: asCaller ? (options, provider) => options.UsePostgresRowLevelSecurity(provider) : null);
        Scenario = new FilingScenario(DateTimeOffset.UtcNow);
    }

    /// <summary>The test's database.</summary>
    public FilingDatabase Database { get; }

    /// <summary>The application's services.</summary>
    public FilingServices Services { get; }

    /// <summary>The data, as it is now.</summary>
    public FilingScenario Scenario { get; }

    /// <summary>
    /// The application over a new database copied from <paramref name="template"/>, with the scenario saved by
    /// the application's own work.
    /// </summary>
    /// <param name="server">The run's Postgres.</param>
    /// <param name="template">The template the database is copied from.</param>
    /// <param name="asCaller">
    /// Whether the context runs every command as the caller, under the policies, as a host on row level security
    /// does. Without it every command runs as the login role, which owns the tables: what the application's own
    /// check answers by itself.
    /// </param>
    /// <param name="seed">Whether the scenario is saved.</param>
    /// <param name="configure">Registers what a test needs before the resources are registered.</param>
    /// <param name="ownContexts">
    /// Whether the host takes its contexts from a pool with a factory, so each reading of the access questions
    /// runs on a context of its own.
    /// </param>
    public static async Task<PostgresFiling> CreateAsync(
        FilingPostgres server,
        FilingPostgres.Template template,
        bool asCaller = false,
        bool seed = true,
        Action<IServiceCollection>? configure = null,
        bool ownContexts = false)
    {
        var filing = new PostgresFiling(await server.CreateDatabaseAsync(template, TestContext.Current.CancellationToken), asCaller, configure, ownContexts);
        if (seed)
        {
            await filing.Scenario.SaveAsync(filing.Services);
            filing.Services.Commands.Reset();
        }

        return filing;
    }

    /// <summary>
    /// A session on the database as <paramref name="caller"/>, past the application: what its own SQL would be
    /// answered. With <paramref name="before"/>, that runs first in the session's transaction, as the login
    /// role, which owns the tables: what a test sets up at the transaction's own moment.
    /// </summary>
    public Task<CallerSession> SessionAsync(Caller caller, string? role = null, string? before = null) => CallerSession.BeginAsync(Database, caller, role, before);

    /// <summary>Runs <paramref name="sql"/> as the login role, which owns everything of the application's.</summary>
    public Task ExecuteAsync(string sql) => FilingPostgres.ExecuteAsync(Database.ConnectionString, sql, TestContext.Current.CancellationToken);

    /// <summary>The first column of every row <paramref name="sql"/> answers the login role.</summary>
    public async Task<List<T>> ListAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(reader.GetFieldValue<T>(0));
        }

        return rows;
    }

    /// <inheritdoc />
    public void Dispose() => Services.Dispose();
}

/// <summary>
/// A query as a caller: a raw connection as the login role, in a transaction, with the role and the claims set
/// for that transaction alone, as the row level security interceptor sets them for a caller. What runs here
/// goes past the application, as its own SQL that forgot a condition would: only the policies, the privileges
/// and the functions' grants stand in its way. Disposing it rolls it back.
/// </summary>
public sealed class CallerSession : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    private CallerSession(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    /// <summary>
    /// Begins a session as <paramref name="caller"/>: the claims of its token, and the database role its queries
    /// run as, a signed-in user's unless <paramref name="role"/> says another.
    /// </summary>
    /// <param name="database">The test's database.</param>
    /// <param name="caller">Whose claims the session carries.</param>
    /// <param name="role">The database role the session's queries run as; a signed-in user's when left out.</param>
    /// <param name="before">
    /// SQL that runs first, in the same transaction, as the login role: before the session becomes the
    /// caller's. The database's clock stands still in a transaction, so a row set to <c>now()</c> here is at
    /// exactly the moment every later question of the session is asked at.
    /// </param>
    public static async Task<CallerSession> BeginAsync(FilingDatabase database, Caller caller, string? role = null, string? before = null)
    {
        var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var session = new CallerSession(connection, await connection.BeginTransactionAsync(TestContext.Current.CancellationToken));
        if (before is not null)
        {
            await using var prepare = new NpgsqlCommand(before, connection, session._transaction);
            await prepare.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var command = new NpgsqlCommand("SELECT set_config('role', $1, true), set_config('request.jwt.claims', $2, true)", connection, session._transaction);
        command.Parameters.Add(new NpgsqlParameter { Value = role ?? PostgresRowLevelSecurityOptions.AuthenticatedRole });
        command.Parameters.Add(new NpgsqlParameter { Value = caller.Claims ?? "{}" });
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return session;
    }

    /// <summary>The first column of every row <paramref name="sql"/> answers, in the order it answers them.</summary>
    public async Task<List<T>> ListAsync<T>(string sql, params object[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, _connection, _transaction);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(reader.GetFieldValue<T>(0));
        }

        return rows;
    }

    /// <summary>Keeps what the session wrote: a session that is disposed without it is rolled back.</summary>
    public Task CommitAsync() => _transaction.CommitAsync(TestContext.Current.CancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
