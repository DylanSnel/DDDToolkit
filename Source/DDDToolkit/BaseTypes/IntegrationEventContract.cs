using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.BaseTypes;

/// <summary>
/// Reads the published name and version off a contract type. Both fall back, so a team that has not
/// split its domain events from its published messages yet still gets a sensible name and version 1.
/// <list type="number">
///   <item><description><c>[IntegrationEvent("name", Version = n)]</c> wins.</description></item>
///   <item><description>Otherwise <c>[DomainEventName("name")]</c> gives the name, with version 1.</description></item>
///   <item><description>Otherwise the class name, with version 1.</description></item>
/// </list>
/// </summary>
public static class IntegrationEventContract
{
    /// <summary>The published name of <paramref name="contractType"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="contractType"/> is null.</exception>
    public static string NameOf(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        if (Attribute.GetCustomAttribute(contractType, typeof(IntegrationEventAttribute), inherit: false) is IntegrationEventAttribute attribute)
        {
            return attribute.Name;
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

    /// <summary>The schema version of <paramref name="contractType"/>, or 1 when it says nothing.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="contractType"/> is null.</exception>
    public static int VersionOf(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

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
}
