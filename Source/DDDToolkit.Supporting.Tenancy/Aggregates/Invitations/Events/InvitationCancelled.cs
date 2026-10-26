using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>An open invitation was cancelled, and can no longer be accepted.</summary>
/// <param name="TenantId">The tenant the invitation was into.</param>
/// <param name="InvitationId">The invitation.</param>
/// <param name="By">
/// Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an
/// operator or a token the work was begun for (<see cref="TenancyActor{TSeatId}"/>). <see langword="null"/> only
/// where the aggregate was changed outside the package's use cases, with no actor handed to it.
/// </param>
[DomainEventName("tenancy.invitation-cancelled")]
public sealed record InvitationCancelled<TTenantId, TInvitationId, TSeatId>(
    TTenantId TenantId,
    TInvitationId InvitationId,
    TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
