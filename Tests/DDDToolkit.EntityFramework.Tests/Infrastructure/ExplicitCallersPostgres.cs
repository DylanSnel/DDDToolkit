using Npgsql;

[assembly: AssemblyFixture(typeof(DDDToolkit.EntityFramework.Tests.Infrastructure.ExplicitCallersPostgres))]

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// One Postgres, with pgmq, for every test of what the toolkit's own work runs as in a host that requires
/// explicit callers. It starts the first time a test asks for a database, not with the run, so a run that
/// needs none of these tests starts no container, and it stops with the run. Each test gets a database of
/// its own, copied from a template prepared once, so the tests change data without seeing each other's.
/// </summary>
/// <remarks>
/// The template has the roles a policy of these tests names, <c>anon</c>, <c>authenticated</c> and the
/// scoped system role, which are the server's, and the pgmq extension. The tests log in as the superuser,
/// which owns every table: the system caller runs as it, past row level security, and the scoped system
/// caller switches to a role the policies hold.
/// </remarks>
public sealed class ExplicitCallersPostgres : IAsyncLifetime
{
    /// <summary>The role a scoped system caller runs as, the interceptor's default.</summary>
    public const string SystemInRole = "ddd_system_in";

    private const string Template = "callers_template";

    /// <summary>A template cannot be copied twice at once, so every copy waits its turn.</summary>
    private static readonly SemaphoreSlim Copying = new(1, 1);

    private readonly Lock _starting = new();
    private Task<PgmqDatabase?>? _start;
    private PgmqDatabase? _server;
    private int _databases;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    /// <summary>
    /// A new database of the calling test's own, as the owner's connection string. Skips the test without
    /// Docker, or fails it where containers are required.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var server = await ServerAsync(cancellationToken);
        RequiredContainers.EnforceOrSkip(
            server is not null,
            RequiredContainers.Required,
            "PostgreSQL",
            $"No Docker here, so '{PgmqDatabase.Image}' could not be started. What the toolkit's own work runs as under explicit callers is not covered on this machine.");

        var name = $"callers_{Interlocked.Increment(ref _databases)}";

        await Copying.WaitAsync(cancellationToken);
        try
        {
            await ExecuteAsync(Unpooled(server!.ConnectionString), $"CREATE DATABASE {name} TEMPLATE {Template}", cancellationToken);
        }
        finally
        {
            Copying.Release();
        }

        return new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = name }.ConnectionString;
    }

    /// <summary>
    /// The server, started the first time a test asks: one start for the run, which no test's own token ends, so
    /// a test that is cancelled while it waits leaves the start to the others. Without Docker there is no server
    /// and every test is skipped, or failed where containers are required. A server that started and could not
    /// be set up fails every test that asks, with the reason: it is never taken for a machine without Docker.
    /// </summary>
    private Task<PgmqDatabase?> ServerAsync(CancellationToken cancellationToken)
    {
        Task<PgmqDatabase?> start;
        lock (_starting)
        {
            // On the pool, so the start neither runs under this lock nor comes back to the asking test's context.
            start = _start ??= Task.Run(StartServerAsync);
        }

        return start.WaitAsync(cancellationToken);
    }

    /// <summary>The server, or <see langword="null"/> when its container could not start; a server that started is set up, or this fails.</summary>
    private async Task<PgmqDatabase?> StartServerAsync()
    {
        var server = await PgmqDatabase.StartAsync(CancellationToken.None);
        if (server is null)
        {
            return null;
        }

        // Kept before it is set up, so the run stops the container whether the setup went through or not.
        _server = server;
        await PrepareAsync(server.ConnectionString, CancellationToken.None);
        return server;
    }

    /// <summary>
    /// The roles, and the template with pgmq in it. Every connection here is unpooled: a pooled one would
    /// stay connected to the template after it closed, and a database cannot be copied while anybody is.
    /// </summary>
    private static async Task PrepareAsync(string connectionString, CancellationToken cancellationToken)
    {
        await ExecuteAsync(Unpooled(connectionString), $"""
            CREATE ROLE anon NOLOGIN NOINHERIT;
            CREATE ROLE authenticated NOLOGIN NOINHERIT;
            CREATE ROLE {SystemInRole} NOLOGIN NOINHERIT;
            """, cancellationToken);
        await ExecuteAsync(Unpooled(connectionString), $"CREATE DATABASE {Template}", cancellationToken);

        var template = new NpgsqlConnectionStringBuilder(Unpooled(connectionString)) { Database = Template }.ConnectionString;
        await ExecuteAsync(template, "CREATE EXTENSION IF NOT EXISTS pgmq", cancellationToken);
    }

    private static string Unpooled(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
