using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A seat was placed in a unit.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="SeatId">The seat placed.</param>
/// <param name="UnitId">The unit it was placed in.</param>
/// <param name="IsPrimary">Whether the placement is the seat's primary one from now on.</param>
/// <param name="By">
/// Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an
/// operator or a token the work was begun for (<see cref="TenancyActor{TSeatId}"/>); for a seat an invitation
/// made, the system, for the seat that issued the invitation, as on <c>InvitationAccepted</c>.
/// <see langword="null"/> only where the aggregate was changed outside the package's use cases, with no actor
/// handed to it.
/// </param>
[DomainEventName("tenancy.seat-placed")]
public sealed record SeatPlaced<TTenantId, TSeatId, TUnitId>(TTenantId TenantId, TSeatId SeatId, TUnitId UnitId, bool IsPrimary, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>;
