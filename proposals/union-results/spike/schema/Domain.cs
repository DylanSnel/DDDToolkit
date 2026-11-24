// Errors carry a marker so the interceptor tells them from the success case.
public interface IDomainError { string Message { get; } }

public sealed record StockUnknown(string Sku) : IDomainError { public string Message => $"There is no stock with SKU {Sku}."; }
public sealed record StockBlocked(string Sku) : IDomainError { public string Message => $"Stock {Sku} is blocked."; }
public sealed record OutOfStock(int Available) : IDomainError { public string Message => $"Only {Available} left."; }
public sealed record SkuInvalid(string Sku) : IDomainError { public string Message => $"{Sku} is no valid SKU."; }

public sealed record Reservation(string Sku, int Quantity);

public union ReserveStockResult(Reservation, StockBlocked, OutOfStock, SkuInvalid);

public static class Handler
{
    public static ReserveStockResult Handle(string sku, int quantity) => sku switch
    {
        "blocked" => new StockBlocked(sku),
        "bad" => new SkuInvalid(sku),
        _ when quantity > 5 => new OutOfStock(5),
        _ => new Reservation(sku, quantity),
    };
}
