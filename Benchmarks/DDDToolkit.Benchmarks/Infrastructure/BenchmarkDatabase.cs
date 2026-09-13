using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Benchmarks.Infrastructure;

/// <summary>
/// One in-memory SQLite database, alive for as long as its connection is open. SQLite in memory is
/// the fastest database a benchmark can talk to, which is the point: it makes the part of a round
/// trip that belongs to Entity Framework and to the identifier as large a share of the total as it
/// will ever be. On a real server the same difference is smaller, never larger.
/// </summary>
public sealed class BenchmarkDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Opens a fresh database and creates the schema for both aggregates.</summary>
    public BenchmarkDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    /// <summary>A context with an empty change tracker, as a request-scoped one would be.</summary>
    public OrderContext CreateContext()
        => new(new DbContextOptionsBuilder<OrderContext>().UseSqlite(_connection).Options);

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();
}
