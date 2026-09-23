using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;
using Wolverine;

namespace DDDToolkit.Messaging.Wolverine;

/// <summary>
/// Publishes every message the outbox delivers through Wolverine the way Wolverine publishes anything: as
/// the contract itself, a message type of its own, routed by Wolverine's own rules.
/// </summary>
/// <remarks>
/// Where it goes is Wolverine's routing, configured by the host. With RabbitMQ's conventional routing,
/// <c>opts.UseRabbitMq(...).UseConventionalRouting()</c>, every contract type gets a fanout exchange and
/// every service that handles the type a queue bound to it, so the sending service names no receiver.
/// <para>
/// What the contract does not carry travels in headers, the ones every toolkit transport writes
/// (<see cref="IntegrationEventHeaders"/>): the outbox's message id, which the receiving inboxes key on,
/// the published name and version, the time and the aggregate.
/// </para>
/// <para>
/// It returns once Wolverine has accepted the message for sending. Should Wolverine throw, the outbox
/// keeps the row and tries again, so a message is published at least once, and the receiving module's
/// inbox makes that once.
/// </para>
/// </remarks>
public sealed class WolverineSink(IMessageBus bus) : IIntegrationEventSink
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The message has no <see cref="IntegrationEventMessage.Body"/> to publish.</exception>
    public async Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var contract = message.Body
            ?? throw new InvalidOperationException(
                $"Message {message.MessageId} ('{message.Name}') has no body. Wolverine publishes the contract object, and the outbox always sets it; a message built by hand has to as well.");

        var options = new DeliveryOptions();
        foreach (var (key, value) in IntegrationEventHeaders.From(message))
        {
            options.Headers[key] = value;
        }

        await bus.PublishAsync(contract, options).ConfigureAwait(false);
    }
}
