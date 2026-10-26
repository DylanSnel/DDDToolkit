using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

// A seat's domain events. None of them carries the identity the seat belongs to: an event is stored and
// may be published, and who a seat is stays with the seat. Each ends with By, who made the change, as every
// event of Tenancy's does: ids and a scope, and never a name.

/// <summary>A seat was added to a tenant.</summary>
/// <param name="TenantId">The tenant the seat was added to.</param>
/// <param name="SeatId">The seat added.</param>
/// <param name="By">
/// Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an
/// operator or a token the work was begun for (<see cref="TenancyActor{TSeatId}"/>); for a seat an invitation
/// made, the system, for the seat that issued the invitation, as on <c>InvitationAccepted</c>.
/// <see langword="null"/> only where the aggregate was changed outside the package's use cases, with no actor
/// handed to it.
/// </param>
[DomainEventName("tenancy.seat-added")]
public sealed record SeatAdded<TTenantId, TSeatId>(TTenantId TenantId, TSeatId SeatId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
