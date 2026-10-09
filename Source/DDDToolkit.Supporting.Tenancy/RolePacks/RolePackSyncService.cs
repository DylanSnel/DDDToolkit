using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Runs <see cref="IRolePackSync"/> once, in the background, once the host has started: what
/// <see cref="TenancyServiceCollectionExtensions.SyncRolePacks"/> registers.
/// </summary>
/// <remarks>
/// It waits for <see cref="IHostApplicationLifetime.ApplicationStarted"/>, so it runs after the host's start-up
/// checks, which run before anything starts, and after every hosted service has started, a seeding of the host's
/// own or the server that binds its port included; the host serves while it runs. A tenant it could not sync is
/// logged as an error and left for the next start, and so is a sync that failed as a whole: the host keeps running
/// either way, with the roles it had. A host that registered no <see cref="IRolePackSync"/> is a host composed
/// wrong, and stops, saying so.
/// </remarks>
internal sealed class RolePackSyncService(IServiceProvider services, IHostApplicationLifetime lifetime, ILogger<RolePackSyncService>? logger = null) : BackgroundService
{
    private readonly ILogger _logger = logger ?? NullLogger<RolePackSyncService>.Instance;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await StartedAsync(stoppingToken).ConfigureAwait(false))
        {
            return;
        }

        var sync = services.GetService<IRolePackSync>()
            ?? throw new InvalidOperationException(
                "SyncRolePacks() runs the IRolePackSync Tenancy's storage registers, and none is registered. Call services.AddTenancy<TContext>(...) "
                + "from Tenancy's Entity Framework package, or register an IRolePackSync of your own.");

        RolePackSyncReport report;
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
            _logger.LogError(failure, "The role packs could not be synced: no tenant's roles changed. The host's next start tries again.");
            return;
        }

        foreach (var (tenant, error) in report.Failed)
        {
            _logger.LogError(error, "The roles of tenant {Tenant} could not follow their packs. The host's next start tries again.", tenant);
        }

        if (report.RolesKeptForAnAdministrator > 0)
        {
            _logger.LogWarning(
                "{Roles} roles were left as they are, because following their packs would leave their tenants without an administrator. They follow once those tenants have another.",
                report.RolesKeptForAnAdministrator);
        }

        _logger.LogInformation(
            "The role packs are synced in {Tenants} tenants: {Changed} roles followed their packs, and {WithoutTheirPack} were made from packs the catalogue no longer has and kept their keys.",
            report.Tenants,
            report.RolesChanged,
            report.RolesWithoutTheirPack);
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
