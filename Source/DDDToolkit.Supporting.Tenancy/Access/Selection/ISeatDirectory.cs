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

    /// <summary>
    /// Every seat the identity has, as <see cref="AllOfAsync(Guid, CancellationToken)"/> finds them, each answered as
    /// <paramref name="view"/> makes it of what the directory found and the application's own seat. A seat has no
    /// name in Tenancy, so this is how a tenant picker shows a seat by what the application keeps on its seat class,
    /// such as the name it is shown by in that tenant: from the seats the lookup reads anyway, with no statement more.
    /// </summary>
    /// <typeparam name="TSeat">The application's seat class, which the storage keeps; or a class it derives from.</typeparam>
    /// <typeparam name="TView">What the application answers of a seat.</typeparam>
    /// <param name="identity">The verified identity, the subject of the caller's token.</param>
    /// <param name="view">
    /// Makes the answer of one seat, once for each. The seat is read for this answer and is not saved: read the fields
    /// the application keeps on it, and change nothing. Where it is placed and the roles it holds are its tenant's to
    /// answer, in that tenant: a database that keeps tenants apart hands them to nobody outside it, so they may be
    /// missing here.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="TSeat"/> is no class the storage's seats are.</exception>
    Task<IReadOnlyList<TView>> AllOfAsync<TSeat, TView>(
        Guid identity,
        Func<SeatOfCaller<TTenantId, TSeatId>, TSeat, TView> view,
        CancellationToken cancellationToken)
        where TSeat : class;
}
