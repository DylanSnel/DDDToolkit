using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// <see cref="IStarterRoleSync"/> over Tenancy's context: the tenants to visit are read across tenants
/// (<see cref="TenancySystemReads.TenantsToSweepAsync{TTenant, TTenantId}"/>), and each tenant's roles follow their
/// starter roles through <c>RoleManager.FollowStarterRolesAsync</c>, as Tenancy's system work in that tenant, in a scope of its own.
/// </summary>
/// <remarks>
/// A tenant that another run, or an administrator, changed between this run's reading and its save fails on the
/// tenant's access revision, and is tried again, up to <see cref="Attempts"/> times in all: the next attempt reads
/// what the other committed, and changes only what is still to change. Any other failure is the tenant's alone: it
/// is reported, and the run goes on with the next tenant.
/// </remarks>
internal sealed class EfStarterRoleSync<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TContext>(
    IServiceScopeFactory scopes,
    ILogger<EfStarterRoleSync<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TContext>>? logger = null) : IStarterRoleSync
    where TTenant : TenantAggregate<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
    where TContext : DbContext
{
    /// <summary>How often a tenant is tried when another change of its access commits first.</summary>
    internal const int Attempts = 3;

    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    /// <inheritdoc />
    public async Task<StarterRoleSyncReport> SyncAsync(CancellationToken cancellationToken)
    {
        var tenants = await TenantsAsync(cancellationToken).ConfigureAwait(false);

        var (changed, kept, gone) = (0, 0, 0);
        var failed = new List<(string Tenant, Exception Error)>();
        foreach (var tenant in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var followed = await FollowAsync(tenant, cancellationToken).ConfigureAwait(false);
                changed += followed.Changed.Count;
                kept += followed.KeptForAnAdministrator.Count;
                gone += followed.WithoutTheirStarterRole.Count;
                _logger.LogDebug(
                    "Tenant {Tenant}: {Changed} roles followed their starter roles, {Kept} were kept for an administrator, {WithoutTheirStarterRole} have no starter role.",
                    tenant,
                    followed.Changed.Count,
                    followed.KeptForAnAdministrator.Count,
                    followed.WithoutTheirStarterRole.Count);
            }
            catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed.Add((tenant.ToString() ?? string.Empty, failure));
            }
        }

        return new StarterRoleSyncReport(tenants.Count, changed, kept, gone, failed);
    }

    /// <summary>
    /// The tenants to visit, read across tenants as Tenancy's system work in no tenant, on a context of Tenancy's
    /// made for the read: the active and the suspended ones. A closed tenant is closed for good.
    /// </summary>
    private async Task<IReadOnlyList<TTenantId>> TenantsAsync(CancellationToken cancellationToken)
    {
        using (TenancyWork.BeginSystem<TTenantId, TSeatId>())
        {
            await using var scope = scopes.CreateAsyncScope();
            var like = scope.ServiceProvider.GetRequiredService<TContext>();
            return await TenancySystemReads.TenantsToSweepAsync<TTenant, TTenantId>(like, TenancyWork.SystemScope, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>One tenant's roles follow their starter roles, as Tenancy's system work in it, tried again on a lost race.</summary>
    private async Task<RoleManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.StarterRolesFollowed> FollowAsync(
        TTenantId tenant,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // The caller is begun before the scope's context is taken, so the context is the work's own.
                using (TenancyWork.BeginSystemIn<TTenantId, TSeatId>(tenant))
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var roles = scope.ServiceProvider
                        .GetRequiredService<RoleManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>>();
                    return await roles.FollowStarterRolesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (ConcurrencyConflictException) when (attempt < Attempts)
            {
                _logger.LogDebug("Tenant {Tenant}: another change of its access committed first; its roles are read again.", tenant);
            }
        }
    }
}
