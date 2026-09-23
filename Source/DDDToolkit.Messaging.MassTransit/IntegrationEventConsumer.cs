using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using MassTransit;

namespace DDDToolkit.Messaging.MassTransit;

/// <summary>
/// The MassTransit consumer of one contract: hands the message, already deserialized by MassTransit, to
/// the modules of this process through <see cref="IntegrationEventReceiver"/>. One is registered per
/// contract this process has to be sent, by <c>bus.AddIntegrationEventConsumers(...)</c>.
/// </summary>
/// <remarks>
/// Every consuming module's handler for the contract runs inside that module's inbox, keyed on the message
/// id the sending outbox wrote, so a redelivered message is applied once. When a handler throws, the
/// receiver throws, and the endpoint's retry policy and then MassTransit's error queue take it from there.
/// MassTransit acknowledges the message only when this returns, so nothing is lost between the broker and
/// the inbox.
/// </remarks>
/// <typeparam name="TContract">The published contract.</typeparam>
public sealed class IntegrationEventConsumer<TContract>(IntegrationEventReceiver receiver) : IConsumer<TContract>
    where TContract : class
{
    private static readonly string[] Keys =
    [
        IntegrationEventHeaders.MessageId,
        IntegrationEventHeaders.Name,
        IntegrationEventHeaders.Version,
        IntegrationEventHeaders.ContentType,
        IntegrationEventHeaders.OccurredAt,
        IntegrationEventHeaders.AggregateType,
        IntegrationEventHeaders.AggregateId,
    ];

    /// <inheritdoc />
    public Task Consume(ConsumeContext<TContract> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var key in Keys)
        {
            headers[key] = context.Headers.Get<string>(key);
        }

        return receiver.ReceiveAsync(context.Message, headers, context.CancellationToken);
    }
}
