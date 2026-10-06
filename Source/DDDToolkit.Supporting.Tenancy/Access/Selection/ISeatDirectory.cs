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

    /// <summary>
    /// Every seat the identity has, in every tenant and in any status, for a tenant picker: the application's own
    /// seats, whole, each beside its tenant (<see cref="SeatInTenant{TSeat}"/>). A seat has no name in Tenancy, so a
    /// picker shows one by what the application keeps on its seat class, such as the name it is shown by in that
    /// tenant, from the seats read here, with no statement more.
    /// </summary>
    /// <typeparam name="TSeat">The application's seat class, which the storage keeps; or a class it derives from.</typeparam>
    /// <param name="identity">The verified identity, the subject of the caller's token.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="TSeat"/> is no class the storage's seats are.</exception>
    Task<IReadOnlyList<SeatInTenant<TSeat>>> AllOfAsync<TSeat>(Guid identity, CancellationToken cancellationToken)
        where TSeat : class;
}
