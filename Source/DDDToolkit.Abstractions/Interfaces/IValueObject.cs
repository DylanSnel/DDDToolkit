
namespace DDDToolkit.Abstractions.Interfaces;

public interface IValueObject<T> : IValueObject where T : class
{
    bool Validate(T value);
}


public interface IValueObject
{
    bool IsValidated { get; }
    bool IsValid { get; }
}
