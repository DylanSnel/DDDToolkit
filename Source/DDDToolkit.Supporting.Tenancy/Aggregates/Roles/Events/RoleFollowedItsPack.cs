using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// A role made from a pack followed it, because the pack changed since the role was made or last followed it:
/// every seat that holds the role gained <paramref name="Added"/> and lost <paramref name="Removed"/>, wherever
/// it holds the role. The application's code changed the pack, so the tenant did not choose this: it is what a
/// host tells the tenant's administrators about. The package sends no message itself.
/// </summary>
/// <param name="TenantId">The tenant the role belongs to.</param>
/// <param name="RoleId">The role.</param>
/// <param name="Pack">The key of the pack the role was made from, and followed.</param>
/// <param name="Added">The keys the role grants from now on and did not before, the keys they imply included, in ordinal order.</param>
/// <param name="Removed">The keys the role granted before and no longer does, in ordinal order.</param>
/// <param name="ManagingAccess">
/// The keys of <paramref name="Added"/> and <paramref name="Removed"/> that manage access, as the catalogue marked
/// them at the time, in ordinal order: empty when the change gives and takes no power over other people's access.
/// A seat's own change of such a key is an administrator's alone; this one the application made.
/// </param>
/// <param name="By">Who made the change: system work, the application's own, unless it was begun for an operator.</param>
[DomainEventName("tenancy.role-followed-its-pack")]
public sealed record RoleFollowedItsPack<TTenantId, TRoleId, TSeatId>(
    TTenantId TenantId,
    TRoleId RoleId,
    string Pack,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> ManagingAccess,
    TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
