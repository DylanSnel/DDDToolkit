using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

// A seat's domain events. None of them carries the identity the seat belongs to: an event is stored and
// may be published, and who a seat is stays with the seat. Each ends with By, who made the change, as every
// event of Tenancy's does: ids and a scope, and never a name.

/// <summary>A seat was added to a tenant.</summary>
[DomainEventName("tenancy.seat-added")]
public sealed record SeatAdded<TTenantId, TSeatId>(TTenantId TenantId, TSeatId SeatId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
