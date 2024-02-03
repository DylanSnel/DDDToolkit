namespace DDDToolkit.BaseTypes;
public abstract record ValueObject
{
    public abstract IEnumerable<object?> GetEqualityComponents();

    public override int GetHashCode()
        => GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);
}
public abstract record ValueObject<TDerived> : ValueObject
{
    //protected virtual TDerived Transform(TDerived value) => value

    public virtual bool Validate() => true;
}
