#if NET
namespace DTS.DDD.Mediator;

/// <summary>
/// Handles one kind of request. A request has exactly one handler. The toolkit's generator registers every handler of
/// a project in <c>Add{Module}Handlers()</c>. A handler may be internal, and it is resolved from the scope that sends.
/// </summary>
/// <typeparam name="TRequest">The request this handler answers.</typeparam>
/// <typeparam name="TResponse">What it answers with.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Answers <paramref name="request"/>.</summary>
    /// <param name="request">The request that was sent.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    ValueTask<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Handles one kind of request answered with <see cref="Unit"/>.</summary>
/// <typeparam name="TRequest">The request this handler answers.</typeparam>
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest<Unit>;

/// <summary>
/// Handles one kind of command. A command has exactly one handler. The toolkit's generator registers every handler of
/// a project in <c>Add{Module}Handlers()</c>. A handler may be internal, and it is resolved from the scope that sends.
/// </summary>
/// <typeparam name="TCommand">The command this handler carries out.</typeparam>
/// <typeparam name="TResponse">What it answers with.</typeparam>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    /// <summary>Carries out <paramref name="command"/>.</summary>
    /// <param name="command">The command that was sent.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    ValueTask<TResponse> Handle(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles one kind of command answered with <see cref="Unit"/>.</summary>
/// <typeparam name="TCommand">The command this handler carries out.</typeparam>
public interface ICommandHandler<in TCommand> : ICommandHandler<TCommand, Unit>
    where TCommand : ICommand<Unit>;

/// <summary>
/// Handles one kind of query. A query has exactly one handler. The toolkit's generator registers every handler of a
/// project in <c>Add{Module}Handlers()</c>. A handler may be internal, and it is resolved from the scope that sends.
/// </summary>
/// <typeparam name="TQuery">The query this handler answers.</typeparam>
/// <typeparam name="TResponse">What it answers with.</typeparam>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    /// <summary>Answers <paramref name="query"/>.</summary>
    /// <param name="query">The query that was sent.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    ValueTask<TResponse> Handle(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Handles one kind of stream request. A stream request has exactly one handler.</summary>
/// <typeparam name="TRequest">The request this handler answers.</typeparam>
/// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <summary>Answers <paramref name="request"/> with a stream.</summary>
    /// <param name="request">The request that was sent.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Handles one kind of stream command. A stream command has exactly one handler.</summary>
/// <typeparam name="TCommand">The command this handler carries out.</typeparam>
/// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
public interface IStreamCommandHandler<in TCommand, out TResponse>
    where TCommand : IStreamCommand<TResponse>
{
    /// <summary>Carries out <paramref name="command"/> and answers with a stream.</summary>
    /// <param name="command">The command that was sent.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    IAsyncEnumerable<TResponse> Handle(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles one kind of stream query. A stream query has exactly one handler.</summary>
/// <typeparam name="TQuery">The query this handler answers.</typeparam>
/// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
public interface IStreamQueryHandler<in TQuery, out TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    /// <summary>Answers <paramref name="query"/> with a stream.</summary>
    /// <param name="query">The query that was sent.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    IAsyncEnumerable<TResponse> Handle(TQuery query, CancellationToken cancellationToken);
}
#endif
