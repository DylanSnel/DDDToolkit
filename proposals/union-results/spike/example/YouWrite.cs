using Sketch;

// ---- errors
[ErrorCode("stock.unknown", "There is no stock with SKU {Sku}.")]
public sealed partial record StockUnknown(string Sku) : NotFoundError;

[ErrorCode("stock.blocked", "Stock {Sku} is blocked.")]
public sealed partial record StockBlocked(string Sku) : ConflictError;

[ErrorCode("stock.out-of-stock", "Only {Available} left.")]
public sealed partial record OutOfStock(int Available) : ConflictError;

[ErrorCode("stock.sku-invalid", "{Sku} is no valid SKU.")]
public sealed partial record SkuInvalid(string Sku) : InvalidError;

// ---- domain
public sealed record Reservation(string Sku, int Quantity);

public union ReserveResult(Reservation, OutOfStock);

public sealed class Stock(string sku, int available)
{
    public ReserveResult Reserve(int quantity)
    {
        if (quantity > available)
        {
            return new OutOfStock(available);
        }
        available -= quantity;
        return new Reservation(sku, quantity);
    }
}

public union FindStockResult(Stock, StockUnknown, StockBlocked);
public union CreateStockResult(Reservation, SkuInvalid);

public interface IStockStore
{
    Task<FindStockResult> FindAsync(string sku, CancellationToken ct);
    Task<CreateStockResult> CreateAndReserveAsync(string sku, int quantity, CancellationToken ct);
}

// ---- application
public sealed record ReserveStockCommand(string Sku, int Quantity);

public partial union ReserveStockResult(Reservation, StockBlocked, OutOfStock, SkuInvalid);

public sealed class ReserveStockHandler(IStockStore store)
{
    public async Task<ReserveStockResult> Handle(ReserveStockCommand command, CancellationToken ct)
    {
        var found = await store.FindAsync(command.Sku, ct);
        return found switch
        {
            Stock stock => stock.Reserve(command.Quantity),
            StockUnknown => await store.CreateAndReserveAsync(command.Sku, command.Quantity, ct),
            StockBlocked blocked => blocked,
        };
    }
}
