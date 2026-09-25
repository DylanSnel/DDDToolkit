using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;

namespace DDDToolkit.EntityFramework.Outbox;

/// <summary>
/// Maps the stable name of a domain event (<see cref="DomainEventName.Of(Type)"/>, i.e. the
/// <c>[DomainEventName]</c> or the conventional name) back to its CLR type, so outbox payloads can be
/// deserialized after the class has been renamed or moved.
/// <para>
/// One name, one type: the shape new events are written as today. When an event has been versioned and
/// the old record is still in the build, both types carry the same name and the newest
/// <c>[IntegrationEvent(Version = n)]</c> wins here. The older shapes belong in
/// <c>IntegrationEventContractRegistry</c>, which is what the processor reads an old row through.
/// </para>
/// <para>
/// Every type is also found by its bare class name, the name an event without <c>[DomainEventName]</c> was
/// stored under before events were named by convention. That is what keeps rows written by an older build
/// readable: a row stored as <c>OrderPlaced</c> still finds <c>OrderPlaced</c> after the type's name became
/// <c>ordering.order-placed</c>. The class name only answers when no current name is spelled the same, and
/// not at all when two registered classes share it.
/// </para>
/// </summary>
public sealed class DomainEventTypeRegistry
{
    private readonly Dictionary<string, (Type Type, int Version)> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, (string Name, int Version)> _described = [];

    /// <summary>Class names as the pre-convention fallback names; null where two registered classes share one.</summary>
    private readonly Dictionary<string, Type?> _classNames = new(StringComparer.Ordinal);

    /// <summary>The registered names.</summary>
    public IReadOnlyCollection<string> Names => _types.Keys;

    /// <summary>Every registered type with the name it is stored under.</summary>
    internal IEnumerable<(Type Type, string Name)> Described => _described.Select(static pair => (pair.Key, pair.Value.Name));

    /// <summary>
    /// Registers <typeparamref name="TEvent"/> under <paramref name="name"/> at <paramref name="version"/>,
    /// as they were read off its attributes when the module was compiled. This is what the generated
    /// <c>outbox.Add{Module}IntegrationEvents()</c> calls; nothing here reads an attribute or scans an
    /// assembly.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or another type is registered under the same name and version.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is below 1.</exception>
    public DomainEventTypeRegistry Register<TEvent>(string name, int version) where TEvent : IDomainEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        return Add(typeof(TEvent), name, version);
    }

    /// <summary>Registers <typeparamref name="TEvent"/> under its stable name, read from its attributes at run time.</summary>
    public DomainEventTypeRegistry Register<TEvent>() where TEvent : IDomainEvent => Register(typeof(TEvent));

    /// <summary>
    /// Registers <paramref name="eventType"/> under its stable name, read from its attributes at run time.
    /// The generated <c>outbox.Add{Module}IntegrationEvents()</c> does the same without reflection.
    /// </summary>
    /// <exception cref="ArgumentException">The type is not a concrete <see cref="IDomainEvent"/>, or another type is registered under the same name.</exception>
    public DomainEventTypeRegistry Register(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        if (!IsConcreteEvent(eventType))
        {
            throw new ArgumentException($"'{eventType}' is not a concrete type implementing {nameof(IDomainEvent)}.", nameof(eventType));
        }

        var name = DomainEventName.Of(eventType);

        if (Attribute.GetCustomAttribute(eventType, typeof(IntegrationEventAttribute), inherit: false) is IntegrationEventAttribute { Name: { } publishedName }
            && !string.Equals(publishedName, name, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{eventType}' is stored under '{name}' and published as '{publishedName}', so a stored row could never be matched to a version. " +
                "Give [IntegrationEvent] the same name as [DomainEventName], leave the name out of [IntegrationEvent] so both follow the convention, " +
                "or publish a separate contract type through an IOutboundIntegrationEvent<TEvent, TContract> or outbox.PublishAs<TEvent, TContract>(...).",
                nameof(eventType));
        }

        return Add(eventType, name, IntegrationEventContract.VersionOf(eventType));
    }

    private DomainEventTypeRegistry Add(Type eventType, string name, int version)
    {
        _described[eventType] = (name, version);
        AddClassName(eventType);

        if (_types.TryGetValue(name, out var existing) && existing.Type != eventType)
        {
            // Two types under one name is how a versioned event looks while the old shape is still
            // readable: keep the newest, because that is the one new events are written as, and let the
            // contract registry hold the older shapes for reading.
            if (existing.Version == version)
            {
                throw new ArgumentException(
                    $"Both '{existing.Type}' and '{eventType}' use the domain event name '{name}' at version {version}. " +
                    "Give one of them a different [DomainEventName], or say which is newer with [IntegrationEvent(\"" + name + "\", Version = n)].",
                    nameof(eventType));
            }

            if (version > existing.Version)
            {
                _types[name] = (eventType, version);
            }

            return this;
        }

        _types[name] = (eventType, version);
        return this;
    }

    private void AddClassName(Type eventType)
    {
        if (_classNames.TryGetValue(eventType.Name, out var existing))
        {
            if (existing != eventType)
            {
                _classNames[eventType.Name] = null;
            }

            return;
        }

        _classNames[eventType.Name] = eventType;
    }

    /// <summary>Registers every concrete <see cref="IDomainEvent"/> type in <paramref name="assembly"/>, by reflection.</summary>
    public DomainEventTypeRegistry RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes().Where(IsConcreteEvent))
        {
            Register(type);
        }

        return this;
    }

    /// <summary>
    /// The type registered under <paramref name="eventName"/>, or under the class name
    /// <paramref name="eventName"/> spells, or <see langword="null"/>.
    /// </summary>
    public Type? Resolve(string eventName) => TryResolve(eventName, out var eventType) ? eventType : null;

    /// <summary>
    /// Looks up the type registered under <paramref name="eventName"/>, and otherwise the registered type
    /// whose class name it is (see the remarks on the class).
    /// </summary>
    public bool TryResolve(string eventName, [NotNullWhen(true)] out Type? eventType)
        => TryResolve(eventName, out eventType, out _);

    /// <summary>
    /// Looks up the type registered under <paramref name="eventName"/>, and otherwise the registered type
    /// whose class name it is. <paramref name="byClassName"/> says which: a row found by its class name was
    /// written by exactly that class, before the type had the name it is registered under now.
    /// </summary>
    public bool TryResolve(string eventName, [NotNullWhen(true)] out Type? eventType, out bool byClassName)
    {
        ArgumentNullException.ThrowIfNull(eventName);

        if (_types.TryGetValue(eventName, out var entry))
        {
            eventType = entry.Type;
            byClassName = false;
            return true;
        }

        if (_classNames.TryGetValue(eventName, out var named) && named is not null)
        {
            eventType = named;
            byClassName = true;
            return true;
        }

        eventType = null;
        byClassName = false;
        return false;
    }

    /// <summary>
    /// The name and version <paramref name="eventType"/> was registered under, so the outbox can write and
    /// read a row without asking the type's attributes.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="eventType"/> is null.</exception>
    public bool TryDescribe(Type eventType, [NotNullWhen(true)] out string? name, out int version)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        if (_described.TryGetValue(eventType, out var described))
        {
            (name, version) = described;
            return true;
        }

        name = null;
        version = 0;
        return false;
    }

    private static bool IsConcreteEvent(Type type)
        => typeof(IDomainEvent).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface && !type.IsGenericTypeDefinition;
}
