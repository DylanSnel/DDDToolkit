using HotChocolate;

// ---- errors: plain records, Message by hand (HotChocolate's error interface needs `message`)
public sealed record StockUnknown(string Sku)
{
    public string Message => $"There is no stock with SKU {Sku}.";
}
public sealed record StockBlocked(string Sku)
{
    public string Message => $"Stock {Sku} is blocked.";
}
public sealed record OutOfStock(int Available)
{
    public string Message => $"Only {Available} left.";
}
public sealed record SkuInvalid(string Sku)
{
    public string Message => $"{Sku} is no valid SKU.";
}

// ---- domain: the same unions
public sealed record Reservation(string Sku, int Quantity);
public union ReserveResult(Reservation, OutOfStock);
public sealed class Stock(string sku, int available)
{
    public ReserveResult Reserve(int quantity)
    {
        if (quantity > available) { return new OutOfStock(available); }
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

// ---- handler: no generated conversions, so each smaller union is unpacked with its own switch
public sealed record ReserveStockCommand(string Sku, int Quantity);
public union ReserveStockResult(Reservation, StockBlocked, OutOfStock, SkuInvalid);

public sealed class ReserveStockHandler(IStockStore store)
{
    public async Task<ReserveStockResult> Handle(ReserveStockCommand command, CancellationToken ct)
    {
        var found = await store.FindAsync(command.Sku, ct);
        switch (found)
        {
            case Stock stock:
                return stock.Reserve(command.Quantity) switch
                {
                    Reservation reservation => reservation,
                    OutOfStock error => error,
                };
            case StockUnknown:
                return await store.CreateAndReserveAsync(command.Sku, command.Quantity, ct) switch
                {
                    Reservation reservation => reservation,
                    SkuInvalid error => error,
                };
            case StockBlocked error:
                return error;
        }
        throw new InvalidOperationException("A FindStockResult without a value.");
    }
}

// ---- GraphQL: HotChocolate knows FieldResult, not C# unions, so the resolver maps by hand (at most 5 error types)
public static class StockMutations
{
    // [Mutation] in an application. HotChocolate reads the error types from the type arguments of FieldResult.
    public static async Task<FieldResult<Reservation, StockBlocked, OutOfStock, SkuInvalid>> ReserveStockAsync(
        string sku, int quantity, ReserveStockHandler handler, CancellationToken ct)
        => await handler.Handle(new ReserveStockCommand(sku, quantity), ct) switch
        {
            Reservation reservation => reservation,
            StockBlocked error => error,
            OutOfStock error => error,
            SkuInvalid error => error,
        };
}
