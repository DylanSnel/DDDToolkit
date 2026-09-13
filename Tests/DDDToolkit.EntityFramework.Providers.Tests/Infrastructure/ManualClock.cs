namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>A <see cref="TimeProvider"/> that only moves when told to.</summary>
/// <param name="start">The instant it reports until something advances it.</param>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => _now += by;
}
