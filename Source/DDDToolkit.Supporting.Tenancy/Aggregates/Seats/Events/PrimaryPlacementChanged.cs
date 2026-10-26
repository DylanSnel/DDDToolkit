using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A seat's primary placement is now the one in this unit.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="SeatId">The seat.</param>
/// <param name="UnitId">The unit of its primary placement from now on.</param>
/// <param name="By">
/// Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an
/// operator or a token the work was begun for (<see cref="TenancyActor{TSeatId}"/>). <see langword="null"/> only
/// where the aggregate was changed outside the package's use cases, with no actor handed to it.
/// </param>
[DomainEventName("tenancy.primary-placement-changed")]
public sealed record PrimaryPlacementChanged<TTenantId, TSeatId, TUnitId>(TTenantId TenantId, TSeatId SeatId, TUnitId UnitId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>;
