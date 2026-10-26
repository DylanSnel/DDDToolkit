namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// The sample's host without a database (<see cref="SampleFactory.WithoutDatabase"/>), one for a test class: what
/// a class takes as its class fixture when its tests read what the host registers, composes or translates, and
/// store nothing.
/// </summary>
/// <remarks>
/// It needs no Docker, so a class that takes it carries no trait and runs in every build. The host starts when
/// the first test asks for its services.
/// </remarks>
public sealed class SampleWithoutDatabase : IAsyncDisposable
{
    private readonly SampleFactory _host = SampleFactory.WithoutDatabase();

    /// <summary>The services of the host, which starts when they are first asked for.</summary>
    public IServiceProvider Services => _host.Services;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
