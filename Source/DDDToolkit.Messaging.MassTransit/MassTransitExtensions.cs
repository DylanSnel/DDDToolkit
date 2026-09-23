using DDDToolkit.EntityFramework.Options;
using MassTransit;

namespace DDDToolkit.Messaging.MassTransit;

/// <summary>Wiring MassTransit in as the toolkit's transport.</summary>
public static class MassTransitExtensions
{
    /// <summary>
    /// Publishes through MassTransit. Pair it with <see cref="AddIntegrationEventConsumer"/> in the
    /// receiving process.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is null.</exception>
    public static OutboxOptions SendToMassTransit(this OutboxOptions outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        return outbox.SendTo<MassTransitSink>();
    }

    /// <summary>
    /// Registers <see cref="IntegrationEventEnvelopeConsumer"/>. Configure the receive endpoint it runs on
    /// yourself: which queue, which contracts it is bound to, and a retry policy, for instance
    /// <c>endpoint.UseMessageRetry(retry => retry.Intervals(250, 1000, 5000))</c>. A retry cannot apply
    /// anything twice: the modules' inboxes hold the rows of the handlers that succeeded.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="configurator"/> is null.</exception>
    public static IConsumerRegistrationConfigurator<IntegrationEventEnvelopeConsumer> AddIntegrationEventConsumer(this IBusRegistrationConfigurator configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        return configurator.AddConsumer<IntegrationEventEnvelopeConsumer>();
    }
}
