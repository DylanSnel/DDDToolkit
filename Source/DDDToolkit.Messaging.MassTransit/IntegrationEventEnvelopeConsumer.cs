using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using MassTransit;

namespace DDDToolkit.Messaging.MassTransit;

/// <summary>
/// The MassTransit consumer of an <see cref="IntegrationEventEnvelope"/>: rebuilds the message and hands
/// it to the modules of this process through <see cref="IntegrationEventReceiver"/>.
/// </summary>
/// <remarks>
/// Every consuming module's handler for the contract runs inside that module's inbox, so a redelivered
/// envelope is applied once. When a handler throws, the receiver throws, and the endpoint's retry policy
/// and then MassTransit's error queue take it from there. MassTransit acknowledges the message only when
/// this returns, so nothing is lost between the broker and the inbox.
/// </remarks>
public sealed class IntegrationEventEnvelopeConsumer(IntegrationEventReceiver receiver) : IConsumer<IntegrationEventEnvelope>
{
    /// <inheritdoc />
    public Task Consume(ConsumeContext<IntegrationEventEnvelope> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return receiver.ReceiveAsync(context.Message.ToMessage(), context.CancellationToken);
    }
}
