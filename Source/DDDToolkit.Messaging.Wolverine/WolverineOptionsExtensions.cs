using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Options;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.ErrorHandling;

namespace DDDToolkit.Messaging.Wolverine;

/// <summary>Wiring Wolverine in as the toolkit's transport.</summary>
public static class WolverineOptionsExtensions
{
    /// <summary>
    /// Publishes through Wolverine, each contract as a message type of its own, routed by Wolverine's own
    /// rules. Pair it with <see cref="ReceiveIntegrationEvents"/> in the receiving process.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is null.</exception>
    public static OutboxOptions SendToWolverine(this OutboxOptions outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        return outbox.SendTo(static services => new WolverineSink(services.GetRequiredService<IMessageBus>()));
    }

    /// <summary>
    /// Adds an <see cref="IntegrationEventHandler{TContract}"/> to Wolverine's discovery for every contract
    /// this process has to be sent: those its modules handle and it does not publish itself
    /// (<see cref="IntegrationEventSubscriptions.FromElsewhere"/>). Register the modules before
    /// <c>UseWolverine</c>, so <paramref name="subscriptions"/> knows them.
    /// </summary>
    /// <remarks>
    /// Wolverine then listens for those types the way it listens for any handled message: with RabbitMQ's
    /// conventional routing, a queue per type bound to the type's exchange. Name the queues per service
    /// (<c>QueueNameForListener</c>), or two services that handle one contract would share a queue and each
    /// get half the messages.
    /// <para>
    /// A failed delivery is retried a few times with a pause, then moved to Wolverine's error queue, where
    /// it can be read and replayed. A retry cannot apply anything twice: the modules' inboxes already hold
    /// the rows of the handlers that succeeded. Listen inline, so a message is acknowledged only after the
    /// modules applied it; a buffered listener acknowledges first, and a crash in between loses it.
    /// </para>
    /// </remarks>
    /// <param name="options">Wolverine's options.</param>
    /// <param name="subscriptions">What this process's modules handle, from <c>services.IntegrationEventSubscriptions()</c>.</param>
    /// <param name="retries">The pauses between attempts; three growing ones when left out.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static WolverineOptions ReceiveIntegrationEvents(this WolverineOptions options, IntegrationEventSubscriptions subscriptions, params TimeSpan[] retries)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(subscriptions);

        subscriptions.VisitFromElsewhere(new HandlerDiscovery(options));

        if (retries.Length == 0)
        {
            retries = [TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)];
        }

        options.Policies.OnException<IntegrationEventDeliveryException>()
            .RetryWithCooldown(retries)
            .Then.MoveToErrorQueue();

        return options;
    }

    private sealed class HandlerDiscovery(WolverineOptions options) : IIntegrationEventContractVisitor
    {
        public void Visit<TContract>() where TContract : class => options.Discovery.IncludeType<IntegrationEventHandler<TContract>>();
    }
}
