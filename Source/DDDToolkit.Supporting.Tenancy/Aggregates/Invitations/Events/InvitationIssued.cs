using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

// An invitation's domain events. They carry ids and dates: never the token or its digest, never the address the
// invitation is for, and never a name. Each ends with By, who made the change, as every event of Tenancy's does.

/// <summary>An invitation was issued: an offer of a seat, placed in a unit, with a role there.</summary>
/// <param name="TenantId">The tenant the invitation is into.</param>
/// <param name="InvitationId">The invitation.</param>
/// <param name="UnitId">The unit the seat would be placed in.</param>
/// <param name="RoleId">The role the seat would hold there.</param>
/// <param name="ExpiresAt">The first moment it can no longer be accepted.</param>
/// <param name="IssuedBy">
/// The seat the invitation keeps as its issuer, whose rights are asked again when it is accepted, or
/// <see langword="null"/> when system work issued it with no seat acting.
/// </param>
/// <param name="By">Who made the change.</param>
[DomainEventName("tenancy.invitation-issued")]
public sealed record InvitationIssued<TTenantId, TInvitationId, TUnitId, TRoleId, TSeatId>(
    TTenantId TenantId,
    TInvitationId InvitationId,
    TUnitId UnitId,
    TRoleId RoleId,
    DateTimeOffset ExpiresAt,
    TSeatId? IssuedBy,
    TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
