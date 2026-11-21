public union Big(StockBlocked, OutOfStock);
public static class C
{
    public static Result<Reservation, Big> A() => new OutOfStock(0);           // case -> union -> Result: two conversions
    public static Result<Reservation, Big> B() => new Reservation("tea", 1);
    public static Result<Reservation, Big> D() => (Big)new OutOfStock(0);
}
