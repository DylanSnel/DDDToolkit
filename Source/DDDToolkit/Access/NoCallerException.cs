namespace DDDToolkit.Access;

/// <summary>
/// Nobody is calling, and the host requires every flow of work to say who it runs as
/// (<see cref="CallerOptions.RequireExplicitCallers"/>). Begin a caller around the work that threw it.
/// </summary>
public sealed class NoCallerException : InvalidOperationException
{
    /// <summary>What went wrong, and the two ways to put it right.</summary>
    public const string DefaultMessage =
        "Nothing said who this work runs as, and this host requires it (RequireExplicitCallers). Begin a caller around it: " +
        "Callers.Begin(Caller.System) for the application's own work, or a scoped caller a module gives you.";

    /// <summary>An exception with <see cref="DefaultMessage"/>.</summary>
    public NoCallerException()
        : base(DefaultMessage)
    {
    }

    /// <summary>An exception with a message that names the work and how to give it a caller.</summary>
    public NoCallerException(string message)
        : base(message)
    {
    }

    /// <summary>An exception with a message and the exception behind it.</summary>
    public NoCallerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
