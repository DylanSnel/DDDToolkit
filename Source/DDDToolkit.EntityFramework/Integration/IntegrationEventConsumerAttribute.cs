namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// Pins the inbox name of a handler, the way <c>[DomainEventName]</c> pins the name of an event.
/// <para>
/// The inbox is keyed on the message and the consumer, so the consumer name is what remembers which
/// handlers have already applied a message. Rename the class without this attribute and the name changes
/// with it, every row for the old name stops matching, and the handler replays its entire backlog. Pick
/// a name you are willing to keep.
/// </para>
/// <code>
/// [IntegrationEventConsumer("billing.invoicer")]
/// public sealed class RaiseInvoice : IIntegrationEventHandler&lt;OrderPlacedV2&gt; { ... }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventConsumerAttribute : Attribute
{
    /// <summary>Pins the consumer name.</summary>
    /// <param name="name">The name the inbox keys on. Cannot be empty.</param>
    /// <exception cref="ArgumentException">The name is empty or white space.</exception>
    public IntegrationEventConsumerAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A consumer name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    /// <summary>The name the inbox keys on.</summary>
    public string Name { get; }
}

/// <summary>Reads the inbox name of a handler.</summary>
public static class IntegrationEventConsumer
{
    /// <summary>
    /// The name <paramref name="handlerType"/> is recorded under: its
    /// <see cref="IntegrationEventConsumerAttribute"/>, otherwise its full CLR type name.
    /// <para>
    /// The fallback is a convenience, not a recommendation. A full type name changes when you rename or
    /// move the class, and the inbox cannot tell that from a new consumer, so it replays everything.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="handlerType"/> is null.</exception>
    public static string NameOf(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);

        return Attribute.GetCustomAttribute(handlerType, typeof(IntegrationEventConsumerAttribute), inherit: false) is IntegrationEventConsumerAttribute attribute
            ? attribute.Name
            : handlerType.FullName ?? handlerType.Name;
    }

    /// <summary>The name <typeparamref name="THandler"/> is recorded under.</summary>
    public static string NameOf<THandler>() => NameOf(typeof(THandler));
}
