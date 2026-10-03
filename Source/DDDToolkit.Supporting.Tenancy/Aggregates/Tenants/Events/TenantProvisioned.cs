using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

// A tenant's domain events. Each is generic over the application's ids and pins its own name, because a
// generic type's name carries a backtick and the package declares no module to name it after.
//
// Every event of Tenancy's ends with By: who made the change, as the Tenancy caller of the command that
// raised it says (a seat, an operator, the system or a token), or null where an aggregate was changed with
// nobody named. It is why an event about a tenant alone still names the seat id's type.

/// <summary>A tenant was provisioned: it exists, and is not active yet.</summary>
[DomainEventName("tenancy.tenant-provisioned")]
public sealed record TenantProvisioned<TTenantId, TSeatId>(TTenantId TenantId, string Slug, TenantShape Shape, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
