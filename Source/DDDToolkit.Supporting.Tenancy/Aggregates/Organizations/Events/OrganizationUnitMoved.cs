using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A unit, and everything below it, moved under another parent.</summary>
[DomainEventName("tenancy.organization-unit-moved")]
public sealed record OrganizationUnitMoved<TTenantId, TUnitId, TSeatId>(TTenantId TenantId, TUnitId UnitId, TUnitId FromParentId, TUnitId ToParentId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
