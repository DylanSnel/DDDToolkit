using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

// An organization's domain events. The organization shares its tenant's id, so TenantId is also the id of
// the organization that raised the event. Each ends with By, who made the change, as every event of
// Tenancy's does.

/// <summary>The organization's name changed.</summary>
[DomainEventName("tenancy.organization-renamed")]
public sealed record OrganizationRenamed<TTenantId, TSeatId>(TTenantId TenantId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
