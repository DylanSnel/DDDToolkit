public sealed record StockUnknown(string Sku);
public sealed record StockBlocked(string Sku);
public sealed record OutOfStock(int Available);
public sealed record SkuInvalid(string Sku);
public sealed record Reservation(string Sku, int Quantity);

public readonly struct Result<T, TError>
{
    private readonly T? _value; private readonly TError _error; public bool IsSuccess { get; }
    private Result(T value) { _value = value; _error = default!; IsSuccess = true; }
    private Result(TError error) { _value = default; _error = error; IsSuccess = false; }
    public T Value => _value!; public TError Error => _error;
    public static implicit operator Result<T, TError>(T value) => new(value);
    public static implicit operator Result<T, TError>(TError error) => new(error);
}
