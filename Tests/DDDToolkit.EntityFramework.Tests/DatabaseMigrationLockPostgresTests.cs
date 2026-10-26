using System.Diagnostics;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The migrations of two modules on one Postgres database, run at the same moment, as two services run theirs when they
/// start together. Both first migrations make the schema <c>ddd</c>. Entity Framework's own lock on Npgsql is each
/// module's history table, so without the toolkit's lock the two run side by side, and the one that makes <c>ddd</c>
/// second fails on <c>pg_namespace</c>'s unique index (23505). With it they run one after the other, every time. The
/// lock is the database's: a migration of another database does not wait for it, and every way into the migrations,
/// <c>MigrateAsync</c>, <c>Migrate</c> and the design-time factory's migrator that <c>dotnet ef database update</c> and a
/// bundle use, takes it.
/// </summary>
public sealed class DatabaseMigrationLockPostgresTests(MigrationLockPostgres postgres) : IClassFixture<MigrationLockPostgres>, IDisposable
{
    /// <summary>How long a test waits for something that should happen in well under a second.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly ServiceProvider _services = new ServiceCollection().AddDDDToolkitEntityFramework().BuildServiceProvider();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task Without_the_lock_two_modules_that_both_make_ddd_migrate_side_by_side_and_one_fails_on_the_schema()
    {
        const int Rounds = 10;
        var failed = 0;

        for (var round = 0; round < Rounds; round++)
        {
            var connectionString = await postgres.CreateDatabaseAsync(Cancellation);
            var outcomes = await MigrateSideBySideAsync(connectionString, withoutLock: true);

            var refused = outcomes.OfType<PostgresException>().ToList();
            outcomes.Where(outcome => outcome is not null and not PostgresException).Should().BeEmpty("a migration fails on the schema or not at all");
            refused.Should().AllSatisfy(exception =>
            {
                exception.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
                exception.ConstraintName.Should().Be("pg_namespace_nspname_index", "both made {0}, each in its own transaction", SharedSchema.Name);
            });
            failed += refused.Count;
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Without the lock, {failed} of {Rounds} rounds failed on pg_namespace_nspname_index.");
        failed.Should().BeGreaterThan(0, "two first migrations that run side by side and both make the schema fail now and then, here nearly always");
    }

    [Fact]
    public async Task With_the_lock_two_modules_that_both_make_ddd_migrate_one_after_the_other_every_time()
    {
        const int Rounds = 25;

        for (var round = 0; round < Rounds; round++)
        {
            var connectionString = await postgres.CreateDatabaseAsync(Cancellation);
            var outcomes = await MigrateSideBySideAsync(connectionString, withoutLock: false);

            outcomes.Should().AllSatisfy(outcome => outcome.Should().BeNull(), "round {0}: the second waits for the first", round + 1);

            var dock = await NotesAsync(connectionString, DockContext.Table);
            var quay = await NotesAsync(connectionString, QuayContext.Table);
            (dock.Ended <= quay.Began || quay.Ended <= dock.Began).Should().BeTrue(
                "round {0}: one migration ended before the other began (dock {1:O} to {2:O}, quay {3:O} to {4:O})",
                round + 1, dock.Began, dock.Ended, quay.Began, quay.Ended);
        }
    }

    [Fact]
    public async Task The_lock_is_the_databases_alone_and_a_migration_of_another_database_does_not_wait_for_it()
    {
        var held = await postgres.CreateDatabaseAsync(Cancellation);
        var other = await postgres.CreateDatabaseAsync(Cancellation);
        await using var holder = await HoldTheLockAsync(held);

        // Another database of the same server: nothing holds its lock, so the migration runs at once.
        await using (var dock = Wired<DockContext>(other))
        {
            await dock.Database.MigrateAsync(Cancellation).WaitAsync(Patience, Cancellation);
        }

        (await AppliedAsync<DockContext>(other)).Should().Equal(CreateDock.Id);

        // The held database: the migration waits, as the database says, until the lock is let go.
        await using (var dock = Wired<DockContext>(held))
        {
            var migrating = dock.Database.MigrateAsync(Cancellation);
            await WaitForWaitersAsync(held, migrating);

            await holder.DisposeAsync();
            await migrating.WaitAsync(Patience, Cancellation);
        }

        (await AppliedAsync<DockContext>(held)).Should().Equal(CreateDock.Id);
    }

    [Fact]
    public async Task Every_way_into_the_migrations_takes_the_lock_and_a_context_that_leaves_it_out_does_not_wait()
    {
        var connectionString = await postgres.CreateDatabaseAsync(Cancellation);
        await using var holder = await HoldTheLockAsync(connectionString);

        // A context that leaves the lock out takes Entity Framework's alone, and migrates while the lock is held.
        await using (var without = Wired<DockContext>(connectionString, withoutLock: true))
        {
            await without.Database.MigrateAsync(Cancellation).WaitAsync(Patience, Cancellation);
        }

        // The host's MigrateAsync and Migrate, the base alone, and the migrator of a design-time factory, which is what
        // dotnet ef database update and a migration bundle run: each asks for the lock, and waits while it is held.
        // Entity Framework asks for it before it reads what is applied, so a database with nothing to apply waits too.
        await using var asynchronous = Wired<QuayContext>(connectionString);
        await using var synchronous = Wired<DockContext>(connectionString);
        var coreOptions = new DbContextOptionsBuilder<QuayContext>().UseNpgsql(Unpooled(connectionString));
        coreOptions.UseDDDToolkitCore(_services);
        await using var core = new QuayContext(coreOptions.Options);
        await using var designTime = new DockContext(new DbContextOptionsBuilder<DockContext>().UseNpgsql(Unpooled(connectionString)).UseDDDToolkitDesignTime().Options);

        Task[] waiting =
        [
            asynchronous.Database.MigrateAsync(Cancellation),
            Task.Run(() => synchronous.Database.Migrate(), Cancellation),
            core.Database.MigrateAsync(Cancellation),
            Task.Run(() => designTime.GetService<IMigrator>().Migrate(), Cancellation),
        ];

        await WaitForWaitersAsync(connectionString, waiting);

        await holder.DisposeAsync();
        await Task.WhenAll(waiting).WaitAsync(Patience, Cancellation);

        (await AppliedAsync<DockContext>(connectionString)).Should().Equal(CreateDock.Id);
        (await AppliedAsync<QuayContext>(connectionString)).Should().Equal(CreateQuay.Id);
    }

    [Fact]
    public async Task A_migration_waits_for_the_lock_as_long_as_its_command_timeout_and_then_fails_with_a_timeout()
    {
        // As it waits for Entity Framework's own lock: a service that may wait behind a long migration gives its own a
        // timeout as long, with SetCommandTimeout, rather than waiting without end for one that never finishes.
        var connectionString = await postgres.CreateDatabaseAsync(Cancellation);
        await using var holder = await HoldTheLockAsync(connectionString);
        await using var dock = Wired<DockContext>(connectionString);
        dock.Database.SetCommandTimeout(TimeSpan.FromSeconds(1));

        var waited = Stopwatch.StartNew();
        var refused = await FluentActions.Awaiting(() => dock.Database.MigrateAsync(Cancellation)).Should().ThrowAsync<Exception>();

        waited.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.9)).And.BeLessThan(Patience);
        refused.Which.GetBaseException().Should().BeOfType<TimeoutException>("Entity Framework logs the statement that timed out, the lock's");
    }

    /// <summary>
    /// Migrates the dock and the quay on <paramref name="connectionString"/>, each from a task of its own, both let go
    /// at the same moment, and returns what each failed with, or <see langword="null"/> for one that went through.
    /// </summary>
    private async Task<Exception?[]> MigrateSideBySideAsync(string connectionString, bool withoutLock)
    {
        await using var dock = Wired<DockContext>(connectionString, withoutLock);
        await using var quay = Wired<QuayContext>(connectionString, withoutLock);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> MigrateAsync(DbContext context)
        {
            await go.Task;
            try
            {
                await context.Database.MigrateAsync(Cancellation);
                return null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return exception;
            }
        }

        var migrating = new[] { Task.Run(() => MigrateAsync(dock), Cancellation), Task.Run(() => MigrateAsync(quay), Cancellation) };
        go.SetResult();
        return await Task.WhenAll(migrating).WaitAsync(Patience, Cancellation);
    }

    /// <summary>
    /// A module's context as the host wires it, with the one call, and without the lock where asked. Its connections are
    /// not pooled: every round has a database of its own, and the idle pools of them all would fill the server.
    /// </summary>
    private TContext Wired<TContext>(string connectionString, bool withoutLock = false)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>().UseNpgsql(Unpooled(connectionString)).UseDDDToolkit(_services);
        if (withoutLock)
        {
            options.WithoutDatabaseMigrationLock();
        }

        return (TContext)Activator.CreateInstance(typeof(TContext), options.Options)!;
    }

    /// <summary>The migrations of <typeparamref name="TContext"/> applied in the database.</summary>
    private async Task<List<string>> AppliedAsync<TContext>(string connectionString)
        where TContext : DbContext
    {
        await using var context = Wired<TContext>(connectionString);
        return [.. await context.Database.GetAppliedMigrationsAsync(Cancellation)];
    }

    /// <summary>
    /// The toolkit's lock of the database, taken in a transaction that lasts until the connection returned is disposed:
    /// what a migration of another service holds while it runs.
    /// </summary>
    private static async Task<NpgsqlConnection> HoldTheLockAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(Unpooled(connectionString));
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand("BEGIN; " + DatabaseMigrationLock.PostgresStatement, connection);
        await command.ExecuteNonQueryAsync(Cancellation);
        return connection;
    }

    /// <summary>
    /// Waits until as many connections of the database wait for the toolkit's lock, as <c>pg_locks</c> says, as there
    /// are <paramref name="migrating"/>, none of which may finish in the meantime.
    /// </summary>
    private static async Task WaitForWaitersAsync(string connectionString, params Task[] migrating)
    {
        var count = migrating.Length;
        var classId = DatabaseMigrationLock.PostgresKey >> 32;
        var objectId = DatabaseMigrationLock.PostgresKey & 0xFFFF_FFFF;
        var stopwatch = Stopwatch.StartNew();
        await using var connection = new NpgsqlConnection(Unpooled(connectionString));
        await connection.OpenAsync(Cancellation);

        while (true)
        {
            await using var command = new NpgsqlCommand(
                $"""
                SELECT pg_catalog.count(*)::int FROM pg_catalog.pg_locks
                WHERE locktype = 'advisory' AND NOT granted AND objsubid = 1
                  AND classid = {classId} AND objid = {objectId}
                  AND database = (SELECT oid FROM pg_catalog.pg_database WHERE datname = pg_catalog.current_database())
                """,
                connection);
            var waiting = (int)(await command.ExecuteScalarAsync(Cancellation))!;

            foreach (var finished in migrating.Where(task => task.IsCompleted))
            {
                await finished;
                finished.IsCompleted.Should().BeFalse("a migration should not finish while the lock of its database is held");
            }

            if (waiting >= count)
            {
                return;
            }

            stopwatch.Elapsed.Should().BeLessThan(Patience, "{0} migrations should be waiting for the lock by now", count);
            await Task.Delay(50, Cancellation);
        }
    }

    /// <summary>When the migration of a module noted it began and ended, by the database's clock.</summary>
    private static async Task<(DateTimeOffset Began, DateTimeOffset Ended)> NotesAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(Unpooled(connectionString));
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            $"""SELECT "Step", "At" FROM "{SharedSchema.Name}"."{table}" """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);

        var notes = new Dictionary<string, DateTimeOffset>();
        while (await reader.ReadAsync(Cancellation))
        {
            notes[reader.GetString(0)] = reader.GetFieldValue<DateTimeOffset>(1);
        }

        return (notes["began"], notes["ended"]);
    }

    private static string Unpooled(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
}
