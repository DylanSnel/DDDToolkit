using System.Diagnostics.CodeAnalysis;
using DDDToolkit.BaseTypes;

namespace DDDToolkit.HotChocolate.Subscriptions;

/// <summary>
/// Says which published contracts reach connected GraphQL clients, and on which topic. Nothing is
/// pushed until you name it here, because a subscription payload is a schema type and the schema is a
/// deliberate list.
/// <code>
/// services.AddIntegrationEventSubscriptions(map => map
///     .Publish&lt;OrderPlacedV2&gt;("orderPlaced")
///     .Publish&lt;ShelfOpenedV3&gt;(message =&gt; $"shelf:{message.AggregateId}"));
/// </code>
/// <para>
/// A constant topic is a firehose every subscriber sees. A topic built from the message is how you scope
/// it, usually to one aggregate, so a client subscribed to one order is not woken by every other order.
/// Whether the client is allowed to subscribe to that topic at all is a question for the subscription
/// field's own authorisation, not for this map.
/// </para>
/// </summary>
public sealed class GraphQlSubscriptionMap
{
    private readonly Dictionary<(string Name, int Version), (Type Contract, Func<IntegrationEventMessage, string?> Topic)> _entries = [];

    /// <summary>The (name, version) pairs that reach clients.</summary>
    public IReadOnlyCollection<(string Name, int Version)> Published => _entries.Keys;

    /// <summary>Pushes <typeparamref name="TContract"/> to every client subscribed to <paramref name="topic"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="topic"/> is empty, or the contract is already mapped.</exception>
    public GraphQlSubscriptionMap Publish<TContract>(string topic) where TContract : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        return Publish<TContract>(_ => topic);
    }

    /// <summary>
    /// Pushes <typeparamref name="TContract"/> to the topic <paramref name="topic"/> builds from the
    /// message, which is how a subscription is scoped to one aggregate. Returning
    /// <see langword="null"/> drops that one occurrence.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="topic"/> is null.</exception>
    /// <exception cref="ArgumentException">The contract is already mapped.</exception>
    public GraphQlSubscriptionMap Publish<TContract>(Func<IntegrationEventMessage, string?> topic) where TContract : class
    {
        ArgumentNullException.ThrowIfNull(topic);

        var key = (IntegrationEventContract.NameOf<TContract>(), IntegrationEventContract.VersionOf<TContract>());

        if (!_entries.TryAdd(key, (typeof(TContract), topic)))
        {
            throw new ArgumentException($"'{key.Item1}' version {key.Item2} is already published to subscribers; a contract has one topic.", nameof(topic));
        }

        return this;
    }

    /// <summary>The contract type and topic for <paramref name="message"/>, when it is published at all.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public bool TryGetTopic(IntegrationEventMessage message, [NotNullWhen(true)] out Type? contractType, out string? topic)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_entries.TryGetValue((message.Name, message.Version), out var entry))
        {
            contractType = entry.Contract;
            topic = entry.Topic(message);
            return true;
        }

        contractType = null;
        topic = null;
        return false;
    }
}
