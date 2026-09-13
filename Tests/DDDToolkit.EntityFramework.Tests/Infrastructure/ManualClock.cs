namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>A <see cref="TimeProvider"/> that only moves when told to.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
