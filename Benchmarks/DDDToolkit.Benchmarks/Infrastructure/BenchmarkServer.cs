using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DDDToolkit.Benchmarks.Infrastructure;

/// <summary>The database a round-trip benchmark runs on.</summary>
public enum BenchmarkServerKind
{
    /// <summary>SQLite in memory, in the process: a statement costs no round trip.</summary>
    Sqlite,

    /// <summary>Postgres 17 in a Docker container on this machine: every statement is a round trip over a socket.</summary>
    Postgres,
}

/// <summary>
/// One database for a benchmark: SQLite in memory, or Postgres in a container that is started for the benchmark and
/// removed after it. Docker must run for the Postgres case.
/// </summary>
public sealed class BenchmarkServer : IAsyncDisposable
{
    /// <summary>The Postgres image: Postgres 17 on Alpine.</summary>
    public const string PostgresImage = "postgres:17-alpine";

    private readonly SqliteConnection? _sqlite;
    private readonly PostgreSqlContainer? _postgres;

    private BenchmarkServer(SqliteConnection? sqlite, PostgreSqlContainer? postgres)
    {
        _sqlite = sqlite;
        _postgres = postgres;
    }

    /// <summary>Opens SQLite in memory, or starts a Postgres container.</summary>
    public static async Task<BenchmarkServer> StartAsync(BenchmarkServerKind kind)
    {
        if (kind == BenchmarkServerKind.Sqlite)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            return new BenchmarkServer(connection, null);
        }

        var container = new PostgreSqlBuilder(PostgresImage).Build();
        await container.StartAsync();
        return new BenchmarkServer(null, container);
    }

    /// <summary>Points a context's options at this database.</summary>
    public DbContextOptionsBuilder Use(DbContextOptionsBuilder options)
        => _sqlite is not null ? options.UseSqlite(_sqlite) : options.UseNpgsql(_postgres!.GetConnectionString());

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_sqlite is not null)
        {
            await _sqlite.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }
}
