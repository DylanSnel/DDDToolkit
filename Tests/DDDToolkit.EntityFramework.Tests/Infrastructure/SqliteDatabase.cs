using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// One in-memory SQLite database per test: the connection stays open for the lifetime of the
/// fixture (closing it would drop the database) and every context created from it gets a fresh
/// change tracker, so read-backs come from the database and never from cached entities.
/// </summary>
public sealed class SqliteDatabase : IDisposable
{
    public SqliteDatabase()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();
    }

    public SqliteConnection Connection { get; }

    public DbContextOptions<TContext> Options<TContext>(Action<DbContextOptionsBuilder<TContext>>? configure = null) where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>().UseSqlite(Connection);
        configure?.Invoke(builder);
        return builder.Options;
    }

    /// <summary>A plain <see cref="LibraryContext"/> without DDDToolkit interceptors, optionally with extra configuration.</summary>
    public LibraryContext CreateLibraryContext(Action<DbContextOptionsBuilder<LibraryContext>>? configure = null)
        => new(Options(configure));

    /// <summary>Creates the schema through a throw-away context.</summary>
    public void EnsureCreated<TContext>(Func<TContext> factory) where TContext : DbContext
    {
        using var context = factory();
        context.Database.EnsureCreated();
    }

    public int CountRows(string table)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose() => Connection.Dispose();
}
