namespace DDDToolkit.Exceptions;
public class SerializationNotAllowedException : DDDToolkitException
{
    public SerializationNotAllowedException(Type objectType) : base($"Direct deserialization of {objectType.Name} is not allowed. Please deserialize to {objectType.Name.Replace("Valid", "")} instead.")
    {
    }
}
