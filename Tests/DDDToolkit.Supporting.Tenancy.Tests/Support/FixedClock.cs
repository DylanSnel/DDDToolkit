namespace DDDToolkit.Supporting.Tenancy.Tests.Support;

/// <summary>A clock that says what the test tells it to, so grant periods can be asked about at a known moment.</summary>
/// <param name="now">The moment it starts at.</param>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    /// <summary>A realistic moment to start at: a version 7 id cannot encode a time before 1970.</summary>
    public static readonly DateTimeOffset Start = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A clock at <see cref="Start"/>.</summary>
    public FixedClock()
        : this(Start)
    {
    }

    /// <summary>What the clock says now.</summary>
    public DateTimeOffset Now { get; private set; } = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>Moves the clock on.</summary>
    public void Advance(TimeSpan by) => Now += by;
}
