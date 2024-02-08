using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.BaseTypes;
public abstract record ValueObject : IValueObject
{
    public bool IsValidated => _isValid != null;

    public bool IsValid => _isValid ??= Validate();

    protected bool? _isValid = null;

    protected virtual bool Validate() => true;

    public virtual List<object> Errors { get; private set; } = [];


    public virtual void EnsureValidated()
    {
        Validate();
        if (!IsValid)
        {
            throw new InvalidOperationException("Value object is not valid");
        }
    }

    public abstract IEnumerable<object?> GetEqualityComponents();

    public override int GetHashCode()
        => GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);

    protected ValueObject()
    {
    }
}

