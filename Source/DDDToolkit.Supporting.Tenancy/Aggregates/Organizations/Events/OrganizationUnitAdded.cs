using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A unit was added: the root when the organization was made, or a unit below an existing one.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="UnitId">The unit added.</param>
/// <param name="ParentId">The unit it was added below, or <see langword="null"/> for the root.</param>
/// <param name="By">
/// Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an
/// operator or a token the work was begun for (<see cref="TenancyActor{TSeatId}"/>). <see langword="null"/> only
/// where the aggregate was changed outside the package's use cases, with no actor handed to it.
/// </param>
[DomainEventName("tenancy.organization-unit-added")]
public sealed record OrganizationUnitAdded<TTenantId, TUnitId, TSeatId>(TTenantId TenantId, TUnitId UnitId, TUnitId? ParentId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
