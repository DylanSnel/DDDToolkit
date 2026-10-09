using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>A role was archived, and grants nothing from now on.</summary>
/// <param name="TenantId">The tenant the role belongs to.</param>
/// <param name="RoleId">The role archived.</param>
/// <param name="By">
/// Who made the change: the actor of the caller whose command raised it, a seat as itself, or the system, an
/// operator or a token the work was begun for (<see cref="TenancyActor{TSeatId}"/>). <see langword="null"/> only
/// where the aggregate was changed outside the package's use cases, with no actor handed to it.
/// </param>
[DomainEventName("tenancy.role-archived")]
public sealed record RoleArchived<TTenantId, TRoleId, TSeatId>(TTenantId TenantId, TRoleId RoleId, TenancyActor<TSeatId>? By) : DomainEvent
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
