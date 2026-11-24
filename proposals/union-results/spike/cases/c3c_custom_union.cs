[System.Runtime.CompilerServices.Union]
public partial struct Big : System.Runtime.CompilerServices.IUnion { }
public partial struct Big
{
    private readonly object? _value;
    public Big(StockBlocked value) => _value = value;
    public Big(OutOfStock value) => _value = value;
    public object? Value => _value;
}
public static class C
{
    public static Big H() => new OutOfStock(1);
    public static string S(Big b) => b switch { StockBlocked => "b", OutOfStock => "o" };
    public static string S2(Big b) => b switch { StockBlocked => "b" };
}
