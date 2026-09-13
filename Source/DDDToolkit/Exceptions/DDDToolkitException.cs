namespace DDDToolkit.Exceptions;

public abstract class DDDToolkitException : Exception
{
    protected DDDToolkitException(string message) : base(message)
    {
    }

    protected DDDToolkitException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
