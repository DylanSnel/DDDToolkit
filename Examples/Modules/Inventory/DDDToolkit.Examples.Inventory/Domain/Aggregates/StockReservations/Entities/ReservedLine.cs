using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Examples.Inventory.Domain.StockReservations;

/// <summary>One SKU of a reservation, and how many were set aside.</summary>
[Entity<Guid>("RSVL")]
public partial class ReservedLine
{
    public ReservedLine(ReservedLineId id, string sku, int quantity) : base(id)
    {
        Sku = sku;
        Quantity = quantity;
    }

    public string Sku { get; private set; } = string.Empty;

    public int Quantity { get; private set; }
}
