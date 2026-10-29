using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Runs <see cref="IStarterRoleSync"/> once, in the background, once the host has started: what
/// <see cref="TenancyServiceCollectionExtensions.SyncStarterRoles"/> registers.
/// </summary>
/// <remarks>
/// It waits for <see cref="IHostApplicationLifetime.ApplicationStarted"/>, so it runs after the host's start-up
/// checks, which run before anything starts, and after every hosted service has started, a seeding of the host's
/// own or the server that binds its port included; the host serves while it runs. A tenant it could not sync is
/// logged as an error and left for the next start, and so is a sync that failed as a whole: the host keeps running
/// either way, with the roles it had. A host that registered no <see cref="IStarterRoleSync"/> is a host composed
/// wrong, and stops, saying so.
/// </remarks>
internal sealed class StarterRoleSyncService(IServiceProvider services, IHostApplicationLifetime lifetime, ILogger<StarterRoleSyncService>? logger = null) : BackgroundService
{
    private readonly ILogger _logger = logger ?? NullLogger<StarterRoleSyncService>.Instance;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await StartedAsync(stoppingToken).ConfigureAwait(false))
        {
            return;
        }

        var sync = services.GetService<IStarterRoleSync>()
            ?? throw new InvalidOperationException(
                "SyncStarterRoles() runs the IStarterRoleSync Tenancy's storage registers, and none is registered. Call services.AddTenancy<TContext>(...) "
                + "from Tenancy's Entity Framework package, or register an IStarterRoleSync of your own.");

        StarterRoleSyncReport report;
        try
        {
            report = await sync.SyncAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception failure)
        {
            _logger.LogError(failure, "The starter roles could not be synced: no tenant's roles changed. The host's next start tries again.");
            return;
        }

        foreach (var (tenant, error) in report.Failed)
        {
            _logger.LogError(error, "The roles of tenant {Tenant} could not follow their starter roles. The host's next start tries again.", tenant);
        }

        if (report.RolesKeptForAnAdministrator > 0)
        {
            _logger.LogWarning(
                "{Roles} roles were left as they are, because following their starter roles would leave their tenants without an administrator. They follow once those tenants have another.",
                report.RolesKeptForAnAdministrator);
        }

        _logger.LogInformation(
            "The starter roles are synced in {Tenants} tenants: {Changed} roles followed their starter roles, and {WithoutTheirStarterRole} were made from starter roles the catalogue no longer has and kept their permissions.",
            report.Tenants,
            report.RolesChanged,
            report.RolesWithoutTheirStarterRole);
    }

    /// <summary>Waits until the host has started; <see langword="false"/> when it stops first.</summary>
    private async Task<bool> StartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        using (stoppingToken.Register(() => started.TrySetCanceled(stoppingToken)))
        {
            try
            {
                await started.Task.ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
