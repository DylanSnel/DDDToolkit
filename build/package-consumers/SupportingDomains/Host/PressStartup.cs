using Microsoft.Extensions.DependencyInjection;

namespace Acme.Press.Host;

/// <summary>
/// What the host does with the module: it calls the module's registration, adds the modules' keys with the one call
/// Tenancy's generator writes into the host, which declares no module, from the lists they mark, and registers every
/// context marked [SupabaseMigrations] for the start-up check with the one call the Supabase package's generator writes
/// into it. This does not compile when either generator did not arrive.
/// </summary>
public static class PressStartup
{
    /// <summary>Registers the module, the keys of every module the host references, and the check of their migrations.</summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddPress(this IServiceCollection services) => PressHost.Add(services).AddTenancyPermissionsOfModules().AddSupabaseMigrations();
}
