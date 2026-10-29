using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// The tenants a round of system work visits, read over <typeparamref name="TContext"/>, Tenancy's own context, as
/// <see cref="TenancySystemReads"/> reads them: on a context of its own, as the system in the scope asked. The access
/// manager asks it; <c>AddTenancy</c> registers it per scope.
/// </summary>
/// <param name="services">The services of the scope, where Tenancy's context is found: it names the type, and the services the read makes its own context from.</param>
internal sealed class EfTenantsToSweep<TTenant, TTenantId, TContext>(IServiceProvider services) : ITenantsToSweep<TTenantId>
    where TTenant : TenantAggregate<TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TContext : DbContext
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TTenantId>> TenantsToSweepAsync(string scope, CancellationToken cancellationToken)
    {
        // A scope that is none is refused before any context is taken, the same on every database.
        _ = CurrentUserSnapshot.SystemIn(scope);

        // The context only names its type and the services it was made by: the read makes one of its own, in a new
        // scope of those services.
        var like = services.GetService<TContext>()
            ?? throw new InvalidOperationException(
                "No '" + typeof(TContext).Name + "' is registered under its own type, and the tenants to visit are read over Tenancy's own context. "
                + "Register it with AddDbContext<" + typeof(TContext).Name + ">, or with a context pool and services.AddScopedFromPool<" + typeof(TContext).Name + ">().");

        return await TenancySystemReads.TenantsToSweepAsync<TTenant, TTenantId>(like, scope, cancellationToken).ConfigureAwait(false);
    }
}
