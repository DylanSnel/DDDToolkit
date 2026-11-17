#if NET
namespace DTS.DDD.Mediator;

/// <summary>What every plain request implements, whatever it is answered with.</summary>
public interface IBaseRequest : IMessage;

/// <summary>
/// A request answered with a <typeparamref name="TResponse"/>. <see cref="ISender"/> sends it to its one
/// <see cref="IRequestHandler{TRequest, TResponse}"/>. A request that changes something is a command
/// (<see cref="ICommand{TResponse}"/>), and one that only reads is a query (<see cref="IQuery{TResponse}"/>).
/// </summary>
/// <typeparam name="TResponse">What the handler answers with.</typeparam>
public interface IRequest<out TResponse> : IBaseRequest;

/// <summary>A request answered with nothing but <see cref="Unit"/>.</summary>
public interface IRequest : IRequest<Unit>;

/// <summary>What every command implements, whatever it is answered with.</summary>
public interface IBaseCommand : IMessage;

/// <summary>
/// A command answered with a <typeparamref name="TResponse"/>: a request that changes something. <see cref="ISender"/>
/// sends it to its one <see cref="ICommandHandler{TCommand, TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">What the handler answers with.</typeparam>
public interface ICommand<out TResponse> : IBaseCommand;

/// <summary>A command answered with nothing but <see cref="Unit"/>.</summary>
public interface ICommand : ICommand<Unit>;

/// <summary>What every query implements, whatever it is answered with.</summary>
public interface IBaseQuery : IMessage;

/// <summary>
/// A query answered with a <typeparamref name="TResponse"/>: a request that only reads. <see cref="ISender"/> sends
/// it to its one <see cref="IQueryHandler{TQuery, TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">What the handler answers with.</typeparam>
public interface IQuery<out TResponse> : IBaseQuery;

/// <summary>What every stream request implements, whatever its items are.</summary>
public interface IBaseStreamRequest : IStreamMessage;

/// <summary>
/// A request answered with a stream of <typeparamref name="TResponse"/>. <see cref="ISender.CreateStream{TResponse}(IStreamRequest{TResponse}, CancellationToken)"/>
/// hands it to its one <see cref="IStreamRequestHandler{TRequest, TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
public interface IStreamRequest<out TResponse> : IBaseStreamRequest;

/// <summary>What every stream command implements, whatever its items are.</summary>
public interface IBaseStreamCommand : IStreamMessage;

/// <summary>
/// A command answered with a stream of <typeparamref name="TResponse"/>. <see cref="ISender.CreateStream{TResponse}(IStreamCommand{TResponse}, CancellationToken)"/>
/// hands it to its one <see cref="IStreamCommandHandler{TCommand, TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
public interface IStreamCommand<out TResponse> : IBaseStreamCommand;

/// <summary>What every stream query implements, whatever its items are.</summary>
public interface IBaseStreamQuery : IStreamMessage;

/// <summary>
/// A query answered with a stream of <typeparamref name="TResponse"/>. <see cref="ISender.CreateStream{TResponse}(IStreamQuery{TResponse}, CancellationToken)"/>
/// hands it to its one <see cref="IStreamQueryHandler{TQuery, TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
public interface IStreamQuery<out TResponse> : IBaseStreamQuery;
#endif
