using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Finds a person's seats by their verified identity: the one lookup that looks across tenants, and only
/// ever for the identity asking. Tenancy's storage implements it.
/// </summary>
public interface ISeatDirectory<TTenantId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
{
    /// <summary>
    /// The identity's seat in the tenant with this slug, or <see langword="null"/> when there is no such
    /// tenant or no seat in it for this identity; the two are not told apart.
    /// </summary>
    /// <param name="identity">The verified identity, the subject of the caller's token.</param>
    /// <param name="tenantSlug">The tenant's slug, trimmed and lowercased.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<SeatOfCaller<TTenantId, TSeatId>?> FindAsync(Guid identity, string tenantSlug, CancellationToken cancellationToken);

    /// <summary>Every seat the identity has, in every tenant and in any status, for a tenant picker.</summary>
    /// <param name="identity">The verified identity, the subject of the caller's token.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<IReadOnlyList<SeatOfCaller<TTenantId, TSeatId>>> AllOfAsync(Guid identity, CancellationToken cancellationToken);
}
