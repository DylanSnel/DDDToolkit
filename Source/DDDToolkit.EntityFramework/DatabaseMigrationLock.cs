using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DDDToolkit.EntityFramework;

/// <summary>
/// The lock the migrations of every context the toolkit wires on Postgres take: one for the whole database, so the
/// modules and services that migrate one database wait for each other, and one on another database waits for none.
/// </summary>
/// <remarks>
/// <para>
/// Entity Framework takes a lock of its own while it migrates. On SQL Server and SQLite that lock is the database's.
/// On Postgres, Npgsql's is the migration history table, <c>LOCK TABLE ... IN ACCESS EXCLUSIVE MODE</c>, and the toolkit
/// keeps every module's history in the module's own schema (<c>UseDDDToolkit</c>). So the lock kept two services from
/// migrating one module at once, and let two modules migrate side by side. Where their migrations make something they
/// share, the schema <c>ddd</c> say, each makes it in its own transaction: the second asks whether it exists while the
/// first has made it and not yet committed, makes it as well, and fails on <c>pg_namespace</c>'s unique index (23505)
/// as soon as the first commits. <c>IF NOT EXISTS</c> does not help between two transactions.
/// </para>
/// <para>
/// So the history repository of a context on Npgsql, wired with <c>UseDDDToolkit</c>, <c>UseDDDToolkitCore</c> or
/// <c>UseDDDToolkitDesignTime</c>, takes <see cref="PostgresStatement"/> first, in the transaction it migrates in, and
/// then Npgsql's own lock. The lock is a transaction-level advisory lock: it lasts until that transaction commits or
/// rolls back, and is never left behind on a connection that goes back to its pool. It holds for <c>Migrate()</c>,
/// <c>MigrateAsync()</c>, <c>dotnet ef database update</c> and a migration bundle, which all migrate through the same
/// repository: the last two through the design-time factory, which calls <c>UseDDDToolkitDesignTime()</c> as DDD00074
/// asks. Advisory locks belong to a database, so services on two databases of one server never wait for each other. The
/// wait counts against the context's command timeout, as the wait for Npgsql's own lock does.
/// </para>
/// <para>
/// A context leaves it out with <see cref="WithoutDatabaseMigrationLock(DbContextOptionsBuilder)"/>. Where Entity
/// Framework takes no lock, it takes none either: a migrations script, and migrations run inside a transaction the
/// application began. A history repository of the host's own, put in with <c>ReplaceService</c>, decides its own lock, as
/// it decides where its history is; so do options with an internal service provider of their own, which the toolkit
/// does not reach.
/// </para>
/// </remarks>
public static class DatabaseMigrationLock
{
    /// <summary>
    /// The key of the advisory lock, 4919131821860013906: the bytes of <c>DDDTMIGR</c>, <c>0x444444544D494752</c>. In
    /// <c>pg_locks</c> it is the lock of type <c>advisory</c> with <c>classid</c> 1145324628, <c>objid</c> 1296648018 and
    /// <c>objsubid</c> 1, which shows who is migrating and who waits:
    /// <code>
    /// SELECT pid, granted FROM pg_catalog.pg_locks
    /// WHERE locktype = 'advisory' AND classid = 1145324628 AND objid = 1296648018 AND objsubid = 1;
    /// </code>
    /// It lies outside the 32-bit keys pgmq locks its queues with, <c>hashtext('pgmq.queue_' || name)</c>.
    /// </summary>
    public const long PostgresKey = 0x4444_4454_4D49_4752;

    /// <summary>
    /// The statement that takes the lock, until the transaction it runs in ends. A script the application runs itself,
    /// outside its migrations, waits for every migration of the database by running it first, in the script's own
    /// transaction:
    /// <code>
    /// BEGIN;
    /// SELECT pg_catalog.pg_advisory_xact_lock(4919131821860013906);
    /// -- the script
    /// COMMIT;
    /// </code>
    /// </summary>
    public const string PostgresStatement = "SELECT pg_catalog.pg_advisory_xact_lock(4919131821860013906)";

    /// <summary>
    /// Leaves the context's migrations without the toolkit's lock for the whole database: they take Entity Framework's
    /// own alone, which on Postgres is the context's migration history table. For a context whose migrations make
    /// nothing another context's do, and should not wait for one that takes long.
    /// <code>
    /// services.AddDbContext&lt;ReportingContext&gt;((services, options) => options
    ///     .UseNpgsql(connectionString)
    ///     .UseDDDToolkit(services)
    ///     .WithoutDatabaseMigrationLock());
    /// </code>
    /// <c>dotnet ef database update</c> and a migration bundle make the context with its design-time factory, which
    /// leaves the lock out the same way, after <c>UseDDDToolkitDesignTime()</c>. It changes nothing on SQL Server or
    /// SQLite, whose lock is Entity Framework's and already the database's.
    /// <para>
    /// It comes after the toolkit's call, which brings the lock, and a later toolkit call keeps it out. The context shares
    /// Entity Framework's internal services with the contexts that take the lock: the choice is read as each one migrates.
    /// </para>
    /// </summary>
    /// <param name="optionsBuilder">The context's options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsBuilder"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The options have none of <c>UseDDDToolkit</c>, <c>UseDDDToolkitCore</c> and <c>UseDDDToolkitDesignTime</c> yet:
    /// there is no lock to leave out.
    /// </exception>
    public static DbContextOptionsBuilder WithoutDatabaseMigrationLock(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        if (optionsBuilder.Options.FindExtension<MigrationHistoryExtension>() is null)
        {
            throw new InvalidOperationException(
                "WithoutDatabaseMigrationLock() leaves out the migration lock that UseDDDToolkit, UseDDDToolkitCore or UseDDDToolkitDesignTime() brings, and these options have none of them yet. " +
                "Call it after the toolkit's call: options.UseNpgsql(connectionString).UseDDDToolkit(services).WithoutDatabaseMigrationLock(). " +
                "A context without the toolkit takes Entity Framework's lock alone already.");
        }

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(MigrationHistoryExtension.WithoutDatabaseLock);
        return optionsBuilder;
    }

    /// <summary>
    /// <see cref="WithoutDatabaseMigrationLock(DbContextOptionsBuilder)"/> for the options of one context type, so a
    /// design-time factory takes their <c>Options</c> in the same expression.
    /// </summary>
    /// <typeparam name="TContext">The context.</typeparam>
    /// <param name="optionsBuilder">The context's options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsBuilder"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The options have none of the toolkit's calls yet.</exception>
    public static DbContextOptionsBuilder<TContext> WithoutDatabaseMigrationLock<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)WithoutDatabaseMigrationLock((DbContextOptionsBuilder)optionsBuilder);
}
