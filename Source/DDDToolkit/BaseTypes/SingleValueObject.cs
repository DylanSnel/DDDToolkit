﻿
namespace DDDToolkit.BaseTypes;
public abstract record SingleValueObject<T> : ValueObject where T : notnull
{
    protected SingleValueObject(T value) => Value = value;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    protected SingleValueObject() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public T Value { get; protected set; }
    protected virtual T Transform(T value) => value;

    public override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}

public abstract record SingleValueObject<T, TDerived> : ValueObject<TDerived> where T : notnull
{
    protected SingleValueObject(T value) : this() => Value = value;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    protected SingleValueObject()
    {
        if (!this.Validate())
        {
            throw new InvalidOperationException();
        }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public T Value { get; protected set; }

    public override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }



}
