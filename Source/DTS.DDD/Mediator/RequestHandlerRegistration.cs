namespace DTS.DDD.Mediator;

/// <summary>
/// The one handler of one message type, as the registrations of <see cref="MediatorServiceCollectionExtensions"/> add
/// it: a request, a command or a query, or one of their stream kinds. The delegates were closed over the message,
/// its response and the handler when the registration was written, so a send casts the message and calls the handler
/// without reflection.
/// </summary>
/// <param name="messageType">The type the handler is declared for.</param>
/// <param name="responseType">What the handler answers with, or the type of each item of its stream.</param>
/// <param name="handlerType">The handler class.</param>
/// <param name="streamed">Whether the handler answers with a stream.</param>
/// <param name="typed">
/// The call that answers with the declared response: a <c>Func&lt;IServiceProvider, object, CancellationToken, ValueTask&lt;TResponse&gt;&gt;</c>,
/// or for a stream a <c>Func&lt;IServiceProvider, object, CancellationToken, IAsyncEnumerable&lt;TResponse&gt;&gt;</c>.
/// </param>
/// <param name="send">The call that answers with the response as an object, for a message sent as one.</param>
/// <param name="stream">The call that answers with the stream's items as objects, for a stream message sent as one.</param>
internal sealed class RequestHandlerRegistration(
    Type messageType,
    Type responseType,
    Type handlerType,
    bool streamed,
    Delegate typed,
    Func<IServiceProvider, object, CancellationToken, ValueTask<object?>>? send,
    Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<object?>>? stream)
{
    /// <summary>The type the handler is declared for.</summary>
    public Type MessageType { get; } = messageType;

    /// <summary>What the handler answers with, or the type of each item of its stream.</summary>
    public Type ResponseType { get; } = responseType;

    /// <summary>The handler class.</summary>
    public Type HandlerType { get; } = handlerType;

    /// <summary>Whether the handler answers with a stream.</summary>
    public bool Streamed { get; } = streamed;

    /// <summary>The call that answers with the declared response, or the declared stream.</summary>
    public Delegate Typed { get; } = typed;

    /// <summary>The call that answers with the response as an object. Null for a stream.</summary>
    public Func<IServiceProvider, object, CancellationToken, ValueTask<object?>>? Send { get; } = send;

    /// <summary>The call that answers with the stream's items as objects. Null for a message answered once.</summary>
    public Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<object?>>? Stream { get; } = stream;
}
