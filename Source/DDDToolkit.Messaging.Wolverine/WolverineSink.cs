using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;
using Wolverine;

namespace DDDToolkit.Messaging.Wolverine;

/// <summary>
/// Publishes every message the outbox delivers through Wolverine, as an
/// <see cref="IntegrationEventEnvelope"/>.
/// </summary>
/// <remarks>
/// Where the envelope goes is Wolverine's routing, configured by the host: a RabbitMQ topic exchange
/// keyed on the contract's name, an Azure Service Bus topic, anything Wolverine publishes to. The sink
/// does not know which, and does not need to.
/// <code>
/// opts.PublishMessagesToRabbitMqExchange&lt;IntegrationEventEnvelope&gt;("integration-events", envelope => envelope.Name);
/// </code>
/// It returns once Wolverine has accepted the message for sending. Should Wolverine throw, the outbox
/// keeps the row and tries again, so a message is published at least once, and the receiving module's
/// inbox makes that once.
/// </remarks>
public sealed class WolverineSink(IMessageBus bus) : IIntegrationEventSink
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public async Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await bus.PublishAsync(IntegrationEventEnvelope.From(message)).ConfigureAwait(false);
    }
}
