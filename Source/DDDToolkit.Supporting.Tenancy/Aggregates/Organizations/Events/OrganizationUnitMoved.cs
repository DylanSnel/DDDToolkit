using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A unit, and everything below it, moved under another parent.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="UnitId">The unit that moved, with everything below it.</param>
/// <param name="FromParentId">The parent it hung under before.</param>
/// <param name="ToParentId">The parent it hangs under now.</param>
/// <param name="By">
/// Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an
/// operator or a token the work was begun for (<see cref="TenancyActor{TSeatId}"/>). <see langword="null"/> only
/// where the aggregate was changed outside the package's use cases, with no actor handed to it.
/// </param>
[DomainEventName("tenancy.organization-unit-moved")]
public sealed record OrganizationUnitMoved<TTenantId, TUnitId, TSeatId>(TTenantId TenantId, TUnitId UnitId, TUnitId FromParentId, TUnitId ToParentId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
