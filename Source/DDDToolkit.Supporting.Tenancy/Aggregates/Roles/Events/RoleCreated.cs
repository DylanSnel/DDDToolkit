using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

// A role's domain events. A change of keys names the keys that came in and the keys that went out, which is
// what a record of the change needs; the keys the role holds now are read from the role, the one place they
// are current. Each event ends with By, who made the change, as every event of Tenancy's does.

/// <summary>A role was created, by hand or copied from a pack.</summary>
/// <param name="TenantId">The tenant the role belongs to.</param>
/// <param name="RoleId">The role created.</param>
/// <param name="FromPack">The key of the catalogue's pack it was copied from, or <see langword="null"/> for a role made by hand.</param>
/// <param name="By">
/// Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an
/// operator or a token the work was begun for (<see cref="TenancyActor{TSeatId}"/>). <see langword="null"/> only
/// where the aggregate was changed outside the package's use cases, with no actor handed to it.
/// </param>
[DomainEventName("tenancy.role-created")]
public sealed record RoleCreated<TTenantId, TRoleId, TSeatId>(TTenantId TenantId, TRoleId RoleId, string? FromPack, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
