#if NET
namespace DTS.DDD.Mediator;

/// <summary>
/// Sends a request, a command or a query to its one handler, through the pipeline behaviors of its type. Resolve it
/// from a scope. The handler and the behaviors come from the same scope.
/// <para>
/// The handler is found by the message's runtime type. A message without a handler is a
/// <see cref="MissingMessageHandlerException"/>, which names the message's type.
/// </para>
/// </summary>
public interface ISender
{
    /// <summary>Sends <paramref name="request"/> to its handler.</summary>
    /// <typeparam name="TResponse">What the handler answers with.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="MissingMessageHandlerException">No handler is registered for the request's type.</exception>
    ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>Sends <paramref name="command"/> to its handler.</summary>
    /// <typeparam name="TResponse">What the handler answers with.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    /// <exception cref="MissingMessageHandlerException">No handler is registered for the command's type.</exception>
    ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    /// <summary>Sends <paramref name="query"/> to its handler.</summary>
    /// <typeparam name="TResponse">What the handler answers with.</typeparam>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    /// <exception cref="MissingMessageHandlerException">No handler is registered for the query's type.</exception>
    ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends <paramref name="message"/> to its handler. This overload is for a message held as <see cref="object"/>,
    /// such as one read back from a queue.
    /// </summary>
    /// <param name="message">A request, a command or a query.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    /// <returns>What the handler answered with.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="InvalidMessageException"><paramref name="message"/> is no request, command or query.</exception>
    /// <exception cref="MissingMessageHandlerException">No handler is registered for the message's type.</exception>
    ValueTask<object?> Send(object message, CancellationToken cancellationToken = default);

    /// <summary>Hands <paramref name="query"/> to its handler and answers with the stream it returns.</summary>
    /// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    /// <exception cref="MissingMessageHandlerException">No handler is registered for the query's type.</exception>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default);

    /// <summary>Hands <paramref name="request"/> to its handler and answers with the stream it returns.</summary>
    /// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="MissingMessageHandlerException">No handler is registered for the request's type.</exception>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>Hands <paramref name="command"/> to its handler and answers with the stream it returns.</summary>
    /// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    /// <exception cref="MissingMessageHandlerException">No handler is registered for the command's type.</exception>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamCommand<TResponse> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hands <paramref name="message"/> to its handler and answers with the stream it returns. This overload is for a
    /// message held as <see cref="object"/>.
    /// </summary>
    /// <param name="message">A stream request, a stream command or a stream query.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="InvalidMessageException"><paramref name="message"/> is no stream request, stream command or stream query.</exception>
    /// <exception cref="MissingMessageHandlerException">No handler is registered for the message's type.</exception>
    IAsyncEnumerable<object?> CreateStream(object message, CancellationToken cancellationToken = default);
}

/// <summary>
/// The toolkit's mediator: it sends requests (<see cref="ISender"/>) and publishes notifications
/// (<see cref="IPublisher"/>). Resolve it from a scope.
/// </summary>
public interface IMediator : ISender, IPublisher;
#endif
