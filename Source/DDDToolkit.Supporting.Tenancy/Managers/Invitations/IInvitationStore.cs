using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Security;

namespace DDDToolkit.Supporting.Tenancy.Managers;

/// <summary>
/// Where the invitation manager loads and keeps invitations, in the unit of work of
/// <see cref="ITenancyStore{TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId}"/>: what is
/// added or changed here is saved by that store's save, with everything else the command changed. The storage package
/// implements it.
/// <para>
/// A token's digest is handed over with its invitation and never comes back. The store keeps it where reading
/// invitations does not read it, and answers one question about it: which invitation it belongs to.
/// </para>
/// <para>
/// Where the application declares an invitation class, the project that declares it, and every project above that
/// sees it, names this interface <c>InvitationStore</c>, a global alias the toolkit's generator writes, closed over the
/// application's classes.
/// </para>
/// </summary>
/// <typeparam name="TInvitation">The application's invitation class.</typeparam>
/// <typeparam name="TInvitationId">The application's invitation id.</typeparam>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
/// <typeparam name="TUnitId">The application's unit id.</typeparam>
/// <typeparam name="TRoleId">The application's role id.</typeparam>
/// <typeparam name="TSeatId">The application's seat id.</typeparam>
public interface IInvitationStore<
    [TemplateType(typeof(InvitationAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TInvitation,
    [TemplateType(typeof(InvitationAggregateAttribute<>))] TInvitationId,
    [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
    [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
    [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId,
    [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId>
    where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
{
    /// <summary>The invitation, or <see langword="null"/> when it is not in the current caller's tenant.</summary>
    Task<TInvitation?> FindAsync(TInvitationId id, CancellationToken cancellationToken);

    /// <summary>
    /// The tenant's invitations that can still be accepted at <paramref name="now"/>: open, and not run out.
    /// For reading only: they are not part of the unit of work.
    /// </summary>
    /// <param name="tenant">The tenant, which is the current caller's.</param>
    /// <param name="now">The moment an invitation must still be open at.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<TInvitation>> ListOpenAsync(TTenantId tenant, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Which invitation the token with this digest is for, in whatever tenant, as ids only; or
    /// <see langword="null"/> when there is none. It is the one read of invitations across tenants: whoever
    /// accepts an invitation names no tenant, the token does. It runs apart from the unit of work, as nobody
    /// in particular, and answers nothing else about the invitation.
    /// </summary>
    /// <param name="digest">The digest of the token somebody sent.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<InvitationOfToken<TInvitationId, TTenantId, TSeatId>?> FindByDigestAsync(byte[] digest, CancellationToken cancellationToken);

    /// <summary>Adds a new invitation to the unit of work, with the digest of its token.</summary>
    /// <param name="invitation">The invitation.</param>
    /// <param name="digest">The digest of its token: <see cref="BearerTokens.DigestLength"/> bytes.</param>
    void Add(TInvitation invitation, byte[] digest);
}
