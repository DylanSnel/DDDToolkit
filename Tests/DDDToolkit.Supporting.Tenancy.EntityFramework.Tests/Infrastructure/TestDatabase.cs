namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A test's own database, on SQLite or on Postgres: how a context connects to it, how a test counts a table's rows
/// straight from it, and the few things the two providers write differently, so one test reads the same on both.
/// </summary>
public abstract class TestDatabase : IDisposable
{
    /// <summary>The provider's name, as a test's reason names it.</summary>
    public abstract string Provider { get; }

    /// <summary>
    /// Whether Tenancy's tables and the widgets' exist already. A new SQLite database starts empty and the first
    /// services over it make them; a Postgres one is copied from a template that has them.
    /// </summary>
    public bool HasTables { get; set; }

    /// <summary>
    /// Whether a role's keys are one text array, as Npgsql stores a primitive collection of strings, rather than one
    /// JSON document, as SQLite does.
    /// </summary>
    public abstract bool KeysAreAnArray { get; }

    /// <summary>What a role's keys are asked with in SQL: <c>json_each</c> on SQLite, <c>= ANY</c> over the array on Postgres.</summary>
    public abstract string KeysContain { get; }

    /// <summary>The filter of the unique index on a seat's primary placement, as the provider writes a true boolean.</summary>
    public abstract string PrimaryFilter { get; }

    /// <summary>Points <paramref name="options"/> at this database.</summary>
    public abstract void Use(DbContextOptionsBuilder options);

    /// <summary>Options for a context of a test's own on this database, with nothing else added yet.</summary>
    public DbContextOptionsBuilder<TContext> Options<TContext>()
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        Use(options);
        return options;
    }

    /// <summary>Counts a table's rows past every filter and every caller, straight from the database.</summary>
    public abstract int CountRows(string table);

    /// <summary>
    /// A table as the provider's SQL names it: <c>"Seats"</c> on SQLite, which has no schemas, and
    /// <c>tenancy."Seats"</c> on Postgres.
    /// </summary>
    public abstract string Table(string schema, string name);

    /// <summary>One of Tenancy's tables as the provider's SQL names it.</summary>
    public string TenancyTable(string name) => Table(TestHost.Persistence.TestTenancyContext.Schema, name);

    public abstract void Dispose();
}

/// <summary>Where a test class's databases come from: a new one for each test, and for each services a test makes.</summary>
public abstract class TestDatabases
{
    /// <summary>A new, empty SQLite database in memory for each.</summary>
    public static TestDatabases Sqlite { get; } = new InMemory();

    /// <summary>A new database of the calling test's own, with Tenancy's tables and the widgets'.</summary>
    public abstract Task<TestDatabase> CreateAsync(CancellationToken cancellationToken);

    /// <summary>Services over a new database of their own, which they dispose of with themselves.</summary>
    public async Task<TestServices> ServicesAsync(Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configure = null)
        => new(configure: configure, database: await CreateAsync(TestContext.Current.CancellationToken), ownsDatabase: true);

    private sealed class InMemory : TestDatabases
    {
        public override Task<TestDatabase> CreateAsync(CancellationToken cancellationToken) => Task.FromResult<TestDatabase>(new SqliteDatabase());
    }
}
