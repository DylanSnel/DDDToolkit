using System.Text.Json;
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;
using HotChocolate.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.HotChocolate.Subscriptions;

/// <summary>
/// Pushes published contracts to the GraphQL clients connected right now, through HotChocolate's
/// <see cref="ITopicEventSender"/>.
/// <para>
/// <b>This is a different axis from the other sinks and it is worth being blunt about it.</b> The
/// in-process module sink is how one module tells another that something happened; pgmq is how a module
/// tells another deployable the same thing. Both are integration: durable, retried, and the receiver
/// gets the message whether or not it was running at the time. A subscription is none of that. It is a
/// screen update. Use it to keep a browser in step with the server, and never as the path by which some
/// other part of the system learns that an order was placed.
/// </para>
/// <list type="bullet">
///   <item><description><b>Not durable.</b> Nothing is stored. A payload with no subscriber is dropped.</description></item>
///   <item><description><b>Only the connected.</b> A client that reconnects has missed what happened while it was away, and has to re-read the state it cares about.</description></item>
///   <item><description><b>No acknowledgement.</b> The sink returns as soon as the topic accepted the payload; whether a socket survived long enough to deliver it is not knowable here.</description></item>
/// </list>
/// <para>
/// <b>It publishes the contract, never the domain event</b>, and the reason is not tidiness. A
/// subscription payload is a schema type: it is declared on the subscription field, it goes through the
/// schema's own authorisation, and a field the client may not see is not in the response. Broadcast a
/// domain event instead and you are pushing your internal type, with your identifiers and whatever you
/// last added to it, to whoever is holding a socket. The contract is the list of things you decided to
/// say out loud.
/// </para>
/// <code>
/// builder.Services
///     .AddGraphQLServer()
///     .AddDDDToolkitTypes()
///     .AddInMemorySubscriptions()          // or AddPostgresSubscriptions, AddRedisSubscriptions, ...
///     .AddSubscriptionType&lt;Subscription&gt;();
///
/// builder.Services.AddIntegrationEventSubscriptions(map => map.Publish&lt;OrderPlacedV2&gt;("orderPlaced"));
///
/// // and on the outbox:
/// options.UseOutbox(outbox => outbox.SendTo&lt;GraphQlSubscriptionSink&gt;());
/// </code>
/// <para>
/// The matching subscription field reads the same topic:
/// </para>
/// <code>
/// public sealed class Subscription
/// {
///     [Subscribe(With = nameof(OnOrderPlacedStream))]
///     public OrderPlacedV2 OrderPlaced([EventMessage] OrderPlacedV2 order) => order;
///
///     public ValueTask&lt;ISourceStream&lt;OrderPlacedV2&gt;&gt; OnOrderPlacedStream(
///         [Service] ITopicEventReceiver receiver, CancellationToken cancellationToken)
///         => receiver.SubscribeAsync&lt;OrderPlacedV2&gt;("orderPlaced", cancellationToken);
/// }
/// </code>
/// </summary>
public sealed class GraphQlSubscriptionSink : IIntegrationEventSink
{
    private readonly ITopicEventSender _sender;
    private readonly GraphQlSubscriptionMap _map;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger _logger;

    /// <summary>Creates the sink.</summary>
    /// <param name="sender">HotChocolate's topic sender, from whichever subscription transport you registered.</param>
    /// <param name="map">Which contracts reach clients, and on which topic.</param>
    /// <param name="jsonOptions">How to read <see cref="IntegrationEventMessage.Payload"/> when the envelope carries no body. Defaults to case-insensitive.</param>
    /// <param name="logger">Optional.</param>
    public GraphQlSubscriptionSink(
        ITopicEventSender sender,
        GraphQlSubscriptionMap map,
        JsonSerializerOptions? jsonOptions = null,
        ILogger<GraphQlSubscriptionSink>? logger = null)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _logger = logger ?? NullLogger<GraphQlSubscriptionSink>.Instance;
    }

    /// <summary>
    /// Sends the contract of <paramref name="message"/> to its topic. A message the map does not name,
    /// or whose topic factory returned <see langword="null"/>, is skipped: not every published contract
    /// is something a screen should see.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public async Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_map.TryGetEntry(message, out var entry))
        {
            _logger.LogDebug("'{Name}' version {Version} is not published to subscribers.", message.Name, message.Version);
            return;
        }

        var topic = entry.Topic(message);
        if (topic is null)
        {
            _logger.LogDebug("Message {MessageId} produced no topic, so no client was woken.", message.MessageId);
            return;
        }

        var payload = Payload(message, entry.Contract);

        // SendAsync is generic and the topic is typed, so the generic argument has to be the contract's
        // own type; a payload sent as object would not match a client's SubscribeAsync<TContract>. The map
        // captured that call when the contract was registered.
        await entry.Send(_sender, topic, payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The object to push. The outbox hands over the contract it just built, so
    /// <see cref="IntegrationEventMessage.Body"/> is normally exactly the right object and there is no
    /// reason to serialize and parse it again. An envelope rebuilt from text has no body, so the payload
    /// is read instead.
    /// <para>
    /// No upcasting happens here, and that is deliberate. An upcaster catches a reader up with a payload
    /// written before it was deployed; a subscription payload was produced seconds ago by this process,
    /// against this schema.
    /// </para>
    /// </summary>
    private object Payload(IntegrationEventMessage message, Type contractType)
    {
        if (message.Body is { } body && contractType.IsInstanceOfType(body))
        {
            return body;
        }

        return JsonSerializer.Deserialize(message.Payload, contractType, _jsonOptions)
            ?? throw new JsonException($"The payload of message {message.MessageId} deserialized to null as '{contractType}'.");
    }
}
