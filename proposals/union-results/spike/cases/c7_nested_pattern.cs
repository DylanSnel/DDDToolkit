public union Inner(StockUnknown, StockBlocked);
public union Outer(OutOfStock, Inner);
public static class C
{
    public static string S(Outer o) => o switch { OutOfStock => "o", Inner { Value: StockUnknown } => "u" };
}
