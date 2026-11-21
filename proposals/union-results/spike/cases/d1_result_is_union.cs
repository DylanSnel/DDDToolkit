public union FindStock(Reservation, StockUnknown, StockBlocked);
public union CreateStock(Reservation, SkuInvalid);
public partial union ReserveStock(Reservation, StockBlocked, OutOfStock, SkuInvalid);

// what the generator would write: CreateStock fits in ReserveStock, FindStock does not (StockUnknown)
public partial union ReserveStock
{
    public static implicit operator ReserveStock(CreateStock from) => from.Value switch
    {
        Reservation r => new ReserveStock(r),
        SkuInvalid e => new ReserveStock(e),
        _ => throw new System.InvalidOperationException(),
    };
}

public static class C
{
    static CreateStock Create() => new Reservation("tea", 1);
    public static ReserveStock A() => new OutOfStock(0);
    public static ReserveStock B() => new Reservation("tea", 1);
    public static ReserveStock PassThrough() => Create();
    public static ReserveStock H(FindStock found) => found switch
    {
        Reservation r => r,
        StockUnknown => Create(),
        StockBlocked b => b,
    };
    public static ReserveStock Bad(FindStock found) => found;
}
