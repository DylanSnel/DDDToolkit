using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// The work a test begins that is not a caller's own, begun as the sample's code begins it: on the access manager, the
/// <c>TenancyAccessManager</c> the generator wrote into the Tenants module, closed over its ids. A test begins it around
/// what it sends to a host, outside any scope of the host's, so it is made here as the container makes it. A
/// <c>Begin</c> reads and changes only the flow of work, so the one made here serves every test.
/// </summary>
internal static class SampleAccess
{
    /// <summary>The access manager, over no services: what a test asks of it reads the flow of work alone.</summary>
    public static TenancyAccessManager Manager { get; } = new(TenancyCatalogue.Build(new ApplicationCatalogue(), []), TimeProvider.System, NoServices.Instance);

    /// <summary>A service provider with nothing in it.</summary>
    private sealed class NoServices : IServiceProvider
    {
        public static NoServices Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
