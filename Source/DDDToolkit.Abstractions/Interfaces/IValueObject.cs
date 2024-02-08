
namespace DDDToolkit.Abstractions.Interfaces;
public interface IValueObject
{
    bool IsValidated { get; }
    bool IsValid { get; }
    List<object> Errors { get; }
}
