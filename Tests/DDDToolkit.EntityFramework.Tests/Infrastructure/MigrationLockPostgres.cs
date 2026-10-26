namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A Postgres server of their own for the tests of the migration lock, which make a database for every round they
/// migrate: dozens of databases, which the server every other test shares should not have to hold. Started once for the
/// class, and stopped with it.
/// </summary>
public sealed class MigrationLockPostgres : IAsyncLifetime
{
    private PgmqDatabase? _server;
    private int _databases;

    public async ValueTask InitializeAsync() => _server = await PgmqDatabase.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    /// <summary>
    /// A new, empty database on the server, as the owner's connection string. Skips the test without Docker, or fails it
    /// where containers are required.
    /// </summary>
    public Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        RequiredContainers.EnforceOrSkip(
            _server is not null,
            RequiredContainers.Required,
            "PostgreSQL",
            $"No Docker here, so '{PgmqDatabase.Image}' could not be started. The migration lock is not covered on this machine.");

        return _server!.CreateDatabaseWithoutPgmqAsync($"lock_{Interlocked.Increment(ref _databases)}", cancellationToken);
    }
}
