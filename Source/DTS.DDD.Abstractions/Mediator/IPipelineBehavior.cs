#if NET
namespace DTS.DDD.Mediator;

/// <summary>
/// Runs around the handler of a request, a command or a query. Register it with
/// <c>services.AddPipelineBehavior(typeof(MyBehavior&lt;,&gt;))</c> for every message, or closed over one message
/// for that message alone.
/// <code>
/// internal sealed class TimeTheHandler&lt;TMessage, TResponse&gt;(ILogger&lt;TMessage&gt; logger)
///     : IPipelineBehavior&lt;TMessage, TResponse&gt;
///     where TMessage : IMessage
/// {
///     public async ValueTask&lt;TResponse&gt; Handle(TMessage message, MessageHandlerDelegate&lt;TMessage, TResponse&gt; next, CancellationToken cancellationToken)
///     {
///         var started = Stopwatch.GetTimestamp();
///         var response = await next(message, cancellationToken);
///         logger.LogDebug("{Message} was handled in {Elapsed}", typeof(TMessage).Name, Stopwatch.GetElapsedTime(started));
///         return response;
///     }
/// }
/// </code>
/// <para>
/// A behavior is resolved closed over the message's handler type and its response, from the scope that sends. An
/// open generic behavior with a constraint applies only to the messages that satisfy the constraint. The behavior
/// registered first is the outermost. A message answered with a stream passes the behaviors of
/// <see cref="IStreamPipelineBehavior{TMessage, TResponse}"/> instead.
/// </para>
/// </summary>
/// <typeparam name="TMessage">The message the handler is declared for.</typeparam>
/// <typeparam name="TResponse">What the handler answers with.</typeparam>
public interface IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    /// <summary>Runs around the handler. Call <paramref name="next"/> to run it.</summary>
    /// <param name="message">The message being handled.</param>
    /// <param name="next">The next behavior, or the handler itself.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);
}

/// <summary>The next step around a handler: another behavior, or the handler.</summary>
/// <typeparam name="TMessage">The message the handler is declared for.</typeparam>
/// <typeparam name="TResponse">What the handler answers with.</typeparam>
/// <param name="message">The message being handled.</param>
/// <param name="cancellationToken">Cancels the handler.</param>
public delegate ValueTask<TResponse> MessageHandlerDelegate<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken)
    where TMessage : notnull, IMessage;

/// <summary>
/// Runs around the handler of a stream request, a stream command or a stream query. Register it with
/// <c>services.AddStreamPipelineBehavior(typeof(MyBehavior&lt;,&gt;))</c>. It is resolved and ordered as an
/// <see cref="IPipelineBehavior{TMessage, TResponse}"/> is.
/// </summary>
/// <typeparam name="TMessage">The message the handler is declared for.</typeparam>
/// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
public interface IStreamPipelineBehavior<TMessage, TResponse>
    where TMessage : IStreamMessage
{
    /// <summary>Runs around the handler. Call <paramref name="next"/> for the stream it answers with.</summary>
    /// <param name="message">The message being handled.</param>
    /// <param name="next">The next behavior, or the handler itself.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    IAsyncEnumerable<TResponse> Handle(TMessage message, StreamHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);
}

/// <summary>The next step around a stream handler: another behavior, or the handler.</summary>
/// <typeparam name="TMessage">The message the handler is declared for.</typeparam>
/// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
/// <param name="message">The message being handled.</param>
/// <param name="cancellationToken">Cancels the stream.</param>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken)
    where TMessage : IStreamMessage;
#endif
