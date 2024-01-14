using FluentResults;

namespace DDDToolkit.BaseTypes;
public abstract class SingleValueObject<T> : ValueObject where T : notnull
{
    protected SingleValueObject(T value) => Value = value;
    public T Value { get; protected set; }
    protected virtual Result Validate(T value) => Result.Ok();
    protected virtual T Transform(T value) => value;

    public override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    protected SingleValueObject() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

}
