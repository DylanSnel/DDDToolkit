namespace DDDToolkit.Exceptions;
public class InvalidValueObjectException : DDDToolkitException
{
    public InvalidValueObjectException(Type objectType) : base($"The value object {objectType.Name} is invalid.")
    {
    }
}
