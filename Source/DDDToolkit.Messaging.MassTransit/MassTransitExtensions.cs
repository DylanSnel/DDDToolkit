using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Options;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Messaging.MassTransit;

/// <summary>Wiring MassTransit in as the toolkit's transport.</summary>
public static class MassTransitExtensions
{
    /// <summary>
    /// Publishes through MassTransit, each contract as a message type of its own. Pair it with
    /// <see cref="AddIntegrationEventConsumers"/> in the receiving process.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is null.</exception>
    public static OutboxOptions SendToMassTransit(this OutboxOptions outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        return outbox.SendTo(static services => new MassTransitSink(services.GetRequiredService<IPublishEndpoint>()));
    }

    /// <summary>
    /// Registers an <see cref="IntegrationEventConsumer{TContract}"/> for every contract this process has to
    /// be sent: those its modules handle and it does not publish itself
    /// (<see cref="IntegrationEventSubscriptions.FromElsewhere"/>). Register the modules first, so
    /// <paramref name="subscriptions"/> knows them.
    /// <para>
    /// Then put them on this service's receive endpoint the way MassTransit puts any consumer there,
    /// <c>endpoint.ConfigureConsumers(context)</c>. MassTransit binds the endpoint's queue to the exchange of
    /// every contract type, so the service asks for exactly its messages without naming one. Add a retry
    /// policy on the endpoint, for instance <c>endpoint.UseMessageRetry(retry => retry.Intervals(250, 1000,
    /// 5000))</c>; a retry cannot apply anything twice, because the modules' inboxes hold the rows of the
    /// handlers that succeeded.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IBusRegistrationConfigurator AddIntegrationEventConsumers(this IBusRegistrationConfigurator configurator, IntegrationEventSubscriptions subscriptions)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(subscriptions);

        subscriptions.VisitFromElsewhere(new ConsumerRegistration(configurator));
        return configurator;
    }

    private sealed class ConsumerRegistration(IBusRegistrationConfigurator configurator) : IIntegrationEventContractVisitor
    {
        public void Visit<TContract>() where TContract : class => configurator.AddConsumer<IntegrationEventConsumer<TContract>>();
    }
}
