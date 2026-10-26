using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// A tenant's seat, as a row to read: whether it counts, never its identity, nor a field the application added to its
/// seat class, such as the name it shows the seat by.
/// </summary>
public sealed class SeatRow<TTenantId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
{
    /// <summary>The seat's id.</summary>
    public TSeatId Id { get; set; }

    /// <summary>The seat's tenant.</summary>
    public TTenantId TenantId { get; set; }

    /// <summary>Whether the seat counts.</summary>
    public SeatStatus Status { get; set; }
}
