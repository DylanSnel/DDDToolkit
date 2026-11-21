public partial union Big(StockBlocked, OutOfStock);
public partial union Big { public string Describe() => Value?.ToString() ?? ""; }
