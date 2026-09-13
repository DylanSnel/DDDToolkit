namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a type as a published contract and pins the name and version it is published under. Put it on
/// the message you send to other systems, not on the domain event you raise inside this one.
/// <para>
/// The name is what a consumer routes on, so it has to survive renaming the class. The version is what
/// a consumer checks when the payload changes shape: bump it when you break the schema, leave it when
/// you only add an optional field.
/// </para>
/// <code>
/// [IntegrationEvent("ordering.order-placed", Version = 2)]
/// public sealed record OrderPlacedV2(Guid OrderId, string Customer, decimal Total);
/// </code>
/// <para>
/// A type without this attribute still publishes: it falls back to
/// <c>[DomainEventName]</c> (or the class name) and version 1, which is what lets a team publish its
/// domain events directly without inventing a second type first.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class IntegrationEventAttribute : Attribute
{
    /// <summary>Pins the published name.</summary>
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

    /// <summary>The name this contract is published under.</summary>
    public string Name { get; }

    /// <summary>The schema version of the payload. Defaults to 1, and cannot be lower.</summary>
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
