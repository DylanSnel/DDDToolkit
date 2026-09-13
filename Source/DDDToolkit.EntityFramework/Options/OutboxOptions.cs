using System.Reflection;
using System.Text.Json;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Interfaces;
using DDDToolkit.Serialization.Converters;

namespace DDDToolkit.EntityFramework.Options;

/// <summary>
/// Settings for the transactional outbox (see <see cref="DDDEntityFrameworkOptions.UseOutbox"/>).
/// <para>
/// The outbox needs an exit. Without one, <see cref="OutboxProcessor{TContext}"/> hands messages back
/// to the same in-process delegate that would have run at save time, which buys durability but never
/// leaves the process. <see cref="SendTo{TSink}()"/> is the exit: register a sink and the processor
/// publishes an <c>IntegrationEventMessage</c> to it instead of calling the delegate.
/// </para>
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>
    /// Serializer options for event payloads. The defaults are case-insensitive and include the
    /// toolkit's <see cref="SingleValueObjectConverterFactory"/>, so class ids and single value objects
    /// are stored as their raw value. Replace or extend as needed; the same options are used to read
    /// the payload back, so change them with care once messages exist.
    /// </summary>
    public JsonSerializerOptions JsonOptions { get; set; } = CreateDefaultJsonOptions();

    /// <summary>
    /// Messages whose <see cref="OutboxMessage.Attempts"/> reached this value are no longer picked up
    /// by the processor. They stay in the table with their <see cref="OutboxMessage.LastError"/> for
    /// inspection; reset <c>Attempts</c> to retry them. Defaults to 10.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>The event types the processor can deserialize, keyed by their stable name.</summary>
    public DomainEventTypeRegistry EventTypes { get; } = new();

    /// <summary>Registers every concrete <see cref="IDomainEvent"/> type in <paramref name="assembly"/>.</summary>
    public OutboxOptions RegisterEventsFromAssembly(Assembly assembly)
    {
        EventTypes.RegisterFromAssembly(assembly);
        return this;
    }

    /// <summary>Registers every concrete <see cref="IDomainEvent"/> type in the assembly that declares <typeparamref name="TMarker"/>.</summary>
    public OutboxOptions RegisterEventsFromAssemblyContaining<TMarker>() => RegisterEventsFromAssembly(typeof(TMarker).Assembly);

    /// <summary>Registers a single event type.</summary>
    public OutboxOptions RegisterEvent<TEvent>() where TEvent : IDomainEvent
    {
        EventTypes.Register<TEvent>();
        return this;
    }

    /// <summary>
    /// The configured sinks, in the order they were registered. Empty means the processor falls back
    /// to the in-process dispatch delegate, which is the 2.x and early 3.0 behaviour.
    /// </summary>
    public IReadOnlyList<IntegrationEventSinkRegistration> Sinks => _sinks;

    /// <summary>True when at least one sink is configured.</summary>
    public bool HasSinks => _sinks.Count > 0;

    /// <summary>
    /// Which domain events leave the process, and as what. Leave it empty to publish domain events
    /// directly; see <see cref="IntegrationEventMap"/>.
    /// </summary>
    public IntegrationEventMap IntegrationEvents { get; } = new();

    /// <summary>
    /// Also invoke the in-process dispatch delegate for every message, on top of the sinks. Off by
    /// default: configuring a sink replaces the delegate. Turn it on when local handlers and a
    /// transport both need the event. The delegate runs first and receives the domain event itself,
    /// never the published contract.
    /// </summary>
    public bool AlsoDispatchInProcess { get; set; }

    /// <summary>
    /// Delivers published messages to <typeparamref name="TSink"/>. The sink is taken from the scope
    /// the processor runs in when it is registered there, and otherwise constructed with its
    /// constructor services injected.
    /// </summary>
    public OutboxOptions SendTo<TSink>() where TSink : IIntegrationEventSink
    {
        _sinks.Add(new IntegrationEventSinkRegistration(typeof(TSink)));
        return this;
    }

    /// <summary>Delivers published messages to <paramref name="sink"/>, an instance you already own.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="sink"/> is null.</exception>
    public OutboxOptions SendTo(IIntegrationEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sinks.Add(new IntegrationEventSinkRegistration(sink));
        return this;
    }

    /// <summary>
    /// Publishes <typeparamref name="TDomainEvent"/> as <typeparamref name="TContract"/>. Shorthand for
    /// <see cref="IntegrationEventMap.PublishAs{TDomainEvent, TContract}"/>.
    /// </summary>
    public OutboxOptions PublishAs<TDomainEvent, TContract>(Func<TDomainEvent, TContract?> convert)
        where TDomainEvent : IDomainEvent
        where TContract : class
    {
        IntegrationEvents.PublishAs(convert);
        return this;
    }

    /// <summary>
    /// Keeps <typeparamref name="TDomainEvent"/> off the sinks. Shorthand for
    /// <see cref="IntegrationEventMap.DoNotPublish{TDomainEvent}"/>.
    /// </summary>
    public OutboxOptions DoNotPublish<TDomainEvent>() where TDomainEvent : IDomainEvent
    {
        IntegrationEvents.DoNotPublish<TDomainEvent>();
        return this;
    }

    private readonly List<IntegrationEventSinkRegistration> _sinks = [];

    private static JsonSerializerOptions CreateDefaultJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new SingleValueObjectConverterFactory());
        return options;
    }
}
