using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A seat was granted a role at a unit where it is placed, for a period.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="SeatId">The seat that holds the role from now on.</param>
/// <param name="UnitId">The unit it holds the role at.</param>
/// <param name="RoleId">The role.</param>
/// <param name="StartsAt">The first moment the grant applies.</param>
/// <param name="EndsAt">The first moment it no longer applies, or <see langword="null"/> when it has no end.</param>
/// <param name="GrantedBy">
/// The seat the grant keeps as its granter, or <see langword="null"/> when system work made it with no seat
/// acting. <paramref name="By"/> says who acted in full: for system work done for a seat this is that seat, and
/// <paramref name="By"/> is the system.
/// </param>
/// <param name="By">Who made the change.</param>
[DomainEventName("tenancy.organization-role-granted")]
public sealed record OrganizationRoleGranted<TTenantId, TSeatId, TUnitId, TRoleId>(
    TTenantId TenantId,
    TSeatId SeatId,
    TUnitId UnitId,
    TRoleId RoleId,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    TSeatId? GrantedBy,
    TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
