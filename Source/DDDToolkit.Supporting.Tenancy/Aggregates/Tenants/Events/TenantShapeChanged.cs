using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A tenant's shape changed, which only ever goes from flat to hierarchical.</summary>
[DomainEventName("tenancy.tenant-shape-changed")]
public sealed record TenantShapeChanged<TTenantId, TSeatId>(TTenantId TenantId, TenantShape From, TenantShape To, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
