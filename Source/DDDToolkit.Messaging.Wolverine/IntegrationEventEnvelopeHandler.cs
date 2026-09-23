using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;

namespace DDDToolkit.Messaging.Wolverine;

/// <summary>
/// The Wolverine handler for an <see cref="IntegrationEventEnvelope"/>: rebuilds the message and hands it
/// to the modules of this process through <see cref="IntegrationEventReceiver"/>.
/// </summary>
/// <remarks>
/// Every consuming module's handler for the contract runs inside that module's inbox, the same as for a
/// message from the module next door, so a redelivered envelope is applied once. When a handler throws,
/// the receiver throws, and Wolverine's error policy decides what happens next: retry, then the error
/// queue. See <see cref="WolverineOptionsExtensions.ReceiveIntegrationEvents"/>.
/// <para>
/// Wolverine finds this by name, as it finds any handler: a class named <c>*Handler</c> with a
/// <c>Handle</c> method. It is added to Wolverine's discovery explicitly, because it lives in this
/// assembly rather than the application's.
/// </para>
/// </remarks>
public sealed class IntegrationEventEnvelopeHandler
{
    /// <summary>Delivers the envelope's message to the modules of this process.</summary>
    public Task Handle(IntegrationEventEnvelope envelope, IntegrationEventReceiver receiver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(receiver);

        return receiver.ReceiveAsync(envelope.ToMessage(), cancellationToken);
    }
}
