namespace DDDToolkit.Tests.Domain;

/// <summary>
/// A <see cref="TimeProvider"/> that only moves when told to. The Entity Framework test project has
/// its own copy; the toolkit deliberately does not ship one, because the .NET ecosystem already has
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> and a test double is not worth a package
/// dependency in a domain library.
/// </summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => _now += by;
}
