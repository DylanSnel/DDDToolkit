using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using System.ComponentModel;

namespace DDDToolkit.BaseTypes;
public abstract record ValueObject : IValueObject
{
    [Internal]
    public bool IsValidated => _isValid != null;
    [Internal]
    public bool IsValid => _isValid ??= Validate();

    protected bool? _isValid = null;

    [Internal]
    protected virtual bool Validate() => true;

    [Internal]
    public virtual void EnsureValidated()
    {
        if (!IsValid)
        {
            throw new InvalidValueObjectException(this.GetType());
        }
    }

    [Internal]
    public abstract IEnumerable<object?> GetEqualityComponents();


    public override int GetHashCode()
        => GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);

    protected ValueObject()
    {
    }
}

public abstract record ValueObject<TInterface> : IValueObject where TInterface : class, IValueObject<TInterface>
{
    [Internal]
    public bool IsValidated => _isValid != null;
    [Internal]
    public bool IsValid => _isValid ??= Validate((this as TInterface)!);


    protected bool? _isValid = null;

    [Internal]
    public virtual bool Validate(TInterface valueObject) => true;

    [EditorBrowsable(EditorBrowsableState.Never)]
    protected static bool ValidateRaw(TInterface valueObject)
    {
        return valueObject.Validate(valueObject);
    }

    [Internal]
    public virtual void EnsureValidated()
    {
        if (!IsValid)
        {
            throw new InvalidValueObjectException(this.GetType());
        }
    }

    [Internal]
    public abstract IEnumerable<object?> GetEqualityComponents();


    public override int GetHashCode()
        => GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);

    protected ValueObject()
    {
    }
}

