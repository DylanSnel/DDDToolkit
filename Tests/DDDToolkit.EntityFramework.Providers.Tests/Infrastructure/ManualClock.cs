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

    /// <summary>
    /// Puts the clock at an exact instant, offset included.
    /// <para>
    /// A real <see cref="TimeProvider"/> reports UTC, so an offset other than zero here is deliberately
    /// unreasonable. It is the cheapest way to hand the outbox a timestamp that is not already
    /// normalized, which is what the storage layer promises to cope with and what a test should
    /// therefore try.
    /// </para>
    /// </summary>
    public void Set(DateTimeOffset now) => _now = now;
}
