using System.Collections.Concurrent;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// Every command and query a host was sent, in the order they were sent: for a test that shows how many questions
/// one request cost, whoever sent them. A GraphQL request sends from scopes of its own, which a test cannot
/// name beforehand, so the requests are noted on their way to their handlers.
/// <code>
/// var sent = new SentRequests();
/// await using var host = await sample.StartAsync(sent.AddTo);
/// </code>
/// </summary>
public sealed class SentRequests
{
    private readonly ConcurrentQueue<object> _sent = new();

    /// <summary>Every request of type <typeparamref name="TRequest"/> sent since the last <see cref="Clear"/>.</summary>
    public IReadOnlyList<TRequest> Of<TRequest>() => [.. _sent.OfType<TRequest>()];

    /// <summary>Forgets what was sent so far.</summary>
    public void Clear() => _sent.Clear();

    /// <summary>
    /// Puts the step that notes in front of every other step of the host's pipeline, the modules' own access
    /// checks included: steps run in the order they are registered.
    /// </summary>
    public void AddTo(IServiceCollection services)
    {
        services.AddSingleton(this);
        services.Insert(0, ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(Noting<,>)));
    }

    /// <summary>A step every request passes on its way to its handler, which notes it and changes nothing.</summary>
    private sealed class Noting<TMessage, TResponse>(SentRequests sent) : IPipelineBehavior<TMessage, TResponse>
        where TMessage : notnull, IMessage
    {
        public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
        {
            sent._sent.Enqueue(message);
            return next(message, cancellationToken);
        }
    }
}
