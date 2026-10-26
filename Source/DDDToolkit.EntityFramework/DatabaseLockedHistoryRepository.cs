using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework;

/// <summary>
/// Npgsql's migration history repository, whose lock is the whole database's: every time Entity Framework asks it for
/// the migration lock, it takes <see cref="DatabaseMigrationLock.PostgresStatement"/> in the migration's transaction
/// first, and then Npgsql's own. Everything else is Npgsql's, unchanged.
/// </summary>
/// <remarks>
/// <para>
/// Entity Framework 10's migrator asks a repository whose lock lasts a transaction, as Npgsql's does
/// (<see cref="LockReleaseBehavior.Transaction"/>), for the lock right after it begins the transaction the migrations
/// run in, and again after each transaction it begins anew, after a migration command that runs outside one. It asks
/// the repository the lock it holds names, so the lock handed back names this repository, and the advisory lock comes
/// back with Npgsql's each time. Both last until the transaction ends, so there is nothing to let go of by hand. A
/// command a migration runs outside any transaction, <c>CREATE INDEX CONCURRENTLY</c> say, runs without either lock, as
/// it did without Npgsql's.
/// </para>
/// <para>
/// The history table is made before the lock is taken, in a transaction of its own, as Npgsql makes it. Each module's
/// is in its own schema, and Npgsql takes a table another connection made at the same moment as made.
/// </para>
/// </remarks>
internal sealed class DatabaseLockedHistoryRepository : IHistoryRepository
{
    /// <summary>The name Npgsql's provider goes by. This package references no Npgsql, so the name is what it checks.</summary>
    internal const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private readonly IHistoryRepository _npgsql;
    private readonly HistoryRepositoryDependencies _dependencies;

    private DatabaseLockedHistoryRepository(IHistoryRepository npgsql, HistoryRepositoryDependencies dependencies)
    {
        _npgsql = npgsql;
        _dependencies = dependencies;
    }

    /// <summary>
    /// <paramref name="repository"/>, the provider's own, with the database's lock where the context is on Npgsql and its
    /// lock lasts a transaction; <paramref name="repository"/> as it is otherwise.
    /// </summary>
    public static IHistoryRepository Around(IHistoryRepository repository, HistoryRepositoryDependencies dependencies, IServiceProvider services)
        => repository.LockReleaseBehavior == LockReleaseBehavior.Transaction
           && string.Equals(services.GetRequiredService<IDatabaseProvider>().Name, NpgsqlProvider, StringComparison.Ordinal)
            ? new DatabaseLockedHistoryRepository(repository, dependencies)
            : repository;

    /// <inheritdoc />
    public LockReleaseBehavior LockReleaseBehavior => _npgsql.LockReleaseBehavior;

    /// <inheritdoc />
    public IMigrationsDatabaseLock AcquireDatabaseLock()
    {
        _dependencies.RawSqlCommandBuilder.Build(DatabaseMigrationLock.PostgresStatement).ExecuteNonQuery(Parameters());
        return new Held(this, _npgsql.AcquireDatabaseLock());
    }

    /// <inheritdoc />
    public async Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = default)
    {
        await _dependencies.RawSqlCommandBuilder.Build(DatabaseMigrationLock.PostgresStatement)
            .ExecuteNonQueryAsync(Parameters(), cancellationToken).ConfigureAwait(false);
        return new Held(this, await _npgsql.AcquireDatabaseLockAsync(cancellationToken).ConfigureAwait(false));
    }

    // Npgsql implements the two below itself, over the interface's defaults: it makes the table and takes one another
    // connection made at the same moment as made. They are called through the interface, which reaches Npgsql's.

    /// <inheritdoc />
    public bool CreateIfNotExists() => _npgsql.CreateIfNotExists();

    /// <inheritdoc />
    public Task<bool> CreateIfNotExistsAsync(CancellationToken cancellationToken = default) => _npgsql.CreateIfNotExistsAsync(cancellationToken);

    /// <inheritdoc />
    public bool Exists() => _npgsql.Exists();

    /// <inheritdoc />
    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default) => _npgsql.ExistsAsync(cancellationToken);

    /// <inheritdoc />
    public void Create() => _npgsql.Create();

    /// <inheritdoc />
    public Task CreateAsync(CancellationToken cancellationToken = default) => _npgsql.CreateAsync(cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<HistoryRow> GetAppliedMigrations() => _npgsql.GetAppliedMigrations();

    /// <inheritdoc />
    public Task<IReadOnlyList<HistoryRow>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = default)
        => _npgsql.GetAppliedMigrationsAsync(cancellationToken);

    /// <inheritdoc />
    public string GetCreateScript() => _npgsql.GetCreateScript();

    /// <inheritdoc />
    public string GetCreateIfNotExistsScript() => _npgsql.GetCreateIfNotExistsScript();

    /// <inheritdoc />
    public string GetInsertScript(HistoryRow row) => _npgsql.GetInsertScript(row);

    /// <inheritdoc />
    public string GetDeleteScript(string migrationId) => _npgsql.GetDeleteScript(migrationId);

    /// <inheritdoc />
    public string GetBeginIfNotExistsScript(string migrationId) => _npgsql.GetBeginIfNotExistsScript(migrationId);

    /// <inheritdoc />
    public string GetBeginIfExistsScript(string migrationId) => _npgsql.GetBeginIfExistsScript(migrationId);

    /// <inheritdoc />
    public string GetEndIfScript() => _npgsql.GetEndIfScript();

    /// <summary>The statement's parameters as Npgsql gives its own lock's: the context's connection, logged as a migration's.</summary>
    private RelationalCommandParameterObject Parameters()
        => new(_dependencies.Connection, null, null, _dependencies.CurrentContext.Context, _dependencies.CommandLogger, CommandSource.Migrations);

    /// <summary>
    /// Both locks, held until the migration's transaction ends. It names the repository that took them, so taking it
    /// again in a new transaction takes both again.
    /// </summary>
    private sealed class Held(IHistoryRepository repository, IMigrationsDatabaseLock npgsql) : IMigrationsDatabaseLock
    {
        IHistoryRepository IMigrationsDatabaseLock.HistoryRepository => repository;

        public void Dispose() => npgsql.Dispose();

        public ValueTask DisposeAsync() => npgsql.DisposeAsync();
    }
}
