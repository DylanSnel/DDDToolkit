namespace DDDToolkit.Supporting.Tenancy.Tests.Support;

/// <summary>
/// A service provider with nothing in it, for an access manager a test makes by hand: its current user then reads the
/// toolkit's ambient caller, and it has no storage package to read the tenants to visit.
/// </summary>
public sealed class NoServices : IServiceProvider
{
    private NoServices()
    {
    }

    /// <summary>The one provider; it keeps no state.</summary>
    public static NoServices Instance { get; } = new();

    /// <inheritdoc />
    public object? GetService(Type serviceType) => null;
}
