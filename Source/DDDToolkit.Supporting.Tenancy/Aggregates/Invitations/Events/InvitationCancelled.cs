using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>An open invitation was cancelled, and can no longer be accepted.</summary>
[DomainEventName("tenancy.invitation-cancelled")]
public sealed record InvitationCancelled<TTenantId, TInvitationId, TSeatId>(
    TTenantId TenantId,
    TInvitationId InvitationId,
    TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
