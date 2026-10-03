using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A seat's primary placement is now the one in this unit.</summary>
[DomainEventName("tenancy.primary-placement-changed")]
public sealed record PrimaryPlacementChanged<TTenantId, TSeatId, TUnitId>(TTenantId TenantId, TSeatId SeatId, TUnitId UnitId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>;
