namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// A clock that shows the moment it was made at until a test sets it on: for scenarios in which several things
/// must happen at one moment, so that only a tie-break orders them.
/// </summary>
/// <remarks>
/// It stands at the real moment, so tokens issued and checked meanwhile are neither early nor expired. It is
/// not for making a period run out: the database compares a period with its own clock, so a scenario in which
/// time has to pass waits for it (<see cref="SampleOnPostgres.WaitUntilItIsPastAsync"/>).
/// </remarks>
public sealed class StoppedClock : TimeProvider
{
    /// <summary>The moment the clock shows.</summary>
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;
}
