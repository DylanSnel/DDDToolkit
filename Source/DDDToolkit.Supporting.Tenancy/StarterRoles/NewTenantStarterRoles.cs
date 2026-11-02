using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Runs every <see cref="ITenantStarterRoles"/> in a tenant that was just provisioned, for <c>TenantManager.ProvisionAsync</c>.
/// <c>AddTenancyCore</c> registers it. An application never takes it.
/// <para>
/// The tenant exists when this runs, so nothing that goes wrong here reaches the caller of <c>ProvisionAsync</c>. A failure is
/// logged as an error, and the next <c>services.SyncStarterRoles()</c> runs them again.
/// </para>
/// </summary>
/// <param name="scopes">Makes the scope each one runs in.</param>
/// <param name="logger">Where a failure is logged.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class NewTenantStarterRoles(IServiceScopeFactory scopes, ILogger<NewTenantStarterRoles>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<NewTenantStarterRoles>.Instance;

    /// <summary>Runs them for the tenant the current work acts in: the system work <c>ProvisionAsync</c> began in it.</summary>
    /// <param name="tenant">The tenant's id, for the log.</param>
    /// <param name="cancellationToken">Cancels the work. Cancelled, it logs and returns as for any other failure.</param>
    public async Task RunAsync(object tenant, CancellationToken cancellationToken)
    {
        try
        {
            var run = await TenantStarterRolesRun.RunAsync(scopes, cancellationToken).ConfigureAwait(false);
            foreach (var failure in run.Failed)
            {
                _logger.LogError(failure, "Tenant {Tenant} is provisioned, and another module's starter roles could not be made there. The next sync of the starter roles makes them.", tenant);
            }
        }
        catch (Exception failure)
        {
            _logger.LogError(failure, "Tenant {Tenant} is provisioned, and other modules' starter roles could not be made there. The next sync of the starter roles makes them.", tenant);
        }
    }
}
