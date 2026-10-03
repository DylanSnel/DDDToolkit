using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// The keys a role grants changed: every seat that holds the role gained <paramref name="Added"/> and lost
/// <paramref name="Removed"/>, wherever it holds the role.
/// </summary>
/// <param name="TenantId">The tenant the role belongs to.</param>
/// <param name="RoleId">The role.</param>
/// <param name="Added">The keys the role grants from now on and did not before, the keys they imply included, in ordinal order.</param>
/// <param name="Removed">The keys the role granted before and no longer does, in ordinal order.</param>
/// <param name="By">Who made the change.</param>
[DomainEventName("tenancy.role-keys-changed")]
public sealed record RoleKeysChanged<TTenantId, TRoleId, TSeatId>(
    TTenantId TenantId,
    TRoleId RoleId,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
