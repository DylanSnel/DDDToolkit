using System.Collections.Concurrent;
using Npgsql;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// The machine's clock, set to the database's: what an application has whose servers keep their clocks in step,
/// which is what the sample on Postgres expects. The host stamps the start of a role with its own clock and the
/// policies compare it with the database's <c>now()</c>, so on a database whose clock runs behind the machine's, a
/// container's for one, a role given a moment ago would not be in force yet, and a scenario that gives one and
/// uses it at once would fail for a reason that is none of its own.
/// </summary>
/// <remarks>
/// The difference is measured once per server, the first time a host on it is made. It errs to the side of running
/// behind the database, by no more than the round trip that measured it: a clock that is behind stamps a start
/// the database already counts as past, and one that is ahead is the fault this is here to take away.
/// <para>
/// It runs, it is not moved: a scenario in which time has to pass waits for it
/// (<see cref="SampleOnPostgres.WaitUntilItIsPastAsync"/>).
/// </para>
/// </remarks>
public sealed class DatabaseClock : TimeProvider
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<DatabaseClock>>> Clocks = new(StringComparer.Ordinal);

    private readonly TimeSpan _ahead;

    private DatabaseClock(TimeSpan ahead) => _ahead = ahead;

    /// <summary>The clock of the server <paramref name="connectionString"/> names, measured the first time it is asked for.</summary>
    /// <param name="connectionString">A connection string to any database of the server, without a pool: nothing may stay connected to a database a test is about to copy.</param>
    public static Task<DatabaseClock> OfAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return Clocks.GetOrAdd($"{builder.Host}:{builder.Port}", static (_, measured) => new Lazy<Task<DatabaseClock>>(() => MeasureAsync(measured)), connectionString).Value;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _ahead;

    // Not on a test's cancellation token: the measurement is every later test's too.
    private static async Task<DatabaseClock> MeasureAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT clock_timestamp()", connection);
        var database = new DateTimeOffset(((DateTime)(await command.ExecuteScalarAsync())!).ToUniversalTime(), TimeSpan.Zero);

        // Read after the answer came back: the database's moment lies before it, so the difference is never too large.
        return new DatabaseClock(database - System.GetUtcNow());
    }
}
