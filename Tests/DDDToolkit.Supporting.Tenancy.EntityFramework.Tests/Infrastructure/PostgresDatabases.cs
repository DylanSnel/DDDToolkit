using DDDToolkit.EntityFramework.Tests.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(DDDToolkit.Supporting.Tenancy.EntityFramework.Tests.Infrastructure.PostgresDatabases))]

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests.Infrastructure;

/// <summary>
/// One Postgres for every test of this run that asks for one, started the first time a test does, so the tests on
/// SQLite start no container and a machine without Docker runs them all; stopped with the run. Each test gets a
/// database of its own, copied from a template that holds Tenancy's tables and the widgets', made once from the
/// models' create scripts, as a migration would make them.
/// </summary>
/// <remarks>
/// The tests connect as the superuser, which owns the tables: they prove what Entity Framework does on Npgsql,
/// with no row level security in the way. The template is filled over unpooled connections, since a database is
/// copied only while nobody is connected to it, and the copies are made one at a time.
/// </remarks>
public sealed class PostgresDatabases : TestDatabases, IAsyncLifetime
{
    /// <summary>Supabase's Postgres 17, with the pgmq it ships.</summary>
    public const string Image = "ghcr.io/pgmq/pg17-pgmq:v1.5.1";

    private const string Template = "tenancy_ef_template";

    /// <summary>A template cannot be copied twice at once, so every copy waits its turn.</summary>
    private static readonly SemaphoreSlim Copying = new(1, 1);

    private readonly Lock _starting = new();
    private Task<bool>? _start;
    private PostgreSqlContainer? _container;
    private string? _whyNot;
    private int _databases;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <inheritdoc />
    /// <remarks>Skips the test without Docker, or fails it where containers are required, with what the start said.</remarks>
    public override async Task<TestDatabase> CreateAsync(CancellationToken cancellationToken)
    {
        var available = await StartAsync(cancellationToken);
        RequiredContainers.EnforceOrSkip(
            available,
            RequiredContainers.Required,
            "PostgreSQL",
            $"'{Image}' could not be started, as on a machine without Docker ({_whyNot}). Tenancy's Entity Framework layer is covered on SQLite alone on this machine.");

        var name = $"tenancy_ef_{Interlocked.Increment(ref _databases)}";
        await Copying.WaitAsync(cancellationToken);
        try
        {
            await ExecuteAsync(Unpooled(ConnectionString("postgres")), $"CREATE DATABASE {name} TEMPLATE {Template}", cancellationToken);
        }
        finally
        {
            Copying.Release();
        }

        return new PostgresDatabase(ConnectionString(name), ConnectionString("postgres")) { HasTables = true };
    }

    private string ConnectionString(string database)
        => new NpgsqlConnectionStringBuilder(_container!.GetConnectionString()) { Database = database, IncludeErrorDetail = true }.ConnectionString;

    private static string Unpooled(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Whether there is a server, started the first time a test asks: one start for the run, which no test's own
    /// token ends, so a test that is cancelled while it waits leaves the start to the others. Without Docker
    /// there is none and every test is skipped, or failed where containers are required, with what the start
    /// said: a container that does not start on a machine that has Docker reads the same, and the reason tells
    /// them apart. A server that started and could not be set up fails every test that asks, with the reason: it
    /// is never taken for a machine without Docker.
    /// </summary>
    private Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        Task<bool> start;
        lock (_starting)
        {
            // On the pool, so the start neither runs under this lock nor comes back to the asking test's context.
            start = _start ??= Task.Run(StartServerAsync);
        }

        return start.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Whether a server was started, and why not when it was not; a server that started is set up, or this fails.
    /// </summary>
    private async Task<bool> StartServerAsync()
    {
        var container = new PostgreSqlBuilder(Image)
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")

            // Every test class runs at once, each with a database and a pool of its own.
            .WithCommand("-c", "max_connections=300")
            .Build();
        try
        {
            await container.StartAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Kept for the test that asks: a skip or a failure that says only "no Docker" hides a pull that was
            // refused, a port that was taken, or a server that did not come up in time.
            _whyNot = exception.Message.Trim();
            await container.DisposeAsync();
            return false;
        }

        _container = container;
        await PrepareTemplateAsync(CancellationToken.None);
        return true;
    }

    /// <summary>The template: Tenancy's tables, with its outbox, and the widgets' table, from the models' create scripts.</summary>
    private async Task PrepareTemplateAsync(CancellationToken cancellationToken)
    {
        await ExecuteAsync(Unpooled(ConnectionString("postgres")), $"CREATE DATABASE {Template}", cancellationToken);

        await using var tenancy = new TestTenancyContext(new DbContextOptionsBuilder<TestTenancyContext>().UseNpgsql("Host=model-only").Options);
        await using var widgets = new TestWidgetContext(new DbContextOptionsBuilder<TestWidgetContext>().UseNpgsql("Host=model-only").Options);

        var template = Unpooled(ConnectionString(Template));
        await ExecuteAsync(template, tenancy.Database.GenerateCreateScript(), cancellationToken);
        await ExecuteAsync(template, widgets.Database.GenerateCreateScript(), cancellationToken);
    }
}

/// <summary>
/// A test's own Postgres database, copied from the template. Every context opens a connection of its own to it, as
/// the superuser; disposing it closes them and drops the database.
/// </summary>
public sealed class PostgresDatabase(string connectionString, string serverConnectionString) : TestDatabase
{
    /// <summary>How a context connects to it.</summary>
    public string ConnectionString { get; } = connectionString;

    /// <inheritdoc />
    public override string Provider => "PostgreSQL";

    /// <inheritdoc />
    public override bool KeysAreAnArray => true;

    /// <inheritdoc />
    public override string KeysContain => "= ANY (";

    /// <inheritdoc />
    public override string PrimaryFilter => "\"IsPrimary\"";

    /// <inheritdoc />
    public override void Use(DbContextOptionsBuilder options) => options.UseNpgsql(ConnectionString);

    /// <inheritdoc />
    /// <remarks>The table is found by its name in whichever schema holds it: Tenancy's, the widgets', the outbox's.</remarks>
    public override int CountRows(string table)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();

        using var find = new NpgsqlCommand("SELECT table_schema FROM information_schema.tables WHERE table_name = $1 AND table_type = 'BASE TABLE'", connection);
        find.Parameters.Add(new NpgsqlParameter { Value = table });
        var schemas = new List<string>();
        using (var reader = find.ExecuteReader())
        {
            while (reader.Read())
            {
                schemas.Add(reader.GetString(0));
            }
        }

        if (schemas.Count != 1)
        {
            throw new InvalidOperationException($"{schemas.Count} tables are named {table}, where one was expected.");
        }

        using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{schemas[0]}\".\"{table}\"", connection);
        return Convert.ToInt32(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override string Table(string schema, string name) => schema + ".\"" + name + "\"";

    public override void Dispose()
    {
        using (var connection = new NpgsqlConnection(ConnectionString))
        {
            NpgsqlConnection.ClearPool(connection);
        }

        var database = new NpgsqlConnectionStringBuilder(ConnectionString).Database;
        using var server = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(serverConnectionString) { Pooling = false }.ConnectionString);
        server.Open();
        using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", server);
        drop.ExecuteNonQuery();
    }
}
