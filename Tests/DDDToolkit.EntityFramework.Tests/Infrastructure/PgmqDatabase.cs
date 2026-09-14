using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A real Postgres with the pgmq extension, started once per test class.
/// <para>
/// pgmq's guarantee is that an enqueue is an ordinary insert in an ordinary transaction, and nothing
/// short of a real Postgres can show that. So these tests use a container rather than a fake, and skip
/// themselves when there is no Docker to start one with, because a skipped test is honest and a mocked
/// one would not be.
/// </para>
/// </summary>
public sealed class PgmqDatabase : IAsyncDisposable
{
    /// <summary>
    /// The image the pgmq project publishes. Pinned, because the function signatures this package calls
    /// are verified against pgmq 1.5.1.
    /// </summary>
    public const string Image = "ghcr.io/pgmq/pg17-pgmq:v1.5.1";

    private readonly PostgreSqlContainer _container;

    private PgmqDatabase(PostgreSqlContainer container) => _container = container;

    /// <summary>The connection string of the database that has the extension.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Starts the container, or returns <see langword="null"/> when Docker is not available here.
    /// Callers skip the test in that case rather than pretending to have covered it.
    /// </summary>
    public static async Task<PgmqDatabase?> StartAsync(CancellationToken cancellationToken)
    {
        var container = new PostgreSqlBuilder(Image)
            .WithDatabase("ddd")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        try
        {
            await container.StartAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await container.DisposeAsync();
            return null;
        }

        var database = new PgmqDatabase(container);
        await database.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS pgmq", cancellationToken);
        return database;
    }

    /// <summary>An open connection to the database that has the extension.</summary>
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// A second database on the same server with no pgmq in it, so the "not installed" path can be
    /// exercised against a real server rather than a rigged connection.
    /// </summary>
    public async Task<string> CreateDatabaseWithoutPgmqAsync(string name, CancellationToken cancellationToken)
    {
        await ExecuteAsync($"CREATE DATABASE {name}", cancellationToken);

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    /// <summary>A context mapped onto this database, with the outbox and the inbox.</summary>
    public PgmqContext CreateContext()
        => new(new DbContextOptionsBuilder<PgmqContext>().UseNpgsql(ConnectionString).Options);

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// The smallest context that can carry an outbox: one table of its own, so the Postgres tests do not
/// drag the whole SQLite test model onto another provider.
/// </summary>
public class PgmqContext(DbContextOptions<PgmqContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddDomainEventOutbox(Database);
        modelBuilder.AddDomainEventInbox(Database);
    }
}
