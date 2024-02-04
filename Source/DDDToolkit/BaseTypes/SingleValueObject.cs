
namespace DDDToolkit.BaseTypes;
public abstract record SingleValueObject<T> : ValueObject where T : notnull
{
    protected SingleValueObject(T value) => Value = value;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    protected SingleValueObject(bool bypassValidation = false) : base(bypassValidation) { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public T Value { get; protected set; }
    protected virtual T Transform(T value) => value;

    public override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
