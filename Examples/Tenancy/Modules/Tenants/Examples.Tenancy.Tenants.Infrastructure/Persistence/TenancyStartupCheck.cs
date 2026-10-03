using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Examples.Tenancy.Tenants.Infrastructure.Persistence;

/// <summary>
/// Checks, before the host takes its first request, that tenancy holds together: the catalogue builds, every
/// context is wired through the toolkit and checks the saves of the rows it keeps to a tenant, and no role holds a
/// key the catalogue has lost.
/// </summary>
/// <remarks>
/// The module registers it with its storage, since what it checks is how contexts are wired and what Tenancy's
/// own context stores; a host that names no context could not. It starts after
/// <see cref="PostgresStartupCheck"/>, which has found every migration applied, and before every hosted service
/// added after the module, the host's seeding included.
/// <list type="bullet">
/// <item>The catalogue is built the first time it is asked for. Asking here means a catalogue that does not
/// hold together, such as two modules declaring one key, stops the start rather than the first request that
/// needs it. Every module has registered its keys by then, whatever the order they were added in.</item>
/// <item>Every context the host registered is checked, not a list of them: a module added later is checked
/// without anyone remembering to add it here. <see cref="EntityFrameworkChecks.EnsureToolkitWired"/> refuses one
/// built without the toolkit's interceptors, which would save and check no invariant, bump no version and store
/// no event; <see cref="TenancyChecks.EnsureWired"/> one that keeps rows to a tenant and does not check its
/// saves. A context without such rows passes the second as it is.</item>
/// <item>A key a role still holds but the catalogue no longer knows was removed from the code instead of
/// retired. It gives nothing and does no harm, so it is a warning, not a refusal to start.</item>
/// </list>
/// </remarks>
/// <param name="services">The host's services.</param>
/// <param name="logger">Where the unknown keys are reported.</param>
public sealed class TenancyStartupCheck(IServiceProvider services, ILogger<TenancyStartupCheck> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var catalogue = services.GetRequiredService<TenancyCatalogue>();

        await using var scope = services.CreateAsyncScope();
        foreach (var contextType in EntityFrameworkChecks.RegisteredContexts(scope.ServiceProvider))
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            EntityFrameworkChecks.EnsureToolkitWired(context);
            TenancyChecks.EnsureWired(context);
        }

        var tenancy = scope.ServiceProvider.GetRequiredService<TenantsContext>();
        foreach (var key in await TenancyChecks.UnknownStoredKeysAsync<Role, RoleId, TenantId>(tenancy, catalogue, cancellationToken))
        {
            logger.LogWarning(
                "A role holds the key {Key}, which the catalogue does not know. It gives no rights. Retire a key rather than removing it from the code, or take it off the roles that hold it.",
                key);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
