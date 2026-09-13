using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;

namespace DDDToolkit.EntityFramework.Outbox;

/// <summary>
/// Maps the stable name of a domain event (<see cref="DomainEventName.Of(Type)"/>, i.e. the
/// <c>[DomainEventName]</c> or the class name) back to its CLR type, so outbox payloads can be
/// deserialized after the class has been renamed or moved.
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
        if (_types.TryGetValue(name, out var existing) && existing != eventType)
        {
            throw new ArgumentException($"Both '{existing}' and '{eventType}' use the domain event name '{name}'. Give one of them a different [DomainEventName].", nameof(eventType));
        }

        _types[name] = eventType;
        return this;
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
