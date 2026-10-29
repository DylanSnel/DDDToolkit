using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Managers;

/// <summary>Which invitation a token is for, as the invitation store answers it across tenants: ids, and nothing else of it.</summary>
/// <typeparam name="TInvitationId">The application's invitation id.</typeparam>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
/// <typeparam name="TSeatId">The application's seat id.</typeparam>
/// <param name="Tenant">The tenant the invitation is into.</param>
/// <param name="Invitation">The invitation.</param>
/// <param name="IssuedBy">The seat that issued it, or that system work issued it for; <see langword="null"/> for none.</param>
public sealed record InvitationOfToken<TInvitationId, TTenantId, TSeatId>(TTenantId Tenant, TInvitationId Invitation, TSeatId? IssuedBy)
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
