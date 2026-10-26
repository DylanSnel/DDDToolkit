using System.Collections.Concurrent;
using Npgsql;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>
/// The machine's clock, set to the database's: what an application has whose servers keep their clocks in step,
/// which is what Tenancy on Postgres expects. A grant's start is stamped by the application's clock and the
/// policies compare it with the database's <c>now()</c>, so on a database whose clock runs behind the machine's, a
/// container's for one, a grant made a moment ago would not be live yet, and the tests that use it at once would
/// fail for a reason that is none of theirs.
/// <para>
/// The difference is measured once per server, the first time a test's services ask. It errs to the side of
/// running behind the database, by no more than the round trip that measured it: a clock that is behind stamps a
/// start the database already counts as past, and one that is ahead is the fault this is here to take away.
/// </para>
/// </summary>
public sealed class DatabaseClock : TimeProvider
{
    private static readonly ConcurrentDictionary<string, Lazy<DatabaseClock>> Clocks = new(StringComparer.Ordinal);

    private readonly TimeSpan _ahead;

    private DatabaseClock(TimeSpan ahead) => _ahead = ahead;

    /// <summary>The clock of the server <paramref name="connectionString"/> names, measured the first time it is asked for.</summary>
    public static DatabaseClock Of(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return Clocks.GetOrAdd($"{builder.Host}:{builder.Port}", static (_, measured) => new Lazy<DatabaseClock>(() => Measure(measured)), connectionString).Value;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _ahead;

    private static DatabaseClock Measure(string connectionString)
    {
        // Unpooled, so nothing stays connected to a database a test may be about to copy.
        using var connection = new NpgsqlConnection(TenancyPostgres.Unpooled(connectionString));
        connection.Open();
        using var command = new NpgsqlCommand("SELECT clock_timestamp()", connection);
        var database = new DateTimeOffset((DateTime)command.ExecuteScalar()!, TimeSpan.Zero);

        // Read after the answer came back: the database's moment lies before it, so the difference is never too large.
        return new DatabaseClock(database - System.GetUtcNow());
    }
}
