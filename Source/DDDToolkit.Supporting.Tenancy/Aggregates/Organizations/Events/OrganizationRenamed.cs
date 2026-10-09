using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

// An organization's domain events. The organization shares its tenant's id, so TenantId is also the id of
// the organization that raised the event. Each ends with By, who made the change, as every event of
// Tenancy's does.

/// <summary>The organization's name changed.</summary>
/// <param name="TenantId">The tenant whose organization it is.</param>
/// <param name="By">
/// Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an
/// operator or a token the work was begun for (<see cref="TenancyActor{TSeatId}"/>). <see langword="null"/> only
/// where the aggregate was changed outside the package's use cases, with no actor handed to it.
/// </param>
[DomainEventName("tenancy.organization-renamed")]
public sealed record OrganizationRenamed<TTenantId, TSeatId>(TTenantId TenantId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
