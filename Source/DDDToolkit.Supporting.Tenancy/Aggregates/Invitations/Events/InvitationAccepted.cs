using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// An invitation was accepted, and made a seat. The seat's own events, added, placed and granted its role, are
/// raised by the same save.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="InvitationId">The invitation.</param>
/// <param name="SeatId">The seat it made, which is the person who accepted.</param>
/// <param name="By">
/// Who made the change: the system, for the seat that issued the invitation. The person who accepts has no
/// seat until the save is done, so acceptance is the application's work on what the issuer offered.
/// </param>
[DomainEventName("tenancy.invitation-accepted")]
public sealed record InvitationAccepted<TTenantId, TInvitationId, TSeatId>(
    TTenantId TenantId,
    TInvitationId InvitationId,
    TSeatId SeatId,
    TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
