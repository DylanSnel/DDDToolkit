using System.Reflection;
using System.Text.Json;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Interfaces;
using DDDToolkit.Serialization.Converters;

namespace DDDToolkit.EntityFramework.Options;

/// <summary>
/// Settings for the transactional outbox (see <see cref="DDDEntityFrameworkOptions.UseOutbox"/>).
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

    private static JsonSerializerOptions CreateDefaultJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new SingleValueObjectConverterFactory());
        return options;
    }
}
