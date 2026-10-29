using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Reads the tenants a round of system work visits, for <see cref="TenancyAccessManager{TTenantId, TSeatId, TUnitId, TRoleId}.TenantsToSweepAsync"/>.
/// The storage package implements it and registers it with its <c>AddTenancy</c>. An application asks the access
/// manager, not this.
/// </summary>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
public interface ITenantsToSweep<TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
{
    /// <summary>
    /// The ids of the active and the suspended tenants, in no order. The read is no seat's and no tenant's: it runs on
    /// its own, outside the work around it, as the system in <paramref name="scope"/>.
    /// </summary>
    /// <param name="scope">The scope of the work that asks: lower case letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope. Nothing is read then.</exception>
    Task<IReadOnlyList<TTenantId>> TenantsToSweepAsync(string scope, CancellationToken cancellationToken);
}
