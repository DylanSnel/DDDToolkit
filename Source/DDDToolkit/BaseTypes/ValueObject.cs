using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;

namespace DDDToolkit.BaseTypes;
public abstract record ValueObject : IValueObject
{
    /// <summary>True once <see cref="IsValid"/> has been read and the verdict cached.</summary>
    [Internal]
    public bool IsValidated => _isValid != null;

    /// <summary>
    /// Whether the value satisfies its own rules. Computed on first read and cached for the lifetime of
    /// the instance, which is safe because a value object never changes after construction.
    /// </summary>
    [Internal]
    public bool IsValid => _isValid ??= Validate();

    /// <summary>The cached verdict: null until something asks for it.</summary>
    protected bool? _isValid = null;

    /// <summary>
    /// The rules. The default accepts everything; DDDToolkit.FluentValidation overrides this with a
    /// generated body that runs the nested <c>Validator</c>.
    /// </summary>
    [Internal]
    protected virtual bool Validate() => true;

    /// <summary>Throws <see cref="InvalidValueObjectException"/> unless <see cref="IsValid"/>.</summary>
    [Internal]
    public virtual void EnsureValidated()
    {
        if (!IsValid)
        {
            throw new InvalidValueObjectException(this.GetType());
        }
    }

    [Internal]
    protected abstract IEnumerable<object?> GetEqualityComponents();


    public override int GetHashCode()
        => GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);

    protected ValueObject()
    {
    }

    /// <summary>
    /// Copy constructor used by <c>record with</c>. It deliberately does <b>not</b> copy the cached
    /// verdict: a copy differs from its source in at least one component, so the source's verdict says
    /// nothing about it. The copy revalidates the first time anything asks.
    /// </summary>
    protected ValueObject(ValueObject original)
    {
        ArgumentNullException.ThrowIfNull(original);
        _isValid = null;
    }
}

//public abstract record ValueObject<TInterface> : IValueObject where TInterface : class, IValueObject<TInterface>
//{
//    [Internal]
//    public bool IsValidated => _isValid != null;
//    [Internal]
//    public bool IsValid => _isValid ??= Validate((this as TInterface)!);


//    protected bool? _isValid = null;

//    [Internal]
//    public virtual bool Validate(TInterface valueObject) => true;

//    [EditorBrowsable(EditorBrowsableState.Never)]
//    protected static bool ValidateRaw(TInterface valueObject)
//    {
//        return valueObject.Validate(valueObject);
//    }

//    [Internal]
//    public virtual void EnsureValidated()
//    {
//        if (!IsValid)
//        {
//            throw new InvalidValueObjectException(this.GetType());
//        }
//    }

//    [Internal]
//    public abstract IEnumerable<object?> GetEqualityComponents();


//    public override int GetHashCode()
//        => GetEqualityComponents()
//            .Select(x => x?.GetHashCode() ?? 0)
//            .Aggregate((x, y) => x ^ y);

//    protected ValueObject()
//    {
//    }
//}

