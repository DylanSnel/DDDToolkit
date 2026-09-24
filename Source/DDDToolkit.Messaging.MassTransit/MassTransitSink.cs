using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;
using MassTransit;

namespace DDDToolkit.Messaging.MassTransit;

/// <summary>
/// Publishes every message the outbox delivers through MassTransit the way MassTransit publishes anything:
/// as the contract itself, a message type of its own. MassTransit gives each contract type its exchange,
/// and every receive endpoint that consumes the type is bound to it, so the sending service names no
/// receiver and no routing key.
/// </summary>
/// <remarks>
/// What the contract does not carry travels in headers, the ones every toolkit transport writes
/// (<see cref="IntegrationEventHeaders"/>): the published name and version, the time and the aggregate.
/// The outbox's message id becomes MassTransit's message id as well, so the receiving inboxes and
/// MassTransit's own tooling see the same identity.
/// <para>
/// It returns once MassTransit has published. Should MassTransit throw, the outbox keeps the row and
/// tries again, so a message is published at least once, and the receiving module's inbox makes that once.
/// </para>
/// </remarks>
public sealed class MassTransitSink(IPublishEndpoint publisher) : IIntegrationEventSink
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The message has no <see cref="IntegrationEventMessage.Body"/> to publish.</exception>
    public async Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var contract = message.Body
            ?? throw new InvalidOperationException(
                $"Message {message.MessageId} ('{message.Name}') has no body. MassTransit publishes the contract object, and the outbox always sets it; a message built by hand has to as well.");

        await publisher.Publish(
            contract,
            contract.GetType(),
            context =>
            {
                context.MessageId = message.MessageId;

                foreach (var (key, value) in IntegrationEventHeaders.From(message))
                {
                    if (value is not null)
                    {
                        context.Headers.Set(key, value);
                    }
                }
            },
            cancellationToken).ConfigureAwait(false);
    }
}
