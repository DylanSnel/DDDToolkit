using DDDToolkit.Interfaces;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// Says which domain events leave this process, and as what. One entry per domain event type.
/// <para>
/// A domain event is internal. You rename it, split it, add a field to it, and nothing outside should
/// care. The moment it crosses the process boundary it becomes a schema other teams depend on, and
/// those two jobs pull in opposite directions. This map is where you keep them apart: register a
/// conversion and the outbox publishes the contract instead of the event.
/// </para>
/// <para>
/// Registering nothing is a valid answer. A domain event with no entry is published as it stands,
/// under its own <c>[DomainEventName]</c>, using the JSON the outbox already stored, so a team that
/// does not want two types yet pays nothing for the seam.
/// </para>
/// </summary>
public sealed class IntegrationEventMap
{
    private readonly Dictionary<Type, Func<IDomainEvent, object?>> _conversions = [];

    /// <summary>The domain event types with an explicit entry.</summary>
    public IReadOnlyCollection<Type> MappedEventTypes => _conversions.Keys;

    /// <summary>
    /// Publishes <typeparamref name="TDomainEvent"/> as <typeparamref name="TContract"/>. The contract
    /// supplies the published name and version (see <c>[IntegrationEvent]</c>) and is serialized with
    /// the outbox's JSON options.
    /// <para>
    /// Returning <see langword="null"/> from <paramref name="convert"/> drops that one occurrence, which
    /// is how you publish only the events that matter: for example only orders above a threshold.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="convert"/> is null.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TDomainEvent"/> is already mapped.</exception>
    public IntegrationEventMap PublishAs<TDomainEvent, TContract>(Func<TDomainEvent, TContract?> convert)
        where TDomainEvent : IDomainEvent
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(convert);
        return Add(typeof(TDomainEvent), domainEvent => convert((TDomainEvent)domainEvent));
    }

    /// <summary>
    /// Keeps <typeparamref name="TDomainEvent"/> inside this process. The outbox still stores and
    /// delivers it, so in-process handlers still see it, but no sink is called and the row is marked
    /// processed.
    /// </summary>
    /// <exception cref="ArgumentException"><typeparamref name="TDomainEvent"/> is already mapped.</exception>
    public IntegrationEventMap DoNotPublish<TDomainEvent>() where TDomainEvent : IDomainEvent
        => Add(typeof(TDomainEvent), static _ => null);

    /// <summary>
    /// Converts <paramref name="domainEvent"/> to the object that should be published.
    /// </summary>
    /// <param name="domainEvent">The event read back from the outbox.</param>
    /// <param name="contract">
    /// The mapped contract, the event itself when nothing is mapped, or <see langword="null"/> when the
    /// event must not be published.
    /// </param>
    /// <returns><see langword="true"/> when an entry decided the outcome, <see langword="false"/> when the event is published as it stands.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="domainEvent"/> is null.</exception>
    public bool TryConvert(IDomainEvent domainEvent, out object? contract)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (_conversions.TryGetValue(domainEvent.GetType(), out var convert))
        {
            contract = convert(domainEvent);
            return true;
        }

        contract = domainEvent;
        return false;
    }

    private IntegrationEventMap Add(Type domainEventType, Func<IDomainEvent, object?> convert)
    {
        if (!_conversions.TryAdd(domainEventType, convert))
        {
            throw new ArgumentException($"'{domainEventType}' is already mapped; a domain event has one published contract.", nameof(domainEventType));
        }

        return this;
    }
}
