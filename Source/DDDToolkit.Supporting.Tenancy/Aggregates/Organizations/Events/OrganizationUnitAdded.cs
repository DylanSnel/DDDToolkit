using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A unit was added: the root when the organization was made, or a unit below an existing one.</summary>
[DomainEventName("tenancy.organization-unit-added")]
public sealed record OrganizationUnitAdded<TTenantId, TUnitId, TSeatId>(TTenantId TenantId, TUnitId UnitId, TUnitId? ParentId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
