using DDDToolkit.EntityFramework.Integration;
using Wolverine;

namespace DDDToolkit.Messaging.Wolverine;

/// <summary>
/// The Wolverine handler of one contract: hands the message, already deserialized by Wolverine, to the
/// modules of this process through <see cref="IntegrationEventReceiver"/>. One is added to Wolverine's
/// discovery per contract this process has to be sent, by <c>opts.ReceiveIntegrationEvents(...)</c>, and
/// Wolverine listens for exactly those types.
/// </summary>
/// <remarks>
/// Every consuming module's handler for the contract runs inside that module's inbox, keyed on the message
/// id the sending outbox wrote into the headers, so a redelivered message is applied once. When a handler
/// throws, the receiver throws, and Wolverine's error policy decides what happens next: retry, then the
/// error queue.
/// </remarks>
/// <typeparam name="TContract">The published contract.</typeparam>
public sealed class IntegrationEventHandler<TContract> where TContract : class
{
    /// <summary>Delivers the message to the modules of this process.</summary>
    public Task Handle(TContract message, Envelope envelope, IntegrationEventReceiver receiver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(receiver);

        return receiver.ReceiveAsync(message, envelope.Headers, cancellationToken);
    }
}
