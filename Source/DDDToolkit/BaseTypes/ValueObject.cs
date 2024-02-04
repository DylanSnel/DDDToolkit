using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.BaseTypes;
public abstract record ValueObject : IValueObject
{
    public virtual bool Validate() => true;

    public abstract IEnumerable<object?> GetEqualityComponents();

    public override int GetHashCode()
        => GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);

    protected ValueObject()
    {

    }
}

