using DDDToolkit.Examples.Ordering.Contracts;

namespace DDDToolkit.Examples.Inventory.Domain.Services;

/// <summary>
/// Sets stock aside for an order, all of it or none of it. A <b>domain service</b>.
/// </summary>
/// <remarks>
/// The rule spans aggregates: an order for coffee and mugs needs both stock items to have enough, and
/// neither item can see the other. So the decision is made here, over all of them, and the aggregates
/// are then told what was decided. Each stock item still guards its own rule,
/// <see cref="StockItem.MustNotOverReserve"/>, on the save.
/// <para>
/// Changing several aggregates in one transaction is usually a smell, and here it is the point: a
/// half-reserved order is exactly what must never exist. It stays inside one module, in one database,
/// in the one save the inbox makes. Across modules the same all-or-nothing would need a distributed
/// transaction, which is why Ordering and Inventory talk in messages instead.
/// </para>
/// <para>
/// Like <c>OrderPricer</c> in Ordering it does no I/O. The handler loads the stock items and hands
/// them in.
/// </para>
/// </remarks>
public static class StockAllocator
{
    /// <summary>
    /// Reserves every line against <paramref name="stock"/>, or reserves nothing and says why.
    /// </summary>
    /// <param name="order">The order being filled.</param>
    /// <param name="lines">The SKUs and quantities it asks for.</param>
    /// <param name="stock">The stock items for those SKUs, by SKU. A SKU with no item has none in stock.</param>
    public static StockReservation Allocate(OrderId order, IReadOnlyList<(string Sku, int Quantity)> lines, IReadOnlyDictionary<string, StockItem> stock)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(stock);

        // Ask first, change nothing until every answer is yes.
        var shortages = lines
            .GroupBy(line => line.Sku)
            .Select(sku => (Sku: sku.Key, Wanted: sku.Sum(line => line.Quantity), Available: stock.TryGetValue(sku.Key, out var item) ? item.Available : 0))
            .Where(sku => sku.Wanted > sku.Available)
            .Select(sku => $"{sku.Sku}: {sku.Wanted} wanted, {sku.Available} available")
            .ToList();

        if (shortages.Count > 0)
        {
            return StockReservation.Refused(order, "Not enough stock. " + string.Join("; ", shortages) + ".");
        }

        foreach (var (sku, quantity) in lines)
        {
            stock[sku].Reserve(quantity);
        }

        return StockReservation.Reserved(order, lines.Select(line => new ReservedLine(ReservedLineId.CreateSequential(), line.Sku, line.Quantity)));
    }
}
