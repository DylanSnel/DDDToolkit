using System.Diagnostics.CodeAnalysis;
using DDDToolkit.BaseTypes;
using HotChocolate.Subscriptions;

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
    private readonly Dictionary<Type, Entry> _byType = [];
    private Dictionary<(string Name, int Version), Entry>? _byName;

    /// <summary>
    /// The (name, version) pairs that reach clients. Read off the contracts' attributes the first time it is
    /// asked for; delivering a message the outbox built never asks, because it finds the entry by the type
    /// of the contract it carries.
    /// </summary>
    public IReadOnlyCollection<(string Name, int Version)> Published => ByName().Keys;

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

        // The send is captured with the contract as its generic argument, so pushing a message later is
        // an ordinary generic call: HotChocolate's topics are typed, and nothing is made by reflection.
        var entry = new Entry(
            typeof(TContract),
            topic,
            static (sender, name, payload, cancellationToken) => sender.SendAsync(name, (TContract)payload, cancellationToken));

        if (!_byType.TryAdd(typeof(TContract), entry))
        {
            throw new ArgumentException(
                $"'{IntegrationEventContract.NameOf<TContract>()}' version {IntegrationEventContract.VersionOf<TContract>()} is already published to subscribers; a contract has one topic.",
                nameof(topic));
        }

        _byName = null;
        return this;
    }

    /// <summary>The contract type and topic for <paramref name="message"/>, when it is published at all.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public bool TryGetTopic(IntegrationEventMessage message, [NotNullWhen(true)] out Type? contractType, out string? topic)
    {
        if (TryGetEntry(message, out var entry))
        {
            contractType = entry.Contract;
            topic = entry.Topic(message);
            return true;
        }

        contractType = null;
        topic = null;
        return false;
    }

    /// <summary>
    /// The entry for <paramref name="message"/>: by the type of the contract it carries, which is how every
    /// message an outbox built arrives, and otherwise by its published name and version.
    /// </summary>
    internal bool TryGetEntry(IntegrationEventMessage message, [NotNullWhen(true)] out Entry? entry)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Body is { } body && _byType.TryGetValue(body.GetType(), out entry))
        {
            return true;
        }

        return ByName().TryGetValue((message.Name, message.Version), out entry);
    }

    /// <summary>The same entries keyed on name and version, for a message rebuilt from text with no body.</summary>
    private Dictionary<(string Name, int Version), Entry> ByName()
        => _byName ??= _byType.Values.ToDictionary(
            static entry => (IntegrationEventContract.NameOf(entry.Contract), IntegrationEventContract.VersionOf(entry.Contract)));

    /// <summary>One published contract: its type, how its topic is built, and how it is pushed.</summary>
    internal sealed record Entry(
        Type Contract,
        Func<IntegrationEventMessage, string?> Topic,
        Func<ITopicEventSender, string, object, CancellationToken, ValueTask> Send);
}
