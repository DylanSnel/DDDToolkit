using DDDToolkit.BaseTypes;
using DDDToolkit.Examples.Microservices;
using DDDToolkit.Messaging.MassTransit;
using MassTransit;

namespace DDDToolkit.Examples.MassTransit.Service;

/// <summary>
/// The MassTransit side of the service. In a file of its own because MassTransit brings an AddMediator of
/// its own: with the MassTransit namespace imported in Program.cs, the Mediator source generator no longer
/// reads the options of the AddMediator call there.
/// </summary>
internal static class RabbitMqTransport
{
    private const string Exchange = "integration-events";

    public static IServiceCollection AddRabbitMqTransport(this IServiceCollection services, ShopService service, string connectionString) =>
        services.AddMassTransit(bus =>
        {
            bus.AddIntegrationEventConsumer();

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(connectionString));

                // Sending: every envelope to one topic exchange; the sink sets the contract's name as routing key.
                rabbit.Message<IntegrationEventEnvelope>(message => message.SetEntityName(Exchange));
                rabbit.Publish<IntegrationEventEnvelope>(publish => publish.ExchangeType = "topic");

                // Receiving: this service's queue, bound to the contracts ShopServices routes to it, retried a
                // few times before MassTransit's error queue. Acknowledged when the modules have applied it.
                rabbit.ReceiveEndpoint(ShopServices.NameOf(service), endpoint =>
                {
                    endpoint.ConfigureConsumeTopology = false;
                    foreach (var contract in ShopServices.ContractsFor(service))
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
