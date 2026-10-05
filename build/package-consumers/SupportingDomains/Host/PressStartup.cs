using Microsoft.Extensions.DependencyInjection;

namespace Acme.Press.Host;

/// <summary>
/// What the host does with the module: it calls the module's registration, and adds the modules' keys with the one
/// call Tenancy's generator writes into the host, which declares no module, from the lists they mark. This does not
/// compile when that generator did not arrive.
/// </summary>
public static class PressStartup
{
    /// <summary>Registers the module, and the keys of every module the host references.</summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddPress(this IServiceCollection services) => PressHost.Add(services).AddTenancyPermissionsOfModules();
}
