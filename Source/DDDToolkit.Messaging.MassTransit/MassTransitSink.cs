using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;
using MassTransit;

namespace DDDToolkit.Messaging.MassTransit;

/// <summary>
/// Publishes every message the outbox delivers through MassTransit, as an
/// <see cref="IntegrationEventEnvelope"/> whose routing key is the contract's published name.
/// </summary>
/// <remarks>
/// On a transport with routing keys, RabbitMQ with a topic exchange for instance, a receive endpoint binds
/// to the contracts it consumes by name; on one without, the key is ignored and every subscriber gets
/// every envelope, which the receiving modules pass over when they have no handler for its contract.
/// <para>
/// It returns once MassTransit has published. Should MassTransit throw, the outbox keeps the row and
/// tries again, so a message is published at least once, and the receiving module's inbox makes that once.
/// </para>
/// </remarks>
public sealed class MassTransitSink(IPublishEndpoint publisher) : IIntegrationEventSink
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public async Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var envelope = IntegrationEventEnvelope.From(message);

        await publisher.Publish(
            envelope,
            context =>
            {
                context.MessageId = message.MessageId;
                context.TrySetRoutingKey(message.Name);
            },
            cancellationToken).ConfigureAwait(false);
    }
}
