public union FindStockError(StockUnknown, StockBlocked);
public union Big(StockBlocked, OutOfStock);
public static class C
{
    static Result<Reservation, Big> Create() => new Reservation("tea", 1);
    public static Result<Reservation, Big> H(FindStockError e) => e switch
    {
        StockUnknown => Create(),
        StockBlocked b => b,
    };
}
