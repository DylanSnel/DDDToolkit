using System.Diagnostics.CodeAnalysis;
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
/// under its own name, using the JSON the outbox already stored, so a team that
/// does not want two types yet pays nothing for the seam.
/// </para>
/// </summary>
public sealed class IntegrationEventMap
{
    private delegate ValueTask<object?> Conversion(IDomainEvent domainEvent, IServiceProvider services, CancellationToken cancellationToken);

    /// <param name="Convert">Runs the entry.</param>
    /// <param name="Immediate">The same conversion when it needs no services and completes at once; null for an <see cref="IOutboundIntegrationEvent{TDomainEvent, TContract}"/>.</param>
    /// <param name="Contract">The published name and version of the contract, when the registration said them; otherwise they are read off the contract when it is published.</param>
    private sealed record Entry(Conversion Convert, Func<IDomainEvent, object?>? Immediate, (string Name, int Version)? Contract = null);

    private readonly Dictionary<Type, Entry> _entries = [];

    /// <summary>The domain event types with an explicit entry.</summary>
    public IReadOnlyCollection<Type> MappedEventTypes => _entries.Keys;

    /// <summary>The published names of the contracts whose entries said them, as the generated registration's do.</summary>
    internal IEnumerable<string> ContractNames => _entries.Values.Where(static entry => entry.Contract is not null).Select(static entry => entry.Contract!.Value.Name);

    /// <summary>
    /// Publishes <typeparamref name="TDomainEvent"/> as <typeparamref name="TContract"/>. The contract
    /// supplies the published name and version (see <c>[IntegrationEvent]</c>) and is serialized with
    /// the outbox's JSON options.
    /// <para>
    /// Returning <see langword="null"/> from <paramref name="convert"/> drops that one occurrence, which
    /// is how you publish only the events that matter: for example only orders above a threshold.
    /// </para>
    /// <para>
    /// Good for a translation that is one expression. Once it grows, or needs a service, move it into an
    /// <see cref="IOutboundIntegrationEvent{TDomainEvent, TContract}"/>, which the generated
    /// <c>outbox.Add{Module}IntegrationEvents()</c> registers. The contract's name and version are read
    /// off its attributes when it is published, which the generated path does not need to do.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="convert"/> is null.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TDomainEvent"/> is already mapped.</exception>
    public IntegrationEventMap PublishAs<TDomainEvent, TContract>(Func<TDomainEvent, TContract?> convert)
        where TDomainEvent : IDomainEvent
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(convert);
        return AddImmediate(typeof(TDomainEvent), domainEvent => convert((TDomainEvent)domainEvent));
    }

    /// <summary>
    /// Publishes <typeparamref name="TDomainEvent"/> through the
    /// <see cref="IOutboundIntegrationEvent{TDomainEvent, TContract}"/> that <paramref name="create"/>
    /// builds, once per message, from the scope the outbox processor runs in.
    /// <para>
    /// The contract's published name and version are passed in rather than read off its
    /// <c>[IntegrationEvent]</c>, so publishing asks no attribute anything. You rarely write this call:
    /// the generated <c>outbox.Add{Module}IntegrationEvents()</c> writes one per outbound class in the
    /// module, with the name and version its compiler saw.
    /// </para>
    /// </summary>
    /// <param name="contractName">The published name of <typeparamref name="TContract"/>.</param>
    /// <param name="contractVersion">The schema version of <typeparamref name="TContract"/>.</param>
    /// <param name="create">Builds the outbound class, taking its dependencies from the processor's scope.</param>
    /// <exception cref="ArgumentNullException"><paramref name="create"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="contractName"/> is empty, or <typeparamref name="TDomainEvent"/> is already mapped.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="contractVersion"/> is below 1.</exception>
    public IntegrationEventMap PublishWith<TDomainEvent, TContract>(
        string contractName,
        int contractVersion,
        Func<IServiceProvider, IOutboundIntegrationEvent<TDomainEvent, TContract>> create)
        where TDomainEvent : IDomainEvent
        where TContract : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentOutOfRangeException.ThrowIfLessThan(contractVersion, 1);
        ArgumentNullException.ThrowIfNull(create);

        return Add(
            typeof(TDomainEvent),
            new Entry(
                async (domainEvent, services, cancellationToken) =>
                    await create(services).CreateAsync((TDomainEvent)domainEvent, cancellationToken).ConfigureAwait(false),
                Immediate: null,
                Contract: (contractName, contractVersion)));
    }

    /// <summary>
    /// Keeps <typeparamref name="TDomainEvent"/> inside this process. The outbox still stores and
    /// delivers it, so in-process handlers still see it, but no sink is called and the row is marked
    /// processed.
    /// </summary>
    /// <exception cref="ArgumentException"><typeparamref name="TDomainEvent"/> is already mapped.</exception>
    public IntegrationEventMap DoNotPublish<TDomainEvent>() where TDomainEvent : IDomainEvent
        => AddImmediate(typeof(TDomainEvent), static _ => null);

    /// <summary>True when <paramref name="domainEventType"/> has an entry, and is therefore not published as it stands.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="domainEventType"/> is null.</exception>
    public bool IsMapped(Type domainEventType)
    {
        ArgumentNullException.ThrowIfNull(domainEventType);
        return _entries.ContainsKey(domainEventType);
    }

    /// <summary>
    /// The published name and version the entry for <paramref name="domainEventType"/> was registered
    /// with. False for an entry that did not say them, such as a <c>PublishAs</c> lambda, and for an event
    /// with no entry.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="domainEventType"/> is null.</exception>
    public bool TryDescribeContract(Type domainEventType, [NotNullWhen(true)] out string? name, out int version)
    {
        ArgumentNullException.ThrowIfNull(domainEventType);

        if (_entries.TryGetValue(domainEventType, out var entry) && entry.Contract is { } contract)
        {
            (name, version) = contract;
            return true;
        }

        name = null;
        version = 0;
        return false;
    }

    /// <summary>
    /// Converts <paramref name="domainEvent"/> to the object that should be published: the mapped
    /// contract, the event itself when nothing is mapped (see <see cref="IsMapped"/>), or
    /// <see langword="null"/> when the event must not be published.
    /// </summary>
    /// <param name="domainEvent">The event read back from the outbox.</param>
    /// <param name="services">The scope the outbox processor runs in, for an <see cref="IOutboundIntegrationEvent{TDomainEvent, TContract}"/> and its dependencies.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <exception cref="ArgumentNullException"><paramref name="domainEvent"/> or <paramref name="services"/> is null.</exception>
    public ValueTask<object?> ConvertAsync(IDomainEvent domainEvent, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(services);

        return _entries.TryGetValue(domainEvent.GetType(), out var entry)
            ? entry.Convert(domainEvent, services, cancellationToken)
            : ValueTask.FromResult<object?>(domainEvent);
    }

    /// <summary>
    /// Converts <paramref name="domainEvent"/> to the object that should be published, for an entry
    /// that needs no services.
    /// </summary>
    /// <param name="domainEvent">The event read back from the outbox.</param>
    /// <param name="contract">
    /// The mapped contract, the event itself when nothing is mapped, or <see langword="null"/> when the
    /// event must not be published.
    /// </param>
    /// <returns><see langword="true"/> when an entry decided the outcome, <see langword="false"/> when the event is published as it stands.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="domainEvent"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The event is published through an <see cref="IOutboundIntegrationEvent{TDomainEvent, TContract}"/>, which needs services and may complete later.</exception>
    [Obsolete("Use ConvertAsync, which also runs the entries registered with PublishWith. TryConvert only handles PublishAs and DoNotPublish.")]
    public bool TryConvert(IDomainEvent domainEvent, out object? contract)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (!_entries.TryGetValue(domainEvent.GetType(), out var entry))
        {
            contract = domainEvent;
            return false;
        }

        if (entry.Immediate is null)
        {
            throw new InvalidOperationException(
                $"'{domainEvent.GetType()}' is published through an {nameof(IOutboundIntegrationEvent<IDomainEvent, object>)}<TDomainEvent, TContract>, which needs services. Call {nameof(ConvertAsync)} instead.");
        }

        contract = entry.Immediate(domainEvent);
        return true;
    }

    private IntegrationEventMap AddImmediate(Type domainEventType, Func<IDomainEvent, object?> convert)
        => Add(domainEventType, new Entry((domainEvent, _, _) => ValueTask.FromResult(convert(domainEvent)), convert));

    private IntegrationEventMap Add(Type domainEventType, Entry entry)
    {
        if (!_entries.TryAdd(domainEventType, entry))
        {
            throw new ArgumentException($"'{domainEventType}' is already mapped; a domain event has one published contract.", nameof(domainEventType));
        }

        return this;
    }
}
