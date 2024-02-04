using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Abstractions.Validation;
public record Raw<TValueObject> where TValueObject : IValueObject
{
    private readonly TValueObject _valueObject;
    private bool? _isValid;

    public Raw(TValueObject valueObject)
    {
        _valueObject = valueObject;
    }

    public bool IsValid
    {
        get
        {
            if (_isValid == null)
            {
                // Validate the _valueObject and cache the result.
                _isValid = _valueObject.Validate();
            }
            return _isValid.Value;
        }
    }

    public bool IsValidated => _isValid != null;

    public static implicit operator TValueObject(Raw<TValueObject> raw)
    {
        if (!raw.IsValid)
        {
            throw new InvalidOperationException("Cannot convert to TValueObject because the object is not valid.");
        }
        return raw._valueObject;
    }
}


