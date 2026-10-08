using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A role a seat held at a unit was revoked, on its own or because the placement was withdrawn.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="SeatId">The seat that held the role.</param>
/// <param name="UnitId">The unit it held the role at.</param>
/// <param name="RoleId">The role revoked.</param>
/// <param name="By">
/// Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an
/// operator or a token the work was begun for (<see cref="TenancyActor{TSeatId}"/>). <see langword="null"/> only
/// where the aggregate was changed outside the package's use cases, with no actor handed to it.
/// </param>
[DomainEventName("tenancy.organization-role-revoked")]
public sealed record OrganizationRoleRevoked<TTenantId, TSeatId, TUnitId, TRoleId>(TTenantId TenantId, TSeatId SeatId, TUnitId UnitId, TRoleId RoleId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
