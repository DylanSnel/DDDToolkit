using Sketch;

// ---- the generator writes this per [ErrorCode] record
public sealed partial record StockUnknown
{
    public override string Code => "stock.unknown";
    public override string Message => $"There is no stock with SKU {Sku}.";
    public override IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?> { ["Sku"] = Sku };
}
public sealed partial record StockBlocked
{
    public override string Code => "stock.blocked";
    public override string Message => $"Stock {Sku} is blocked.";
    public override IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?> { ["Sku"] = Sku };
}
public sealed partial record OutOfStock
{
    public override string Code => "stock.out-of-stock";
    public override string Message => $"Only {Available} left.";
    public override IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?> { ["Available"] = Available };
}
public sealed partial record SkuInvalid
{
    public override string Code => "stock.sku-invalid";
    public override string Message => $"{Sku} is no valid SKU.";
    public override IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?> { ["Sku"] = Sku };
}

// ---- the generator writes this per pair of unions where every case of the smaller one is in the larger one
public partial union ReserveStockResult
{
    public static implicit operator ReserveStockResult(ReserveResult from) => from.Value switch
    {
        Reservation value => new ReserveStockResult(value),
        OutOfStock value => new ReserveStockResult(value),
        _ => throw new InvalidOperationException("A ReserveResult without a value."),
    };

    public static implicit operator ReserveStockResult(CreateStockResult from) => from.Value switch
    {
        Reservation value => new ReserveStockResult(value),
        SkuInvalid value => new ReserveStockResult(value),
        _ => throw new InvalidOperationException("A CreateStockResult without a value."),
    };
}
