public union Small(StockBlocked);
public union Big(StockBlocked, OutOfStock);
public static class Widening
{
    extension<T>(Result<T, Small>)
    {
        public static implicit operator Result<T, Big>(Result<T, Small> r) => default;
    }
}
