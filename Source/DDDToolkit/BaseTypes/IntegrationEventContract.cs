using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.BaseTypes;

/// <summary>
/// Reads the published name and version off a contract type. Both fall back, so a contract nobody named
/// gets its module and class name (<c>ordering.order-placed</c>) and the version its class name ends in.
/// <list type="number">
///   <item><description>The name: <c>[IntegrationEvent("name")]</c>, otherwise <c>[DomainEventName("name")]</c>,
///   otherwise the conventional name (<see cref="DomainEventName.ConventionalNameOf"/>).</description></item>
///   <item><description>The version: a trailing <c>V</c> and a number in the class name, <c>OrderPlacedV2</c>,
///   otherwise <c>[IntegrationEvent(Version = n)]</c>, otherwise 1.</description></item>
/// </list>
/// <para>
/// The suffix comes first because it is the one a reader sees. Where the two disagree the attribute is
/// ignored, and the analyzer says so (DDD00034).
/// </para>
/// </summary>
public static class IntegrationEventContract
{
    /// <summary>The published name of <paramref name="contractType"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="contractType"/> is null.</exception>
    public static string NameOf(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        if (Attribute.GetCustomAttribute(contractType, typeof(IntegrationEventAttribute), inherit: false) is IntegrationEventAttribute { Name: { } name })
        {
            return name;
        }

        return DomainEventName.Of(contractType);
    }

    /// <summary>The published name of <typeparamref name="TContract"/>.</summary>
    public static string NameOf<TContract>() => NameOf(typeof(TContract));

    /// <summary>The published name of <paramref name="contract"/>'s runtime type.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="contract"/> is null.</exception>
    public static string NameOf(object contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return NameOf(contract.GetType());
    }

    /// <summary>
    /// The schema version of <paramref name="contractType"/>: the one its class name ends in, otherwise
    /// <c>[IntegrationEvent(Version = n)]</c>, otherwise 1.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="contractType"/> is null.</exception>
    public static int VersionOf(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        if (DomainEventName.VersionSuffixOf(contractType) is { } suffix)
        {
            return suffix;
        }

        var attribute = (IntegrationEventAttribute?)Attribute.GetCustomAttribute(contractType, typeof(IntegrationEventAttribute), inherit: false);
        return attribute?.Version ?? 1;
    }

    /// <summary>The schema version of <typeparamref name="TContract"/>.</summary>
    public static int VersionOf<TContract>() => VersionOf(typeof(TContract));

    /// <summary>The schema version of <paramref name="contract"/>'s runtime type.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="contract"/> is null.</exception>
    public static int VersionOf(object contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return VersionOf(contract.GetType());
    }

    /// <summary>
    /// The name a broker gives the contract's own exchange or topic: its published name and its version,
    /// <c>ordering.order-placed.v2</c>. A transport that gives every message type an entity of its own, as
    /// MassTransit and Wolverine do on RabbitMQ, uses this in place of the CLR type name, so renaming or
    /// moving the class does not move its messages to another exchange. Each version is an entity of its
    /// own, because each version is a type of its own there.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="contractType"/> is null.</exception>
    public static string EntityNameOf(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        return NameOf(contractType) + ".v" + VersionOf(contractType).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The broker entity name of <typeparamref name="TContract"/> (see <see cref="EntityNameOf(Type)"/>).</summary>
    public static string EntityNameOf<TContract>() => EntityNameOf(typeof(TContract));

    /// <summary>
    /// Whether <paramref name="type"/> says it is an event the toolkit names: it carries
    /// <c>[IntegrationEvent]</c> or <c>[DomainEventName]</c>, or it is a domain event. A transport that
    /// renames its entities after <see cref="EntityNameOf(Type)"/> asks this first, so the other messages
    /// the application sends keep the names the transport gives them.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public static bool IsNamedByToolkit(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return Attribute.IsDefined(type, typeof(IntegrationEventAttribute), inherit: false)
               || Attribute.IsDefined(type, typeof(DomainEventNameAttribute), inherit: false)
               || typeof(Interfaces.IDomainEvent).IsAssignableFrom(type);
    }
}
