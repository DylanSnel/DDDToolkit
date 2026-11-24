public union FindStockError(StockUnknown, StockBlocked);
public union Big(StockBlocked, OutOfStock);
public static class C { public static Big H(FindStockError e) => e switch { StockUnknown => new OutOfStock(0) }; }
