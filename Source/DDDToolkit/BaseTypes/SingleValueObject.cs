using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.BaseTypes;



public abstract record SingleValueObject<T> : ValueObject, ISingleValueObject where T : notnull
{
    protected SingleValueObject(T value) => Value = value;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    protected SingleValueObject() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public T Value { get; protected set; }

    public override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}

public abstract record SingleValueObject<T, TInterface> : ValueObject<TInterface>, ISingleValueObject where T : notnull where TInterface : class, IValueObject<TInterface>
{
    protected SingleValueObject(T value) => Value = value;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    protected SingleValueObject() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public T Value { get; protected set; }

    public override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}



public interface ISingleValueObject { }
