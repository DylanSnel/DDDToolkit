using System.Globalization;

namespace DDDToolkit.BaseTypes;

/// <summary>
/// An <see cref="IntegrationEventMessage"/> on the wire: its routing fields as text headers, next to the
/// payload as the body. Every transport the toolkit reaches writes the same headers, so a message leaves
/// one process as the same envelope whichever broker carries it, and the receiving side rebuilds it with
/// <see cref="ToMessage"/> the same way whichever broker delivered it.
/// </summary>
/// <remarks>
/// Headers rather than a JSON envelope around the payload, because every broker has them and shows them:
/// a person looking at a queue sees what a message is without parsing its body, and a consumer can route
/// on the name and version before reading anything else.
/// </remarks>
public static class IntegrationEventHeaders
{
    /// <summary><see cref="IntegrationEventMessage.MessageId"/>, the idempotency key.</summary>
    public const string MessageId = "messageId";

    /// <summary><see cref="IntegrationEventMessage.Name"/>.</summary>
    public const string Name = "name";

    /// <summary><see cref="IntegrationEventMessage.Version"/>.</summary>
    public const string Version = "version";

    /// <summary><see cref="IntegrationEventMessage.ContentType"/>.</summary>
    public const string ContentType = "contentType";

    /// <summary><see cref="IntegrationEventMessage.OccurredAt"/>, round-trip formatted.</summary>
    public const string OccurredAt = "occurredAt";

    /// <summary><see cref="IntegrationEventMessage.AggregateType"/>.</summary>
    public const string AggregateType = "aggregateType";

    /// <summary><see cref="IntegrationEventMessage.AggregateId"/>.</summary>
    public const string AggregateId = "aggregateId";

    /// <summary>The headers <paramref name="message"/> travels with. The payload is not among them.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public static IReadOnlyDictionary<string, string?> From(IntegrationEventMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessageId] = message.MessageId.ToString(),
            [Name] = message.Name,
            [Version] = message.Version.ToString(CultureInfo.InvariantCulture),
            [ContentType] = message.ContentType,
            [OccurredAt] = message.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
            [AggregateType] = message.AggregateType,
            [AggregateId] = message.AggregateId,
        };
    }

    /// <summary>
    /// Rebuilds the message a transport delivered. <see cref="IntegrationEventMessage.Body"/> is
    /// <see langword="null"/>: the receiving side reads the contract from the payload.
    /// </summary>
    /// <param name="headers">The headers the message arrived with.</param>
    /// <param name="payload">The body it arrived with.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="FormatException">
    /// A header the message cannot be rebuilt without is missing or malformed: the id, the name or the
    /// time. Such a message did not come from a toolkit outbox, and delivering it anyway would put a
    /// made-up identity in an inbox.
    /// </exception>
    public static IntegrationEventMessage ToMessage(IReadOnlyDictionary<string, string?> headers, string payload)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(payload);

        if (!Guid.TryParse(Read(headers, MessageId), out var messageId))
        {
            throw new FormatException($"The '{MessageId}' header is missing or is not a Guid.");
        }

        var name = Read(headers, Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new FormatException($"The '{Name}' header is missing.");
        }

        if (!DateTimeOffset.TryParse(Read(headers, OccurredAt), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var occurredAt))
        {
            throw new FormatException($"The '{OccurredAt}' header is missing or is not a timestamp.");
        }

        return new IntegrationEventMessage
        {
            MessageId = messageId,
            Name = name,
            Version = int.TryParse(Read(headers, Version), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) ? version : 1,
            Payload = payload,
            ContentType = Read(headers, ContentType) is { Length: > 0 } contentType ? contentType : "application/json",
            OccurredAt = occurredAt,
            AggregateType = Read(headers, AggregateType),
            AggregateId = Read(headers, AggregateId),
        };
    }

    private static string? Read(IReadOnlyDictionary<string, string?> headers, string key)
        => headers.TryGetValue(key, out var value) ? value : null;
}
