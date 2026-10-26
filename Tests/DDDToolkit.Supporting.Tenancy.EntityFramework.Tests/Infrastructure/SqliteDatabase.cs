using Microsoft.Data.Sqlite;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests.Infrastructure;

/// <summary>
/// One SQLite database per test. By default it is in memory: the connection stays open for the lifetime of the
/// fixture (closing it would drop the database) and every context created from it gets a fresh change tracker,
/// so read-backs come from the database and never from cached entities. Every context of a test uses this one
/// connection, so Tenancy's tables and a consumer's are in one database, as the read model needs.
/// <para>
/// <see cref="InFile"/> puts it in a file instead, where every context opens a connection of its own, as on a
/// server database: for a test about which connection something runs on.
/// </para>
/// </summary>
public sealed class SqliteDatabase : TestDatabase
{
    private readonly string? _file;

    public SqliteDatabase()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();
    }

    private SqliteDatabase(string file)
    {
        _file = file;

        // Without pooling, so nothing holds the file once the test is done with it.
        ConnectionString = new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString();
        Connection = new SqliteConnection(ConnectionString);
        Connection.Open();
    }

    /// <summary>The connection every context uses in memory; in a file, the one the test's own reads use.</summary>
    public SqliteConnection Connection { get; }

    /// <summary>For a database in a file, what each context connects with; <see langword="null"/> in memory.</summary>
    public string? ConnectionString { get; }

    /// <inheritdoc />
    public override string Provider => "SQLite";

    /// <inheritdoc />
    public override bool KeysAreAnArray => false;

    /// <inheritdoc />
    public override string KeysContain => "json_each";

    /// <inheritdoc />
    public override string PrimaryFilter => "\"IsPrimary\" = 1";

    /// <summary>A database in a new file of its own, deleted when it is disposed.</summary>
    public static SqliteDatabase InFile()
        => new(Path.Combine(Path.GetTempPath(), "tenancy-tests-" + Guid.NewGuid().ToString("N") + ".db"));

    /// <inheritdoc />
    public override void Use(DbContextOptionsBuilder options)
    {
        if (ConnectionString is { } file)
        {
            options.UseSqlite(file);
        }
        else
        {
            options.UseSqlite(Connection);
        }
    }

    /// <inheritdoc />
    public override int CountRows(string table)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <inheritdoc />
    public override string Table(string schema, string name) => "\"" + name + "\"";

    public override void Dispose()
    {
        Connection.Dispose();
        if (_file is not null)
        {
            File.Delete(_file);
        }
    }
}
