using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A role a seat held at a unit was revoked, on its own or because the placement was withdrawn.</summary>
[DomainEventName("tenancy.organization-role-revoked")]
public sealed record OrganizationRoleRevoked<TTenantId, TSeatId, TUnitId, TRoleId>(TTenantId TenantId, TSeatId SeatId, TUnitId UnitId, TRoleId RoleId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
