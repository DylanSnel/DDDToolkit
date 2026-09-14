namespace DDDToolkit.Examples.Tests;

/// <summary>
/// A clock that does not move. The toolkit ships no fake one on purpose: <c>FakeTimeProvider</c> from
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> already exists, and where a test only needs a fixed
/// instant this is the whole of it.
/// </summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
