using DDDToolkit.EntityFramework;
using DDDToolkit.Messaging.MassTransit;
using MassTransit;

namespace DDDToolkit.Examples.MassTransit.Fulfilment;

/// <summary>
/// The MassTransit side of the service. In a file of its own because MassTransit brings an AddMediator of
/// its own: with the MassTransit namespace imported in Program.cs, the Mediator source generator no longer
/// reads the options of the AddMediator call there.
/// </summary>
internal static class RabbitMq
{
    /// <summary>
    /// RabbitMQ the way MassTransit uses it. Every contract is a message type with an exchange of its own;
    /// this service's queue is bound to the exchange of every contract it has to be sent, which MassTransit
    /// works out from the consumers on the endpoint. Nothing here names another service or a contract: the
    /// modules say what this service handles, so call this after registering them.
    /// </summary>
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, string connectionString) =>
        services.AddMassTransit(bus =>
        {
            // A consumer per contract the modules handle and another service publishes.
            bus.AddIntegrationEventConsumers(services.IntegrationEventSubscriptions());

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(connectionString));

                // Receiving: this service's queue, retried a few times before MassTransit's error queue, and
                // acknowledged when the modules have applied it.
                rabbit.ReceiveEndpoint("fulfilment", endpoint =>
                {
                    endpoint.UseMessageRetry(retry => retry.Intervals(250, 1000, 5000));
                    endpoint.ConfigureConsumers(context);
                });
            });
        });
}
