namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>
/// A clock stopped at one moment, the database's <c>now()</c> read just before, so a question asked in C# and the same
/// question asked in SQL decide what is live at the same moment. It is never moved: on Postgres, time passes for a test by
/// moving the data, never the clock.
/// </summary>
/// <param name="now">The moment the clock says it is.</param>
public sealed class StoppedClock(DateTimeOffset now) : TimeProvider
{
    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => now;
}
