using DDDToolkit.BaseTypes;
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
    private const string Exchange = "integration-events";

    /// <summary>
    /// What this service wants from the others, by the contracts' published names. The senders do not know
    /// it exists: they publish to the exchange, and this service's queue is bound to what it consumes.
    /// </summary>
    private static readonly string[] Consumes =
    [
        "ordering.order-placed",
        "ordering.order-cancelled",
        "ordering.order-confirmed",
    ];

    public static IServiceCollection AddRabbitMq(this IServiceCollection services, string connectionString) =>
        services.AddMassTransit(bus =>
        {
            bus.AddIntegrationEventConsumer();

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(connectionString));

                // Sending: every envelope to one topic exchange; the sink sets the contract's name as routing key.
                rabbit.Message<IntegrationEventEnvelope>(message => message.SetEntityName(Exchange));
                rabbit.Publish<IntegrationEventEnvelope>(publish => publish.ExchangeType = "topic");

                // Receiving: this service's queue, retried a few times before MassTransit's error queue, and
                // acknowledged when the modules have applied it.
                rabbit.ReceiveEndpoint("fulfilment", endpoint =>
                {
                    endpoint.ConfigureConsumeTopology = false;
                    foreach (var contract in Consumes)
                    {
                        endpoint.Bind(Exchange, exchange =>
                        {
                            exchange.ExchangeType = "topic";
                            exchange.RoutingKey = contract;
                        });
                    }

                    endpoint.UseMessageRetry(retry => retry.Intervals(250, 1000, 5000));
                    endpoint.ConfigureConsumer<IntegrationEventEnvelopeConsumer>(context);
                });
            });
        });
}
