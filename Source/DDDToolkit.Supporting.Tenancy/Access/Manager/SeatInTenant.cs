using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>The tenant a caller acts in and its seat, as <see cref="CurrentTenant{TTenantId, TSeatId}.RequireSeat"/> answers them.</summary>
/// <param name="Id">The tenant.</param>
/// <param name="Seat">The caller's seat, or the seat system work acts for.</param>
/// <param name="BySystem">Whether this is system work in the tenant rather than a seat.</param>
public readonly record struct SeatInTenant<TTenantId, TSeatId>(TTenantId Id, TSeatId Seat, bool BySystem)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
