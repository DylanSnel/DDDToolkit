using Microsoft.Extensions.DependencyInjection;

namespace Acme.Press.Host;

/// <summary>What the host does with the module: it calls the module's registration, and nothing generated.</summary>
public static class PressStartup
{
    /// <summary>Registers the module.</summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddPress(this IServiceCollection services) => PressHost.Add(services);
}
