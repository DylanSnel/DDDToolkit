#if NET
namespace DTS.DDD.Mediator;

/// <summary>
/// Thrown when a request, a command or a query is sent and no handler is registered for its type. The message names
/// the type. A handler of a base class or an interface of the type does not count. The generated
/// <c>Add{Module}Handlers()</c> of the module that handles it registers the handler.
/// </summary>
public sealed class MissingMessageHandlerException : Exception
{
    /// <summary>Makes the exception for <paramref name="message"/>.</summary>
    /// <param name="message">The message that has no handler.</param>
    public MissingMessageHandlerException(object? message)
        : base(Describe(message))
    {
        MediatorMessage = message;
    }

    /// <summary>The message that has no handler.</summary>
    public object? MediatorMessage { get; }

    private static string Describe(object? message)
        => message is null
            ? "No handler is registered for the message, which is null."
            : $"No handler is registered for '{message.GetType().FullName}'. A handler of a base class or an interface of it does not handle it. The generated Add{{Module}}Handlers() of the module that handles it registers its handler.";
}

/// <summary>
/// Thrown when <see cref="ISender.Send(object, CancellationToken)"/> or <see cref="ISender.CreateStream(object, CancellationToken)"/>
/// is handed an object that is no message of the kind it sends. The message names the object's type.
/// </summary>
public sealed class InvalidMessageException : Exception
{
    /// <summary>Makes the exception for <paramref name="message"/>.</summary>
    /// <param name="message">The object that is no message of the kind.</param>
    public InvalidMessageException(object? message)
        : base(message is null
            ? "The message is null."
            : $"'{message.GetType().FullName}' is no request, command or query of the toolkit's mediator, or no stream one where a stream was asked for.")
    {
        MediatorMessage = message;
    }

    /// <summary>The object that is no message of the kind.</summary>
    public object? MediatorMessage { get; }
}
#endif
