namespace DTS.DDD.Mediator;

/// <summary>
/// The toolkit's <see cref="IMediator"/>. It is scoped, and resolves every handler and behavior from its own scope.
/// It publishes through the scope's <see cref="IPublisher"/>.
/// </summary>
/// <param name="services">The scope's provider.</param>
/// <param name="routes">The handler of every message type.</param>
/// <param name="publisher">The scope's publisher.</param>
internal sealed class Sender(IServiceProvider services, RequestRoutes routes, IPublisher publisher) : IMediator
{
    /// <inheritdoc />
    public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => SendTyped<TResponse>(request ?? throw new ArgumentNullException(nameof(request)), cancellationToken);

    /// <inheritdoc />
    public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
        => SendTyped<TResponse>(command ?? throw new ArgumentNullException(nameof(command)), cancellationToken);

    /// <inheritdoc />
    public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
        => SendTyped<TResponse>(query ?? throw new ArgumentNullException(nameof(query)), cancellationToken);

    /// <inheritdoc />
    public ValueTask<object?> Send(object message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message is not (IBaseRequest or IBaseCommand or IBaseQuery))
        {
            throw new InvalidMessageException(message);
        }

        var route = routes.For(message.GetType(), streamed: false) ?? throw new MissingMessageHandlerException(message);
        return route.Send!(services, message, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default)
        => StreamTyped<TResponse>(query ?? throw new ArgumentNullException(nameof(query)), cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        => StreamTyped<TResponse>(request ?? throw new ArgumentNullException(nameof(request)), cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamCommand<TResponse> command, CancellationToken cancellationToken = default)
        => StreamTyped<TResponse>(command ?? throw new ArgumentNullException(nameof(command)), cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<object?> CreateStream(object message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message is not (IBaseStreamRequest or IBaseStreamCommand or IBaseStreamQuery))
        {
            throw new InvalidMessageException(message);
        }

        var route = routes.For(message.GetType(), streamed: true) ?? throw new MissingMessageHandlerException(message);
        return route.Stream!(services, message, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
        => publisher.Publish(notification, cancellationToken);

    /// <inheritdoc />
    public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
        => publisher.Publish(notification, cancellationToken);

    private ValueTask<TResponse> SendTyped<TResponse>(object message, CancellationToken cancellationToken)
    {
        var route = routes.For(message.GetType(), streamed: false) ?? throw new MissingMessageHandlerException(message);

        // The handler's own response type is the usual case. A message declared IRequest<Derived> and sent as
        // IRequest<Base> answers with its own type, so it goes through the call that answers with an object.
        return route.Typed is Func<IServiceProvider, object, CancellationToken, ValueTask<TResponse>> typed
            ? typed(services, message, cancellationToken)
            : Cast<TResponse>(route.Send!(services, message, cancellationToken));
    }

    private IAsyncEnumerable<TResponse> StreamTyped<TResponse>(object message, CancellationToken cancellationToken)
    {
        var route = routes.For(message.GetType(), streamed: true) ?? throw new MissingMessageHandlerException(message);

        return route.Typed is Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<TResponse>> typed
            ? typed(services, message, cancellationToken)
            : CastEach<TResponse>(route.Stream!(services, message, cancellationToken));
    }

    private static async ValueTask<TResponse> Cast<TResponse>(ValueTask<object?> response) => (TResponse)(await response.ConfigureAwait(false))!;

    private static async IAsyncEnumerable<TResponse> CastEach<TResponse>(IAsyncEnumerable<object?> items)
    {
        await foreach (var item in items.ConfigureAwait(false))
        {
            yield return (TResponse)item!;
        }
    }
}
