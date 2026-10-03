using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

// A role's domain events. A change of keys names the keys that came in and the keys that went out, which is
// what a record of the change needs; the keys the role holds now are read from the role, the one place they
// are current. Each event ends with By, who made the change, as every event of Tenancy's does.

/// <summary>A role was created, by hand or copied from a pack.</summary>
[DomainEventName("tenancy.role-created")]
public sealed record RoleCreated<TTenantId, TRoleId, TSeatId>(TTenantId TenantId, TRoleId RoleId, string? FromPack, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
