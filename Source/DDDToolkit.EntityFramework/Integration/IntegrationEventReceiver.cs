using DDDToolkit.BaseTypes;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// Hands a message that arrived from outside this process to the modules in it: the receiving end of a
/// queue or a broker, as <see cref="ModuleIntegrationEventSink"/> is the sending end of the module sink.
/// </summary>
/// <remarks>
/// It is the same delivery, reached from the other side. A message published by a module in this
/// process reaches the other modules through the module sink; a message published by another process
/// arrives through a transport's consumer, which rebuilds the envelope
/// (<see cref="IntegrationEventHeaders.ToMessage"/>) and calls this. Either way every consuming module's
/// handlers for its contract run inside that module's inbox, so a message delivered twice is applied
/// once, and a message the handlers reject throws, which is the transport's cue to redeliver it later.
/// <para>
/// Each call gets a scope of its own, and with it a context per module, as a message handled by the
/// outbox processor does.
/// </para>
/// <code>
/// // in a transport's consumer
/// await receiver.ReceiveAsync(IntegrationEventHeaders.ToMessage(headers, body), cancellationToken);
/// </code>
/// </remarks>
public sealed class IntegrationEventReceiver(IServiceScopeFactory scopes)
{
    /// <summary>
    /// Delivers <paramref name="message"/> to every module in this process that consumes its contract.
    /// Returns when all of them applied it, or had already applied it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="IntegrationEventDeliveryException">One or more handlers threw; redeliver it later.</exception>
    public async Task ReceiveAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var scope = scopes.CreateAsyncScope();
        var modules = scope.ServiceProvider.GetRequiredService<ModuleIntegrationEventSink>();

        await modules.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
