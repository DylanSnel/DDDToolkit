namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a type as a published contract. Put it on the message you send to other systems, not on the
/// domain event you raise inside this one.
/// <code>
/// [IntegrationEvent]
/// public sealed record OrderPlacedV2(Guid OrderId, string Customer, decimal Total);
/// </code>
/// <para>
/// With nothing in the brackets the contract is named by convention: its module and its class name in kebab
/// case, with a trailing <c>V</c> and a number read as the version. The record above, in
/// <c>[assembly: Module("Ordering")]</c>, is published as <c>ordering.order-placed</c> version 2. Pin the name
/// only when the convention would give the wrong one, typically after renaming the class, so consumers keep
/// routing on what they always routed on:
/// </para>
/// <code>
/// [IntegrationEvent("ordering.order-placed")]
/// public sealed record PlacedOrderV2(Guid OrderId, string Customer, decimal Total);
/// </code>
/// <para>
/// The version is what a consumer checks when the payload changes shape: bump it when you break the schema,
/// leave it when you only add an optional field. Say it in the class name; <see cref="Version"/> is for a
/// class whose name does not end in one. When both are written they have to agree (DDD00034).
/// </para>
/// <para>
/// A type without this attribute still publishes, under <c>[DomainEventName]</c> or the conventional name,
/// which is what lets a team publish its domain events directly without inventing a second type first.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class IntegrationEventAttribute : Attribute
{
    /// <summary>Marks a published contract named by convention.</summary>
    public IntegrationEventAttribute()
    {
    }

    /// <summary>Marks a published contract and pins the name it is published under.</summary>
    /// <param name="name">The name consumers route on. Cannot be empty.</param>
    /// <exception cref="ArgumentException">The name is empty or white space.</exception>
    public IntegrationEventAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An integration event name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    /// <summary>The name this contract is published under, or null when the convention names it.</summary>
    public string? Name { get; }

    /// <summary>
    /// The schema version of the payload, for a class whose name does not end in <c>V</c> and a number.
    /// Defaults to 1, and cannot be lower.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The version is below 1.</exception>
    public int Version
    {
        get => _version;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "An integration event version starts at 1.");
            }

            _version = value;
        }
    }

    private int _version = 1;
}
