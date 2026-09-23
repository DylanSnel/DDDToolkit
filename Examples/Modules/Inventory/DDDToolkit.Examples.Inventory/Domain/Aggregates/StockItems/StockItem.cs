using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Examples.Inventory.Domain.StockItems;

/// <summary>How many of one SKU the warehouse holds, and how many of those are promised to orders.</summary>
/// <remarks>
/// One aggregate per SKU, not one for the whole warehouse: two orders for different products never
/// contend for the same row, and the version column only makes orders wait for each other when they
/// want the same thing.
/// </remarks>
[AggregateRoot<Guid>("STK")]
public partial class StockItem
{
    public StockItem(StockItemId id, string sku, int onHand) : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentOutOfRangeException.ThrowIfNegative(onHand);

        Sku = sku;
        OnHand = onHand;
    }

    public string Sku { get; private set; } = string.Empty;

    /// <summary>On the shelf, reserved or not.</summary>
    public int OnHand { get; private set; }

    /// <summary>Set aside for orders that have not shipped.</summary>
    public int Reserved { get; private set; }

    /// <summary>What a new order can still have.</summary>
    public int Available => OnHand - Reserved;

    /// <summary>Goods arrived.</summary>
    public void Receive(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
        OnHand += quantity;
    }

    /// <summary>
    /// Sets <paramref name="quantity"/> aside. Only <c>StockAllocator</c> calls this, after
    /// checking every line of the order can be had; <see cref="MustNotOverReserve"/> is what holds if
    /// anybody else ever does.
    /// </summary>
    internal void Reserve(int quantity) => Reserved += quantity;

    /// <summary>Puts <paramref name="quantity"/> back on the shelf for others.</summary>
    internal void Release(int quantity) => Reserved -= quantity;
}
