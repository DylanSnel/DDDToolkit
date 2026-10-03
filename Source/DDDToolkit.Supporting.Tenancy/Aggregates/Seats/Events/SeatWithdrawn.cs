using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A seat's placement in a unit was withdrawn, after every role it held there was revoked.</summary>
[DomainEventName("tenancy.seat-withdrawn")]
public sealed record SeatWithdrawn<TTenantId, TSeatId, TUnitId>(TTenantId TenantId, TSeatId SeatId, TUnitId UnitId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>;
