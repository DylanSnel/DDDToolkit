using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Options;
using Wolverine;
using Wolverine.ErrorHandling;

namespace DDDToolkit.Messaging.Wolverine;

/// <summary>Wiring Wolverine in as the toolkit's transport.</summary>
public static class WolverineOptionsExtensions
{
    /// <summary>
    /// Publishes to the transport Wolverine routes <see cref="BaseTypes.IntegrationEventEnvelope"/> to.
    /// Pair it with <see cref="ReceiveIntegrationEvents"/> in the receiving process, and route the
    /// envelope in Wolverine's options.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is null.</exception>
    public static OutboxOptions SendToWolverine(this OutboxOptions outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        return outbox.SendTo<WolverineSink>();
    }

    /// <summary>
    /// Handles every <see cref="BaseTypes.IntegrationEventEnvelope"/> this process listens for, by handing
    /// it to the modules registered with <c>AddModuleIntegrationEvents</c>.
    /// </summary>
    /// <remarks>
    /// A failed delivery is retried a few times with a pause, then moved to Wolverine's error queue, where
    /// it can be read and replayed. A retry cannot apply anything twice: the modules' inboxes already hold
    /// the rows of the handlers that succeeded.
    /// <para>
    /// Listen inline, <c>ListenToRabbitQueue(...).ProcessInline()</c> or its equivalent, so an envelope is
    /// acknowledged only after the modules applied it. A buffered listener acknowledges first, and a crash
    /// in between loses the message.
    /// </para>
    /// </remarks>
    /// <param name="options">Wolverine's options.</param>
    /// <param name="retries">The pauses between attempts; three growing ones when left out.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static WolverineOptions ReceiveIntegrationEvents(this WolverineOptions options, params TimeSpan[] retries)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Discovery.IncludeType<IntegrationEventEnvelopeHandler>();

        if (retries.Length == 0)
        {
            retries = [TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)];
        }

        options.Policies.OnException<IntegrationEventDeliveryException>()
            .RetryWithCooldown(retries)
            .Then.MoveToErrorQueue();

        return options;
    }
}
