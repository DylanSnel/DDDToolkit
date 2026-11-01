using DDDToolkit.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// What running every <see cref="ITenantStarterRoles"/> did in one tenant (<see cref="RunAsync"/>), added up. Tenancy's
/// <see cref="IStarterRoleSync"/> and <c>TenantManager.ProvisionAsync</c> run them this way, and so does a host on other
/// storage that registers its own <see cref="IStarterRoleSync"/>.
/// </summary>
/// <param name="RolesMade">How many roles were made from a starter role the tenant lacked.</param>
/// <param name="RolesChanged">How many roles gained or lost a permission.</param>
/// <param name="RolesWithoutTheirStarterRole">How many roles were made from a starter role that is no longer declared, and kept their permissions.</param>
/// <param name="Failed">What went wrong in each one that failed. The others ran all the same.</param>
public sealed record TenantStarterRolesRun(int RolesMade, int RolesChanged, int RolesWithoutTheirStarterRole, IReadOnlyList<Exception> Failed)
{
    /// <summary>How often one of them is tried when another change of the same row commits first.</summary>
    private const int Attempts = 3;

    /// <summary>
    /// Runs every <see cref="ITenantStarterRoles"/> that is registered, for the tenant the current work acts in. Begin that
    /// work first, <c>access.BeginSystemIn(tenant)</c>. Each one runs in a scope of its own, made here, so a context it
    /// takes from that scope runs as that work. A failure of one is kept in <see cref="Failed"/>, and the others go on. A
    /// row another run changed between the reading and the save is read again, up to three times.
    /// </summary>
    /// <param name="scopes">Makes the scopes.</param>
    /// <param name="cancellationToken">Cancels the work, between two of them or inside one.</param>
    /// <returns>What they did, added up.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static async Task<TenantStarterRolesRun> RunAsync(IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var (made, changed, gone) = (0, 0, 0);
        var failed = new List<Exception>();
        for (var index = 0; ; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await FollowAsync(scopes, index, cancellationToken).ConfigureAwait(false) is not { } followed)
                {
                    return new TenantStarterRolesRun(made, changed, gone, failed);
                }

                made += followed.RolesMade;
                changed += followed.RolesChanged;
                gone += followed.RolesWithoutTheirStarterRole;
            }
            catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed.Add(failure);
            }
        }
    }

    /// <summary>
    /// Runs the one at <paramref name="index"/>, in a scope of its own, tried again on a lost race;
    /// <see langword="null"/> when there are not that many.
    /// </summary>
    private static async Task<TenantStarterRolesFollowed?> FollowAsync(IServiceScopeFactory scopes, int index, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                return scope.ServiceProvider.GetServices<ITenantStarterRoles>().ElementAtOrDefault(index) is { } roles
                    ? await roles.FollowAsync(cancellationToken).ConfigureAwait(false)
                    : null;
            }
            catch (ConcurrencyConflictException) when (attempt < Attempts)
            {
            }
        }
    }
}
