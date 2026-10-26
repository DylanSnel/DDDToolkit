using System.Diagnostics;
using Mediator;

namespace Examples.Tenancy.Host.Requests;

/// <summary>
/// The outermost step of every request's pipeline: an activity around everything that follows, the access check
/// and the handler, so a trace shows each command and query by name, how long it took, and how it ended.
/// </summary>
/// <remarks>
/// The pipeline of a request, in the order the steps were registered: this, registered by the host before the
/// modules (<see cref="RequestTracingServices.AddRequestTracing"/>); the access check of the request's module,
/// registered by that module; then the handler, which saves through its store. So a request that is refused
/// before its handler runs still has its activity, tagged with the code it was refused with.
/// <para>
/// A refusal is an answer, not a fault, so it is tagged and the activity is not marked failed. So is everything
/// else the host answers with a status of its own: a value that broke its rules, a rule an aggregate reports
/// broken, and a lost race (<see cref="RequestTracing.AnswerOf"/>). A request whose caller went away is neither
/// answered nor failed, and is left unmarked. Anything else that is thrown is a fault, and marks it.
/// </para>
/// <para>
/// It keeps nothing of a request: the activity is a local of the call, so requests sent side by side within one
/// scope each have their own.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The command or query.</typeparam>
/// <typeparam name="TResponse">What its handler answers with.</typeparam>
/// <param name="tracing">The application's source of request activities.</param>
public sealed class RequestTracingBehavior<TMessage, TResponse>(RequestTracing tracing) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        using var activity = tracing.Start(typeof(TMessage));
        try
        {
            return await next(message, cancellationToken);
        }
        catch (Exception answered) when (RequestTracing.AnswerOf(answered) is { } code)
        {
            activity?.SetTag(RequestTracing.RefusalTag, code);
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception fault)
        {
            activity?.SetStatus(ActivityStatusCode.Error, fault.GetType().Name);
            throw;
        }
    }
}
