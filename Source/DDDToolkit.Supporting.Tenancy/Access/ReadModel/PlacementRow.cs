using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Where a seat is placed, as a row to read. It has no tenant of its own: it is reached through the seat's
/// id, and the seat belongs to one tenant.
/// </summary>
public sealed class PlacementRow<TSeatId, TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
{
    /// <summary>The seat.</summary>
    public TSeatId SeatId { get; set; }

    /// <summary>The unit it is placed in.</summary>
    public TUnitId UnitId { get; set; }

    /// <summary>Whether this is the seat's primary placement.</summary>
    public bool IsPrimary { get; set; }
}
