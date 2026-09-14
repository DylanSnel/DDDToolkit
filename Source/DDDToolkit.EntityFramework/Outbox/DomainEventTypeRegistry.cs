using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;

namespace DDDToolkit.EntityFramework.Outbox;

/// <summary>
/// Maps the stable name of a domain event (<see cref="DomainEventName.Of(Type)"/>, i.e. the
/// <c>[DomainEventName]</c> or the class name) back to its CLR type, so outbox payloads can be
/// deserialized after the class has been renamed or moved.
/// <para>
/// One name, one type: the shape new events are written as today. When an event has been versioned and
/// the old record is still in the build, both types carry the same name and the newest
/// <c>[IntegrationEvent(Version = n)]</c> wins here. The older shapes belong in
/// <c>IntegrationEventContractRegistry</c>, which is what the processor reads an old row through.
/// </para>
/// </summary>
public sealed class DomainEventTypeRegistry
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal);

    /// <summary>The registered names.</summary>
    public IReadOnlyCollection<string> Names => _types.Keys;

    /// <summary>Registers <typeparamref name="TEvent"/> under its stable name.</summary>
    public DomainEventTypeRegistry Register<TEvent>() where TEvent : IDomainEvent => Register(typeof(TEvent));

    /// <summary>Registers <paramref name="eventType"/> under its stable name.</summary>
    /// <exception cref="ArgumentException">The type is not a concrete <see cref="IDomainEvent"/>, or another type is registered under the same name.</exception>
    public DomainEventTypeRegistry Register(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        if (!IsConcreteEvent(eventType))
        {
            throw new ArgumentException($"'{eventType}' is not a concrete type implementing {nameof(IDomainEvent)}.", nameof(eventType));
        }

        var name = DomainEventName.Of(eventType);

        if (Attribute.GetCustomAttribute(eventType, typeof(IntegrationEventAttribute), inherit: false) is IntegrationEventAttribute published
            && !string.Equals(published.Name, name, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{eventType}' is stored under '{name}' and published as '{published.Name}', so a stored row could never be matched to a version. " +
                "Give [IntegrationEvent] the same name as [DomainEventName], or publish through outbox.PublishAs<TEvent, TContract>(...) with a separate contract type.",
                nameof(eventType));
        }

        if (_types.TryGetValue(name, out var existing) && existing != eventType)
        {
            // Two types under one name is how a versioned event looks while the old shape is still
            // readable: keep the newest, because that is the one new events are written as, and let the
            // contract registry hold the older shapes for reading.
            var kept = Newer(existing, eventType, name);
            _types[name] = kept;
            return this;
        }

        _types[name] = eventType;
        return this;
    }

    private static Type Newer(Type existing, Type candidate, string name)
    {
        var existingVersion = IntegrationEventContract.VersionOf(existing);
        var candidateVersion = IntegrationEventContract.VersionOf(candidate);

        if (existingVersion == candidateVersion)
        {
            throw new ArgumentException(
                $"Both '{existing}' and '{candidate}' use the domain event name '{name}' at version {existingVersion}. " +
                "Give one of them a different [DomainEventName], or say which is newer with [IntegrationEvent(\"" + name + "\", Version = n)].",
                nameof(candidate));
        }

        return candidateVersion > existingVersion ? candidate : existing;
    }

    /// <summary>Registers every concrete <see cref="IDomainEvent"/> type in <paramref name="assembly"/>.</summary>
    public DomainEventTypeRegistry RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes().Where(IsConcreteEvent))
        {
            Register(type);
        }

        return this;
    }

    /// <summary>The type registered under <paramref name="eventName"/>, or <see langword="null"/>.</summary>
    public Type? Resolve(string eventName) => _types.GetValueOrDefault(eventName);

    /// <summary>Looks up the type registered under <paramref name="eventName"/>.</summary>
    public bool TryResolve(string eventName, [NotNullWhen(true)] out Type? eventType) => _types.TryGetValue(eventName, out eventType);

    private static bool IsConcreteEvent(Type type)
        => typeof(IDomainEvent).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface && !type.IsGenericTypeDefinition;
}
