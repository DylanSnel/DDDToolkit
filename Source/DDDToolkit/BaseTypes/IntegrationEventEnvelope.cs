namespace DDDToolkit.BaseTypes;

/// <summary>
/// An <see cref="IntegrationEventMessage"/> as one object, for a broker whose client sends objects rather
/// than a body and headers: MassTransit, Wolverine. The same headers every other transport writes
/// (<see cref="IntegrationEventHeaders"/>), next to the payload, so the far side rebuilds the same
/// message whichever broker carried it.
/// </summary>
/// <remarks>
/// One envelope type for every contract, not one message type per contract. A broker that routes by type
/// would otherwise need the contracts' assemblies to route them, and a service would need every other
/// service's contracts just to pass messages along. Route on <see cref="Name"/> instead, which is the
/// contract's published name: a topic, a routing key, a subscription filter.
/// </remarks>
/// <param name="Headers">The message's routing fields, keyed as in <see cref="IntegrationEventHeaders"/>.</param>
/// <param name="Payload">The contract, serialized.</param>
public sealed record IntegrationEventEnvelope(IReadOnlyDictionary<string, string?> Headers, string Payload)
{
    /// <summary>The contract's published name, what a broker routes on.</summary>
    public string Name => Headers.TryGetValue(IntegrationEventHeaders.Name, out var name) && name is not null
        ? name
        : throw new FormatException($"The envelope has no '{IntegrationEventHeaders.Name}' header.");

    /// <summary>The envelope <paramref name="message"/> travels in.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public static IntegrationEventEnvelope From(IntegrationEventMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new IntegrationEventEnvelope(IntegrationEventHeaders.From(message), message.Payload);
    }

    /// <summary>The message this envelope carries. See <see cref="IntegrationEventHeaders.ToMessage"/>.</summary>
    /// <exception cref="FormatException">A header the message cannot be rebuilt without is missing.</exception>
    public IntegrationEventMessage ToMessage() => IntegrationEventHeaders.ToMessage(Headers, Payload);
}
