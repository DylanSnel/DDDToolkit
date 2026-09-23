using DDDToolkit.Invariants;

namespace DDDToolkit.Examples.Inventory.Domain.StockItems;

public partial class StockItem
{
    /// <summary>
    /// More set aside than there is, or less than nothing. Either one means two orders were promised the
    /// same mug.
    /// </summary>
    /// <remarks>
    /// This is the rule the whole module exists for, so it is named and has a code. It is checked on
    /// every save that writes a stock item, which matters here more than anywhere: a reservation touches
    /// several stock items in one save, and the save asks every one of them.
    /// </remarks>
    public sealed class MustNotOverReserve : IInvariant<StockItem>
    {
        public const string ViolationCode = "STOCK_OVER_RESERVED";

        public string Code => ViolationCode;

        public InvariantFailure? Check(StockItem item)
            => item.Reserved > item.OnHand || item.Reserved < 0
                ? $"{item.Sku}: {item.Reserved} reserved of {item.OnHand} on hand."
                : null;
    }
}
