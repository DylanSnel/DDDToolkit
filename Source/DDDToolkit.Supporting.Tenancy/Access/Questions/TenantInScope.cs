using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>The tenant a caller acts in.</summary>
/// <param name="Tenant">The tenant.</param>
/// <param name="Seat">The caller's seat, or the seat system work acts for, if any.</param>
/// <param name="BySystem">Whether this is system work in the tenant rather than a seat.</param>
public readonly record struct TenantInScope<TTenantId, TSeatId>(TTenantId Tenant, TSeatId? Seat, bool BySystem)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
