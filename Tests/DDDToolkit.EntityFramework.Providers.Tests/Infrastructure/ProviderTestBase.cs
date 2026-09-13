namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>
/// Shared plumbing for a test that needs a real database: one database per test, created before the
/// test and dropped after it.
/// <para>
/// Every test body starts with <see cref="SkipIfUnavailable"/>. That is deliberately explicit rather
/// than hidden in a base class hook, because a skipped test and a passing test look the same in a CI
/// summary and the difference has to be visible in the code that decides it.
/// </para>
/// </summary>
public abstract class ProviderTestBase(ProviderFixture fixture) : IAsyncLifetime
{
    /// <summary>The container this test talks to.</summary>
    protected ProviderFixture Fixture { get; } = fixture;

    /// <summary>This test's own database. Null when the container never started.</summary>
    protected ProviderDatabase Database { get; private set; } = null!;

    /// <summary>The token xunit cancels when the test is abandoned.</summary>
    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        if (Fixture.IsAvailable)
        {
            Database = await Fixture.CreateDatabaseAsync(Cancellation);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        if (Database is not null)
        {
            await Database.DisposeAsync();
        }
    }

    /// <summary>Skips the test, naming the real reason, when the container did not start.</summary>
    protected void SkipIfUnavailable() => Assert.SkipWhen(!Fixture.IsAvailable, Fixture.SkipReason ?? string.Empty);
}
